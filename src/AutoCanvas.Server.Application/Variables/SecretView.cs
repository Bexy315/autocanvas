namespace AutoCanvas.Server.Application.Variables;

/// <summary>A secret as listed in its scope. There is deliberately no value: nobody reads it back.</summary>
/// <param name="Name">The name as written.</param>
/// <param name="UpdatedAt">When it was last set.</param>
/// <param name="UpdatedByName">Who set it last.</param>
public sealed record SecretView(string Name, DateTimeOffset UpdatedAt, string UpdatedByName);
