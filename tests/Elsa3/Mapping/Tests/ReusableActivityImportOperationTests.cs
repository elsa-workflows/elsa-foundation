using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Locking.Core;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Endpoints;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Persistence.Groundwork.Services;
using Elsa3.Activities.Design.Import.Services;
using Elsa3.Models;
using Groundwork.Documents.Store;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa3.Mapping.Tests;

public sealed class ReusableActivityImportOperationTests
{
    private static readonly ReusableActivityImportAccessScope Scope = new("tenant-a", "user-a");

    [Fact]
    public async Task Upload_is_bounded_immutable_scoped_cancellable_and_expiring()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-07-17T10:00:00Z"));
        var harness = Harness(clock, maximumBytes: 8_000);
        var payload = Json(ReusableActivityImportFixtures.Workflow(
            "a",
            "a-v1",
            1,
            true,
            ReusableActivityImportFixtures.Leaf("root")));

        var upload = await harness.Service.UploadAsync(payload, payload.Length, Scope);

        Assert.Equal(1, upload.SourceVersionCount);
        Assert.Equal(clock.GetUtcNow().AddHours(1), upload.ExpiresAt);
        var page = await harness.Service.AnalyzeAsync(upload.CollectionHandle, 0, 10, Scope);
        Assert.Single(page.Items);
        await Assert.ThrowsAsync<ReusableActivityImportNotFoundException>(async () =>
            await harness.Service.AnalyzeAsync(upload.CollectionHandle, 0, 10, new("tenant-b", "user-a")));
        await Assert.ThrowsAsync<ReusableActivityImportNotFoundException>(async () =>
            await harness.Service.AnalyzeAsync(upload.CollectionHandle, 0, 10, new("tenant-a", "user-b")));

        clock.Advance(TimeSpan.FromHours(2));
        await Assert.ThrowsAsync<ReusableActivityImportExpiredException>(async () =>
            await harness.Service.AnalyzeAsync(upload.CollectionHandle, 0, 10, Scope));

