using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Scoping;
using Groundwork.Core.Scoping;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkStoreSessionFactoryTests
{
    public static TheoryData<PersistenceAccessContext, PersistenceAccessPolicy, DocumentStoreAccessKind, string?, string?>
        AccessMappings => new()
        {
            {
                PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")),
                PersistenceAccessPolicy.Ordinary,
                DocumentStoreAccessKind.Scoped,
                "tenant-a",
                null
            },
            {
                PersistenceAccessContext.Global,
                PersistenceAccessPolicy.Ordinary,
                DocumentStoreAccessKind.Global,
                null,
                null
            },
            {
                PersistenceAccessContext.PrivilegedScoped(
                    new PersistenceScope("tenant-b"),
                    new PersistenceAccessPurpose("repair-index")),
                PersistenceAccessPolicy.Privileged,
                DocumentStoreAccessKind.PrivilegedScoped,
                "tenant-b",
                "repair-index"
            },
            {
                PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("rotate-host-key")),
                PersistenceAccessPolicy.Privileged,
                DocumentStoreAccessKind.PrivilegedGlobal,
                null,
                "rotate-host-key"
            },
            {
                PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("recover-stalled-work")),
                PersistenceAccessPolicy.Privileged,
                DocumentStoreAccessKind.PrivilegedAcrossScopes,
                null,
                "recover-stalled-work"
            }
        };

    [Theory]
    [MemberData(nameof(AccessMappings))]
    public void Mapper_covers_every_scope_and_access_branch(
        PersistenceAccessContext context,
        PersistenceAccessPolicy requiredPolicy,
        DocumentStoreAccessKind expectedKind,
        string? expectedScope,
        string? expectedPurpose)
    {
        var access = GroundworkPersistenceAccessMapper.Map(context, requiredPolicy);

        Assert.Equal(expectedKind, access.Kind);
        Assert.Equal(expectedScope, access.Scope?.Value);
        Assert.Equal(expectedPurpose, access.Privilege?.Reason);
    }

    [Fact]
    public void Mapper_uses_least_privilege_for_ordinary_operations_and_rejects_across_scope_downgrade()
    {
        var purpose = new PersistenceAccessPurpose("tenant-repair");

        var scoped = GroundworkPersistenceAccessMapper.Map(
            PersistenceAccessContext.PrivilegedScoped(new PersistenceScope("tenant-a"), purpose),
            PersistenceAccessPolicy.Ordinary);
        var global = GroundworkPersistenceAccessMapper.Map(
            PersistenceAccessContext.PrivilegedGlobal(purpose),
            PersistenceAccessPolicy.Ordinary);

        Assert.Equal(DocumentStoreAccessKind.Scoped, scoped.Kind);
        Assert.False(scoped.IsPrivileged);
        Assert.Equal(DocumentStoreAccessKind.Global, global.Kind);
        Assert.False(global.IsPrivileged);
        Assert.Throws<InvalidOperationException>(() => GroundworkPersistenceAccessMapper.Map(
            PersistenceAccessContext.PrivilegedAcrossScopes(purpose),
            PersistenceAccessPolicy.Ordinary));
    }

    [Fact]
    public void Mapper_rejects_missing_authority_null_context_and_unknown_policy()
    {
        var ordinary = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"));

        Assert.Throws<InvalidOperationException>(() =>
            GroundworkPersistenceAccessMapper.Map(ordinary, PersistenceAccessPolicy.Privileged));
        Assert.Throws<ArgumentNullException>(() =>
            GroundworkPersistenceAccessMapper.Map(null!, PersistenceAccessPolicy.Ordinary));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GroundworkPersistenceAccessMapper.Map(ordinary, (PersistenceAccessPolicy)42));
    }

    [Fact]
    public async Task Factory_reads_the_current_context_for_every_session_and_returns_fresh_bound_resources()
    {
        var accessor = new MutableAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var source = new RecordingSessionSource();
        var factory = new GroundworkStoreSessionFactory(accessor, source);

        await using var first = await factory.CreateAsync(PersistenceAccessPolicy.Ordinary);
        accessor.Current = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b"));
        await using var second = await factory.CreateAsync(PersistenceAccessPolicy.Ordinary);

        Assert.Equal(["tenant-a", "tenant-b"], source.Accesses.Select(access => access.Scope?.Value));
        Assert.Equal("tenant-a", first.Access.Scope?.Value);
        Assert.Equal("tenant-b", second.Access.Scope?.Value);
        Assert.NotSame(first.DocumentStore, second.DocumentStore);
        Assert.NotSame(first.BoundedDocumentStore, second.BoundedDocumentStore);
        Assert.NotNull(first.BoundedDocumentMutationStore);
        Assert.NotNull(second.BoundedDocumentMutationStore);
    }

    [Fact]
    public async Task Ordinary_global_session_is_explicit_bound_and_rejects_privileged_contexts()
    {
        var accessor = new MutableAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var source = new RecordingSessionSource();
        var factory = new GroundworkStoreSessionFactory(accessor, source);

        await using var session = await factory.CreateOrdinaryGlobalAsync();

        Assert.Equal(DocumentStoreAccess.Global, session.Access);
        Assert.Equal([DocumentStoreAccess.Global], source.Accesses);

        accessor.Current = PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("repair"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => factory.CreateOrdinaryGlobalAsync().AsTask());
        Assert.Single(source.Accesses);
    }

    [Fact]
    public async Task Ordinary_global_session_rejects_mismatched_resources_and_disposes_the_lease()
    {
        var accessor = new MutableAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var mismatchedLease = new TrackingLease();
        var source = new RecordingSessionSource
        {
            CreateResources = _ => Resources(
                DocumentStoreAccess.Scoped(new StorageScope("tenant-a")),
                mismatchedLease)
        };
        var factory = new GroundworkStoreSessionFactory(accessor, source);

        await Assert.ThrowsAsync<InvalidOperationException>(() => factory.CreateOrdinaryGlobalAsync().AsTask());

        Assert.True(mismatchedLease.IsDisposed);
    }

    [Fact]
    public async Task Factory_rejects_cancellation_before_opening_a_source_and_remains_reusable()
    {
        var accessor = new MutableAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var source = new RecordingSessionSource();
        var factory = new GroundworkStoreSessionFactory(accessor, source);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            factory.CreateAsync(PersistenceAccessPolicy.Ordinary, cancellation.Token).AsTask());
        Assert.Empty(source.Accesses);

        await using var session = await factory.CreateAsync(PersistenceAccessPolicy.Ordinary);
        Assert.Single(source.Accesses);
        Assert.Equal("tenant-a", session.Access.Scope?.Value);
    }

    [Fact]
    public async Task Source_failure_is_not_cached_and_the_next_acquisition_uses_the_latest_context()
    {
        var accessor = new MutableAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var source = new RecordingSessionSource { FailNext = true };
        var factory = new GroundworkStoreSessionFactory(accessor, source);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            factory.CreateAsync(PersistenceAccessPolicy.Ordinary).AsTask());
        Assert.Equal("source failure", exception.Message);

        accessor.Current = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b"));
        await using var recovered = await factory.CreateAsync(PersistenceAccessPolicy.Ordinary);

        Assert.Equal(["tenant-a", "tenant-b"], source.Accesses.Select(access => access.Scope?.Value));
        Assert.Equal("tenant-b", recovered.Access.Scope?.Value);
    }

    [Fact]
    public async Task Mismatched_source_access_is_rejected_and_its_lease_is_disposed()
    {
        var accessor = new MutableAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var mismatchedLease = new TrackingLease();
        var source = new RecordingSessionSource
        {
            CreateResources = _ => Resources(
                DocumentStoreAccess.Scoped(new StorageScope("tenant-b")),
                mismatchedLease)
        };
        var factory = new GroundworkStoreSessionFactory(accessor, source);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            factory.CreateAsync(PersistenceAccessPolicy.Ordinary).AsTask());

        Assert.True(mismatchedLease.IsDisposed);
        Assert.DoesNotContain("tenant-a", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant-b", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mismatched_source_access_preserves_validation_failure_when_lease_cleanup_also_fails()
    {
        var accessor = new MutableAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var cleanupFailure = new ApplicationException("lease cleanup failure");
        var mismatchedLease = new TrackingLease(cleanupFailure);
        var source = new RecordingSessionSource
        {
            CreateResources = _ => Resources(
                DocumentStoreAccess.Scoped(new StorageScope("tenant-b")),
                mismatchedLease)
        };
        var factory = new GroundworkStoreSessionFactory(accessor, source);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            factory.CreateAsync(PersistenceAccessPolicy.Ordinary).AsTask());

        Assert.Equal("The Groundwork session resources are bound to a different access context.", exception.Message);
        Assert.Equal(1, mismatchedLease.DisposeCount);
        Assert.Contains(exception.Data.Values.Cast<object>(), value => ReferenceEquals(value, cleanupFailure));
    }

    [Fact]
    public async Task Privileged_mismatched_source_access_is_rejected_before_audit_acquisition()
    {
        var accessor = new MutableAccessContextAccessor(
            PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("host-repair")));
        var lease = new TrackingLease();
        var source = new RecordingSessionSource
        {
            CreateResources = _ => Resources(
                DocumentStoreAccess.Scoped(new StorageScope("tenant-b")),
                lease)
        };
        var sink = new GroundworkPrivilegedAccessSink();
        var factory = new GroundworkStoreSessionFactory(
            accessor,
            source,
            new GroundworkPrivilegedAccessRecorder(sink));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            factory.CreateAsync(PersistenceAccessPolicy.Privileged).AsTask());

        Assert.Equal(1, lease.DisposeCount);
        Assert.Empty(sink.Snapshot());
    }

    [Fact]
    public async Task Acquisition_telemetry_failure_releases_resources_and_preserves_telemetry_failure()
    {
        var accessor = new MutableAccessContextAccessor(
            PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("host-repair")));
        var telemetryFailure = new InvalidOperationException("audit sink unavailable");
        var cleanupFailure = new ApplicationException("lease cleanup failure");
        var lease = new TrackingLease(cleanupFailure);
        var source = new RecordingSessionSource
        {
            CreateResources = access => Resources(access, lease)
        };
        var emitter = new ControllableEmitter(new GroundworkPrivilegedAccessRecorder(new GroundworkPrivilegedAccessSink()))
        {
            AcquisitionFailure = telemetryFailure
        };
        var factory = new GroundworkStoreSessionFactory(accessor, source, emitter);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            factory.CreateAsync(PersistenceAccessPolicy.Privileged).AsTask());

        Assert.Same(telemetryFailure, exception);
        Assert.Equal(1, lease.DisposeCount);
        Assert.Contains(exception.Data.Values.Cast<object>(), value => ReferenceEquals(value, cleanupFailure));
    }

    [Fact]
    public async Task Privileged_acquisition_without_an_audit_emitter_fails_closed_and_releases_resources()
    {
        var accessor = new MutableAccessContextAccessor(
            PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("host-repair")));
        var source = new RecordingSessionSource();
        var factory = new GroundworkStoreSessionFactory(accessor, source);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            factory.CreateAsync(PersistenceAccessPolicy.Privileged).AsTask());

        Assert.Equal("Privileged Groundwork sessions require an audit emitter.", exception.Message);
        Assert.True(Assert.Single(source.Leases).IsDisposed);
    }

    [Fact]
    public async Task Session_disposal_is_idempotent_releases_the_lease_once_and_blocks_resource_reuse()
    {
        var accessor = new MutableAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var source = new RecordingSessionSource();
        var factory = new GroundworkStoreSessionFactory(accessor, source);
        var session = await factory.CreateAsync(PersistenceAccessPolicy.Ordinary);
        var lease = Assert.Single(source.Leases);

        await session.DisposeAsync();
        await session.DisposeAsync();

        Assert.Equal(1, lease.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => session.DocumentStore);
        Assert.Throws<ObjectDisposedException>(() => session.BoundedDocumentStore);
        Assert.Throws<ObjectDisposedException>(() => session.BoundedDocumentMutationStore);
    }

    [Fact]
    public async Task Concurrent_session_disposal_joins_one_cleanup_and_observes_the_same_failure()
    {
        var accessor = new MutableAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var cleanupFailure = new ApplicationException("lease cleanup failure");
        var lease = new TrackingLease(cleanupFailure);
        var source = new RecordingSessionSource
        {
            CreateResources = access => Resources(access, lease)
        };
        var factory = new GroundworkStoreSessionFactory(accessor, source);
        var session = await factory.CreateAsync(PersistenceAccessPolicy.Ordinary);

        var first = session.DisposeAsync().AsTask();
        var second = session.DisposeAsync().AsTask();
        var firstFailure = await Assert.ThrowsAsync<ApplicationException>(() => first);
        var secondFailure = await Assert.ThrowsAsync<ApplicationException>(() => second);

        Assert.Same(cleanupFailure, firstFailure);
        Assert.Same(cleanupFailure, secondFailure);
        Assert.Equal(1, lease.DisposeCount);
    }

    [Fact]
    public async Task Privileged_execution_records_acquisition_and_success_automatically()
    {
        var accessor = new MutableAccessContextAccessor(
            PersistenceAccessContext.PrivilegedScoped(
                new PersistenceScope("tenant-a"),
                new PersistenceAccessPurpose("repair-index")));
        var source = new RecordingSessionSource();
        var sink = new GroundworkPrivilegedAccessSink();
        var recorder = new GroundworkPrivilegedAccessRecorder(sink);
        var factory = new GroundworkStoreSessionFactory(accessor, source, recorder);

        var result = await factory.ExecutePrivilegedAsync(
            (session, _) => ValueTask.FromResult(session.Access.Kind));

        Assert.Equal(DocumentStoreAccessKind.PrivilegedScoped, result);
        var records = sink.Snapshot();
        Assert.Equal(2, records.Count);
        Assert.Equal(GroundworkPrivilegedAccessEventKind.Acquisition, records[0].EventKind);
        Assert.Equal(GroundworkPrivilegedAccessOutcome.Succeeded, records[1].Outcome);
        Assert.True(Assert.Single(source.Leases).IsDisposed);
    }

    [Fact]
    public async Task Across_scope_execution_rejects_other_contexts_before_opening_provider_resources()
    {
        var purpose = new PersistenceAccessPurpose("repair-all-tenants");
        var accessor = new MutableAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var source = new RecordingSessionSource();
        var sink = new GroundworkPrivilegedAccessSink();
        var factory = new GroundworkStoreSessionFactory(
            accessor,
            source,
            new GroundworkPrivilegedAccessRecorder(sink));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            factory.ExecutePrivilegedAcrossScopesAsync(
                (session, _) => ValueTask.FromResult(session.Access.Kind)).AsTask());
        accessor.Current = PersistenceAccessContext.PrivilegedScoped(new PersistenceScope("tenant-a"), purpose);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            factory.ExecutePrivilegedAcrossScopesAsync(
                (session, _) => ValueTask.FromResult(session.Access.Kind)).AsTask());
        Assert.Empty(source.Accesses);

        accessor.Current = PersistenceAccessContext.PrivilegedAcrossScopes(purpose);
        var accessKind = await factory.ExecutePrivilegedAcrossScopesAsync(
            (session, _) => ValueTask.FromResult(session.Access.Kind));

        Assert.Equal(DocumentStoreAccessKind.PrivilegedAcrossScopes, accessKind);
        Assert.Single(source.Accesses);
        Assert.Equal(GroundworkPrivilegedAccessOutcome.Succeeded, sink.Snapshot()[^1].Outcome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Privileged_execution_records_failure_or_cancellation_and_rethrows(bool canceled)
    {
        var accessor = new MutableAccessContextAccessor(
            PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("host-repair")));
        var source = new RecordingSessionSource();
        var sink = new GroundworkPrivilegedAccessSink();
        var recorder = new GroundworkPrivilegedAccessRecorder(sink);
        var factory = new GroundworkStoreSessionFactory(accessor, source, recorder);

        if (canceled)
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => factory.ExecutePrivilegedAsync<int>(
                (_, _) => ValueTask.FromException<int>(new OperationCanceledException())).AsTask());
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => factory.ExecutePrivilegedAsync<int>(
                (_, _) => ValueTask.FromException<int>(new InvalidOperationException("sensitive failure"))).AsTask());
        }

        var outcome = Assert.Single(
            sink.Snapshot(),
            record => record.EventKind == GroundworkPrivilegedAccessEventKind.Outcome);
        Assert.Equal(
            canceled ? GroundworkPrivilegedAccessOutcome.Canceled : GroundworkPrivilegedAccessOutcome.Failed,
            outcome.Outcome);
        Assert.Equal(canceled ? null : nameof(InvalidOperationException), outcome.FailureType);
        Assert.True(Assert.Single(source.Leases).IsDisposed);
    }

    [Fact]
    public async Task Privileged_success_with_lease_failure_records_failed_terminal_outcome_and_throws_cleanup()
    {
        var accessor = new MutableAccessContextAccessor(
            PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("host-repair")));
        var cleanupFailure = new ApplicationException("lease cleanup failure");
        var lease = new TrackingLease(cleanupFailure);
        var source = new RecordingSessionSource
        {
            CreateResources = access => Resources(access, lease)
        };
        var sink = new GroundworkPrivilegedAccessSink();
        var factory = new GroundworkStoreSessionFactory(
            accessor,
            source,
            new GroundworkPrivilegedAccessRecorder(sink));

        var exception = await Assert.ThrowsAsync<ApplicationException>(() => factory.ExecutePrivilegedAsync(
            (_, _) => ValueTask.FromResult(42)).AsTask());

        Assert.Same(cleanupFailure, exception);
        Assert.Equal(1, lease.DisposeCount);
        var outcome = Assert.Single(
            sink.Snapshot(),
            record => record.EventKind == GroundworkPrivilegedAccessEventKind.Outcome);
        Assert.Equal(GroundworkPrivilegedAccessOutcome.Failed, outcome.Outcome);
        Assert.Equal(nameof(ApplicationException), outcome.FailureType);
        Assert.Equal(nameof(ApplicationException), outcome.CleanupFailureType);
    }

    [Fact]
    public async Task Privileged_operation_failure_wins_over_cleanup_failure_and_both_are_audited()
    {
        var accessor = new MutableAccessContextAccessor(
            PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("host-repair")));
        var operationFailure = new InvalidOperationException("operation failure");
        var cleanupFailure = new ApplicationException("lease cleanup failure");
        var lease = new TrackingLease(cleanupFailure);
        var source = new RecordingSessionSource
        {
            CreateResources = access => Resources(access, lease)
        };
        var sink = new GroundworkPrivilegedAccessSink();
        var factory = new GroundworkStoreSessionFactory(
            accessor,
            source,
            new GroundworkPrivilegedAccessRecorder(sink));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.ExecutePrivilegedAsync<int>(
            (_, _) => ValueTask.FromException<int>(operationFailure)).AsTask());

        Assert.Same(operationFailure, exception);
        Assert.Equal(1, lease.DisposeCount);
        Assert.Contains(exception.Data.Values.Cast<object>(), value => ReferenceEquals(value, cleanupFailure));
        var outcome = Assert.Single(
            sink.Snapshot(),
            record => record.EventKind == GroundworkPrivilegedAccessEventKind.Outcome);
        Assert.Equal(GroundworkPrivilegedAccessOutcome.Failed, outcome.Outcome);
        Assert.Equal(nameof(InvalidOperationException), outcome.FailureType);
        Assert.Equal(nameof(ApplicationException), outcome.CleanupFailureType);
    }

    [Fact]
    public async Task Privileged_cancellation_wins_over_cleanup_failure_while_terminal_audit_is_failed()
    {
        var accessor = new MutableAccessContextAccessor(
            PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("host-repair")));
        var cancellationFailure = new OperationCanceledException("operation canceled");
        var cleanupFailure = new ApplicationException("lease cleanup failure");
        var lease = new TrackingLease(cleanupFailure);
        var source = new RecordingSessionSource
        {
            CreateResources = access => Resources(access, lease)
        };
        var sink = new GroundworkPrivilegedAccessSink();
        var factory = new GroundworkStoreSessionFactory(
            accessor,
            source,
            new GroundworkPrivilegedAccessRecorder(sink));

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => factory.ExecutePrivilegedAsync<int>(
            (_, _) => ValueTask.FromException<int>(cancellationFailure)).AsTask());

        Assert.Same(cancellationFailure, exception);
        Assert.Contains(exception.Data.Values.Cast<object>(), value => ReferenceEquals(value, cleanupFailure));
        var outcome = Assert.Single(
            sink.Snapshot(),
            record => record.EventKind == GroundworkPrivilegedAccessEventKind.Outcome);
        Assert.Equal(GroundworkPrivilegedAccessOutcome.Failed, outcome.Outcome);
        Assert.Equal(nameof(ApplicationException), outcome.FailureType);
        Assert.Equal(nameof(ApplicationException), outcome.CleanupFailureType);
    }

    [Fact]
    public async Task Operation_failure_wins_when_terminal_telemetry_fails_after_lease_cleanup()
    {
        var accessor = new MutableAccessContextAccessor(
            PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("host-repair")));
        var operationFailure = new InvalidOperationException("operation failure");
        var telemetryFailure = new ApplicationException("audit sink unavailable");
        var source = new RecordingSessionSource();
        var sink = new GroundworkPrivilegedAccessSink();
        var emitter = new ControllableEmitter(new GroundworkPrivilegedAccessRecorder(sink))
        {
            OutcomeFailure = telemetryFailure
        };
        var factory = new GroundworkStoreSessionFactory(accessor, source, emitter);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.ExecutePrivilegedAsync<int>(
            (_, _) => ValueTask.FromException<int>(operationFailure)).AsTask());

        Assert.Same(operationFailure, exception);
        Assert.True(Assert.Single(source.Leases).IsDisposed);
        Assert.Contains(exception.Data.Values.Cast<object>(), value => ReferenceEquals(value, telemetryFailure));
        Assert.Single(sink.Snapshot(), record => record.EventKind == GroundworkPrivilegedAccessEventKind.Acquisition);
        Assert.DoesNotContain(sink.Snapshot(), record => record.EventKind == GroundworkPrivilegedAccessEventKind.Outcome);
    }

    [Fact]
    public async Task Disposing_a_privileged_session_without_a_terminal_signal_records_abandoned_once()
    {
        var accessor = new MutableAccessContextAccessor(
            PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("host-repair")));
        var source = new RecordingSessionSource();
        var sink = new GroundworkPrivilegedAccessSink();
        var recorder = new GroundworkPrivilegedAccessRecorder(sink);
        var factory = new GroundworkStoreSessionFactory(accessor, source, recorder);
        var session = await factory.CreateAsync(PersistenceAccessPolicy.Privileged);

        await session.DisposeAsync();
        await session.DisposeAsync();

        var outcome = Assert.Single(
            sink.Snapshot(),
            record => record.EventKind == GroundworkPrivilegedAccessEventKind.Outcome);
        Assert.Equal(GroundworkPrivilegedAccessOutcome.Abandoned, outcome.Outcome);
    }

    [Fact]
    public async Task Privileged_session_rejects_duplicate_outcomes_and_ordinary_session_rejects_outcomes()
    {
        var accessor = new MutableAccessContextAccessor(
            PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("host-repair")));
        var source = new RecordingSessionSource();
        var recorder = new GroundworkPrivilegedAccessRecorder(new GroundworkPrivilegedAccessSink());
        var factory = new GroundworkStoreSessionFactory(accessor, source, recorder);
        await using var privileged = await factory.CreateAsync(PersistenceAccessPolicy.Privileged);

        privileged.RecordPrivilegedOutcome(GroundworkPrivilegedAccessOutcome.Succeeded);
        Assert.Throws<InvalidOperationException>(() =>
            privileged.RecordPrivilegedOutcome(GroundworkPrivilegedAccessOutcome.Succeeded));

        accessor.Current = PersistenceAccessContext.Global;
        await using var ordinary = await factory.CreateAsync(PersistenceAccessPolicy.Ordinary);
        Assert.Throws<InvalidOperationException>(() =>
            ordinary.RecordPrivilegedOutcome(GroundworkPrivilegedAccessOutcome.Succeeded));
    }

    private static GroundworkStoreSessionResources Resources(
        DocumentStoreAccess access,
        TrackingLease? lease = null) =>
        new(new TestDocumentStore(access), new TestBoundedDocumentStore(), lease ?? new TrackingLease());

    private sealed class MutableAccessContextAccessor(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; set; } = current;
    }

    private sealed class RecordingSessionSource : IGroundworkStoreSessionSource
    {
        public List<DocumentStoreAccess> Accesses { get; } = [];
        public List<TrackingLease> Leases { get; } = [];
        public bool FailNext { get; set; }
        public Func<DocumentStoreAccess, GroundworkStoreSessionResources>? CreateResources { get; set; }

        public ValueTask<GroundworkStoreSessionResources> OpenAsync(
            DocumentStoreAccess access,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Accesses.Add(access);
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("source failure");
            }

            var resources = CreateResources?.Invoke(access) ?? Resources(access);
            if (resources.Lease is TrackingLease lease)
                Leases.Add(lease);
            return ValueTask.FromResult(resources);
        }
    }

    private sealed class TrackingLease(Exception? failure = null) : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }
        public bool IsDisposed => DisposeCount > 0;

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return failure is null ? ValueTask.CompletedTask : ValueTask.FromException(failure);
        }
    }

    private sealed class ControllableEmitter(IGroundworkPrivilegedAccessEmitter inner)
        : IGroundworkPrivilegedAccessEmitter
    {
        public Exception? AcquisitionFailure { get; init; }
        public Exception? OutcomeFailure { get; init; }

        public GroundworkPrivilegedAccessAudit RecordAcquisition(DocumentStoreAccess access)
        {
            if (AcquisitionFailure is not null)
                throw AcquisitionFailure;
            return inner.RecordAcquisition(access);
        }

        public void RecordOutcome(
            GroundworkPrivilegedAccessAudit audit,
            GroundworkPrivilegedAccessOutcome outcome,
            Exception? failure = null,
            Exception? cleanupFailure = null)
        {
            if (OutcomeFailure is not null)
                throw OutcomeFailure;
            inner.RecordOutcome(audit, outcome, failure, cleanupFailure);
        }
    }

    private sealed class TestDocumentStore(DocumentStoreAccess access) : IDocumentStore
    {
        public DocumentStoreAccess Access { get; } = access;
        public TransactionBoundary TransactionBoundary => TransactionBoundary.CrossUnitAtomic;

        public Task<DocumentStoreWriteResult> SaveAsync(
            SaveDocumentRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentEnvelope?> LoadAsync(
            string documentKind,
            string id,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentStoreWriteResult> DeleteAsync(
            DeleteDocumentRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(
            DocumentStoreQuery query,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

#pragma warning disable GW0004
        public Task<DocumentQueryResult> QueryAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> AnyAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
#pragma warning restore GW0004

        public Task<IDocumentUnitOfWork> BeginAsync(
            DocumentCommitScope scope,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TestBoundedDocumentStore : IBoundedDocumentStore
    {
        public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
