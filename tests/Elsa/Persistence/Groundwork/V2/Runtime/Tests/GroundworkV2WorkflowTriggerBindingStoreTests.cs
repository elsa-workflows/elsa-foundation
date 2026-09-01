using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Kernel;
using Groundwork.MongoDb;
using Groundwork.PostgreSql;
using Groundwork.Query.Model;
using Groundwork.Sqlite;
using Groundwork.SqlServer;
using Groundwork.Store;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Persistence.Groundwork.V2.Runtime.Tests;

public sealed class GroundworkV2WorkflowTriggerBindingStoreTests
{
    public static TheoryData<string> Providers => new() { "sqlite", "postgresql", "sqlserver", "mongodb" };

    [Fact]
    public void The_v2_store_implements_the_public_trigger_binding_contract()
    {
        Assert.Contains(
            typeof(IWorkflowTriggerBindingStore),
            typeof(GroundworkV2WorkflowTriggerBindingStore).GetInterfaces());
    }

    [Fact]
    public async Task Sqlite_round_trips_pages_and_publication_lifecycle_without_serving_prepared_rows()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var bindingUnit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowTriggerBindingDocumentKind);
        var stateUnit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.PublicationProjectionStateDocumentKind);
        connection.Schema.Apply(bindingUnit);
        connection.Schema.Apply(stateUnit);

        var source = new DirectSessionSource(connection);
        var accessor = new TestAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IWorkflowTriggerBindingStore store = new GroundworkV2WorkflowTriggerBindingStore(source, accessor);
        var first = Binding("pub-a", "artifact-a", "node-a", "orders", "h1");
        var second = Binding("pub-a", "artifact-a", "node-b", "orders", "h2");
        var replacement = Binding("pub-b", "artifact-b", "node-a", "orders", "h1");

        await store.PrepareActivationAsync("pub-a", [first, second]);
        var prepared = await store.ListByActivationAsync(
            new WorkflowTriggerBindingActivationPageQuery("pub-a"));
        Assert.Equal(new[] { first.TriggerBindingId, second.TriggerBindingId }.Order(StringComparer.Ordinal),
            prepared.Items.Select(binding => binding.TriggerBindingId));
        Assert.All(prepared.Items, binding => Assert.False(binding.IsActive));
        Assert.Empty((await store.ListByStimulusAsync(new WorkflowTriggerBindingPageQuery("Event", "h1"))).Items);

        await store.ActivateAsync("pub-a", null);
        Assert.Single((await store.ListByStimulusAsync(new WorkflowTriggerBindingPageQuery("Event", "h1"))).Items);
        var typePage = await store.ListByStimulusTypeAsync(
            new WorkflowTriggerBindingTypePageQuery("Event", limit: 1));
        Assert.Equal(2, typePage.TotalCount);
        Assert.NotNull(typePage.NextContinuationToken);
        var typeSecond = await store.ListByStimulusTypeAsync(
            new WorkflowTriggerBindingTypePageQuery("Event", limit: 1, typePage.NextContinuationToken));
        Assert.Equal(2, typeSecond.TotalCount);
        Assert.Single(typeSecond.Items);

        await store.PrepareActivationAsync("pub-b", [replacement]);
        await store.ActivateAsync("pub-b", "pub-a");
        Assert.Empty((await store.ListByStimulusAsync(new WorkflowTriggerBindingPageQuery("Event", "h2"))).Items);
        Assert.Single((await store.ListByStimulusAsync(new WorkflowTriggerBindingPageQuery("Event", "h1"))).Items);
        Assert.False((await store.ListByActivationAsync(
            new WorkflowTriggerBindingActivationPageQuery("pub-a"))).Items.Single(binding => binding.StimulusHash == "h1").IsActive);

        await store.DeleteByActivationAsync("pub-b");
        Assert.Empty((await store.ListByActivationAsync(
            new WorkflowTriggerBindingActivationPageQuery("pub-b"))).Items);
    }

    [Fact]
    public async Task Artifact_delete_removes_prepared_and_active_bindings_and_keeps_other_artifacts()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var bindingUnit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowTriggerBindingDocumentKind);
        var stateUnit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.PublicationProjectionStateDocumentKind);
        connection.Schema.Apply(bindingUnit);
        connection.Schema.Apply(stateUnit);
        var source = new DirectSessionSource(connection);
        var accessor = new TestAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IWorkflowTriggerBindingStore store = new GroundworkV2WorkflowTriggerBindingStore(source, accessor);
        var oldOne = Binding("pub-old", "artifact-old", "node-a", "orders", "old-1");
        var oldTwo = Binding("pub-old", "artifact-old", "node-b", "orders", "old-2");
        var pending = Binding("pub-pending", "artifact-old", "node-c", "orders", "pending");
        var retained = Binding("pub-retained", "artifact-retained", "node-a", "orders", "keep");

        await store.PrepareActivationAsync("pub-old", [oldOne, oldTwo]);
        await store.ActivateAsync("pub-old", null);
        await store.PrepareActivationAsync("pub-pending", [pending]);
        await store.SaveAsync(retained);

        Assert.Equal(3, await store.DeleteByArtifactAsync("artifact-old"));
        Assert.Empty((await store.ListByArtifactAsync(
            new WorkflowTriggerBindingArtifactPageQuery("artifact-old"))).Items);
        Assert.Single((await store.ListByArtifactAsync(
            new WorkflowTriggerBindingArtifactPageQuery("artifact-retained"))).Items);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ActivateAsync("pub-old", null).AsTask());

        await store.PrepareActivationAsync("pub-old", [oldOne]);
        await store.ActivateAsync("pub-old", null);
        Assert.Single((await store.ListByStimulusAsync(
            new WorkflowTriggerBindingPageQuery("Event", "old-1"))).Items);

        var splitFirst = Binding("pub-split", "artifact-split", "node-a", "orders", "split-a");
        var splitSecond = Binding("pub-split", "artifact-other", "node-b", "orders", "split-b");
        await store.PrepareActivationAsync("pub-split", [splitFirst, splitSecond]);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.DeleteByArtifactAsync("artifact-split").AsTask());
        Assert.Equal(2, (await store.ListByActivationAsync(
            new WorkflowTriggerBindingActivationPageQuery("pub-split"))).Items.Count);
    }

    [Fact]
    public async Task Queries_use_exact_lookup_projections_and_scope_isolation()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var bindingUnit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowTriggerBindingDocumentKind);
        var stateUnit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.PublicationProjectionStateDocumentKind);
        connection.Schema.Apply(bindingUnit);
        connection.Schema.Apply(stateUnit);
        var source = new DirectSessionSource(connection);
        var storeA = new GroundworkV2WorkflowTriggerBindingStore(
            source,
            new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        var storeB = new GroundworkV2WorkflowTriggerBindingStore(
            source,
            new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b"))));
        var binding = Binding(null, "artifact-a", "node-a", "orders", "h1");

        await storeA.SaveAsync(binding);
        Assert.Single((await storeA.ListByStimulusAsync(new WorkflowTriggerBindingPageQuery("Event", "h1"))).Items);
        Assert.Empty((await storeB.ListByStimulusAsync(new WorkflowTriggerBindingPageQuery("Event", "h1"))).Items);

        var requests = new List<QueryRequest>();
        var recording = new RecordingSessionSource(connection, requests);
        var recordedStore = new GroundworkV2WorkflowTriggerBindingStore(
            recording,
            new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        await recordedStore.ListByStimulusAsync(new WorkflowTriggerBindingPageQuery("Event", "h1", 7));
        var request = Assert.Single(requests);
        Assert.Equal(7, request.Paging.Limit);
        Assert.Equal(
            [ElsaRuntimeV2StorageManifest.TriggerBindingIdField],
            request.Order.Select(term => term.Column.Name));
        var conjunction = Assert.IsType<Predicate.And>(request.Where);
        Assert.Contains(conjunction.Terms, term =>
            term is Predicate.Equal equality &&
            equality.Column.Name == ElsaRuntimeV2StorageManifest.StimulusLookupKeyField);
        Assert.Contains(conjunction.Terms, term =>
            term is Predicate.Equal equality &&
            equality.Column.Name == ElsaRuntimeV2StorageManifest.WorkflowTriggerBindingIsActiveField);
    }

    [Fact]
    public async Task Prepare_replaces_existing_rows_and_never_overwrites_a_prepared_projection()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        connection.Schema.Apply(ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowTriggerBindingDocumentKind));
        connection.Schema.Apply(ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.PublicationProjectionStateDocumentKind));
        var source = new DirectSessionSource(connection);
        var accessor = new TestAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IWorkflowTriggerBindingStore store = new GroundworkV2WorkflowTriggerBindingStore(source, accessor);
        var existing = Binding("pub-replace", "artifact-replace", "node-a", "orders", "existing");
        var replacement = Binding("pub-replace", "artifact-replace", "node-b", "orders", "replacement");

        await store.SaveAsync(existing);
        await store.PrepareActivationAsync("pub-replace", [replacement]);
        var replacedItems = (await store.ListByActivationAsync(
            new WorkflowTriggerBindingActivationPageQuery("pub-replace"))).Items;
        Assert.Equal([replacement.TriggerBindingId], replacedItems.Select(binding => binding.TriggerBindingId));

        var first = Binding("pub-race", "artifact-race", "node-a", "orders", "first");
        var conflicting = Binding("pub-race", "artifact-race", "node-b", "orders", "second");
        await store.PrepareActivationAsync("pub-race", [first]);
        await store.PrepareActivationAsync("pub-race", [first]);
        var conflictError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.PrepareActivationAsync("pub-race", [conflicting]).AsTask());
        Assert.Contains("already prepared", conflictError.Message, StringComparison.Ordinal);
        Assert.Equal(first.TriggerBindingId,
            Assert.Single((await store.ListByActivationAsync(
                new WorkflowTriggerBindingActivationPageQuery("pub-race"))).Items).TriggerBindingId);
    }

    [Fact]
    public async Task Invalid_cardinality_is_refused_before_a_row_is_written()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        connection.Schema.Apply(ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowTriggerBindingDocumentKind));
        connection.Schema.Apply(ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.PublicationProjectionStateDocumentKind));
        var store = new GroundworkV2WorkflowTriggerBindingStore(
            new DirectSessionSource(connection),
            new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        var malformed = Binding(null, "artifact-invalid", "node-invalid", "orders", "invalid") with
        {
            Cardinality = (TriggerCardinality)999
        };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SaveAsync(malformed).AsTask());
        Assert.Empty((await store.ListByArtifactAsync(
            new WorkflowTriggerBindingArtifactPageQuery("artifact-invalid"))).Items);
    }

    [Fact]
    public async Task Activation_refuses_a_projection_state_when_rows_are_missing()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var bindingUnit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowTriggerBindingDocumentKind);
        var stateUnit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.PublicationProjectionStateDocumentKind);
        connection.Schema.Apply(bindingUnit);
        connection.Schema.Apply(stateUnit);
        var source = new DirectSessionSource(connection);
        var store = new GroundworkV2WorkflowTriggerBindingStore(
            source,
            new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        var binding = Binding("pub-missing-row", "artifact-missing-row", "node-a", "orders", "missing-row");

        await store.PrepareActivationAsync("pub-missing-row", [binding]);
        var rawSession = source.Open(
            bindingUnit.Id.Value,
            StorageAccess.Scoped(new StorageScope("tenant-a")));
        Assert.Equal(
            WriteOutcomeStatus.Deleted,
            rawSession.Delete(GroundworkRuntimeRowStore.Key(binding.TriggerBindingId)).Status);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.ActivateAsync("pub-missing-row", null).AsTask());
    }

    [Fact]
    public async Task Concurrent_prepares_converge_for_equal_projections_and_refuse_conflicting_projections()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connectionA = runtime.OpenConnection(sharedMemory: true);
        using var connectionB = runtime.OpenConnection(sharedMemory: true);
        connectionA.Schema.Apply(ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowTriggerBindingDocumentKind));
        connectionA.Schema.Apply(ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.PublicationProjectionStateDocumentKind));

        var equalA = Binding("pub-concurrent-equal", "artifact-equal", "node-a", "orders", "equal");
        var equalB = Binding("pub-concurrent-equal", "artifact-equal", "node-a", "orders", "equal");
        var equalOutcomes = await RunConcurrentPreparesAsync(
            connectionA,
            connectionB,
            "pub-concurrent-equal",
            equalA,
            equalB);

        Assert.All(equalOutcomes, Assert.Null);
        var inspectionSource = new DirectSessionSource(connectionA);
        var inspectionStore = new GroundworkV2WorkflowTriggerBindingStore(
            inspectionSource,
            new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        var equalItems = (await inspectionStore.ListByActivationAsync(
            new WorkflowTriggerBindingActivationPageQuery("pub-concurrent-equal"))).Items;
        var equalItem = Assert.Single(equalItems);
        Assert.Equal(equalA.TriggerBindingId, equalItem.TriggerBindingId);
        Assert.False(equalItem.IsActive);

        var conflictingA = Binding("pub-concurrent-conflict", "artifact-conflict", "node-a", "orders", "left");
        var conflictingB = Binding("pub-concurrent-conflict", "artifact-conflict", "node-b", "orders", "right");
        var conflictingOutcomes = await RunConcurrentPreparesAsync(
            connectionA,
            connectionB,
            "pub-concurrent-conflict",
            conflictingA,
            conflictingB);

        Assert.Equal(1, conflictingOutcomes.Count(outcome => outcome is null));
        Assert.Equal(1, conflictingOutcomes.Count(outcome => outcome is not null));
        var conflictingItems = (await inspectionStore.ListByActivationAsync(
            new WorkflowTriggerBindingActivationPageQuery("pub-concurrent-conflict"))).Items;
        var conflictingItem = Assert.Single(conflictingItems);
        Assert.Contains(
            conflictingItem.TriggerBindingId,
            new[] { conflictingA.TriggerBindingId, conflictingB.TriggerBindingId });
        Assert.False(conflictingItem.IsActive);

        var activationBinding = Binding("pub-concurrent-activate", "artifact-concurrent-activate", "node-a", "orders", "activate");
        await inspectionStore.PrepareActivationAsync("pub-concurrent-activate", [activationBinding]);
        var activationOutcomes = await RunConcurrentOperationsAsync(
            connectionA,
            connectionB,
            store => store.ActivateAsync("pub-concurrent-activate", null),
            store => store.ActivateAsync("pub-concurrent-activate", null));
        Assert.InRange(activationOutcomes.Count(outcome => outcome is null), 1, 2);
        var activatedItem = Assert.Single((await inspectionStore.ListByActivationAsync(
            new WorkflowTriggerBindingActivationPageQuery("pub-concurrent-activate"))).Items);
        Assert.True(activatedItem.IsActive);

        var deleteRaceBinding = Binding("pub-concurrent-delete", "artifact-concurrent-delete", "node-a", "orders", "delete");
        await inspectionStore.PrepareActivationAsync("pub-concurrent-delete", [deleteRaceBinding]);
        var deleteRaceOutcomes = await RunConcurrentOperationsAsync(
            connectionA,
            connectionB,
            store => store.ActivateAsync("pub-concurrent-delete", null),
            store => store.DeleteByActivationAsync("pub-concurrent-delete"));
        Assert.InRange(deleteRaceOutcomes.Count(outcome => outcome is null), 1, 2);
        var deleteRaceItems = (await inspectionStore.ListByActivationAsync(
            new WorkflowTriggerBindingActivationPageQuery("pub-concurrent-delete"))).Items;
        if (deleteRaceItems.Count == 0)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                inspectionStore.ActivateAsync("pub-concurrent-delete", null).AsTask());
        }
        else
        {
            Assert.Single(deleteRaceItems);
            Assert.True(deleteRaceItems[0].IsActive);
            await inspectionStore.ActivateAsync("pub-concurrent-delete", null);
        }
    }

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Configured_native_provider_round_trips_the_public_lookup_contract(string providerName)
    {
        var sqlitePath = providerName == "sqlite"
            ? Path.Combine(Path.GetTempPath(), $"elsa-trigger-binding-matrix-{Guid.NewGuid():N}.db")
            : null;
        var connectionString = providerName == "sqlite"
            ? $"Data Source={sqlitePath}"
            : Environment.GetEnvironmentVariable($"GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING");
        Skip.If(string.IsNullOrWhiteSpace(connectionString),
            $"Set GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING to run the {providerName} provider proof.");

        try
        {
            using var connection = CreateConnection(providerName, connectionString!);
            var runId = Guid.NewGuid().ToString("N")[..8];
            var bindingUnit = PhysicalUnit(
                ElsaRuntimeV2StorageManifest.WorkflowTriggerBindingDocumentKind,
                $"gwv2_{runId}_trigger");
            var stateUnit = PhysicalUnit(
                ElsaRuntimeV2StorageManifest.PublicationProjectionStateDocumentKind,
                $"gwv2_{runId}_projection");
            connection.Schema.Apply(bindingUnit);
            connection.Schema.Apply(stateUnit);
            var units = new Dictionary<string, StorageUnit>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.WorkflowTriggerBindingDocumentKind] = bindingUnit,
                [ElsaRuntimeV2StorageManifest.PublicationProjectionStateDocumentKind] = stateUnit
            };
            var source = new MappedSessionSource(connection, units);
            IWorkflowTriggerBindingStore store = new GroundworkV2WorkflowTriggerBindingStore(
                source,
                new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope($"matrix-{providerName}"))));
            var first = Binding("pub-matrix", "artifact-matrix", "node-a", "orders", "matrix-hash-a");
            var second = Binding("pub-matrix", "artifact-matrix", "node-b", "orders", "matrix-hash-b");
            var replacementFirst = Binding("pub-replacement", "artifact-replacement", "node-a", "orders", "replacement-hash-a");
            var replacementSecond = Binding("pub-replacement", "artifact-replacement", "node-b", "orders", "replacement-hash-b");

            await store.PrepareActivationAsync("pub-matrix", [first, second]);
            await store.ActivateAsync("pub-matrix", null);
            await store.PrepareActivationAsync("pub-replacement", [replacementFirst, replacementSecond]);
            await store.ActivateAsync("pub-replacement", "pub-matrix");

            Assert.Empty((await store.ListByStimulusAsync(
                new WorkflowTriggerBindingPageQuery("Event", "matrix-hash-a"))).Items);

            var firstPage = await store.ListByArtifactAsync(
                new WorkflowTriggerBindingArtifactPageQuery("artifact-replacement", limit: 1));
            Assert.Equal(2, firstPage.TotalCount);
            Assert.Single(firstPage.Items);
            Assert.NotNull(firstPage.NextContinuationToken);

            var secondPage = await store.ListByArtifactAsync(
                new WorkflowTriggerBindingArtifactPageQuery(
                    "artifact-replacement",
                    limit: 1,
                    firstPage.NextContinuationToken));
            Assert.Equal(2, secondPage.TotalCount);
            Assert.Single(secondPage.Items);
            Assert.NotEqual(firstPage.Items[0].TriggerBindingId, secondPage.Items[0].TriggerBindingId);

            await store.DeleteByActivationAsync("pub-replacement");
            Assert.Empty((await store.ListByActivationAsync(
                new WorkflowTriggerBindingActivationPageQuery("pub-replacement"))).Items);
        }
        finally
        {
            if (sqlitePath is not null)
            {
                foreach (var path in new[] { sqlitePath, $"{sqlitePath}-shm", $"{sqlitePath}-wal" })
                    if (File.Exists(path))
                        File.Delete(path);
            }
        }
    }

    private static WorkflowTriggerBinding Binding(
        string? activationId,
        string artifactId,
        string nodeId,
        string definitionId,
        string stimulusHash) =>
        new(
            WorkflowTriggerBinding.BuildId(activationId ?? "unpublished", artifactId, nodeId, stimulusHash),
            artifactId,
            definitionId,
            "1.0.0",
            $"hash-{artifactId}",
            nodeId,
            "Event",
            stimulusHash,
            null,
            new Dictionary<string, string> { ["source"] = "v2-test" },
            new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero),
            activationId,
            activationId is null ? null : "slot-a",
            TriggerCardinality.FanOut,
            true);

    private sealed class TestAccessContextAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private static async Task<Exception?[]> RunConcurrentPreparesAsync(
        IStorageProviderConnection connectionA,
        IStorageProviderConnection connectionB,
        string activationId,
        WorkflowTriggerBinding bindingA,
        WorkflowTriggerBinding bindingB)
        => await RunConcurrentOperationsAsync(
            connectionA,
            connectionB,
            store => store.PrepareActivationAsync(activationId, [bindingA]),
            store => store.PrepareActivationAsync(activationId, [bindingB]));

    private static async Task<Exception?[]> RunConcurrentOperationsAsync(
        IStorageProviderConnection connectionA,
        IStorageProviderConnection connectionB,
        Func<IWorkflowTriggerBindingStore, ValueTask> operationA,
        Func<IWorkflowTriggerBindingStore, ValueTask> operationB)
    {
        using var gate = new ManualResetEventSlim(false);
        using var ready = new CountdownEvent(2);
        var sourceA = new GatedSessionSource(connectionA, gate, ready);
        var sourceB = new GatedSessionSource(connectionB, gate, ready);
        var storeA = new GroundworkV2WorkflowTriggerBindingStore(
            sourceA,
            new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        var storeB = new GroundworkV2WorkflowTriggerBindingStore(
            sourceB,
            new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        var taskA = Task.Run(() => CaptureAsync(() => operationA(storeA)));
        var taskB = Task.Run(() => CaptureAsync(() => operationB(storeB)));

        try
        {
            Assert.True(ready.Wait(TimeSpan.FromSeconds(10)), "Both prepare operations must reach the start gate.");
            gate.Set();
            return await Task.WhenAll(taskA, taskB);
        }
        finally
        {
            gate.Set();
        }
    }

    private static async Task<Exception?> CaptureAsync(Func<ValueTask> operation)
    {
        try
        {
            await operation();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private sealed class DirectSessionSource(IStorageProviderConnection connection) :
        IGroundworkStorageSessionSource,
        IGroundworkStorageCapabilitySource
    {
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
            connection.OpenSession(ElsaRuntimeV2StorageManifest.Require(unitId), access);

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) =>
            connection.BeginUnitOfWork(
                access,
                options,
                unitIds.Select(ElsaRuntimeV2StorageManifest.Require).ToArray());

        public StorageUnit Unit(string unitId, string? targetName = null) => ElsaRuntimeV2StorageManifest.Require(unitId);

        public IReadOnlyList<CapabilityDescriptor> Capabilities(string? targetName = null) => connection.Capabilities;
    }

    private sealed class GatedSessionSource(
        IStorageProviderConnection connection,
        ManualResetEventSlim gate,
        CountdownEvent ready) :
        IGroundworkStorageSessionSource,
        IGroundworkStorageCapabilitySource
    {
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
            connection.OpenSession(ElsaRuntimeV2StorageManifest.Require(unitId), access);

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null)
        {
            ready.Signal();
            gate.Wait();
            return connection.BeginUnitOfWork(
                access,
                options,
                unitIds.Select(ElsaRuntimeV2StorageManifest.Require).ToArray());
        }

        public StorageUnit Unit(string unitId, string? targetName = null) => ElsaRuntimeV2StorageManifest.Require(unitId);

        public IReadOnlyList<CapabilityDescriptor> Capabilities(string? targetName = null) => connection.Capabilities;
    }

    private sealed class RecordingSessionSource(
        IStorageProviderConnection connection,
        ICollection<QueryRequest> requests) :
        IGroundworkStorageSessionSource,
        IGroundworkStorageCapabilitySource
    {
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
            new RecordingSession(connection.OpenSession(ElsaRuntimeV2StorageManifest.Require(unitId), access), requests);

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) =>
            connection.BeginUnitOfWork(
                access,
                options,
                unitIds.Select(ElsaRuntimeV2StorageManifest.Require).ToArray());

        public StorageUnit Unit(string unitId, string? targetName = null) => ElsaRuntimeV2StorageManifest.Require(unitId);

        public IReadOnlyList<CapabilityDescriptor> Capabilities(string? targetName = null) => connection.Capabilities;
    }

    private sealed class RecordingSession(IStorageSession inner, ICollection<QueryRequest> requests) : SynchronousStorageSessionTestDouble, IStorageSession
    {
        public StorageUnit Unit => inner.Unit;
        public StorageAccess Access => inner.Access;
        public StoredEntry? Read(StorageKey key) => inner.Read(key);
        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null)
        {
            requests.Add(request);
            return inner.Query(request, options);
        }
        public AggregationResult Aggregate(AggregationQuery query) => inner.Aggregate(query);
        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => inner.Insert(values, options);
        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => inner.Update(values, options);
        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => inner.Upsert(values, options);
        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => inner.Delete(key, options);
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => inner.Append(operationId, values);
    }

    private sealed class MappedSessionSource(
        IStorageProviderConnection connection,
        IReadOnlyDictionary<string, StorageUnit> units) :
        IGroundworkStorageSessionSource,
        IGroundworkStorageCapabilitySource
    {
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
            connection.OpenSession(Resolve(unitId), access);

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) =>
            connection.BeginUnitOfWork(access, options, unitIds.Select(Resolve).ToArray());

        public StorageUnit Unit(string unitId, string? targetName = null) => Resolve(unitId);

        public IReadOnlyList<CapabilityDescriptor> Capabilities(string? targetName = null) => connection.Capabilities;

        private StorageUnit Resolve(string unitId) =>
            units.TryGetValue(unitId, out var unit)
                ? unit
                : units.Values.Single(candidate => StringComparer.Ordinal.Equals(candidate.Id.Value, unitId));
    }

    private sealed class NativeProviderRuntime(string path) : IAsyncDisposable
    {
        private readonly string path = path;
        private readonly string sharedMemoryName = $"elsa-trigger-binding-{Guid.NewGuid():N}";

        public static NativeProviderRuntime Create() =>
            new(Path.Combine(Path.GetTempPath(), $"elsa-runtime-trigger-binding-{Guid.NewGuid():N}.db"));

        public IStorageProviderConnection OpenConnection(bool sharedMemory = false) =>
            new SqliteProviderFactory().Create(sharedMemory
                ? $"Data Source={sharedMemoryName};Mode=Memory;Cache=Shared"
                : $"Data Source={path}");

        public ValueTask DisposeAsync()
        {
            foreach (var candidate in new[] { path, $"{path}-shm", $"{path}-wal" })
                if (File.Exists(candidate))
                    File.Delete(candidate);
            return ValueTask.CompletedTask;
        }
    }

    private static StorageUnit PhysicalUnit(string unitId, string physicalName) =>
        ElsaRuntimeV2StorageManifest.Require(unitId) with
        {
            Id = new StorageUnitId(physicalName),
            Name = physicalName
        };

    private static IStorageProviderConnection CreateConnection(string providerName, string connectionString) =>
        providerName switch
        {
            "sqlite" => new SqliteProviderFactory().Create(connectionString),
            "postgresql" => new PostgreSqlProviderFactory().Create(connectionString),
            "sqlserver" => new SqlServerProviderFactory().Create(connectionString),
            "mongodb" => new MongoProviderFactory().Create(connectionString),
            _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
        };
}
