using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Audit;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Automations;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Application.Identity;
using AutoCanvas.Server.Application.Instance;
using AutoCanvas.Server.Application.Runners;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Setup;

/// <summary>
/// Finishes the setup of a fresh installation in one transaction: the first admin, the addresses,
/// optionally the example automation and a registration token for the bundled runner. The host
/// checks the setup token and that setup is still open.
/// </summary>
public sealed class SetupService(
    AutoCanvasDbContext database,
    UserService users,
    InstanceSettingsService settings,
    FolderService folders,
    AutomationService automations,
    RunnerRegistrationService registration,
    IRunnerTokenHandoff handoff,
    IAuditLog audit)
{
    /// <summary>Whether the installation has no account yet, so it can be set up.</summary>
    public async Task<bool> IsOpenAsync(CancellationToken cancellationToken) =>
        !await database.Users.AnyAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>Sets the installation up; nothing is kept when any part fails.</summary>
    public async Task<UseCaseResult<SetupOutcome>> CompleteAsync(SetupRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.BundledRunner && !handoff.IsAvailable)
        {
            return UseCase.Failure<SetupOutcome>(UseCaseError.Invalid("bundledRunner", "This server has no bundled runner to hand a token to (Setup:RunnerTokenFile is not set)."));
        }

        var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            if (!await IsOpenAsync(cancellationToken).ConfigureAwait(false))
            {
                return UseCase.Failure<SetupOutcome>(UseCaseError.Conflict("This installation is set up already."));
            }

            var created = await users.CreateAsync(
                new NewUser(request.Admin.UserName, request.Admin.Password)
                {
                    DisplayName = request.Admin.DisplayName,
                    Email = request.Admin.Email,
                    IsAdmin = true,
                    MustChangePassword = false,
                },
                cancellationToken).ConfigureAwait(false);
            if (created.Error is { } invalidAdmin)
            {
                return UseCase.Failure<SetupOutcome>(invalidAdmin with { Field = invalidAdmin.Field is { } field ? $"admin.{field}" : null });
            }

            var admin = created.Value!;
            var addresses = await settings.UpdateAsync(admin.Id, request.PublicAddress, request.RunnerAddress, cancellationToken).ConfigureAwait(false);
            if (addresses.Error is { } invalidAddress)
            {
                return UseCase.Failure<SetupOutcome>(invalidAddress);
            }

            Guid? exampleId = null;
            if (request.Example)
            {
                var example = await CreateExampleAsync(new Actor(admin.Id, IsAdmin: true), cancellationToken).ConfigureAwait(false);
                if (example.Error is { } broken)
                {
                    return UseCase.Failure<SetupOutcome>(broken);
                }

                exampleId = example.Value;
            }

            if (request.BundledRunner)
            {
                var token = await registration.CreateTokenAsync("Bundled runner (setup)", null, cancellationToken).ConfigureAwait(false);
                await handoff.WriteAsync(token.Value!.Token, cancellationToken).ConfigureAwait(false);
            }

            await RecordAsync(request, admin, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return UseCase.Success(new SetupOutcome(admin, request.BundledRunner, exampleId));
        }
    }

    private async Task RecordAsync(SetupRequest request, UserView admin, CancellationToken cancellationToken)
    {
        audit.Record(new AuditEntry(AuditActions.SetupCompleted)
        {
            TargetType = "instance",
            Actor = new AuditActor(AuditActorKind.User, admin.Id, admin.DisplayName),
            Details = AuditEntry.DetailsOf(("userName", admin.UserName), ("example", request.Example), ("bundledRunner", request.BundledRunner)),
        });
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<UseCaseResult<Guid>> CreateExampleAsync(Actor actor, CancellationToken cancellationToken)
    {
        var folder = await folders.CreateAsync(actor, new NewFolder(ExampleAutomation.FolderName, Description: ExampleAutomation.FolderDescription), cancellationToken).ConfigureAwait(false);
        if (folder.Error is { } folderError)
        {
            return UseCase.Failure<Guid>(folderError);
        }

        var automation = await automations.CreateAsync(actor, folder.Value!.Id, ExampleAutomation.Name, ExampleAutomation.Yaml, "Created by the setup", cancellationToken).ConfigureAwait(false);
        return automation.Error is { } automationError ? UseCase.Failure<Guid>(automationError) : UseCase.Success(automation.Value!.Id);
    }
}
