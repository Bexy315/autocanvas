using AutoCanvas.Server.Application.Audit;
using Microsoft.Extensions.Time.Testing;

namespace AutoCanvas.Server.Application.Tests;

public sealed class AuditEntryTests
{
    [Theory]
    [InlineData("")]
    [InlineData("created")]
    [InlineData("Folder.Created")]
    [InlineData("folder.")]
    [InlineData(".created")]
    [InlineData("folder created")]
    [InlineData("folder.created!")]
    public void Actions_must_be_dotted_lower_case_names(string action) =>
        Should.Throw<ArgumentException>(() => NewLog().Record(new AuditEntry(action)))
            .Message.ShouldContain("is not an audit action");

    [Fact]
    public void Actions_longer_than_their_column_are_rejected() =>
        Should.Throw<ArgumentException>(() => NewLog().Record(new AuditEntry("a." + new string('b', 100))));

    [Fact]
    public void Every_catalogued_action_is_a_valid_name() =>
        typeof(AuditActions).GetFields()
            .Select(field => (string)field.GetRawConstantValue()!)
            .ShouldAllBe(action => AuditLog.IsAction(action));

    [Fact]
    public void The_system_is_the_default_actor()
    {
        var context = new SystemAuditContext();

        context.Actor.ShouldBe(AuditActor.System);
        context.Actor.Id.ShouldBeNull();
        context.ClientAddress.ShouldBeNull();
        context.UserAgent.ShouldBeNull();
    }

    // Invalid actions are rejected before the database is touched.
    private static AuditLog NewLog() => new(null!, new SystemAuditContext(), new FakeTimeProvider());
}
