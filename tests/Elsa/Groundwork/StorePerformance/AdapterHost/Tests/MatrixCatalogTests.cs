using Elsa.Groundwork.StorePerformance.AdapterHost;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using System.Text.Json;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

public sealed class MatrixCatalogTests
{
    [Fact]
    public void Describes_every_current_workload_without_a_second_hardcoded_runner_catalog()
    {
        var document = MatrixCatalog.Build(SourceProvenance.FindRepositoryRoot());

        Assert.Equal(3, document.SchemaVersion);
        var currentRevision = SourceProvenance.AssemblyRevision(typeof(SourceProvenance).Assembly);
        Assert.Equal(currentRevision, document.Build.AdapterHostRevision);
        Assert.Equal(currentRevision, document.Build.HarnessRevision);
        Assert.Equal(17, document.Registrations.Count);
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
            Assert.Equal("ready", registration.MeasurementStatus);
            Assert.Equal("ready", registration.TimingStatus);
        }

        foreach (var workload in new[] { "queue-drain", "command-send-lease-ack" })
        {
            var relational = Single(registrations, workload, "groundwork-v2", "sqlite");
            Assert.Equal(["sqlite", "sqlserver", "postgresql"], relational.Providers);
            Assert.Equal("complete", relational.CapturePlanStatus);
            Assert.Equal("ready", relational.CorrectnessStatus);
            Assert.Equal("ready", relational.MeasurementStatus);
            Assert.Equal("ready", relational.TimingStatus);

            var mongo = Single(registrations, workload, "groundwork-v2", "mongodb");
            Assert.Equal(["mongodb"], mongo.Providers);
            Assert.Equal("correctness-ready-native-plan-blocked", mongo.CapturePlanStatus);
            Assert.Equal(BenchmarkAdapterRegistry.MongoRuntimeNativePlanBlockedReason, mongo.CapturePlanReason);
            Assert.Equal("ready", mongo.CorrectnessStatus);
            Assert.Equal("correctness.ready", mongo.CorrectnessReason);
            Assert.Equal("blocked", mongo.MeasurementStatus);
            Assert.Equal("blocked", mongo.TimingStatus);
            Assert.Equal(BenchmarkAdapterRegistry.MongoRuntimeNativePlanBlockedReason, mongo.TimingReason);
        }

        var diagnostics = Single(registrations, "diagnostics-durable-history", DiagnosticsDurableHistoryAdapter.AdapterId);
        Assert.Equal("partial-blocked", diagnostics.CapturePlanStatus);
        Assert.Equal("ready", diagnostics.CorrectnessStatus);
        Assert.Equal("ungraded", diagnostics.MeasurementStatus);
        Assert.Equal(DiagnosticsAdmission.UngradedMeasurementReasonCode, diagnostics.MeasurementReason);
        Assert.Equal("blocked", diagnostics.TimingStatus);
        Assert.Equal(ReproducibleWorkloadScenarioCatalog.DiagnosticsBlockedReasonCode, diagnostics.TimingReason);

        var diagnosticsEf = Single(registrations, "diagnostics-durable-history", "ef-diagnostics-oracle");
        Assert.Equal(["sqlite"], diagnosticsEf.Providers);
        Assert.Equal(
            ["Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore.Sqlite"],
            diagnosticsEf.ProviderPackages["sqlite"]);
        Assert.Equal("correctness-only", diagnosticsEf.CapturePlanStatus);
        Assert.Equal("blocked", diagnosticsEf.MeasurementStatus);
        Assert.Equal(DiagnosticsAdmission.EfCorrectnessOnlyMeasurementReasonCode, diagnosticsEf.MeasurementReason);
        Assert.Equal("blocked", diagnosticsEf.TimingStatus);
    }

    [Fact]
    public void Serializes_as_a_closed_machine_readable_document()
    {
        var document = MatrixCatalog.Build(SourceProvenance.FindRepositoryRoot());
        var json = JsonSerializer.Serialize(document, ArtifactStore.JsonOptions);
        using var parsed = JsonDocument.Parse(json);

        Assert.Equal(3, parsed.RootElement.GetProperty("SchemaVersion").GetInt32());
        Assert.Equal(
            SourceProvenance.AssemblyRevision(typeof(SourceProvenance).Assembly),
            parsed.RootElement.GetProperty("Build").GetProperty("AdapterHostRevision").GetString());
        Assert.Equal(17, parsed.RootElement.GetProperty("Registrations").GetArrayLength());
    }

    private static MatrixRegistrationDocument Single(
        IReadOnlyList<MatrixRegistrationDocument> registrations,
        string workload,
        string adapter,
        string? provider = null) =>
        Assert.Single(registrations, item =>
            string.Equals(item.WorkloadId, workload, StringComparison.Ordinal) &&
            string.Equals(item.Adapter, adapter, StringComparison.Ordinal) &&
            (provider is null || item.Providers.Contains(provider, StringComparer.Ordinal)));
}