        var oversized = new MemoryStream(new byte[8_001]);
        await Assert.ThrowsAsync<ReusableActivityImportPayloadException>(async () =>
            await harness.Service.UploadAsync(oversized, oversized.Length, Scope));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await harness.Service.UploadAsync(Json(), null, Scope, cancellation.Token));
    }

    [Fact]
    public async Task Analysis_is_deterministic_paged_side_effect_free_and_exposes_typed_paths_and_complete_cycles()
    {
        var a = ReusableActivityImportFixtures.Workflow(
            "a",
            "a-v1",
            1,
            true,
            ReusableActivityImportFixtures.Reference("a-to-b", targetVersionId: "b-v1"));
        var b = ReusableActivityImportFixtures.Workflow(
            "b",
            "b-v1",
            1,
            true,
            ReusableActivityImportFixtures.Reference("b-to-a", targetVersionId: "a-v1"));
        var missing = ReusableActivityImportFixtures.Workflow(
            "missing",
            "missing-v1",
            1,
            false,
            ReusableActivityImportFixtures.Reference("missing-ref", targetVersionId: "absent-v1"));
        var harness = Harness();
        var upload = await harness.Service.UploadAsync(Json(a, b, missing), null, Scope);
        var savesBefore = harness.Store.SaveCount;

        var first = await harness.Service.AnalyzeAsync(upload.CollectionHandle, 0, 2, Scope);
        var second = await harness.Service.AnalyzeAsync(upload.CollectionHandle, 2, 2, Scope);
        var repeated = await harness.Service.AnalyzeAsync(upload.CollectionHandle, 0, 2, Scope);

        Assert.Equal(first.PlanId, second.PlanId);
        Assert.Equal(first.PlanId, repeated.PlanId);
        Assert.Equal(3, first.Total);
        Assert.Equal(2, first.Processed);
        Assert.Equal(2, first.NextOffset);
        Assert.True(second.IsComplete);
        Assert.Equal(savesBefore, harness.Store.SaveCount);
        var cycle = Assert.Single(first.Diagnostics.Where(x => x.Code == ReusableActivityImportDiagnosticCodes.DependencyCycle));
        Assert.Equal(["a-v1", "b-v1", "a-v1"], cycle.Cycle);
        Assert.Equal(Elsa3MigrationPathSegmentKind.SourceVersion, cycle.PathSegments[0].Kind);
        var missingDiagnostic = Assert.Single(first.Diagnostics.Where(x => x.Code == ReusableActivityImportDiagnosticCodes.MissingReference));
        Assert.Equal(
            [Elsa3MigrationPathSegmentKind.SourceVersion, Elsa3MigrationPathSegmentKind.Node, Elsa3MigrationPathSegmentKind.DependencySourceVersion],
            missingDiagnostic.PathSegments.Select(x => x.Kind));
    }

    [Fact]
    public async Task Closure_expansion_is_authoritative_and_ignores_unrelated_invalid_members()
    {
        var a = ReusableActivityImportFixtures.Workflow("a", "a-v1", 1, true, ReusableActivityImportFixtures.Leaf("a-root"));
        var b = ReusableActivityImportFixtures.Workflow("b", "b-v1", 1, true, ReusableActivityImportFixtures.Reference("b-to-a", targetVersionId: "a-v1"));
        var invalid = ReusableActivityImportFixtures.Workflow("invalid", "invalid-v1", 1, false, ReusableActivityImportFixtures.Reference("missing", targetVersionId: "absent"));
        var harness = Harness();
        var upload = await harness.Service.UploadAsync(Json(a, b, invalid), null, Scope);
        var analysis = await harness.Service.AnalyzeAsync(upload.CollectionHandle, 0, 10, Scope);

        var readiness = await harness.Service.ExpandSelectionAsync(
            upload.CollectionHandle,
            analysis.PlanId,
            ["b-v1"],
            Scope);

        Assert.True(readiness.IsReady);
        Assert.Equal(["a-v1", "b-v1"], readiness.ExpandedSourceVersionIds);
        Assert.Equal(["a-v1"], readiness.AddedDependencySourceVersionIds);
        Assert.DoesNotContain(readiness.Diagnostics, x => x.Metadata.GetValueOrDefault("SourceVersionId") == "invalid-v1");
    }

    [Fact]
    public async Task Apply_is_atomic_exact_idempotent_and_reconciles_lost_responses_with_per_source_navigation()
    {
        var a = ReusableActivityImportFixtures.Workflow("a", "a-v1", 1, true, ReusableActivityImportFixtures.Leaf("a-root"));
        var b = ReusableActivityImportFixtures.Workflow("b", "b-v1", 1, true, ReusableActivityImportFixtures.Reference("b-to-a", targetVersionId: "a-v1"));
        var harness = Harness();
        var upload = await harness.Service.UploadAsync(Json(a, b), null, Scope);
        var analysis = await harness.Service.AnalyzeAsync(upload.CollectionHandle, 0, 10, Scope);

        var applied = await harness.Service.ApplyAsync(
            upload.CollectionHandle,
            analysis.PlanId,
            ["a-v1", "b-v1"],
            "operation-1",
            Scope);
        var retried = await harness.Service.ApplyAsync(
            upload.CollectionHandle,
            analysis.PlanId,
            ["a-v1", "b-v1"],
            "operation-1",
            Scope);
        var recovered = await harness.Service.GetStatusAsync("operation-1", Scope);

        Assert.Equal(ReusableActivityImportReceiptStatus.Applied, applied.Status);
        Assert.Equal(ReusableActivityImportReceiptStatus.AlreadyImported, retried.Status);
        Assert.Equal(applied.ReceiptId, recovered.ReceiptId);
        Assert.Equal(2, applied.Sources.Count);
        Assert.Equal(["a", "b"], applied.Sources.Select(x => x.SourceDefinitionId).Order(StringComparer.Ordinal));
        Assert.Equal(["a-v1", "b-v1"], applied.Sources.Select(x => x.SourceVersionId).Order(StringComparer.Ordinal));
        Assert.All(applied.Sources, source =>
        {
            Assert.Equal(ReusableActivityImportResourceDisposition.Created, source.WorkflowDisposition);
            Assert.StartsWith("/design/workflows/definitions/", source.WorkflowNavigationIdentity, StringComparison.Ordinal);
            Assert.NotNull(source.ActivityDefinitionId);
            Assert.NotNull(source.ActivityVersionNavigationIdentity);
        });
        Assert.Single(harness.Store.Snapshot("elsa3ReusableImportReceipt"));
        Assert.Equal(2, harness.Store.Snapshot("activityDefinition").Count);
        Assert.Equal(2, harness.Store.Snapshot("workflowDefinition").Count);

        await Assert.ThrowsAsync<ReusableActivityImportIdempotencyConflictException>(async () =>
            await harness.Service.ApplyAsync(
                upload.CollectionHandle,
                analysis.PlanId,
                ["a-v1"],
                "operation-1",
                Scope));
        await Assert.ThrowsAsync<ReusableActivityImportNotFoundException>(async () =>
            await harness.Service.GetStatusAsync("operation-1", new("tenant-b", "user-a")));
    }

    [Fact]
    public async Task Stale_plan_and_non_closed_selection_write_no_receipt_or_design_documents()
    {
        var a = ReusableActivityImportFixtures.Workflow("a", "a-v1", 1, true, ReusableActivityImportFixtures.Leaf("a-root"));
        var b = ReusableActivityImportFixtures.Workflow("b", "b-v1", 1, true, ReusableActivityImportFixtures.Reference("b-to-a", targetVersionId: "a-v1"));
        var harness = Harness();
        var upload = await harness.Service.UploadAsync(Json(a, b), null, Scope);
        var analysis = await harness.Service.AnalyzeAsync(upload.CollectionHandle, 0, 10, Scope);

        await Assert.ThrowsAsync<ReusableActivityImportValidationException>(async () =>
            await harness.Service.ApplyAsync(upload.CollectionHandle, "stale-plan", ["a-v1"], "stale", Scope));
        await Assert.ThrowsAsync<ReusableActivityImportValidationException>(async () =>
            await harness.Service.ApplyAsync(upload.CollectionHandle, analysis.PlanId, ["b-v1"], "open", Scope));

        Assert.Empty(harness.Store.Snapshot("elsa3ReusableImportReceipt"));
        Assert.Empty(harness.Store.Snapshot("activityDefinition"));
        Assert.Empty(harness.Store.Snapshot("workflowDefinition"));
    }

    [Fact]
    public async Task Public_bounds_and_invalid_requests_fail_before_mutation()
    {
        var harness = Harness(maximumBytes: 2_000);
        await Assert.ThrowsAsync<ReusableActivityImportPayloadException>(async () =>
            await harness.Service.UploadAsync(new MemoryStream("not-json"u8.ToArray()), null, Scope));
        await Assert.ThrowsAsync<ReusableActivityImportPayloadException>(async () =>
            await harness.Service.UploadAsync(new MemoryStream("[]"u8.ToArray()), null, Scope));
        var countHarness = Harness(maximumBytes: 1024 * 1024);
        await Assert.ThrowsAsync<ReusableActivityImportPayloadException>(async () =>
            await countHarness.Service.UploadAsync(Json(
                Enumerable.Range(0, 101)
                    .Select(x => ReusableActivityImportFixtures.Workflow(
                        $"d-{x}",
                        $"v-{x}",
                        1,
                        false,
                        ReusableActivityImportFixtures.Leaf($"n-{x}")))
                    .ToArray()), null, Scope));

        var valid = ReusableActivityImportFixtures.Workflow("valid", "valid-v1", 1, true, ReusableActivityImportFixtures.Leaf("root"));
        var upload = await harness.Service.UploadAsync(Json(valid), null, Scope);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await harness.Service.AnalyzeAsync(upload.CollectionHandle, -1, 10, Scope));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await harness.Service.AnalyzeAsync(upload.CollectionHandle, 0, 51, Scope));
        var analysis = await harness.Service.AnalyzeAsync(upload.CollectionHandle, 0, 10, Scope);
        var unknown = await harness.Service.ExpandSelectionAsync(upload.CollectionHandle, analysis.PlanId, ["unknown"], Scope);
        var empty = await harness.Service.ExpandSelectionAsync(upload.CollectionHandle, analysis.PlanId, [], Scope);
        Assert.False(unknown.IsReady);
        Assert.False(empty.IsReady);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await harness.Service.ApplyAsync(upload.CollectionHandle, analysis.PlanId, ["valid-v1"], new string('x', 201), Scope));
        await Assert.ThrowsAsync<ReusableActivityImportNotFoundException>(async () =>
            await harness.Service.GetStatusAsync("missing", Scope));
        Assert.Empty(harness.Store.Snapshot("elsa3ReusableImportReceipt"));
        Assert.Empty(harness.Store.Snapshot("activityDefinition"));
    }

    private static HarnessState Harness(
        MutableTimeProvider? clock = null,
        long maximumBytes = 64 * 1024)
    {
        clock ??= new(DateTimeOffset.Parse("2026-07-17T10:00:00Z"));
        var store = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        var operationStore = new GroundworkReusableActivityImportOperationStore(store);
        var command = new GroundworkReusableActivityImportCommand(
            store,
            Serializer(),
            new GroundworkActivityManagementProjectionWriter(store, new ImmediateLockProvider(), store),
            clock);
        var analyzer = new ReusableActivityCollectionAnalyzer();
        var importer = new ReusableActivityCollectionImporter(analyzer, ReusableActivityImportFixtures.Materializer(), command);
        var options = Options.Create(new ReusableActivityImportOptions
        {
            MaximumUploadBytes = maximumBytes,
            MaximumSourceVersions = 100,
            DefaultPageSize = 10,
            MaximumPageSize = 50,
            CollectionLifetime = TimeSpan.FromHours(1)
        });
        return new(
            new ReusableActivityImportOperationService(operationStore, importer, options, clock),
            store);
    }

    private static MemoryStream Json(params Elsa3.Models.Elsa3WorkflowDefinition[] definitions) =>
        new(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(definitions, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

    private static IPayloadSerializer Serializer() => new JsonPayloadSerializer(new JsonPayloadConverterRegistry());

    private sealed record HarnessState(IReusableActivityImportOperationService Service, InMemoryDocumentStore Store);

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }

    private sealed class ImmediateLockProvider : IDistributedLockProvider
    {
        public IDistributedSynchronizationHandle? TryAcquireLock(
            string name,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) => new Handle();

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(
            string name,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDistributedSynchronizationHandle?>(new Handle());

        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(
            string name,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle());

        private sealed class Handle : IDistributedSynchronizationHandle
        {
            public CancellationToken HandleLostToken => CancellationToken.None;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
