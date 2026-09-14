using Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.Tests.Support;
using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.Tests;

[Collection(PostgresContainerCollection.Name)]
public sealed class SecretsPackageFeedProbeTests(PostgresContainerFixture fixture)
{
    [SkippableFact]
    public async Task Packed_module_composes_in_a_clean_child_process_and_survives_validation_restart()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();

        await using var package = await SecretsPackageFeed.CreateAsync();
        var autoMigrate = await SecretsPackageFeedProbeRunner.RunAsync(
            package.ModuleAssemblyPath,
            connectionString,
            "AutoMigrate");
        AssertProbeSucceeded(autoMigrate);

        var validate = await SecretsPackageFeedProbeRunner.RunAsync(
            package.ModuleAssemblyPath,
            connectionString,
            "Validate");
        AssertProbeSucceeded(validate);

        AssertNoPackageLoadFailures(autoMigrate);
        AssertNoPackageLoadFailures(validate);
    }

    private static void AssertProbeSucceeded(ProbeProcessResult result)
    {
        Assert.True(result.ExitCode == 0, result.Describe());
        using var json = System.Text.Json.JsonDocument.Parse(result.StandardOutput.Trim());
        Assert.Equal("ok", json.RootElement.GetProperty("status").GetString());
    }

    private static void AssertNoPackageLoadFailures(ProbeProcessResult result)
    {
        var logs = result.StandardOutput + Environment.NewLine + result.StandardError;
        Assert.DoesNotContain("FileLoadException", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("TypeLoadException", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("missing provider", logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("duplicate EF", logs, StringComparison.OrdinalIgnoreCase);
    }
}
