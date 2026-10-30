using Microsoft.AspNetCore.Authorization;

namespace AutoCanvas.Server.Authentication;

/// <summary>
/// Who may call an endpoint. Endpoints say <c>RequireAuthorization()</c> (signed in, password
/// changed) or name one of these policies; anonymous endpoints say <c>AllowAnonymous()</c>.
/// </summary>
internal static class AutoCanvasPolicies
{
    /// <summary>Instance administrators.</summary>
    public const string Admin = "Admin";

    /// <summary>Signed in, even while the password must still be changed (me, sign-out).</summary>
    public const string SignedIn = "SignedIn";

    /// <summary>Signed in with a session, not a token; also while the password must be changed.</summary>
    public const string PasswordChange = "PasswordChange";

    /// <summary>Signed in with a session, not a token (managing tokens and sessions).</summary>
    public const string SessionOnly = "SessionOnly";

    public static IServiceCollection AddAutoCanvasAuthorization(this IServiceCollection services)
    {
        var standard = new AuthorizationPolicyBuilder(AutoCanvasClaims.Scheme)
            .RequireAuthenticatedUser()
            .AddRequirements(new PasswordChangedRequirement())
            .Build();

        services.AddAuthorizationBuilder()
            // No fallback policy: unknown routes stay 404, and ApiContractTests makes every API
            // endpoint declare who may call it.
            .SetDefaultPolicy(standard)
            .AddPolicy(Admin, policy => policy.Combine(standard).RequireClaim(AutoCanvasClaims.Admin, "true"))
            .AddPolicy(SignedIn, policy => policy.AddAuthenticationSchemes(AutoCanvasClaims.Scheme).RequireAuthenticatedUser())
            .AddPolicy(PasswordChange, policy => policy.AddAuthenticationSchemes(AutoCanvasClaims.Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim(AutoCanvasClaims.Method, AutoCanvasClaims.SessionMethod))
            .AddPolicy(SessionOnly, policy => policy.Combine(standard).RequireClaim(AutoCanvasClaims.Method, AutoCanvasClaims.SessionMethod));
        services.AddSingleton<IAuthorizationHandler, PasswordChangedHandler>();
        return services;
    }
}
