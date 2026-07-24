using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Catalogs;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Records;
using Elsa.Diagnostics.Persistence.Tests.Fixtures;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork;
using Groundwork.DiagnosticRecords;
using Groundwork.Documents.Store;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Elsa.Diagnostics.Persistence.Tests;

/// <summary>
/// Shared real-provider plan evidence for every diagnostic-record and physical catalog route used by
/// the diagnostics adapters. Assertions use the stable plan envelope only, never provider-specific text.
/// Catalog save/delete and capture-ledger load/save deliberately remain outside this suite because the
/// public <see cref="IDocumentStore"/> contract has no native mutation-plan inspector.
/// </summary>
[Collection(DiagnosticsProviderCollection.Name)]
public sealed class DiagnosticsBoundedExecutionTests(DiagnosticsProviderFixture providers)
{
    public static TheoryData<DiagnosticsProviderKind> ProviderKinds =>
        DiagnosticsGroundworkProviderConformanceTests.ProviderKinds;

    [Theory]
    [MemberData(nameof(ProviderKinds))]
    public async Task All_scale_bearing_diagnostic_record_and_catalog_routes_expose_native_bounded_plans(
        DiagnosticsProviderKind providerKind)
    {
        var provider = await providers.CreateIsolatedAsync(providerKind);
        var telemetryBinding = GroundworkOpenTelemetryBinding.Create("tenant-a", "scope-a", "collector-a");
        var structuredBinding = new StructuredLogStoreBinding("tenant-a", "scope-a", "structured-logs");
        await using var telemetry = await DiagnosticsGroundworkProviderHarness.CreateOpenTelemetryAsync(provider, telemetryBinding);
        await using var structured = await DiagnosticsGroundworkProviderHarness.CreateStructuredLogsAsync(provider, structuredBinding);
        var definitions = new[]
        {
            GroundworkStructuredLogStore.CreateStreamDefinition(structuredBinding.StreamId)
        }.Concat(OpenTelemetryGroundworkStorageSchema.CreateStreams(telemetryBinding)).ToArray();
        var deployment = new DiagnosticRecordDeploymentManifest(
            OpenTelemetryGroundworkStorageSchema.CreateDocumentManifest(),
            definitions);
        var records = DiagnosticsGroundworkProviderHarness.CreateDiagnosticRecordPlanInspector(provider);

        foreach (var route in DiagnosticRecordQueries(telemetryBinding, structuredBinding))
        {
            var plan = await records.InspectQueryAsync(deployment, route.Query);
            AssertNativeDiagnosticPlan(records, DiagnosticRecordPlanOperation.Query, route.Name, plan);
        }

        foreach (var route in DiagnosticRecordTrims(telemetryBinding, structuredBinding))
        {
            var plan = await records.InspectTrimSelectionAsync(deployment, route.Request);
            AssertNativeDiagnosticPlan(records, DiagnosticRecordPlanOperation.TrimSelection, route.Name, plan);
        }

        var explainer = Assert.IsAssignableFrom<IPhysicalDocumentQueryExplainer>(telemetry.Stores.DocumentQueries);
        foreach (var route in CatalogQueries())
            await AssertNativeCatalogPlanAsync(explainer, route);
    }

