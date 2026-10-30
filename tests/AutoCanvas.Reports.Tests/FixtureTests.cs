namespace AutoCanvas.Reports.Tests;

/// <summary>Real reports from the tools AutoCanvas supports (see Fixtures/README.md).</summary>
public sealed class FixtureTests
{
    [Theory]
    [InlineData(ReportFormat.Trx, "trx/xunit-v3-mtp.trx", 8, 4, 3, 1)]
    [InlineData(ReportFormat.JUnit, "junit/pytest.xml", 7, 4, 2, 1)]
    [InlineData(ReportFormat.JUnit, "junit/jest-junit.xml", 4, 2, 1, 1)]
    [InlineData(ReportFormat.JUnit, "junit/go-junit-report.xml", 6, 4, 1, 1)]
    [InlineData(ReportFormat.JUnit, "junit/maven-surefire.xml", 6, 2, 3, 1)]
    [InlineData(ReportFormat.JUnit, "junit/gradle.xml", 3, 1, 1, 1)]
    public void Fixtures_are_counted_like_their_tools_count_them(ReportFormat format, string file, int total, int passed, int failed, int skipped)
    {
        var report = Fixture.ReadTests(format, file).Succeeds();

        report.Counts.ShouldBe(new TestCounts(total, passed, failed, skipped, report.Counts.Duration));
        report.Cases.Count.ShouldBe(total);
        report.Truncated.ShouldBeFalse();
    }

    [Fact]
    public void TRX_names_class_assembly_and_data_rows()
    {
        var report = Fixture.ReadTests(ReportFormat.Trx, "trx/xunit-v3-mtp.trx").Succeeds();

        var row = report.Case("Classify_works(value: -1, expected: \"positive\")");
        row.Suite.ShouldBe("Sample.Tests.CalculatorTests");
        row.Group.ShouldBe("Sample.Tests");
        row.Outcome.ShouldBe(TestOutcome.Failed);
        row.Message.ShouldNotBeNull().ShouldStartWith("Assert.Equal() Failure: Strings differ");
        row.StackTrace.ShouldNotBeNull().ShouldContain("CalculatorTests.cs:line 24");
        row.FullName.ShouldBe("Sample.Tests.CalculatorTests.Classify_works(value: -1, expected: \"positive\")");
        report.Cases.Count(c => c.Name.StartsWith("Classify_works(", StringComparison.Ordinal)).ShouldBe(3);
    }

    [Fact]
    public void TRX_keeps_output_and_uses_the_skip_reason()
    {
        var report = Fixture.ReadTests(ReportFormat.Trx, "trx/xunit-v3-mtp.trx").Succeeds();

        report.Case("Add_is_broken_on_purpose").Output.ShouldBe("checking 2 + 2");
        var skipped = report.Case("Needs_a_database");
        skipped.Outcome.ShouldBe(TestOutcome.Skipped);
        skipped.Message.ShouldBe("Waits for the database");
        report.Case("Divide_by_zero_throws").Message.ShouldBe("System.DivideByZeroException : Attempted to divide by zero.");
    }

    [Fact]
    public void Pytest_failures_and_errors_both_fail()
    {
        var report = Fixture.ReadTests(ReportFormat.JUnit, "junit/pytest.xml").Succeeds();

        var failure = report.Case("test_tier_is_wrong_on_purpose");
        failure.Suite.ShouldBe("tests.test_shop");
        failure.Group.ShouldBe("pytest");
        failure.Message.ShouldNotBeNull().ShouldStartWith("AssertionError: assert 'silver' == 'gold'");
        failure.StackTrace.ShouldNotBeNull().ShouldContain("tests\\test_shop.py:15: AssertionError");
        report.Case("test_broken_fixture").Outcome.ShouldBe(TestOutcome.Failed);
        report.Case("test_refunds").Message.ShouldNotBeNull().ShouldContain("not implemented yet");
        report.Case("test_tiers[200-gold]").Outcome.ShouldBe(TestOutcome.Passed);
    }

    [Fact]
    public void Jest_failures_without_message_use_the_first_line()
    {
        var failure = Fixture.ReadTests(ReportFormat.JUnit, "junit/jest-junit.xml").Succeeds().Case("labels are wrong on purpose");

        failure.Suite.ShouldBe("cart");
        failure.Message.ShouldBe("Error: expect(received).toBe(expected) // Object.is equality");
        failure.StackTrace.ShouldNotBeNull().ShouldContain("Received: \"2 items\"");
    }

    [Fact]
    public void Go_subtests_and_cdata_are_read()
    {
        var report = Fixture.ReadTests(ReportFormat.JUnit, "junit/go-junit-report.xml").Succeeds();

        report.Case("TestTable/left").Outcome.ShouldBe(TestOutcome.Passed);
        report.Case("TestMaxWrongOnPurpose").StackTrace.ShouldNotBeNull().ShouldContain("expected 4, got 3");
        report.Case("TestSkipped").Message.ShouldBe("Skipped");
        report.Case("TestMax").Group.ShouldBe("example.org/sample");
    }

    [Fact]
    public void Surefire_reruns_and_odd_times_are_understood()
    {
        var report = Fixture.ReadTests(ReportFormat.JUnit, "junit/maven-surefire.xml").Succeeds();

        var flaky = report.Case("talksToPaymentService");
        flaky.Outcome.ShouldBe(TestOutcome.Passed);
        flaky.Message.ShouldBe("Passed after 2 failed attempts.");
        flaky.Duration.ShouldBe(TimeSpan.FromSeconds(1203.25));
        report.Case("refundsAlwaysFail").Outcome.ShouldBe(TestOutcome.Failed);
        var error = report.Case("loadsFromDatabase");
        error.Outcome.ShouldBe(TestOutcome.Failed);
        error.Message.ShouldBe("Connection refused");
        report.Case("labelIsWrongOnPurpose").Output.ShouldBe("label=2");
        report.Case("sumsPrices").Group.ShouldBe("com.example.CartTest");
    }

    [Fact]
    public void Gradle_failures_without_message_attribute_text_are_read()
    {
        var report = Fixture.ReadTests(ReportFormat.JUnit, "junit/gradle.xml").Succeeds();

        report.Case("divides by zero()").Message.ShouldBe("java.lang.ArithmeticException: / by zero");
        report.Case("rounds()").Message.ShouldBeNull();
        report.Counts.Duration.ShouldBe(TimeSpan.FromMilliseconds(11));
    }
}
