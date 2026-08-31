using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Kernel;
using Groundwork.Kernel.Schema;
using Groundwork.Query.Model;
using Groundwork.Sqlite;
using Groundwork.Store;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

/// <summary>Runs the public-v2 workflow-design projection and restart proof against native SQLite.</summary>
public sealed class GroundworkWorkflowDefinitionSqliteProofTests
{
    [Fact]
    public async Task Public_v2_definition_projection_and_case_insensitive_route_survive_sqlite_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"elsa-workflow-design-v2-{Guid.NewGuid():N}.db");
        try
        {
            using (var connection = new SqliteProviderFactory().Create($"Data Source={path};Pooling=False"))
            {
                foreach (var unit in WorkflowsDesignStorageManifest.CreateUnits())
                    connection.Schema.Apply(unit);

                var definitionUnit = WorkflowsDesignStorageManifest.Require(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind);
                var access = StorageAccess.Scoped(new StorageScope("sqlite-proof"));
                var definition = new WorkflowDefinition
                {
                    Id = "sqlite-alpha",
                    TenantId = "sqlite-proof",
                    Name = "SQLite Alpha",
                    Description = "Native public-v2 projection proof",
                    CreatedAt = DateTimeOffset.UtcNow,
                    LastModifiedAt = DateTimeOffset.UtcNow
                };
                var values = GroundworkDesignStorage.Values(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                    definition,
                    GroundworkDesignDocumentSerialization.Create(new FakePayloadSerializer()),
                    WorkflowsDesignStorageManifest.WorkflowDefinitionCollection);
                var outcome = connection.OpenSession(definitionUnit, access).Insert(values, WriteOptions.CreateOnly);
                Assert.True(outcome.Succeeded, $"SQLite seed failed: {outcome.Status}");
            }

            using var reopened = new SqliteProviderFactory().Create($"Data Source={path};Pooling=False");
            var source = new NativeSessionSource(reopened);
            var store = new GroundworkWorkflowDefinitionStore(
                source,
                DesignGroundworkTestAccess.AccessContext("sqlite-proof"));

            var point = await store.FindByIdAsync("sqlite-alpha");
            // Name equality is ordinal, per the four-provider design query contract: a differently-cased
            // form does not match. The identity and SearchTerm routes below stay case-insensitive -- those
            // run on persisted search keys, which is what makes folding portable.
            var miss = await store.ListAsync(new WorkflowDefinitionFilter { Name = "sqlite alpha" });
            var exact = await store.ListAsync(new WorkflowDefinitionFilter { Name = "SQLite Alpha" });
            source.Queries.Clear();
            var search = await store.ListAsync(new WorkflowDefinitionFilter { SearchTerm = "ALP" });
            source.Queries.Clear();
            var byId = await store.ListAsync(new WorkflowDefinitionFilter { Id = "SQLITE-ALPHA" });

            Assert.NotNull(point);
            Assert.Equal("SQLite Alpha", point!.Name);
            Assert.Empty(miss);
            Assert.Equal(["sqlite-alpha"], exact.Select(definition => definition.Id));
            Assert.Equal(["sqlite-alpha"], search.Select(definition => definition.Id));
            Assert.Equal(["sqlite-alpha"], byId.Select(definition => definition.Id));
            var idSearch = Assert.Single(source.Queries, query =>
                query.Options?.SelectedIndex == WorkflowsDesignStorageManifest.DefinitionByIdSearchIndex);
            var idPredicate = Assert.IsType<Predicate.Equal>(idSearch.Request.Where);
            Assert.Equal(WorkflowsDesignStorageManifest.DefinitionIdLookupHashField, idPredicate.Column.Name);
            Assert.Equal(WorkflowsDesignStorageManifest.DefinitionByIdSearchIndex, idSearch.Options!.SelectedIndex);
            var idIndex = Assert.Single(idSearch.Options.Indexes);
            Assert.Equal(
                [WorkflowsDesignStorageManifest.DefinitionIdLookupHashField],
                idIndex.Columns.ToArray());
        }
        finally
        {
            foreach (var file in new[] { path, $"{path}-shm", $"{path}-wal" })
                if (File.Exists(file))
                    File.Delete(file);
        }
    }

    [Fact]
    public void Definition_projection_uses_a_new_versioned_table_for_the_clean_schema_boundary()
    {
        var path = Path.Combine(Path.GetTempPath(), $"elsa-workflow-design-v2-boundary-{Guid.NewGuid():N}.db");
        try
        {
            using var connection = new SqliteProviderFactory().Create($"Data Source={path};Pooling=False");
            var current = WorkflowsDesignStorageManifest.Require(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind);
            var legacy = current with
            {
                Name = "elsa_workflow_definitions",
                SchemaVersion = 1,
                Columns = current.Columns
                    .Where(column => column.Name != WorkflowsDesignStorageManifest.DefinitionIdLookupHashField)
                    .ToArray(),
                Indexes =
                [
                    new IndexDefinition
                    {
                        Name = WorkflowsDesignStorageManifest.DefinitionByIdIndex,
                        IsUnique = true,
                        Columns = [new IndexColumn(WorkflowsDesignStorageManifest.DefinitionIdField)]
                    },
                    new IndexDefinition
                    {
                        Name = WorkflowsDesignStorageManifest.DefinitionByIdSearchIndex,
                        IsUnique = true,
                        Columns = [new IndexColumn(WorkflowsDesignStorageManifest.DefinitionIdSearchKeyField)]
                    },
                    new IndexDefinition
                    {
                        Name = WorkflowsDesignStorageManifest.DefinitionByNameIndex,
                        IsUnique = true,
                        Columns =
                        [
                            new IndexColumn(WorkflowsDesignStorageManifest.DefinitionNameField),
                            new IndexColumn(WorkflowsDesignStorageManifest.DefinitionIdField)
                        ]
                    },
                    new IndexDefinition
                    {
                        Name = WorkflowsDesignStorageManifest.DefinitionByDescriptionIndex,
                        IsUnique = true,
                        Columns =
                        [
                            new IndexColumn(WorkflowsDesignStorageManifest.DefinitionDescriptionField),
                            new IndexColumn(WorkflowsDesignStorageManifest.DefinitionIdField)
                        ]
                    }
                ]
            };

            connection.Schema.Apply(legacy);
            var refusal = Assert.Throws<PhysicalSchemaPlanRefusedException>(() => connection.Schema.Apply(current));
            Assert.Contains("Rebuild the target from the current declaration", refusal.Message, StringComparison.Ordinal);
            Assert.Equal("elsa_workflow_definitions_v2", current.Name);
            Assert.Equal(WorkflowsDesignStorageManifest.DefinitionStorageSchemaVersion, current.SchemaVersion);
        }
        finally
        {
            foreach (var file in new[] { path, $"{path}-shm", $"{path}-wal" })
                if (File.Exists(file))
                    File.Delete(file);
        }
    }

    [Fact]
    public async Task Candidate_probe_accepts_10000_and_refuses_10001_before_residual_filtering()
    {
        var acceptedPath = Path.Combine(Path.GetTempPath(), $"elsa-workflow-design-v2-probe-accepted-{Guid.NewGuid():N}.db");
        var refusalPath = Path.Combine(Path.GetTempPath(), $"elsa-workflow-design-v2-probe-refusal-{Guid.NewGuid():N}.db");
        try
        {
            var definitionUnit = WorkflowsDesignStorageManifest.Require(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind);
            var access = StorageAccess.Scoped(new StorageScope("sqlite-probe"));
            using (var connection = new SqliteProviderFactory().Create($"Data Source={acceptedPath};Pooling=False"))
            {
                foreach (var unit in WorkflowsDesignStorageManifest.CreateUnits())
                    connection.Schema.Apply(unit);
                var session = connection.OpenSession(definitionUnit, access);
                for (var index = 0; index < 10_000; index++)
                    Assert.True(session.Insert(DefinitionValues($"probe-{index:D5}", "match"), WriteOptions.CreateOnly).Succeeded);
            }

            using (var reopened = new SqliteProviderFactory().Create($"Data Source={acceptedPath};Pooling=False"))
            {
                var source = new NativeSessionSource(reopened);
                var store = new GroundworkWorkflowDefinitionStore(
                    source,
                    DesignGroundworkTestAccess.AccessContext("sqlite-probe"));
                var accepted = await store.ListAsync(new WorkflowDefinitionFilter { SearchTerm = "MATCH" });

                Assert.Equal(10_000, accepted.Count);
                var acceptedProbe = Assert.Single(source.Queries, query => query.Options?.SelectedIndex == WorkflowsDesignStorageManifest.DefinitionByIdIndex);
                Assert.Equal(GroundworkDesignStorage.SearchTermProbeAcceptance.Id, acceptedProbe.Request.AcceptedScan?.Id);
                Assert.Equal(GroundworkDesignStorage.SearchTermProbeLimit, acceptedProbe.Request.Paging.Limit);
            }

            using (var connection = new SqliteProviderFactory().Create($"Data Source={refusalPath};Pooling=False"))
            {
                foreach (var unit in WorkflowsDesignStorageManifest.CreateUnits())
                    connection.Schema.Apply(unit);
                var session = connection.OpenSession(definitionUnit, access);
                for (var index = 0; index < 10_000; index++)
                    Assert.True(session.Insert(DefinitionValues($"catalog-{index:D5}", "other"), WriteOptions.CreateOnly).Succeeded);
                Assert.True(session.Insert(DefinitionValues("catalog-match", "match"), WriteOptions.CreateOnly).Succeeded);
            }

            using (var reopened = new SqliteProviderFactory().Create($"Data Source={refusalPath};Pooling=False"))
            {
                var source = new NativeSessionSource(reopened);
                var store = new GroundworkWorkflowDefinitionStore(
                    source,
                    DesignGroundworkTestAccess.AccessContext("sqlite-probe"));
                var refusal = await Assert.ThrowsAsync<GroundworkQueryReadinessException>(() =>
                    store.ListAsync(new WorkflowDefinitionFilter { SearchTerm = "MATCH" }));
                Assert.Contains("10,000", refusal.Message, StringComparison.Ordinal);
                Assert.Single(source.Queries);
                Assert.Equal(GroundworkDesignStorage.SearchTermProbeAcceptance.Id, source.Queries[0].Request.AcceptedScan?.Id);
                Assert.All(source.Queries, query => Assert.Equal(WorkflowsDesignStorageManifest.DefinitionByIdIndex, query.Options?.SelectedIndex));
            }
        }
        finally
        {
            foreach (var file in new[]
                     {
                         acceptedPath, $"{acceptedPath}-shm", $"{acceptedPath}-wal",
                         refusalPath, $"{refusalPath}-shm", $"{refusalPath}-wal"
                     })
                if (File.Exists(file))
                    File.Delete(file);
        }
    }

    private static StorageValues DefinitionValues(string id, string name) =>
        GroundworkDesignStorage.Values(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            new WorkflowDefinition
            {
                Id = id,
                TenantId = "sqlite-probe",
                Name = name,
                CreatedAt = DateTimeOffset.UtcNow,
                LastModifiedAt = DateTimeOffset.UtcNow
            },
            GroundworkDesignDocumentSerialization.Create(new FakePayloadSerializer()),
            WorkflowsDesignStorageManifest.WorkflowDefinitionCollection);

    private sealed class NativeSessionSource(IStorageProviderConnection connection)
        : IGroundworkStorageSessionSource, IGroundworkStorageCapabilitySource
    {
        private readonly IReadOnlyDictionary<string, StorageUnit> units =
            WorkflowsDesignStorageManifest.CreateUnits().ToDictionary(unit => unit.Id.Value, StringComparer.Ordinal);
        public List<RecordedQuery> Queries { get; } = [];

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
            new RecordingSession(connection.OpenSession(Unit(unitId, targetName), access), Queries);

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) => throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null) => units[unitId];

        public IReadOnlyList<CapabilityDescriptor> Capabilities(string? targetName = null) => connection.Capabilities;

        public sealed record RecordedQuery(QueryRequest Request, QueryRenderOptions? Options);

        private sealed class RecordingSession(IStorageSession inner, ICollection<RecordedQuery> queries) : SynchronousStorageSessionTestDouble, IStorageSession
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
}
