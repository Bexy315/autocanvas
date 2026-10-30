using System.Globalization;
using System.Text;

namespace AutoCanvas.Benchmarks;

/// <summary>
/// Generates a large but realistic automation: <c>jobs</c> jobs in layers of ten, each needing the
/// previous layer's first job, with ten steps that use expressions, services and outputs.
/// </summary>
internal static class SampleAutomation
{
    public static string Yaml(int jobs)
    {
        var yaml = new StringBuilder("""
            version: 1
            name: Benchmark
            triggers:
              manual: true
              push: { branches: [main] }
            variables: { TAG: '${{ run.commit_short_sha }}' }
            jobs:

            """);

        for (var i = 0; i < jobs; i++)
        {
            AppendJob(yaml, i);
        }

        return yaml.ToString();
    }

    private static void AppendJob(StringBuilder yaml, int index)
    {
        var culture = CultureInfo.InvariantCulture;
        yaml.Append(culture, $"  job-{index}:\n");
        if (index >= 10)
        {
            var need = (index / 10 * 10) - 10;
            yaml.Append(culture, $"    needs: [job-{need}]\n");
            yaml.Append(culture, $"    if: jobs.job-{need}.outputs.ok == 'true' && trigger.branch == 'main'\n");
        }

        yaml.Append("""
                runs_on: [linux, docker]
                image: mcr.microsoft.com/dotnet/sdk:10.0
                services: { db: { preset: postgres, version: 17 } }
                env: { MODE: '${{ vars.MODE }}', TOKEN: '${{ secrets.TOKEN }}' }
                steps:

            """);
        for (var s = 0; s < 10; s++)
        {
            yaml.Append(culture, $"      - id: s{s}\n        run: echo ${{{{ vars.TAG }}}} step {s} ${{{{ trigger.branch }}}}\n");
        }

        yaml.Append("    outputs: { ok: '${{ steps.s9.outputs.ok }}' }\n");
    }
}
