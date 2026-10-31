using System.Text;
using AutoCanvas.Server.Application.Variables;

namespace AutoCanvas.Server.Application.Tests;

public sealed class SecretCipherTests
{
    private static readonly SecretCipher Cipher = new(MasterKey.Generate());

    [Fact]
    public void Values_decrypt_only_for_their_scope_name_and_key()
    {
        var sealedValue = Cipher.Encrypt("folder:1", "TOKEN", "s3cr3t ✓");

        Cipher.Decrypt("folder:1", "TOKEN", sealedValue).ShouldBe("s3cr3t ✓");
        Cipher.Decrypt("folder:2", "TOKEN", sealedValue).ShouldBeNull();
        Cipher.Decrypt("folder:1", "OTHER", sealedValue).ShouldBeNull();
        new SecretCipher(MasterKey.Generate()).Decrypt("folder:1", "TOKEN", sealedValue).ShouldBeNull();
    }

    [Fact]
    public void Ciphertexts_reveal_nothing_and_differ_every_time()
    {
        var first = Cipher.Encrypt("instance", "TOKEN", "s3cr3t-value");
        var second = Cipher.Encrypt("instance", "TOKEN", "s3cr3t-value");

        first.ShouldNotBe(second, StringComparer.Ordinal);
        Encoding.UTF8.GetString(Convert.FromBase64String(first)).ShouldNotContain("s3cr3t");
    }

    [Theory]
    [InlineData("not base64!")]
    [InlineData("AAAA")]
    [InlineData("")]
    public void Damaged_ciphertexts_do_not_decrypt(string ciphertext) =>
        Cipher.Decrypt("instance", "TOKEN", ciphertext).ShouldBeNull();

    [Fact]
    public void A_changed_byte_is_noticed()
    {
        var bytes = Convert.FromBase64String(Cipher.Encrypt("instance", "TOKEN", "s3cr3t-value"));
        bytes[^1] ^= 1;

        Cipher.Decrypt("instance", "TOKEN", Convert.ToBase64String(bytes)).ShouldBeNull();
    }

    [Theory]
    [InlineData(null, "must be 32 bytes")]
    [InlineData("%%%", "not valid base64")]
    [InlineData("AAEC", "must be 32 bytes")]
    public void Keys_must_be_32_bytes_of_base64(string? text, string problem)
    {
        MasterKey.Parse(text, out var message).ShouldBeNull();

        message.ShouldNotBeNull().ShouldContain(problem);
    }

    [Fact]
    public void Keys_have_a_stable_id_that_does_not_reveal_them()
    {
        var key = MasterKey.Generate();
        var again = MasterKey.Parse(key.Text, out _).ShouldNotBeNull();

        again.Id.ShouldBe(key.Id);
        key.Id.Length.ShouldBe(8);
        MasterKey.Generate().Id.ShouldNotBe(key.Id, StringComparer.Ordinal);
    }
}
