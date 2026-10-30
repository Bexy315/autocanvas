using AutoCanvas.Server.Application.Identity;

namespace AutoCanvas.Server.Application.Tests;

public sealed class PasswordTests
{
    private static readonly PasswordHasher Hasher = new(new PasswordHashingOptions { Iterations = 1_000 });

    [Fact]
    public void A_hash_names_algorithm_and_cost_and_verifies_the_password()
    {
        var hash = Hasher.Hash("correct horse battery staple");

        hash.ShouldStartWith("pbkdf2-sha256$1000$");
        hash.Split('$').Length.ShouldBe(4);
        Hasher.Verify("correct horse battery staple", hash).ShouldBe(PasswordVerification.Succeeded);
        Hasher.Verify("correct horse battery stapler", hash).ShouldBe(PasswordVerification.Failed);
    }

    [Fact]
    public void The_same_password_gets_a_different_salt_each_time() =>
        string.Equals(Hasher.Hash("correct horse battery staple"), Hasher.Hash("correct horse battery staple"), StringComparison.Ordinal).ShouldBeFalse();

    [Fact]
    public void Hashes_cheaper_than_the_current_cost_ask_for_a_rehash()
    {
        var old = new PasswordHasher(new PasswordHashingOptions { Iterations = 500 }).Hash("correct horse battery staple");

        Hasher.Verify("correct horse battery staple", old).ShouldBe(PasswordVerification.SucceededRehashNeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain text")]
    [InlineData("bcrypt$10$abc$def")]
    [InlineData("pbkdf2-sha256$x$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2-sha256$0$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2-sha256$1000$not base64$aGFzaA==")]
    [InlineData("pbkdf2-sha256$1000$c2FsdA==$")]
    [InlineData("pbkdf2-sha256$1000$c2FsdA==$aGFzaA==$extra")]
    public void Hashes_AutoCanvas_did_not_write_never_verify(string stored) =>
        Hasher.Verify("correct horse battery staple", stored).ShouldBe(PasswordVerification.Failed);

    [Fact]
    public void Checking_an_unknown_user_does_the_same_work_without_failing() =>
        Should.NotThrow(() => Hasher.VerifyNothing("whatever"));

    [Fact]
    public void Generated_passwords_are_long_random_and_unambiguous()
    {
        var passwords = Enumerable.Range(0, 20).Select(_ => PasswordGenerator.Create()).ToList();

        passwords.ShouldAllBe(p => p.Length == 20 && PasswordPolicy.Check("password", p, "ada") == null);
        passwords.Distinct(StringComparer.Ordinal).Count().ShouldBe(20);
        string.Concat(passwords).ShouldNotContain('0');
        string.Concat(passwords).ShouldNotContain('l');
    }

    [Theory]
    [InlineData(null, "at least 12")]
    [InlineData("short", "at least 12")]
    [InlineData("ADA-LOVELACE", "not be the user name")]
    public void Weak_passwords_are_refused(string? password, string reason) =>
        PasswordPolicy.Check("password", password, "ada-lovelace").ShouldNotBeNull().Message.ShouldContain(reason);

    [Fact]
    public void Overlong_passwords_are_refused() =>
        PasswordPolicy.Check("password", new string('x', 257), "ada").ShouldNotBeNull().Message.ShouldContain("at most 256");

    [Theory]
    [InlineData("ada", true)]
    [InlineData("Ada.Lovelace_1-x", true)]
    [InlineData("9", true)]
    [InlineData("", false)]
    [InlineData(".ada", false)]
    [InlineData("ada lovelace", false)]
    [InlineData("ada@example.org", false)]
    [InlineData("ädä", false)]
    public void User_names_are_simple_identifiers(string userName, bool valid) =>
        (IdentityNames.CheckUserName(userName) is null).ShouldBe(valid);

    [Fact]
    public void User_names_have_at_most_64_characters()
    {
        IdentityNames.CheckUserName(new string('a', 64)).ShouldBeNull();
        IdentityNames.CheckUserName(new string('a', 65)).ShouldNotBeNull();
        IdentityNames.CheckUserName(null).ShouldNotBeNull();
    }

    [Theory]
    [InlineData("Release managers", true)]
    [InlineData("  ", false)]
    [InlineData("tab\there", false)]
    public void Group_names_are_free_text_without_control_characters(string name, bool valid) =>
        (IdentityNames.CheckGroupName(name) is null).ShouldBe(valid);

    [Fact]
    public void Names_compare_trimmed_and_regardless_of_case() =>
        IdentityNames.Normalize(" Ada ").ShouldBe(IdentityNames.Normalize("ada"));
}
