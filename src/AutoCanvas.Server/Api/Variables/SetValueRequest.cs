namespace AutoCanvas.Server.Api.Variables;

/// <summary>The value of a variable or secret to create or replace.</summary>
/// <param name="Value">At most 64 KiB. Variables may be empty; secrets may not. A secret's value is never shown again.</param>
internal sealed record SetValueRequest(string Value);
