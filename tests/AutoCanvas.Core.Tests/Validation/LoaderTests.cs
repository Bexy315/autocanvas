using AutoCanvas.Core.Diagnostics;

namespace AutoCanvas.Core.Tests.Validation;

public sealed class LoaderTests
{
    [Fact]
    public void A_valid_file_loads()
    {
        var result = AutomationLoader.Load("version: 1\njobs:\n  a: { steps: [ { run: a } ] }\n");

        result.Succeeded.ShouldBeTrue();
        result.Diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void Validation_problems_fail_the_load_and_keep_positions()
    {
        var result = AutomationLoader.Load("version: 1\njobs:\n  a: { needs: [b], steps: [ { run: a } ] }\n");

        result.Succeeded.ShouldBeFalse();
        var problem = result.Diagnostics.ShouldHaveSingleItem();
        problem.Code.ShouldBe(DiagnosticCodes.UnknownJob);
        problem.Location.ShouldNotBeNull();
    }

    [Fact]
    public void Validation_is_skipped_when_parsing_fails() =>
        AutomationLoader.Load("version: 1\njobs:\n  a: { needs: [b], steps: [] }\n").Diagnostics
            .ShouldHaveSingleItem().Code.ShouldBe(DiagnosticCodes.WrongType);
}
