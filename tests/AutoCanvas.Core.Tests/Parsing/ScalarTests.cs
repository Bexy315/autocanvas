using AutoCanvas.Core.Parsing;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace AutoCanvas.Core.Tests.Parsing;

public sealed class ScalarTests
{
    [Theory]
    [InlineData("", nameof(ScalarKind.Null))]
    [InlineData("~", nameof(ScalarKind.Null))]
    [InlineData("NULL", nameof(ScalarKind.Null))]
    [InlineData("True", nameof(ScalarKind.Boolean))]
    [InlineData("FALSE", nameof(ScalarKind.Boolean))]
    [InlineData("-12", nameof(ScalarKind.Integer))]
    [InlineData("0o17", nameof(ScalarKind.Integer))]
    [InlineData("0x1F", nameof(ScalarKind.Integer))]
    [InlineData("1.5e3", nameof(ScalarKind.Float))]
    [InlineData(".5", nameof(ScalarKind.Float))]
    [InlineData("-.inf", nameof(ScalarKind.Float))]
    [InlineData(".NaN", nameof(ScalarKind.Float))]
    [InlineData("yes", nameof(ScalarKind.String))]
    [InlineData("1.2.3", nameof(ScalarKind.String))]
    [InlineData("0x", nameof(ScalarKind.String))]
    // The expected kind is passed by name because ScalarKind is internal and test methods are public.
    public void Plain_scalars_follow_the_YAML_1_2_core_schema(string text, string expected) =>
        Scalars.KindOf(new YamlScalarNode(text) { Style = ScalarStyle.Plain }).ToString().ShouldBe(expected);

    [Theory]
    [InlineData(ScalarStyle.SingleQuoted)]
    [InlineData(ScalarStyle.DoubleQuoted)]
    [InlineData(ScalarStyle.Literal)]
    public void Quoted_scalars_are_always_text(ScalarStyle style) =>
        Scalars.KindOf(new YamlScalarNode("42") { Style = style }).ShouldBe(ScalarKind.String);
}
