namespace AutoCanvas.Core.Expressions;

/// <summary>Literal text of a template, copied as is.</summary>
/// <param name="Text">The text.</param>
public sealed record TextPart(string Text) : TemplatePart;
