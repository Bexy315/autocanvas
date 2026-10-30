using System.Globalization;
using System.Threading.RateLimiting;
using AutoCanvas.Server.Api;
using AutoCanvas.Server.Api.Identity;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Authentication;

/// <summary>Sign-in, sessions, authorization and their protections (ADR-0011).</summary>
internal static class AuthenticationRegistration
{
    /// <summary>Sign-in attempts per client address and minute.</summary>
    public const int SignInsPerMinute = 10;

    /// <summary>Registers authentication before the application layer, so its audit context wins.</summary>
    public static IServiceCollection AddAutoCanvasAuthentication(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IAuditContext, HttpAuditContext>();

        services.AddOptions<SessionLifetimeOptions>()
            .BindConfiguration(SessionLifetimeOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SessionLifetimeOptions>, SessionOptionsValidator>();

        services.AddAuthentication(AutoCanvasClaims.Scheme)
            .AddScheme<AuthenticationSchemeOptions, AutoCanvasAuthenticationHandler>(AutoCanvasClaims.Scheme, null);
        services.AddAutoCanvasAuthorization();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemAuthorizationResultHandler>();

        services.AddRateLimiter(limiter =>
        {
            limiter.AddPolicy(AuthEndpoints.SignInRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = SignInsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            limiter.OnRejected = async (rejected, cancellationToken) =>
            {
                var response = rejected.HttpContext.Response;
                response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                await rejected.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = rejected.HttpContext,
                    ProblemDetails =
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Type = ProblemTypes.TooManyRequests,
                        Title = "Too many requests",
                        Detail = "Too many sign-in attempts from this address. Wait a minute.",
                    },
                }).ConfigureAwait(false);
            };
        });

        return services;
    }

    public static WebApplication UseAutoCanvasAuthentication(this WebApplication app)
    {
        app.UseMiddleware<CsrfHeaderMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        return app;
    }
}
