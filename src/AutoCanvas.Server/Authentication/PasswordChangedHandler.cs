using Microsoft.AspNetCore.Authorization;

namespace AutoCanvas.Server.Authentication;

/// <summary>Meets <see cref="PasswordChangedRequirement"/> unless the user must change the password.</summary>
internal sealed class PasswordChangedHandler : AuthorizationHandler<PasswordChangedRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PasswordChangedRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true && !context.User.HasClaim(AutoCanvasClaims.MustChangePassword, "true"))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
