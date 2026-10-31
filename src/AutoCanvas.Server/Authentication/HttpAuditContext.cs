using System.Security.Claims;
using AutoCanvas.Persistence.Audit;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Runners;

namespace AutoCanvas.Server.Authentication;

/// <summary>
/// The actor of changes made over HTTP: the signed-in user (through a session or a token), a runner
/// on its gRPC calls, or an anonymous client. Outside a request (hosted services) it is the system.
/// </summary>
internal sealed class HttpAuditContext(IHttpContextAccessor accessor) : IAuditContext
{
    public AuditActor Actor
    {
        get
        {
            if (accessor.HttpContext is not { } context)
            {
                return AuditActor.System;
            }

            var user = context.User;
            if (user.Identity?.IsAuthenticated != true)
            {
                return AuditActor.Anonymous;
            }

            if (user.GetRunnerId() is { } runnerId)
            {
                return new AuditActor(AuditActorKind.Runner, runnerId, user.FindFirstValue(RunnerClaims.RunnerName) ?? string.Empty);
            }

            var kind = user.UsesSession() ? AuditActorKind.User : AuditActorKind.Token;
            return new AuditActor(kind, user.GetUserId(), user.FindFirstValue(AutoCanvasClaims.UserName) ?? string.Empty);
        }
    }

    public string? ClientAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => accessor.HttpContext?.Request.Headers.UserAgent.ToString() is { Length: > 0 } agent ? agent : null;
}
