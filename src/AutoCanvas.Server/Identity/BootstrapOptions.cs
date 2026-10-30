namespace AutoCanvas.Server.Identity;

/// <summary>
/// The first admin for installations without a terminal, such as containers (configuration
/// section <c>Bootstrap</c>). Used only while there is no account at all.
/// </summary>
internal sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    /// <summary>User name of the first admin.</summary>
    public string? AdminUserName { get; set; }

    /// <summary>Initial password of the first admin; it must be changed at the first sign-in.</summary>
    public string? AdminPassword { get; set; }
}
