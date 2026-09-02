using System.Text.Json;
using Elsa.Groundwork.StorePerformance.AdapterHost;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

public sealed class MatrixCatalogTests
{
    [Fact]
    public void Describes_every_current_workload_without_a_second_hardcoded_runner_catalog()
    {
        var document = MatrixCatalog.Build(SourceProvenance.FindRepositoryRoot());

        Assert.Equal(2, document.SchemaVersion);
        var currentRevision = SourceProvenance.AssemblyRevision(typeof(SourceProvenance).Assembly);
        Assert.Equal(currentRevision, document.Build.AdapterHostRevision);
        Assert.Equal(currentRevision, document.Build.HarnessRevision);
        Assert.Equal(15, document.Registrations.Count);
        Assert.Equal(13, document.Registrations.Select(item => item.WorkloadId).Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(document.Registrations, item => item.WorkloadVersion == "1.0.0");
        Assert.All(document.Registrations, item => Assert.NotEmpty(item.Providers));
        var targets = document.Registrations.SelectMany(item => item.Providers.Select(provider =>
            $"{item.WorkloadId}/{item.Adapter}/{item.PhysicalForm}/{provider}")).ToArray();
        Assert.Equal(targets.Length, targets.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Separates_correctness_capture_and_timing_readiness()
    {
        var registrations = MatrixCatalog.Build(SourceProvenance.FindRepositoryRoot()).Registrations;

        var checkpoint = Single(registrations, "checkpoint-commit", "groundwork-v2");
        Assert.Equal("routeless", checkpoint.CapturePlanStatus);
        Assert.Equal("ready", checkpoint.CorrectnessStatus);
        Assert.Equal("ready", checkpoint.TimingStatus);
        Assert.Equal(["Groundwork.Sqlite"], checkpoint.ProviderPackages["sqlite"]);
        Assert.Equal(["Groundwork.PostgreSql"], checkpoint.ProviderPackages["postgresql"]);

        var bookmark = Single(registrations, "bookmark-lookup", "groundwork-v2");
        Assert.Equal("complete", bookmark.CapturePlanStatus);
        Assert.Equal("ready", bookmark.CorrectnessStatus);
        Assert.Equal("ready", bookmark.TimingStatus);

        foreach (var workload in new[]
                 {
                     "trigger-binding-stimulus-lookup",
                     "recurring-schedule-selection",
                     "due-timer-selection",
                     "placement-takeover",
                     "outbox-drain"
                 })
        {
            var registration = Single(registrations, workload, "groundwork-v2");
            Assert.Equal("complete", registration.CapturePlanStatus);
            Assert.Equal("ready", registration.CorrectnessStatus);
            Assert.Equal("ready", registration.TimingStatus);
        }

        var diagnostics = Single(registrations, "diagnostics-durable-history", DiagnosticsDurableHistoryAdapter.AdapterId);
        Assert.Equal("partial-blocked", diagnostics.CapturePlanStatus);
        Assert.Equal("ready", diagnostics.CorrectnessStatus);
        Assert.Equal("blocked", diagnostics.TimingStatus);
        Assert.Equal("gate.diagnostics.absolute-budget-required", diagnostics.TimingReason);

        var diagnosticsEf = Single(registrations, "diagnostics-durable-history", "ef-diagnostics-oracle");
        Assert.Equal(["sqlite"], diagnosticsEf.Providers);
        Assert.Equal(
            ["Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore.Sqlite"],
            diagnosticsEf.ProviderPackages["sqlite"]);
        Assert.Equal("correctness-only", diagnosticsEf.CapturePlanStatus);
        Assert.Equal("blocked", diagnosticsEf.TimingStatus);
    }

    [Fact]
    public void Serializes_as_a_closed_machine_readable_document()
    {
        var document = MatrixCatalog.Build(SourceProvenance.FindRepositoryRoot());
        var json = JsonSerializer.Serialize(document, ArtifactStore.JsonOptions);
        using var parsed = JsonDocument.Parse(json);

        Assert.Equal(2, parsed.RootElement.GetProperty("SchemaVersion").GetInt32());
        Assert.Equal(
            SourceProvenance.AssemblyRevision(typeof(SourceProvenance).Assembly),
            parsed.RootElement.GetProperty("Build").GetProperty("AdapterHostRevision").GetString());
        Assert.Equal(15, parsed.RootElement.GetProperty("Registrations").GetArrayLength());
    }

    private static MatrixRegistrationDocument Single(
        IReadOnlyList<MatrixRegistrationDocument> registrations,
        string workload,
        string adapter) =>
        Assert.Single(registrations, item =>
            string.Equals(item.WorkloadId, workload, StringComparison.Ordinal) &&
            string.Equals(item.Adapter, adapter, StringComparison.Ordinal));
}
