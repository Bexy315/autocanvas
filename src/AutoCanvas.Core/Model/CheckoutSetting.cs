namespace AutoCanvas.Core.Model;

/// <summary>Which repositories a job clones into its workspace.</summary>
/// <param name="Kind">How the job's checkout was specified.</param>
/// <param name="Repositories">The repositories for <see cref="CheckoutKind.Explicit"/>; otherwise empty.</param>
public sealed record CheckoutSetting(CheckoutKind Kind, IReadOnlyList<CheckoutRepository> Repositories)
{
    /// <summary>Nothing specified: a repository automation checks out its own commit.</summary>
    public static CheckoutSetting Default { get; } = new(CheckoutKind.Default, []);

    /// <summary><c>checkout: false</c>: nothing is checked out.</summary>
    public static CheckoutSetting Disabled { get; } = new(CheckoutKind.Disabled, []);
}
