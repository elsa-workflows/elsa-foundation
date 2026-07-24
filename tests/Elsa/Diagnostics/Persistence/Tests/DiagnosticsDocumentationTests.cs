using System.Runtime.CompilerServices;
using Xunit;

namespace Elsa.Diagnostics.Persistence.Tests;

public sealed class DiagnosticsDocumentationTests
{
    [Fact]
    public void Owning_extension_point_catalog_declares_public_seam_semantics_and_helper_boundary()
    {
        var catalogPath = RepositoryPath("src/Elsa/Diagnostics/Persistence/EXTENSION_POINTS.md");

        Assert.True(File.Exists(catalogPath), $"Missing owning catalog: {catalogPath}");
        var catalog = File.ReadAllText(catalogPath);
        Assert.Contains("IDiagnosticsDrainTarget", catalog, StringComparison.Ordinal);
        Assert.Contains("IDiagnosticsPersistenceObserver", catalog, StringComparison.Ordinal);
        Assert.Contains("Bridge", catalog, StringComparison.Ordinal);
        Assert.Contains("Replacement", catalog, StringComparison.Ordinal);
        Assert.Contains("conflict", catalog, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("helper boundary", catalog, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DiagnosticsPersistenceObserverRegistrationValidator", catalog, StringComparison.Ordinal);
        Assert.Contains("constitution-mandated", catalog, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("first-party implementation", catalog, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("internal Options adapter", catalog, StringComparison.Ordinal);
        Assert.Contains("not an adapter extension contract", catalog, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("## Overridable contracts", catalog, StringComparison.Ordinal);
        Assert.Contains("## Implementable contributor interfaces", catalog, StringComparison.Ordinal);
        Assert.Contains("## Events", catalog, StringComparison.Ordinal);
        Assert.True(
            catalog.Contains("*(Core —", StringComparison.Ordinal) ||
            catalog.Contains("*(Feature contract —", StringComparison.Ordinal));
    }

    [Fact]
    public void Root_extension_point_index_links_the_diagnostics_persistence_owner()
    {
        var rootIndex = File.ReadAllText(RepositoryPath("EXTENSION_POINTS.md"));

        Assert.Contains(
            "src/Elsa/Diagnostics/Persistence/EXTENSION_POINTS.md",
            rootIndex,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Replacement_contract_kind_is_declared_on_the_contracts_themselves()
    {
        var drainTarget = File.ReadAllText(RepositoryPath(
            "src/Elsa/Diagnostics/Persistence/Draining/DiagnosticsDrainContracts.cs"));
        var observer = File.ReadAllText(RepositoryPath(
            "src/Elsa/Diagnostics/Persistence/Observability/DiagnosticsPersistenceObservability.cs"));

        Assert.Contains("Replacement contract", drainTarget, StringComparison.Ordinal);
        Assert.Contains("Replacement contract", observer, StringComparison.Ordinal);
    }

    [Fact]
    public void Ef_oracle_inventory_names_exact_source_and_test_evidence_for_every_required_area()
    {
        var inventory = File.ReadAllText(RepositoryPath("specs/139-groundwork-diagnostics-persistence/oracle-inventory.md"));
        var requiredEvidence = new[]
        {
            "src/Elsa/Diagnostics/StructuredLogs/Persistence/EFCore/Storage/EfCoreStructuredLogStore.cs",
            "tests/Elsa/Diagnostics/StructuredLogs/Persistence/Tests/EfCoreStructuredLogStoreTests.cs",
            "tests/Elsa/Diagnostics/StructuredLogs/Persistence/Tests/EfCoreStructuredLogStorePruneRetryTests.cs",
            "src/Elsa/Diagnostics/StructuredLogs/Persistence/EFCore/EFCoreStructuredLogsPersistenceFeatureBase.cs",
            "tests/Elsa/Diagnostics/StructuredLogs/Persistence/Tests/SqliteStructuredLogsPersistenceFeatureTests.cs",
            "src/Elsa/Diagnostics/StructuredLogs/Persistence/EFCore/Sqlite/Migrations/20260618004216_Initial.cs",
            "src/Elsa/Diagnostics/OpenTelemetry/Persistence/EFCore/Storage/EfCoreOpenTelemetryStore.cs",
            "tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Tests/EfCoreOpenTelemetryStoreTests.cs",
            "src/Elsa/Diagnostics/OpenTelemetry/Persistence/EFCore/EFCoreOpenTelemetryPersistenceFeatureBase.cs",
            "tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Tests/SqliteOpenTelemetryPersistenceFeatureTests.cs",
            "src/Elsa/Diagnostics/OpenTelemetry/Persistence/EFCore/Sqlite/Migrations/20260623005000_Initial.cs",
            "src/Elsa/Persistence/EFCore/Storage/ChannelDrainingStoreBase.cs",
            "tests/Elsa/Diagnostics/StructuredLogs/Persistence/Tests/EfCoreStructuredLogStoreResilienceTests.cs",
            "tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Tests/EfCoreOpenTelemetryStoreResilienceTests.cs",
            "src/Elsa/Diagnostics/StructuredLogs/Persistence/EFCore/Tasks/StartStructuredLogDrainingStartupTask.cs",
            "src/Elsa/Diagnostics/StructuredLogs/Persistence/EFCore/Tasks/StopStructuredLogDrainingShellTerminator.cs",
            "src/Elsa/Diagnostics/OpenTelemetry/Persistence/EFCore/Tasks/StartOpenTelemetryDrainingStartupTask.cs",
            "src/Elsa/Diagnostics/OpenTelemetry/Persistence/EFCore/Tasks/StopOpenTelemetryDrainingShellTerminator.cs",
            "src/Elsa/Diagnostics/StructuredLogs/Live/InMemoryStructuredLogLiveFeed.cs",
            "src/Elsa/Diagnostics/OpenTelemetry/Providers/InMemory/InMemoryOpenTelemetryLiveFeed.cs"
        };

        Assert.All(requiredEvidence, path => Assert.Contains($"`{path}`", inventory, StringComparison.Ordinal));
    }

    private static string RepositoryPath(string relativePath, [CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "../../../../..", relativePath));
}
