namespace AutoCanvas.Server.Application.Instance;

/// <summary>A setting's value in effect and where it comes from.</summary>
/// <param name="Value">The value in effect, if any.</param>
/// <param name="Source">Where it comes from.</param>
public sealed record SettingValue(Uri? Value, SettingSource Source);
