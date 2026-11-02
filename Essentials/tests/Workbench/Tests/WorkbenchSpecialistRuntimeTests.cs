using Xunit;

namespace Elsa.Workbench.Tests;

/// <summary>
/// #3525 T032 on the stock Workbench: user-defined agents can be test-run and published, so the stock Development shell
/// composes the specialist runtime (the runtime consumer publication preflight requires), the background workers that
/// run model turns, and the durable turn journal the runtime's settlement requires.
/// </summary>
public sealed class WorkbenchSpecialistRuntimeTests
{
    [Fact]
    public async Task Stock_development_shell_runs_the_specialist_runtime_with_a_durable_journal()
    {
        await using var workbench = await WorkbenchProcess.StartAsync(WorkbenchShell.Development);

        var catalog = (await workbench.ReadFeatureCatalogAsync()).ToDictionary(feature => feature.Id, StringComparer.Ordinal);
        foreach (var feature in new[] { "SpecialistsDesign", "SpecialistsRuntime", "BackgroundActivities", "SpecialistsEntityFrameworkCore" })
            Assert.True(catalog.TryGetValue(feature, out var entry) && entry.Runs, $"{feature} does not run. Host output:{Environment.NewLine}{workbench.Output}");
        // The development-only journal loses turns on restart; the stock shell must not use it.
        Assert.False(catalog.TryGetValue("SpecialistsInMemoryJournal", out var inMemory) && inMemory.Runs, "The stock shell runs the in-memory specialist journal.");
    }
}
