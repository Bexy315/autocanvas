using AutoCanvas.Persistence;
using AutoCanvas.Server.Api;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Setup;

/// <summary>
/// While setup mode is on, answers every API request but the setup's own with 503 (ADR-0020). An
/// account created meanwhile (by <c>admin create</c>, or by another server on the same database)
/// ends setup mode at the next request.
/// </summary>
internal sealed class SetupGateMiddleware(RequestDelegate next, SetupState state, IProblemDetailsService problems)
{
    public const string SetupPath = "/api/v1/setup";

    public async Task InvokeAsync(HttpContext context, AutoCanvasDbContext database)
    {
        var path = context.Request.Path;
        if (state.IsOpen
            && path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWithSegments(SetupPath, StringComparison.OrdinalIgnoreCase)
            && !path.StartsWithSegments("/api/v1/info", StringComparison.OrdinalIgnoreCase))
        {
            if (await database.Users.AnyAsync(context.RequestAborted).ConfigureAwait(false))
            {
                state.Close();
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await problems.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context,
                    ProblemDetails =
                    {
                        Status = StatusCodes.Status503ServiceUnavailable,
                        Type = ProblemTypes.SetupRequired,
                        Title = "Setup required",
                        Detail = "This installation is not set up yet. Open the setup address from the server's log in a browser.",
                    },
                }).ConfigureAwait(false);
                return;
            }
        }

        await next(context).ConfigureAwait(false);
    }
}
