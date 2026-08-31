using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Primitives.Entities;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;
using Groundwork.Testing;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

/// <summary>Provider-neutral public-v2 fixture for the preserved workflow-design behavior cases.</summary>
internal sealed class DesignGroundworkTestPersistence : IGroundworkStorageSessionSource, IGroundworkStorageCapabilitySource, IDisposable
{
    private readonly IReadOnlyDictionary<string, StorageUnit> units =
        WorkflowsDesignStorageManifest.CreateUnits().ToDictionary(unit => unit.Id.Value, StringComparer.Ordinal);
    private readonly IStorageProviderConnection connection;
    private readonly Dictionary<(string UnitId, string Id), StorageValues> committed = [];
    private readonly object gate = new();

    public int BeginCount { get; private set; }
    public int SaveCount { get; private set; }
    public int LoadCount { get; private set; }
    public int DeleteCount { get; private set; }
    public List<StorageAccess> OpenedAccesses { get; } = [];
    public bool RecordQueries { get; set; }
    public List<RecordedQuery> Queries { get; } = [];

    public DesignGroundworkTestPersistence()
    {
        connection = new InMemoryProviderFactory().Create($"workflow-design-tests:{Guid.NewGuid():N}");
        foreach (var unit in units.Values)
            connection.Schema.Apply(unit);
    }

    public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
    {
        LoadCount++;
        OpenedAccesses.Add(access);
        var session = connection.OpenSession(Unit(unitId, targetName), access);
        return RecordQueries ? new RecordingStorageSession(session, Queries) : session;
    }