    [Fact]
    public async Task Missing_sqlite_diagnostic_schema_is_rejected_without_materialization()
    {
        var provider = await providers.CreateIsolatedAsync(DiagnosticsProviderKind.Sqlite);
        var binding = GroundworkOpenTelemetryBinding.Create("tenant-a", "scope-a", "collector-a");
        var definition = OpenTelemetryGroundworkStorageSchema.CreateStreams(binding)[0];
        var deployment = new DiagnosticRecordDeploymentManifest(
            OpenTelemetryGroundworkStorageSchema.CreateDocumentManifest(),
            [definition]);
        var inspector = DiagnosticsGroundworkProviderHarness.CreateDiagnosticRecordPlanInspector(provider);
        var query = new DiagnosticRecordQuery(
            new(binding.TenantId, binding.ScopeId),
            new(binding.TraceStreamId),
            Limit: 1,
            Order: new(RecordFields.StartTime, DiagnosticSortDirection.Descending));

        await Assert.ThrowsAsync<DiagnosticRecordDeploymentAdmissionException>(async () =>
            await inspector.InspectQueryAsync(deployment, query));

        await using var connection = new SqliteConnection(provider.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'groundwork_diagnostic_records';";
        Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    private static IEnumerable<(string Name, DiagnosticRecordQuery Query)> DiagnosticRecordQueries(
        GroundworkOpenTelemetryBinding telemetry,
        StructuredLogStoreBinding structured)
    {
        var scope = new DiagnosticStorageScope(telemetry.TenantId, telemetry.ScopeId);
        var time = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
        yield return (
            "structured-recent-filter",
            new(
                new(structured.TenantId, structured.ScopeId),
                new(structured.StreamId),
                Limit: 7,
                Order: DiagnosticRecordOrder.CursorDescending,
                Predicate: new DiagnosticRecordPredicate.All(
                [
                    DiagnosticRecordPredicate.RangeInclusive("level", DiagnosticFieldValue.Int64(2), DiagnosticFieldValue.Int64(4)),
                    DiagnosticRecordPredicate.Equal("categoryKey", DiagnosticFieldValue.String(new string('A', 64))),
                    DiagnosticRecordPredicate.Equal("sourceKey", DiagnosticFieldValue.String(new string('B', 64)))
                ])));
        yield return (
            "structured-replay-scan",
            new(new(structured.TenantId, structured.ScopeId), new(structured.StreamId), Limit: 7, Order: DiagnosticRecordOrder.CursorAscending));
        yield return (
            "structured-tail",
            new(new(structured.TenantId, structured.ScopeId), new(structured.StreamId), Limit: 1, Order: DiagnosticRecordOrder.CursorDescending));
        yield return (
            "structured-replay-anchor",
            new(
                new(structured.TenantId, structured.ScopeId),
                new(structured.StreamId),
                Limit: 1,
                Predicate: DiagnosticRecordPredicate.Equal("replayToken", DiagnosticFieldValue.String("record-token"))));
        yield return (
            "trace-latest-per-id",
            new(scope, new(telemetry.TraceStreamId), Limit: 7,
                Order: new(RecordFields.StartTime, DiagnosticSortDirection.Descending),
                Predicate: new DiagnosticRecordPredicate.All(
                [
                    DiagnosticRecordPredicate.Contains(RecordFields.TraceId, "trace"),
                    DiagnosticRecordPredicate.Equal(RecordFields.ResourceId, DiagnosticFieldValue.String("resource")),
                    DiagnosticRecordPredicate.Contains(RecordFields.WorkflowInstanceId, "workflow"),
                    DiagnosticRecordPredicate.Equal(RecordFields.Status, DiagnosticFieldValue.Int64(1)),
                    DiagnosticRecordPredicate.RangeInclusive(RecordFields.StartTime, DiagnosticFieldValue.Timestamp(time), DiagnosticFieldValue.Timestamp(time.AddMinutes(1))),
                    new DiagnosticRecordPredicate.Any(
                    [
                        DiagnosticRecordPredicate.Contains(RecordFields.TraceId, "trace"),
                        DiagnosticRecordPredicate.Contains(RecordFields.Name, "request")
                    ])
                ]),
                LatestPerKeyField: RecordFields.TraceId));
        yield return (
            "trace-detail-summary",
            new(scope, new(telemetry.TraceStreamId), Limit: 1,
                Order: new(RecordFields.StartTime, DiagnosticSortDirection.Descending),
                Predicate: DiagnosticRecordPredicate.Equal(RecordFields.TraceId, DiagnosticFieldValue.String("trace")),
                LatestPerKeyField: RecordFields.TraceId));
        yield return (
            "span-by-trace",
            new(scope, new(telemetry.SpanStreamId), Limit: 7,
                Order: new(RecordFields.OrderKey, DiagnosticSortDirection.Ascending),
                Predicate: DiagnosticRecordPredicate.Equal(RecordFields.TraceId, DiagnosticFieldValue.String("trace"))));
        yield return (
            "metric-point-by-instrument",
            new(scope, new(telemetry.MetricPointStreamId), Limit: 7,
                Order: new(RecordFields.OrderKey, DiagnosticSortDirection.Descending),
                Predicate: new DiagnosticRecordPredicate.All(
                [
                    DiagnosticRecordPredicate.Equal(RecordFields.ResourceId, DiagnosticFieldValue.String("resource")),
                    DiagnosticRecordPredicate.Contains(RecordFields.InstrumentName, "duration"),
                    DiagnosticRecordPredicate.RangeInclusive(RecordFields.Timestamp, DiagnosticFieldValue.Timestamp(time), DiagnosticFieldValue.Timestamp(time.AddMinutes(1)))
                ])));
        yield return (
            "telemetry-log-filtered-history",
            new(scope, new(telemetry.LogStreamId), Limit: 7,
                Order: new(RecordFields.OrderKey, DiagnosticSortDirection.Descending),
                Predicate: new DiagnosticRecordPredicate.All(
                [
                    DiagnosticRecordPredicate.Equal(RecordFields.ResourceId, DiagnosticFieldValue.String("resource")),
                    DiagnosticRecordPredicate.Contains(RecordFields.TraceId, "trace"),
                    DiagnosticRecordPredicate.Contains(RecordFields.SpanId, "span"),
                    DiagnosticRecordPredicate.Contains(RecordFields.SeverityText, "information"),
                    DiagnosticRecordPredicate.Contains(RecordFields.Body, "request"),
                    DiagnosticRecordPredicate.RangeInclusive(RecordFields.Timestamp, DiagnosticFieldValue.Timestamp(time), DiagnosticFieldValue.Timestamp(time.AddMinutes(1)))
                ])));
        yield return (
            "trace-detail-log-by-trace",
            new(scope, new(telemetry.LogStreamId), Limit: 7,
                Order: new(RecordFields.OrderKey, DiagnosticSortDirection.Ascending),
                Predicate: DiagnosticRecordPredicate.Equal(RecordFields.TraceId, DiagnosticFieldValue.String("trace"))));
    }

    private static IEnumerable<(string Name, DiagnosticTrimRequest Request)> DiagnosticRecordTrims(
        GroundworkOpenTelemetryBinding telemetry,
        StructuredLogStoreBinding structured)
    {
        var issuedAt = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
        foreach (var (name, scope, stream) in new[]
                 {
                     ("structured-log", new DiagnosticStorageScope(structured.TenantId, structured.ScopeId), new DiagnosticStreamId(structured.StreamId)),
                     ("trace", new DiagnosticStorageScope(telemetry.TenantId, telemetry.ScopeId), new DiagnosticStreamId(telemetry.TraceStreamId)),
                     ("span", new DiagnosticStorageScope(telemetry.TenantId, telemetry.ScopeId), new DiagnosticStreamId(telemetry.SpanStreamId)),
                     ("metric-point", new DiagnosticStorageScope(telemetry.TenantId, telemetry.ScopeId), new DiagnosticStreamId(telemetry.MetricPointStreamId)),
                     ("telemetry-log", new DiagnosticStorageScope(telemetry.TenantId, telemetry.ScopeId), new DiagnosticStreamId(telemetry.LogStreamId))
                 })
        {
            yield return (name, DiagnosticTrimRequest.Create(scope, stream, new(issuedAt, $"plan-{name}"), keepNewest: name == "structured-log" ? 0 : 7));
        }
    }

    private static IEnumerable<(string Name, DocumentQuery Query, bool IsCount)> CatalogQueries()
    {
        yield return ("resource-history", new(CatalogDocuments.ResourceKind, OpenTelemetryGroundworkStorageSchema.ResourcesByLastSeenQuery, take: 7), false);
        yield return ("resource-status", new(
            CatalogDocuments.ResourceKind,
            OpenTelemetryGroundworkStorageSchema.ResourcesByStatusQuery,
            [DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                CatalogDocuments.ResourceStatusPath.TrimStart('/'),
                "1"))],
            take: 7), false);
        yield return ("instrument-history", new(CatalogDocuments.InstrumentKind, OpenTelemetryGroundworkStorageSchema.InstrumentsByLastSeenQuery, take: 7), false);
        yield return ("resource-count", new(CatalogDocuments.ResourceKind, OpenTelemetryGroundworkStorageSchema.ResourcesByLastSeenQuery, take: 0), true);
        yield return ("instrument-count", new(CatalogDocuments.InstrumentKind, OpenTelemetryGroundworkStorageSchema.InstrumentsByLastSeenQuery, take: 0), true);
        yield return ("resource-capacity-selection", new(CatalogDocuments.ResourceKind, OpenTelemetryGroundworkStorageSchema.ResourcesByLastSeenQuery, skip: 7, take: 1_000), false);
        yield return ("instrument-capacity-selection", new(CatalogDocuments.InstrumentKind, OpenTelemetryGroundworkStorageSchema.InstrumentsByLastSeenQuery, skip: 7, take: 1_000), false);
    }

