namespace AutoCanvas.Core.Nodes;

/// <summary>
/// The node types AutoCanvas ships (ADR-0016): the server refuses automations that use any other,
/// and the reference runner implements these. A plugin system would replace where this list comes
/// from, not how it is checked.
/// </summary>
public static class BuiltInNodeTypes
{
    /// <summary>Builds (and pushes) a container image.</summary>
    public const string DockerBuild = "docker_build";

    /// <summary>Sends an HTTP request.</summary>
    public const string HttpRequest = "http_request";

    /// <summary>HTTP methods <see cref="HttpRequest"/> sends.</summary>
    public static readonly IReadOnlyList<string> Methods = ["GET", "HEAD", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"];

    /// <summary>Every built-in node type, by name.</summary>
    public static IReadOnlyDictionary<string, NodeType> All { get; } = new Dictionary<string, NodeType>(StringComparer.Ordinal)
    {
        [DockerBuild] = new(
            DockerBuild,
            "Builds a container image from a Dockerfile in the workspace with rootless BuildKit, and pushes it if asked to.",
            [
                new("image", NodeInputKind.Text, required: true, "The image's name, such as my-app or ghcr.io/team/app; with push, a name in the registry."),
                new("tags", NodeInputKind.TextList, required: false, "Tags of the image; latest when none are given."),
                new("context", NodeInputKind.Text, required: false, "The build context, a directory relative to the workspace; the workspace itself by default."),
                new("dockerfile", NodeInputKind.Text, required: false, "The Dockerfile, relative to the context; Dockerfile by default."),
                new("build_args", NodeInputKind.TextMap, required: false, "Build arguments (ARG) by name."),
                new("target", NodeInputKind.Text, required: false, "The stage of a multi-stage Dockerfile to build."),
                new("push", NodeInputKind.Boolean, required: false, "Whether to push the image; false by default, which only builds it."),
                new("registry", NodeInputKind.Text, required: false, "The registry host the credentials are for; taken from image when not given."),
                new("username", NodeInputKind.Text, required: false, "User name for the registry."),
                new("password", NodeInputKind.Text, required: false, "Password or token for the registry; give it from a secret."),
            ],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["digest"] = "The image's digest, sha256:….",
                ["image"] = "The image with its first tag, such as my-app:latest.",
            }),
        [HttpRequest] = new(
            HttpRequest,
            "Sends an HTTP request from the runner and returns the response's status and body.",
            [
                new("url", NodeInputKind.Text, required: true, "The address, http or https."),
                new("method", NodeInputKind.Text, Required: false, "The HTTP method; GET by default.", Methods),
                new("headers", NodeInputKind.TextMap, required: false, "Request headers by name."),
                new("body", NodeInputKind.Text, required: false, "The request body."),
                new("timeout_seconds", NodeInputKind.WholeNumber, required: false, "How long to wait for the response, 1 to 600 seconds; 30 by default."),
                new("fail_on_status", NodeInputKind.Boolean, required: false, "Whether a status of 400 or more fails the step; true by default."),
            ],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["status"] = "The response's status code, such as 200.",
                ["body"] = "The response's body as text, cut after 32 KiB.",
            }),
    };
}
