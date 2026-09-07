using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.Groundwork;
using Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;
using Groundwork.Kernel;
using Groundwork.MongoDb;
using Groundwork.PostgreSql;
using Groundwork.Sqlite;
using Groundwork.SqlServer;
using Groundwork.Store;
using System.Text.Json;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Workflows.Publishing.Api.GroundworkTests;

/// <summary>
/// The four-provider proof for the draft-test-run receipt store.
/// <para>
/// The rest of this project's suites run the receipt store on the in-memory and SQLite providers, which
/// is enough to pin behaviour but is not evidence about the other three. What matters here is provider
/// behaviour rather than API shape: the receipt is written create-only, so a second start on the same
/// identity has to lose at the provider and be resolved by reading the winner, and the idempotency-key
/// lookup has to come back through a real index rather than a scan. Those are the parts a driver can
/// get wrong on its own.
/// </para>
/// <para>
/// This project starts no containers, so the container-free lane runs it too and its three non-SQLite
/// cases skip there — that lane has no connection strings to give them. The native provider matrix job
/// runs it again with <c>GROUNDWORK_V2_REQUIRE_NATIVE_PROVIDER_MATRIX</c> set, which turns a missing
/// string into a failure so these cases cannot quietly decay back into a permanent skip.
/// </para>
/// </summary>
public sealed class ActivityDraftTestRunProviderMatrixTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Providers => new() { "sqlite", "postgresql", "sqlserver", "mongodb" };

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Receipts_are_create_only_and_round_trip_on_every_native_provider(string providerName)
    {
        var sqlitePath = providerName == "sqlite"
            ? Path.Join(Path.GetTempPath(), $"elsa-draft-test-run-matrix-{Guid.NewGuid():N}.db")
            : null;
        var connectionString = sqlitePath is not null
            ? $"Data Source={sqlitePath};Pooling=False"
            : Environment.GetEnvironmentVariable(EnvironmentVariable(providerName));
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Gate the escalation on the explicit native-matrix opt-in, never on CI alone: the
            // container-free lane by construction has no provider connection strings, and demanding
            // them there would fail a job that can never supply them.
            if (Environment.GetEnvironmentVariable("GROUNDWORK_V2_REQUIRE_NATIVE_PROVIDER_MATRIX") is "1" or "true")
            {
                throw new InvalidOperationException(
                    $"The {providerName} draft-test-run provider proof requires {EnvironmentVariable(providerName)}.");
            }

            Skip.If(true, $"Set {EnvironmentVariable(providerName)} to run the {providerName} draft-test-run provider proof.");
        }

        try
        {
            using var connection = CreateConnection(providerName, connectionString!);
            var units = PublishingGroundworkStorageManifest.CreateUnits();
            foreach (var unit in units)
                connection.Schema.Apply(unit);

            // A fresh tenant per run: these providers are long-lived in CI, and a deterministic receipt
            // id would otherwise collide with the previous run's row and prove the wrong thing.
            var tenant = $"draft-run-matrix-{providerName}-{Guid.NewGuid():N}";
            var sessions = new UnitSessionSource(connection, units);
            var access = new FixedAccessContextAccessor(
                PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));
            var serializer = new PublishingGroundworkDocumentSerializer();
            var store = new GroundworkActivityDraftTestRunStore(sessions, access, serializer);
            const string idempotencyKey = "rerun-42";
            var receipt = Receipt(tenant, idempotencyKey, "fingerprint-a");

            var created = await store.TryCreateAsync(receipt);
            Assert.True(created.Created);

            // The create-only write has to lose at the provider, and the loser has to resolve to the
            // winner's row rather than overwriting it.
            var conflicting = await store.TryCreateAsync(Receipt(tenant, idempotencyKey, "fingerprint-b"));
            Assert.False(conflicting.Created);
            Assert.Equal("fingerprint-a", conflicting.Receipt.RequestFingerprint);

            // A second store over the same connection: the receipt has to come back from the provider,
            // not from anything the first instance kept.
            var reopened = new GroundworkActivityDraftTestRunStore(sessions, access, serializer);
            Assert.Equal(receipt, await reopened.FindAsync(receipt.TestRunId));
            Assert.Equal(
                receipt.TestRunId,
                (await reopened.FindByIdempotencyKeyAsync(receipt.OperationScope, receipt.DraftId, idempotencyKey))?.TestRunId);

            // The sharpest assertion in the suite this belongs to: the raw key is never stored, only its
            // hash. Providers disagree on whether a JSON column reads back as text or as a parsed
            // element, so accept either and assert on the content.
            var session = sessions.Open(
                PublishingGroundworkStorageManifest.ActivityDraftTestRunDocumentKind,
                StorageAccess.Scoped(new StorageScope(tenant)));
            var row = session.Read(new StorageKey(new Dictionary<string, object?>
            {
                [PublishingGroundworkStorageManifest.IdField] = receipt.TestRunId
            }));
            Assert.NotNull(row);
            var content = row!.Values.Values[PublishingGroundworkStorageManifest.ContentField];
            var payload = content as string ?? ((JsonElement)content!).GetRawText();
            Assert.DoesNotContain(idempotencyKey, payload, StringComparison.Ordinal);

            // Retention is driven by the receipt's own expiry, not the source reference's, and the
            // bounded delete has to honour that ordering on the provider's own query plan.
            Assert.Equal(0, await reopened.DeleteExpiredAsync(receipt.SourceReferenceExpiresAt, 10));
            Assert.Equal(1, await reopened.DeleteExpiredAsync(receipt.ReceiptExpiresAt, 10));
            Assert.Null(await reopened.FindAsync(receipt.TestRunId));
        }
        finally
        {
            if (sqlitePath is not null)
            {
                foreach (var path in new[] { sqlitePath, $"{sqlitePath}-shm", $"{sqlitePath}-wal" }.Where(File.Exists))
                    File.Delete(path);
            }
        }
    }

    /// <summary>
    /// The transactional write path, on every native provider.
    ///
    /// Everything else in the four-provider matrix — this file's create-only proof, the publishing catalog
    /// proof, and the filtered runtime classes the matrix job runs — writes one row at a time through
    /// <c>IStorageSession</c>. The product does not: a runtime checkpoint and a publication both commit
    /// through <c>BeginUnitOfWork</c>, which batches its rows and flushes them as one statement. That is a
    /// different statement generator, so a matrix that only ever writes single rows proves the providers
    /// and not the path, and #1432 is what that gap was hiding.
    /// </summary>
    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public void Transactional_writes_reach_every_native_provider(string providerName)
    {
        var sqlitePath = providerName == "sqlite"
            ? Path.Join(Path.GetTempPath(), $"elsa-draft-test-run-uow-{Guid.NewGuid():N}.db")
            : null;
        var connectionString = sqlitePath is not null
            ? $"Data Source={sqlitePath};Pooling=False"
            : Environment.GetEnvironmentVariable(EnvironmentVariable(providerName));
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            if (Environment.GetEnvironmentVariable("GROUNDWORK_V2_REQUIRE_NATIVE_PROVIDER_MATRIX") is "1" or "true")
            {
                throw new InvalidOperationException(
                    $"The {providerName} transactional-write proof requires {EnvironmentVariable(providerName)}.");
            }

            Skip.If(true, $"Set {EnvironmentVariable(providerName)} to run the {providerName} transactional-write proof.");
        }

        try
        {
            using var connection = CreateConnection(providerName, connectionString!);
            var unit = PublishingGroundworkStorageManifest.Require(
                PublishingGroundworkStorageManifest.ActivityDraftTestRunDocumentKind);
            connection.Schema.Apply(unit);

            var tenant = $"uow-matrix-{providerName}-{Guid.NewGuid():N}";
            var access = StorageAccess.Scoped(new StorageScope(tenant));
            var id = $"uow-{Guid.NewGuid():N}";
            var values = RequiredRow(unit, id);

            using (var unitOfWork = connection.BeginUnitOfWork(access, BatchWriteOptions.Exact, [unit]))
            {
                unitOfWork.Stage(RowWrite.Upsert(unit, values, WriteOptions.Unconditional));
                unitOfWork.CommitWithOutcomes();
            }

            // Committed rather than merely accepted: the batch has to be visible to a fresh session.
            Assert.NotNull(connection.OpenSession(unit, access).Read(new StorageKey(new Dictionary<string, object?>
            {
                [PublishingGroundworkStorageManifest.IdField] = id
            })));
        }
        finally
        {
            if (sqlitePath is not null)
            {
                foreach (var path in new[] { sqlitePath, $"{sqlitePath}-shm", $"{sqlitePath}-wal" }.Where(File.Exists))
                    File.Delete(path);
            }
        }
    }

    /// <summary>
    /// A row every declared column will accept, filled from the unit itself rather than a hand-written list.
    /// The declared projections are <c>Required()</c>, so a "minimal" row of id/schemaVersion/content is
    /// rejected — as NULL on SQL Server, as a required-column error on MongoDB, and, misleadingly, as
    /// <c>UniqueViolation</c> on SQLite. Deriving the row from the manifest also means a column added later
    /// does not quietly stop being covered here.
    /// </summary>
    private static StorageValues RequiredRow(StorageUnit unit, string id)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [PublishingGroundworkStorageManifest.IdField] = id,
            [PublishingGroundworkStorageManifest.SchemaVersionField] = PublishingGroundworkStorageManifest.SchemaVersion,
            [PublishingGroundworkStorageManifest.ContentField] = "{}"
        };

        foreach (var column in unit.Columns)
        {
            // The optimistic token is system-owned: supplying it is refused before the provider is reached.
            if (values.ContainsKey(column.Name) ||
                column.IsNullable ||
                column.Name == PublishingGroundworkStorageManifest.ConcurrencyTokenField)
                continue;

            values[column.Name] = column.Type switch
            {
                PortableType.String => $"uow-{column.Name}",
                PortableType.DateTimeOffset => new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero),
                PortableType.Int32 => 0,
                PortableType.Int64 => 0L,
                PortableType.Boolean => false,
                PortableType.Guid => Guid.Empty,
                _ => throw new NotSupportedException($"Unhandled matrix column type '{column.Type}'.")
            };
        }

        return new StorageValues(values);
    }

    private static ActivityDraftTestRunReceipt Receipt(string tenant, string idempotencyKey, string fingerprint)
    {
        var operationScope = ActivityDraftTestRunIdentity.CreateOperationScope(tenant);
        return new ActivityDraftTestRunReceipt(
            TestRunId: ActivityDraftTestRunIdentity.CreateTestRunId(operationScope, "draft-1", idempotencyKey),
            OperationScope: operationScope,
            IdempotencyKeyHash: ActivityDraftTestRunIdentity.HashIdempotencyKey(idempotencyKey),
            DraftId: "draft-1",
            DraftRevision: 8,
            DefinitionId: "definition-1",
            TenantId: tenant,
            ResourceTenantId: tenant,
            RequestFingerprint: fingerprint,
            WorkflowExecutionId: ActivityDraftTestRunIdentity.CreateWorkflowExecutionId(operationScope, "draft-1", idempotencyKey),
            Status: ActivityDraftTestRunReceiptStatus.Preparing,
            CommandDispatchStatus: null,
            Failure: null,
            ArtifactId: null,
            SourceReferenceId: null,
            RequestedAt: Now,
            UpdatedAt: Now,
            SourceReferenceExpiresAt: Now.AddMinutes(30),
            ReceiptExpiresAt: Now.AddDays(7),
            CancellationStatus: ActivityDraftTestRunCancellationStatus.Available,
            Revision: 1);
    }

    private static IStorageProviderConnection CreateConnection(string providerName, string connectionString) => providerName switch
    {
        "sqlite" => new SqliteProviderFactory().Create(connectionString),
        "postgresql" => new PostgreSqlProviderFactory().Create(connectionString),
        "sqlserver" => new SqlServerProviderFactory().Create(connectionString),
        "mongodb" => new MongoProviderFactory().Create(connectionString),
        _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
    };

    private static string EnvironmentVariable(string providerName) =>
        $"GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING";

    private sealed class UnitSessionSource(
        IStorageProviderConnection connection,
        IReadOnlyList<StorageUnit> units) : IGroundworkStorageSessionSource, IGroundworkStorageCapabilitySource
    {
        private readonly IReadOnlyDictionary<string, StorageUnit> byId =
            units.ToDictionary(unit => unit.Id.Value, StringComparer.Ordinal);

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

        private StorageUnit Resolve(string unitId) => byId[unitId];
    }

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}
