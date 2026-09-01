using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.V2.Tests;

public sealed class GroundworkPrivilegedQueryAuditExecutorTests
{
    [Theory]
    [MemberData(nameof(NonAcrossScopeContexts))]
    public async Task Non_explicit_across_scope_contexts_refuse_before_audit_or_session_io(
        PersistenceAccessContext context)
    {
        var source = new RecordingSource();
        var sink = new RecordingAuditSink();
        var executor = new GroundworkPrivilegedQueryAuditExecutor(
            source,
            new FixedAccessContextAccessor(context),
            sink);

        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(
            source.DeclaredUnit.Id.Value,
            "elsa-shared-query",
            static (_, _) => Task.FromResult(1)));

        Assert.Equal(0, source.UnitCount);
        Assert.Equal(0, source.OpenCount);
        Assert.Empty(sink.Acquisitions);
        Assert.Empty(sink.Outcomes);
    }

    [Fact]
    public async Task Successful_query_records_one_acquisition_and_one_success_with_provider_audit_binding()
    {
        var source = new RecordingSource();
        var sink = new RecordingAuditSink();
        var purpose = new PersistenceAccessPurpose("activity-design-cross-scope");
        var executor = new GroundworkPrivilegedQueryAuditExecutor(
            source,
            new FixedAccessContextAccessor(PersistenceAccessContext.PrivilegedAcrossScopes(purpose)),
            sink);

        var result = await executor.ExecuteAsync(
            source.DeclaredUnit.Id.Value,
            "elsa-activities-design",
            static (_, _) => Task.FromResult("result"));

        Assert.Equal("result", result);
        Assert.Equal(1, source.OpenCount);
        var acquisition = Assert.Single(sink.Acquisitions);
        Assert.Equal("elsa-activities-design", acquisition.AuditIdentity);
        Assert.Equal(purpose.Value, acquisition.Purpose);
        Assert.True(acquisition.Access.IsPrivilegedAcrossScopes);
        Assert.Equal("elsa-activities-design", acquisition.Access.Audit?.Identity);
        Assert.Equal(purpose.Value, acquisition.Access.Audit?.Purpose);
        var outcome = Assert.Single(sink.Outcomes);
        Assert.Equal(acquisition.Id, outcome.Acquisition.Id);
        Assert.Equal(GroundworkPrivilegedQueryOutcome.Succeeded, outcome.Outcome);
        Assert.Null(outcome.Failure);
    }

    [Fact]
    public async Task Provider_failure_records_one_failed_outcome_and_rethrows_the_same_failure()
    {
        var source = new RecordingSource();
        var sink = new RecordingAuditSink();
        var executor = new GroundworkPrivilegedQueryAuditExecutor(
            source,
            new FixedAccessContextAccessor(PersistenceAccessContext.PrivilegedAcrossScopes(
                new PersistenceAccessPurpose("workflow-attention-query"))),
            sink);
        var failure = new InvalidOperationException("provider failed");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(
            source.DeclaredUnit.Id.Value,
            "elsa-workflows-runtime",
            (_, _) => Task.FromException<int>(failure)));

        Assert.Same(failure, thrown);
        Assert.Equal(1, source.OpenCount);
        var outcome = Assert.Single(sink.Outcomes);
        Assert.Equal(GroundworkPrivilegedQueryOutcome.Failed, outcome.Outcome);
        Assert.Same(failure, outcome.Failure);
        Assert.Single(sink.Acquisitions);
    }

    [Fact]
    public async Task Cancellation_records_one_canceled_outcome_and_rethrows_cancellation()
    {
        var source = new RecordingSource();
        var sink = new RecordingAuditSink();
        var executor = new GroundworkPrivilegedQueryAuditExecutor(
            source,
            new FixedAccessContextAccessor(PersistenceAccessContext.PrivilegedAcrossScopes(
                new PersistenceAccessPurpose("workflow-attention-query"))),
            sink);
        await Assert.ThrowsAsync<OperationCanceledException>(() => executor.ExecuteAsync(
            source.DeclaredUnit.Id.Value,
            "elsa-workflows-runtime",
            static (_, _) => Task.FromException<int>(new OperationCanceledException("operation canceled"))));

        Assert.Equal(1, source.OpenCount);
        var outcome = Assert.Single(sink.Outcomes);
        Assert.Equal(GroundworkPrivilegedQueryOutcome.Canceled, outcome.Outcome);
        Assert.Null(outcome.Failure);
        Assert.Single(sink.Acquisitions);
    }

    [Fact]
    public async Task Pre_canceled_query_refuses_before_unit_or_session_io_without_orphan_audit()
    {
        var source = new RecordingSource();
        var sink = new RecordingAuditSink();
        var executor = new GroundworkPrivilegedQueryAuditExecutor(
            source,
            new FixedAccessContextAccessor(PersistenceAccessContext.PrivilegedAcrossScopes(
                new PersistenceAccessPurpose("workflow-attention-query"))),
            sink);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => executor.ExecuteAsync(
            source.DeclaredUnit.Id.Value,
            "elsa-workflows-runtime",
            static (_, _) => Task.FromResult(1),
            cancellation.Token));

        Assert.Equal(0, source.UnitCount);
        Assert.Equal(0, source.OpenCount);
        Assert.Empty(sink.Acquisitions);
        Assert.Empty(sink.Outcomes);
    }

    [Fact]
    public void Synchronous_query_executor_exposes_only_the_public_query_capability()
    {
        var source = new RecordingSource();
        var sink = new RecordingAuditSink();
        var executor = new GroundworkPrivilegedQueryAuditExecutor(
            source,
            new FixedAccessContextAccessor(PersistenceAccessContext.PrivilegedAcrossScopes(
                new PersistenceAccessPurpose("activity-design-cross-scope"))),
            sink);

        var result = executor.Execute(
            source.DeclaredUnit.Id.Value,
            "elsa-activities-design",
            query => query.QueryAcrossScopes(new QueryRequest(
                new TableId(source.DeclaredUnit.Name),
                Predicate.AlwaysTrue.Instance,
                [],
                Projection.All,
                Paging.None)));

        Assert.Single(result.Rows);
        Assert.Equal(1, source.OpenCount);
        Assert.Equal(GroundworkPrivilegedQueryOutcome.Succeeded, Assert.Single(sink.Outcomes).Outcome);
    }

    [Fact]
    public async Task Missing_query_capability_records_one_failure_and_never_exposes_write_session()
    {
        var source = new RecordingSource(useQueryCapability: false);
        var sink = new RecordingAuditSink();
        var executor = new GroundworkPrivilegedQueryAuditExecutor(
            source,
            new FixedAccessContextAccessor(PersistenceAccessContext.PrivilegedAcrossScopes(
                new PersistenceAccessPurpose("activity-design-cross-scope"))),
            sink);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(
            source.DeclaredUnit.Id.Value,
            "elsa-activities-design",
            static (_, _) => Task.FromResult(1)));

        Assert.Contains("query capability", failure.Message, StringComparison.Ordinal);
        Assert.Equal(GroundworkPrivilegedQueryOutcome.Failed, Assert.Single(sink.Outcomes).Outcome);
    }

    [Fact]
    public async Task Sink_failure_does_not_trigger_a_second_terminal_outcome()
    {
        var source = new RecordingSource();
        var sink = new ThrowingAuditSink();
        var executor = new GroundworkPrivilegedQueryAuditExecutor(
            source,
            new FixedAccessContextAccessor(PersistenceAccessContext.PrivilegedAcrossScopes(
                new PersistenceAccessPurpose("activity-design-cross-scope"))),
            sink);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(
            source.DeclaredUnit.Id.Value,
            "elsa-activities-design",
            static (_, _) => Task.FromResult(1)));

        Assert.Equal("audit completion failed", failure.Message);
        Assert.Equal(1, sink.OutcomeCalls);
    }

    [Fact]
    public async Task Sink_failure_preserves_operation_failure_and_still_attempts_one_terminal_outcome()
    {
        var source = new RecordingSource();
        var sink = new ThrowingAuditSink();
        var executor = new GroundworkPrivilegedQueryAuditExecutor(
            source,
            new FixedAccessContextAccessor(PersistenceAccessContext.PrivilegedAcrossScopes(
                new PersistenceAccessPurpose("activity-design-cross-scope"))),
            sink);
        var operationFailure = new InvalidOperationException("query failed");

        var aggregate = await Assert.ThrowsAsync<AggregateException>(() => executor.ExecuteAsync(
            source.DeclaredUnit.Id.Value,
            "elsa-activities-design",
            (_, _) => Task.FromException<int>(operationFailure)));

        Assert.Contains(operationFailure, aggregate.InnerExceptions);
        Assert.Contains(aggregate.InnerExceptions, exception => exception.Message == "audit completion failed");
        Assert.Equal(1, sink.OutcomeCalls);
    }

    [Fact]
    public void Public_v2_sink_is_bounded_and_never_retains_exception_messages()
    {
        var sink = new GroundworkPrivilegedQueryAuditSink(capacity: 2);
        var access = StorageAccess.PrivilegedAcrossScopes(
            new StorageAccessAudit("elsa-activities-design", "activity-design-cross-scope"));

        var first = sink.RecordAcquisition(access);
        sink.RecordOutcome(first, GroundworkPrivilegedQueryOutcome.Succeeded);
        var second = sink.RecordAcquisition(access);
        sink.RecordOutcome(
            second,
            GroundworkPrivilegedQueryOutcome.Failed,
            new InvalidOperationException("Server=db.internal;Password=secret"));

        var records = sink.Snapshot();
        Assert.Equal(2, records.Count);
        Assert.Equal([3L, 4L], records.Select(record => record.Sequence));
        Assert.DoesNotContain(records, record => record.ToString().Contains("Password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(records, record => record.ToString().Contains("db.internal", StringComparison.Ordinal));
        Assert.All(records, record => Assert.Equal("elsa-activities-design", record.AuditIdentity));
    }

    [Fact]
    public void V2_storage_registration_publishes_the_audit_executor_and_bounded_sink()
    {
        var services = new ServiceCollection();
        services.AddPersistenceCore();
        services.AddGroundworkStorageUnit(RecordingSource.CreateUnit());
        using var provider = services.BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<GroundworkPrivilegedQueryAuditSink>(),
            provider.GetRequiredService<IGroundworkPrivilegedQueryAuditSink>());
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<GroundworkPrivilegedQueryAuditExecutor>());
    }

    public static IEnumerable<object[]> NonAcrossScopeContexts() =>
    [
        [PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))],
        [PersistenceAccessContext.Global],
        [PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("global-only"))]
    ];

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext context) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = context;
    }

    private sealed class RecordingSource : IGroundworkStorageSessionSource
    {
        public static StorageUnit CreateUnit() => StorageUnit.Declare("audit-unit", "audit_unit")
            .String("id", 64, column => column.Required())
            .Key("id")
            .Scoped()
            .Build();

        public RecordingSource(bool useQueryCapability = true)
        {
            UseQueryCapability = useQueryCapability;
        }

        public StorageUnit DeclaredUnit { get; } = CreateUnit();
        public bool UseQueryCapability { get; }

        public int UnitCount { get; private set; }
        public int OpenCount { get; private set; }

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            OpenCount++;
            return UseQueryCapability
                ? new QueryCapableSession(DeclaredUnit, access)
                : new NoOpSession(DeclaredUnit, access);
        }

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) => throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null)
        {
            UnitCount++;
            return DeclaredUnit;
        }
    }

    private sealed class NoOpSession(StorageUnit unit, StorageAccess access) : SynchronousStorageSessionTestDouble, IStorageSession
    {
        public StorageUnit Unit { get; } = unit;
        public StorageAccess Access { get; } = access;
        public StoredEntry? Read(StorageKey key) => null;
        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null) => throw new NotSupportedException();
        public AggregationResult Aggregate(AggregationQuery query) => throw new NotSupportedException();
        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => throw new NotSupportedException();

    }

    private sealed class QueryCapableSession(StorageUnit unit, StorageAccess access) : SynchronousStorageSessionTestDouble, IStorageSession, IPrivilegedCrossScopeQuerySession
    {
        public StorageUnit Unit { get; } = unit;
        public StorageAccess Access { get; } = access;
        public StoredEntry? Read(StorageKey key) => null;
        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null) => throw new NotSupportedException();
        public AggregationResult Aggregate(AggregationQuery query) => throw new NotSupportedException();
        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => throw new NotSupportedException();
        public CrossScopeQueryResult QueryAcrossScopes(QueryRequest request, QueryRenderOptions? options = null) =>
            new(
                [new CrossScopeQueryRow(new StorageScope("tenant-a"),
                    new Dictionary<string, object?> { ["id"] = "row-1" })],
                1,
                null);
    }

    private sealed class ThrowingAuditSink : IGroundworkPrivilegedQueryAuditSink
    {
        public int OutcomeCalls { get; private set; }

        public GroundworkPrivilegedQueryAuditAcquisition RecordAcquisition(StorageAccess access)
        {
            var audit = access.Audit!;
            return new(Guid.NewGuid(), audit.Identity, audit.Purpose, access);
        }

        public void RecordOutcome(
            GroundworkPrivilegedQueryAuditAcquisition acquisition,
            GroundworkPrivilegedQueryOutcome outcome,
            Exception? failure = null)
        {
            OutcomeCalls++;
            throw new InvalidOperationException("audit completion failed");
        }
    }

    private sealed class RecordingAuditSink : IGroundworkPrivilegedQueryAuditSink
    {
        public List<GroundworkPrivilegedQueryAuditAcquisition> Acquisitions { get; } = [];
        public List<(GroundworkPrivilegedQueryAuditAcquisition Acquisition, GroundworkPrivilegedQueryOutcome Outcome, Exception? Failure)> Outcomes { get; } = [];

        public GroundworkPrivilegedQueryAuditAcquisition RecordAcquisition(StorageAccess access)
        {
            var audit = access.Audit ?? throw new InvalidOperationException("Expected provider audit binding.");
            var acquisition = new GroundworkPrivilegedQueryAuditAcquisition(
                Guid.NewGuid(), audit.Identity, audit.Purpose, access);
            Acquisitions.Add(acquisition);
            return acquisition;
        }

        public void RecordOutcome(
            GroundworkPrivilegedQueryAuditAcquisition acquisition,
            GroundworkPrivilegedQueryOutcome outcome,
            Exception? failure = null) => Outcomes.Add((acquisition, outcome, failure));
    }
}
