using AutoCanvas.Core.Secrets;

namespace AutoCanvas.Core.Tests.Secrets;

public sealed class SecretRedactorTests
{
    [Theory]
    [InlineData("token=s3cr3t-value", "token=***")]
    [InlineData("s3cr3t-value and s3cr3t-value", "*** and ***")]
    [InlineData("nothing secret here", "nothing secret here")]
    [InlineData("S3CR3T-VALUE differs in case", "S3CR3T-VALUE differs in case")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Known_values_are_replaced_wherever_they_appear(string? text, string expected) =>
        new SecretRedactor(["s3cr3t-value"]).Redact(text).ShouldBe(expected);

    [Fact]
    public void A_secret_containing_another_is_replaced_whole()
    {
        var redactor = new SecretRedactor(["abcd", "abcdefgh"]);

        redactor.Redact("x=abcdefgh y=abcd").ShouldBe("x=*** y=***");
    }

    [Fact]
    public void Values_too_short_to_matter_are_left_alone()
    {
        var redactor = new SecretRedactor(["abc", null, string.Empty]);

        redactor.IsEmpty.ShouldBeTrue();
        redactor.Redact("abc abc").ShouldBe("abc abc");
    }

    [Fact]
    public void Duplicates_do_not_matter()
    {
        var redactor = new SecretRedactor(["pass-word", "pass-word"]);

        redactor.IsEmpty.ShouldBeFalse();
        redactor.Redact("pass-word").ShouldBe(SecretRedactor.Marker);
    }
}
