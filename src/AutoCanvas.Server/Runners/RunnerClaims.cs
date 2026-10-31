using System.Globalization;
using System.Security.Claims;
using AutoCanvas.Server.Application.Runners;

namespace AutoCanvas.Server.Runners;

/// <summary>
/// The identity of a runner on its gRPC calls. It is set by <see cref="RunnerAuthInterceptor"/>
/// from the runner credential and never by the HTTP API's authentication, so neither side accepts
/// the other's credentials (ADR-0017).
/// </summary>
internal static class RunnerClaims
{
    public const string Scheme = "Runner";
    public const string RunnerId = "autocanvas:runner_id";
    public const string RunnerName = "autocanvas:runner_name";

    public static ClaimsPrincipal Create(RunnerState runner) =>
        new(new ClaimsIdentity([new Claim(RunnerId, runner.Id.ToString()), new Claim(RunnerName, runner.Name)], Scheme, RunnerName, null));

    /// <summary>The runner a call was authenticated as; <see langword="null"/> for any other caller.</summary>
    public static Guid? GetRunnerId(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(RunnerId) is { } id ? Guid.Parse(id, CultureInfo.InvariantCulture) : null;
}