    public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null)
    {
        BeginCount++;
        return new TrackingUnitOfWork(
            connection.BeginUnitOfWork(access, options, unitIds.Select(unitId => Unit(unitId, targetName)).ToArray()),
            this);
    }

    public StorageUnit Unit(string unitId, string? targetName = null) => units[unitId];

    public IReadOnlyList<CapabilityDescriptor> Capabilities(string? targetName = null) => connection.Capabilities;

    public void SeedDefinition(WorkflowDefinition definition)
    {
        EnsureTimestamps(definition);
        Insert(WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind, definition.TenantId!,
            GroundworkDesignStorage.Values(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                definition,
                GroundworkDesignDocumentSerialization.Create(new FakePayloadSerializer()),
                WorkflowsDesignStorageManifest.WorkflowDefinitionCollection));
    }

    public void SeedVersion(WorkflowDefinitionVersion version)
    {
        EnsureTimestamps(version);
        Insert(WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind, version.TenantId!,
            GroundworkDesignStorage.Values(
                WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
                version,
                GroundworkDesignDocumentSerialization.Create(new FakePayloadSerializer()),
                WorkflowsDesignStorageManifest.WorkflowDefinitionVersionCollection));
    }

    public void SeedDraft(WorkflowDefinitionDraft draft, IReadOnlyCollection<DesignMetadataRecord>? layout = null,
        IReadOnlyCollection<ActivityPresentationRecord>? presentation = null)
    {
        EnsureTimestamps(draft);
        Insert(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind, draft.TenantId!,
            GroundworkDesignStorage.Values(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
                draft,
                GroundworkDesignDocumentSerialization.Create(new FakePayloadSerializer()),
                WorkflowsDesignStorageManifest.WorkflowDefinitionDraftCollection,
                layout ?? [],
                presentation ?? []));
    }

    public void SeedLayout(WorkflowDefinitionVersionLayout layout)
    {
        EnsureTimestamps(layout);
        Insert(WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind, layout.TenantId!,
            GroundworkDesignStorage.Values(
                WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind,
                layout,
                GroundworkDesignDocumentSerialization.Create(new FakePayloadSerializer()),
                WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutCollection));
    }

    public void DeleteRaw(string unitId, string id, string tenant = DesignGroundworkTestAccess.DefaultScopeValue)
    {
        connection.OpenSession(Unit(unitId), StorageAccess.Scoped(new StorageScope(tenant)))
            .Delete(GroundworkDesignStorage.Key(id), WriteOptions.Unconditional);
        lock (gate)
            committed.Remove((unitId, id));
    }

    public void InsertRaw(string unitId, StorageValues values, string tenant = DesignGroundworkTestAccess.DefaultScopeValue) =>
        Insert(unitId, tenant, values);

    public IReadOnlyCollection<StorageValues> Snapshot(string unitId)
    {
        lock (gate)
            return committed.Where(pair => pair.Key.UnitId == unitId).Select(pair => pair.Value).ToArray();
    }

    private void Insert(string unitId, string tenant, StorageValues values)
    {
        var outcome = connection.OpenSession(Unit(unitId), StorageAccess.Scoped(new StorageScope(tenant)))
            .Insert(values, WriteOptions.Unconditional);
        if (!outcome.Succeeded)
            throw new InvalidOperationException($"Failed to seed {unitId}: {outcome.Status}");
        lock (gate)
            committed[(unitId, values.Values[WorkflowsDesignStorageManifest.IdField]!.ToString()!)] = values;
    }

    private void Apply(IReadOnlyList<RowWrite> writes)
    {
        lock (gate)
        {
            foreach (var write in writes)
            {
                var id = write.Values?.Values.TryGetValue(WorkflowsDesignStorageManifest.IdField, out var value) == true
                    ? value?.ToString()
                    : write.Key?.Values.TryGetValue(WorkflowsDesignStorageManifest.IdField, out var keyValue) == true
                        ? keyValue?.ToString()
                        : null;
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                var key = (write.Unit.Id.Value, id);
                if (write.Mode == RowWriteMode.Delete)
                {
                    committed.Remove(key);
                    DeleteCount++;
                }
                else if (write.Values is not null)
                    committed[key] = write.Values;
            }
        }
    }

    private static void EnsureTimestamps(Elsa.Primitives.Entities.Entity entity)
    {
        var now = DateTimeOffset.UtcNow;
        if (entity.CreatedAt == default)
            entity.CreatedAt = now;
        if (entity.LastModifiedAt == default)
            entity.LastModifiedAt = entity.CreatedAt;
        if (entity is TenantEntity tenant && tenant.TenantId is null)
            tenant.TenantId = DesignGroundworkTestAccess.DefaultScopeValue;
    }

    private sealed class TrackingUnitOfWork(IUnitOfWork inner, DesignGroundworkTestPersistence owner) : IUnitOfWork
    {
        private readonly List<RowWrite> staged = [];
        public IStorageSession OpenSession(StorageUnit unit) => inner.OpenSession(unit);
        public void Stage(RowWrite write) { staged.Add(write); inner.Stage(write); }
        public BatchWriteSummary Commit() { var result = inner.Commit(); owner.SaveCount++; owner.Apply(staged); return result; }
        public BatchWriteReport CommitWithOutcomes() { var result = inner.CommitWithOutcomes(); if (result.IsSuccessful) { owner.SaveCount++; owner.Apply(staged); } return result; }
        public async ValueTask<BatchWriteReport> CommitWithOutcomesAsync(CancellationToken cancellationToken = default) { var result = await inner.CommitWithOutcomesAsync(cancellationToken); if (result.IsSuccessful) { owner.SaveCount++; owner.Apply(staged); } return result; }
        public async ValueTask<BatchWriteSummary> CommitAsync(CancellationToken cancellationToken = default) { var result = await inner.CommitAsync(cancellationToken); owner.SaveCount++; owner.Apply(staged); return result; }
        public void Rollback() => inner.Rollback();
        public void Dispose() => inner.Dispose();
    }

    public void Dispose() => connection.Dispose();

    public sealed record RecordedQuery(QueryRequest Request, QueryRenderOptions? Options)
    {
        public string? IndexName => Options?.SelectedIndex;
    }

    private sealed class RecordingStorageSession(IStorageSession inner, ICollection<RecordedQuery> queries) : SynchronousStorageSessionTestDouble, IStorageSession
    {
        public StorageUnit Unit => inner.Unit;
        public StorageAccess Access => inner.Access;
        public StoredEntry? Read(StorageKey key) => inner.Read(key);
        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null)
        {
            queries.Add(new RecordedQuery(request, options));
            return inner.Query(request, options);
        }
        public AggregationResult Aggregate(AggregationQuery query) => inner.Aggregate(query);
        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => inner.Insert(values, options);
        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => inner.Update(values, options);
        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => inner.Upsert(values, options);
        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => inner.Delete(key, options);
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => inner.Append(operationId, values);
    }
}

internal static class DesignGroundworkTestAccess
{
    public const string DefaultScopeValue = "default";
    public static IPersistenceAccessContextAccessor DefaultAccessContextAccessor { get; } = AccessContext(DefaultScopeValue);
    public static IPersistenceAccessContextAccessor AccessContext(string scope) =>
        new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(scope)));

    public static MutableAccessContextAccessor Mutable(PersistenceAccessContext current) =>
        new(current);

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    internal sealed class MutableAccessContextAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; set; } = current;
    }
}

// Existing behavior fixtures use this name; it now resolves only to the public-v2 access fixture above.
internal static class GroundworkTestAccess
{
    public const string DefaultScopeValue = DesignGroundworkTestAccess.DefaultScopeValue;
    public static IPersistenceAccessContextAccessor DefaultAccessContextAccessor => DesignGroundworkTestAccess.DefaultAccessContextAccessor;
    public static IPersistenceAccessContextAccessor AccessContext(string scope) => DesignGroundworkTestAccess.AccessContext(scope);
}
