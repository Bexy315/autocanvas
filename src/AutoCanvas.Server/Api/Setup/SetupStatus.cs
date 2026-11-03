namespace AutoCanvas.Server.Api.Setup;

/// <summary>Whether this installation still needs to be set up, and with the right token, what the setup needs to know.</summary>
/// <param name="Open">Setup mode is on: there is no account yet.</param>
/// <param name="Details">Only with a valid setup token.</param>
internal sealed record SetupStatus(bool Open, SetupDetails? Details);