    private static void AssertNativeDiagnosticPlan(
        IDiagnosticRecordPlanInspector inspector,
        DiagnosticRecordPlanOperation operation,
        string route,
        DiagnosticRecordNativePlan plan)
    {
        Assert.Equal(inspector.Provider, plan.Provider, ignoreCase: true);
        Assert.Equal(operation, plan.Operation);
        Assert.False(string.IsNullOrWhiteSpace(route));
        Assert.False(string.IsNullOrWhiteSpace(plan.Format));
        Assert.NotEmpty(plan.RawPlans);
        Assert.All(plan.RawPlans, raw => Assert.False(string.IsNullOrWhiteSpace(raw)));
    }

    private static async Task AssertNativeCatalogPlanAsync(
        IPhysicalDocumentQueryExplainer explainer,
        (string Name, DocumentQuery Query, bool IsCount) route)
    {
        var query = route.Query;
        var explanation = await explainer.ExplainAsync(query);

        Assert.Equal(query.DocumentKind, explanation.Plan.StorageUnit.Value);
        Assert.Equal(query.QueryIdentity, explanation.Plan.QueryIdentity);
        Assert.True(explanation.Plan.IsScaleBearing);
        Assert.NotEmpty(explanation.Commands);
        Assert.All(explanation.Commands, command =>
        {
            Assert.False(string.IsNullOrWhiteSpace(command.NativePlanFormat));
            Assert.False(string.IsNullOrWhiteSpace(command.NativePlan));
        });
        if (route.IsCount)
            Assert.Contains(explanation.Commands, command => command.Kind == PhysicalDocumentQueryCommandKind.Count);
        else
            Assert.Contains(explanation.Commands, command => command.ProviderAppliedMaximumRows == query.Take);
    }
}
