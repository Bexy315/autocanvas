namespace AutoCanvas.Core.Expressions;

/// <summary>A syntax error in an expression or template.</summary>
/// <param name="Message">What is wrong, for the user.</param>
/// <param name="Offset">Position in the text where the problem starts, from 0.</param>
public sealed record ExpressionError(string Message, int Offset);
