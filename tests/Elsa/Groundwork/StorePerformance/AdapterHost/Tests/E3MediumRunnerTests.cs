using System.Diagnostics;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

public sealed class E3MediumRunnerTests
{
    [Fact]
    public async Task Runner_fails_closed_and_names_every_missing_provider_plan()
    {
        var root = SourceProvenance.FindRepositoryRoot();
        var evidence = Directory.CreateTempSubdirectory("elsa-e3-evidence");
        var output = Directory.CreateTempSubdirectory("elsa-e3-output");
        try
        {
            var start = new ProcessStartInfo("python3")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add(Path.Combine(root, "tools/groundwork/run-e3-medium-baseline.py"));
            start.ArgumentList.Add("--provider");
            start.ArgumentList.Add("sqlite");
            start.ArgumentList.Add("--evidence-dir");
            start.ArgumentList.Add(evidence.FullName);
            start.ArgumentList.Add("--out");
            start.ArgumentList.Add(output.FullName);
            using var process = Process.Start(start)!;
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.Equal(2, process.ExitCode);
            Assert.Empty(stdout);
            Assert.Contains("checkpoint-commit.sqlite.native-plan.json", stderr, StringComparison.Ordinal);
            Assert.Contains("bookmark-lookup.sqlite.native-plan.json", stderr, StringComparison.Ordinal);
            Assert.Contains("queue-drain.sqlite.native-plan.json", stderr, StringComparison.Ordinal);
            Assert.Contains("outbox-drain.sqlite.native-plan.json", stderr, StringComparison.Ordinal);
            Assert.Contains("will not synthesize", stderr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            evidence.Delete(recursive: true);
            output.Delete(recursive: true);
        }
    }
}
