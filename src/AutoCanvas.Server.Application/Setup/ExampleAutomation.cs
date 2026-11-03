namespace AutoCanvas.Server.Application.Setup;

/// <summary>
/// The automation the setup offers to create: one job on the bundled runner that greets and writes
/// a small test and coverage report, so the first run shows a log, tests and coverage.
/// </summary>
public static class ExampleAutomation
{
    /// <summary>The folder it is created in.</summary>
    public const string FolderName = "Getting started";

    /// <summary>What the folder says about itself.</summary>
    public const string FolderDescription = "Created by the setup, with an example to start a first run from.";

    /// <summary>The automation's name.</summary>
    public const string Name = "hello";

    /// <summary>The automation file.</summary>
    public const string Yaml = """
        # Created by the setup. Start it with Run, then look at its log, tests and coverage.
        version: 1
        name: Hello
        description: A first run on the bundled runner, with a small test and coverage report.

        triggers:
          manual: true

        jobs:
          hello:
            runs_on: linux
            image: alpine:3.22
            steps:
              - name: Greet
                run: echo "Hello from AutoCanvas on $(uname -m)"
              - name: Write a test and a coverage report
                run: |-
                  cat > TEST-hello.xml <<'EOF'
                  <testsuite name="hello" tests="3" skipped="1">
                    <testcase classname="Hello.Greeting" name="says_hello" time="0.004"/>
                    <testcase classname="Hello.Greeting" name="says_goodbye" time="0.003"/>
                    <testcase classname="Hello.Greeting" name="speaks_klingon" time="0"><skipped message="Not yet"/></testcase>
                  </testsuite>
                  EOF
                  cat > coverage.xml <<'EOF'
                  <coverage line-rate="0.8" branch-rate="0" lines-covered="4" lines-valid="5" version="1" timestamp="0">
                    <packages><package name="Hello"><classes>
                      <class name="Hello.Greeting" filename="Greeting.cs" line-rate="0.8"><lines>
                        <line number="1" hits="3"/><line number="2" hits="3"/><line number="3" hits="1"/><line number="4" hits="0"/><line number="5" hits="2"/>
                      </lines></class>
                    </classes></package></packages>
                  </coverage>
                  EOF
                  echo "Wrote a test and a coverage report"
            reports:
              - format: junit
                path: TEST-hello.xml
              - format: cobertura
                path: coverage.xml
        """;
}
