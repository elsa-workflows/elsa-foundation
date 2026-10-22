using Xunit;

namespace Elsa.Primitives.Tests.Spec263Probe;

// THROWAWAY (spec 263 validation, never merged): exercises the shard runner's retry, quarantine and slow-test report.
public class Spec263ProbeTests
{
    // Fails on its first run in a CI job and passes on the retry, which runs on the same runner.
    [Fact]
    public void Fails_once_then_passes()
    {
        var marker = Path.Combine(Path.GetTempPath(), $"spec263-flip-{Environment.GetEnvironmentVariable("GITHUB_RUN_ID")}-{Environment.GetEnvironmentVariable("GITHUB_JOB")}");
        if (File.Exists(marker))
            return;
        File.WriteAllText(marker, "first run");
        Assert.Fail("Spec 263 probe: deliberate first-run failure; the retry must pass.");
    }

    // Always fails; quarantined in tools/ci/test-tiers.json, so it must be reported without failing the shard.
    [Fact]
    public void Quarantined_always_fails() => Assert.Fail("Spec 263 probe: quarantined failure.");

    // Probe B: always fails and is not quarantined, so its retry fails too and the run must fail, naming it.
    [Fact]
    public void Fails_twice() => Assert.Fail("Spec 263 probe B: deliberate double failure.");

    // Takes 12 seconds; a PR-tier test over 10 s must be reported, not failed.
    [Fact]
    public async Task Takes_twelve_seconds() => await Task.Delay(TimeSpan.FromSeconds(12));
}
