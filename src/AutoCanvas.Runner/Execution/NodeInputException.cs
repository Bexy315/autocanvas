namespace AutoCanvas.Runner.Execution;

/// <summary>A node input that cannot be used as given; the step fails with the message.</summary>
internal sealed class NodeInputException : Exception
{
    public NodeInputException()
    {
    }

    public NodeInputException(string message)
        : base(message)
    {
    }

    public NodeInputException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
