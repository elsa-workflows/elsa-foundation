using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowExecutableRootWriteLeaseManagerTests
{
    // The default lease duration; the manager renews at a third of it.
    private static readonly TimeSpan LeaseDuration = new WorkflowExecutableGarbageCollectionOptions().RootWriteLeaseDuration;
    private static readonly TimeSpan RenewalCadence = LeaseDuration / 3;

    // Guards the handshakes with the background renewal loop so a regression fails fast instead of hanging CI.
    private static readonly TimeSpan AwaitTimeout = TimeSpan.FromSeconds(5);

    private readonly ManualTimerTimeProvider _timeProvider = new(new DateTimeOffset(2026, 6, 11, 12, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData("artifact")] [InlineData("owner")] [InlineData("empty-token")]
    public async Task Fenced_set_refuses_malformed_grants_without_using_or_releasing_an_unrelated_owner(string defect)
    {
        var inner = new InMemoryWorkflowExecutableStore(); await inner.SaveBatchAsync([Executable("a"), Executable("b"), Executable("other")]);
        var now = _timeProvider.GetUtcNow();
        var unrelated = (await inner.TryAcquireRootWriteLeaseAsync(defect == "artifact" ? "other" : "b", "unrelated-owner", now.Add(LeaseDuration), now))!;
        var returned = defect == "artifact" ? unrelated with { LeaseId = "writer" } :
            defect == "empty-token" ? unrelated with { LeaseId = "writer", ConcurrencyToken = "" } : unrelated;
        var provider = new MalformedGrantStore(inner, returned); var called = false;
        await Assert.ThrowsAsync<InvalidDataException>(() => NewManager(provider).ExecuteFencedAsync(
            [Executable("a").Identity, Executable("b").Identity], "writer", (_, _) => { called = true; return ValueTask.CompletedTask; }).AsTask());
        Assert.False(called); Assert.Empty(provider.Released.Where(lease => lease.ArtifactId != "a"));
        Assert.NotNull(await inner.TryBeginDeletionAsync("a", "gc", now.Add(LeaseDuration), now));
        Assert.True(await inner.RenewRootWriteLeaseAsync(unrelated, now.Add(LeaseDuration), now));
    }

    private sealed class MalformedGrantStore(IWorkflowExecutableStore inner, WorkflowExecutableRootWriteLease returned) : WorkflowExecutableStoreDecorator(inner)
    {
        public List<WorkflowExecutableRootWriteLease> Released { get; } = [];
        public override ValueTask<WorkflowExecutableRootWriteLease?> TryAcquireRootWriteLeaseAsync(string artifactId, string leaseId,
            DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            artifactId == "b" ? ValueTask.FromResult<WorkflowExecutableRootWriteLease?>(returned) : base.TryAcquireRootWriteLeaseAsync(artifactId, leaseId, expiresAt, now, cancellationToken);
        public override ValueTask ReleaseRootWriteLeaseAsync(WorkflowExecutableRootWriteLease lease, CancellationToken cancellationToken = default)
        { Released.Add(lease); return base.ReleaseRootWriteLeaseAsync(lease, cancellationToken); }
    }

    [Fact]
    public async Task Fenced_callback_receives_only_the_immutable_actual_complete_grants()
    {
        var store = new InMemoryWorkflowExecutableStore(); var dependency = Executable("dependency");
        var first = Executable("first", dependency); var second = Executable("second", dependency);
        await store.SaveBatchAsync([dependency, first, second]);
        IReadOnlyList<WorkflowExecutableRootWriteLease>? captured = null;
        await NewManager(store).ExecuteFencedAsync([second.Identity, first.Identity], "atomic-writer", async (grants, token) =>
        {
            captured = grants;
            Assert.Equal(new[] { "dependency", "first", "second" }, grants.Select(grant => grant.ArtifactId));
            Assert.Throws<NotSupportedException>(() => ((IList<WorkflowExecutableRootWriteLease>)grants).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<WorkflowExecutableRootWriteLease>)grants)[0] = grants[0] with { ConcurrencyToken = "changed" });
            foreach (var grant in grants)
            {
                Assert.Equal("atomic-writer", grant.LeaseId); Assert.False(string.IsNullOrWhiteSpace(grant.ConcurrencyToken));
                var now = _timeProvider.GetUtcNow();
                Assert.True(await store.RenewRootWriteLeaseAsync(grant, now.Add(LeaseDuration), now, token));
            }
            Assert.Equal("published", store.ExecuteUnderRootWriteLeases(grants, _timeProvider, () => "published", token));
        });
        foreach (var grant in captured!)
            Assert.False(await store.RenewRootWriteLeaseAsync(grant, _timeProvider.GetUtcNow().Add(LeaseDuration), _timeProvider.GetUtcNow()));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Legacy_manager_refuses_actual_grant_callback_without_using_its_old_overload(bool cancel)
    {
        var legacy = new LegacyRootManager(); IWorkflowExecutableRootWriteLeaseManager manager = legacy; var called = false;
        var token = new CancellationToken(cancel);
        Task Call() => manager.ExecuteFencedAsync([Executable("root").Identity], "writer", (_, _) => { called = true; return ValueTask.CompletedTask; }, token).AsTask();
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(Call);
        else await Assert.ThrowsAsync<NotSupportedException>(Call);
        Assert.False(called); Assert.Equal(0, legacy.Calls);
    }

    [Theory]
    [InlineData("expired")] [InlineData("released")] [InlineData("token")] [InlineData("guard")]
    [InlineData("cancelled")] [InlineData("valid")]
    public async Task Native_memory_commit_fence_checks_actual_tokens_and_fresh_time_before_publication(string scenario)
    {
        var store = new InMemoryWorkflowExecutableStore(); await store.SaveBatchAsync([Executable("a"), Executable("b")]);
        var now = _timeProvider.GetUtcNow();
        var first = (await store.TryAcquireRootWriteLeaseAsync("a", "writer", now.AddMinutes(1), now))!;
        var last = (await store.TryAcquireRootWriteLeaseAsync("b", "writer", now.AddMinutes(1), now))!;
        if (scenario is "released" or "guard") await store.ReleaseRootWriteLeaseAsync(last);
        if (scenario == "guard") Assert.NotNull(await store.TryBeginDeletionAsync("b", "gc", now.AddMinutes(2), now));
        if (scenario == "token") last = last with { ConcurrencyToken = "forged" };
        if (scenario == "expired") _timeProvider.Advance(TimeSpan.FromMinutes(1));
        var published = false; var cancellation = new CancellationToken(scenario == "cancelled");
        void Publish() => store.ExecuteUnderRootWriteLeases(new[] { first, last }, _timeProvider, () => published = true, cancellation);
        if (scenario == "valid") Publish();
        else if (scenario == "cancelled") Assert.ThrowsAny<OperationCanceledException>(Publish);
        else Assert.Throws<WorkflowExecutableRootWriteLeaseLostException>(Publish);
        Assert.Equal(scenario == "valid", published);
    }

    [Fact]
    public async Task ExecuteAsync_RenewsLeaseUntilWriteCompletes()
    {
        var store = new RecordingExecutableStore(new InMemoryWorkflowExecutableStore());
        await store.SaveAsync(Executable("artifact-1"));
        var manager = NewManager(store);
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var writeTask = manager.ExecuteAsync("artifact-1", "writer-1", async cancellationToken =>
        {
            writeStarted.SetResult();
            await finishWrite.Task.WaitAsync(cancellationToken);
        }).AsTask();

        await writeStarted.Task.WaitAsync(AwaitTimeout);
        await _timeProvider.TimerCreated.WaitAsync(AwaitTimeout);

        // Renew once at the cadence, pushing expiry out to cadence + duration.
        _timeProvider.AdvanceAndFire(RenewalCadence);
        await store.FirstRenewalObserved.WaitAsync(AwaitTimeout);

        // Land between the original expiry (duration) and the renewed one (cadence + duration) without firing the next
        // renewal, so the lease can only still be held because renewal extended it.
        _timeProvider.Advance(LeaseDuration - RenewalCadence / 2);

        var now = _timeProvider.GetUtcNow();
        var guardWhileWriting = await store.TryBeginDeletionAsync("artifact-1", "gc-1", now.AddMinutes(1), now);

        Assert.Null(guardWhileWriting);

        finishWrite.SetResult();
        await writeTask.WaitAsync(AwaitTimeout);

        var guardAfterWrite = await store.TryBeginDeletionAsync("artifact-1", "gc-1", now.AddMinutes(1), now);
        Assert.NotNull(guardAfterWrite);
    }

    [Fact]
    public async Task ExecuteAsync_LeasesTheDistinctDependencyClosureInOrdinalArtifactOrder()
    {
        var inner = new InMemoryWorkflowExecutableStore();
        var child = Executable("artifact-a");
        var root = Executable("artifact-z", child);
        await inner.SaveAsync(child);
        await inner.SaveAsync(root);
        var store = new RecordingExecutableStore(inner);
        var now = _timeProvider.GetUtcNow();
        var manager = NewManager(store);

        await manager.ExecuteAsync(root.Identity, "writer", async _ =>
        {
            Assert.Null(await store.TryBeginDeletionAsync("artifact-a", "gc-a", now.AddMinutes(1), now));
            Assert.Null(await store.TryBeginDeletionAsync("artifact-z", "gc-z", now.AddMinutes(1), now));
        });

        Assert.Equal(["artifact-a", "artifact-z"], store.AcquiredArtifactIds);
        Assert.NotNull(await store.TryBeginDeletionAsync("artifact-a", "gc-a", now.AddMinutes(1), now));
        Assert.NotNull(await store.TryBeginDeletionAsync("artifact-z", "gc-z", now.AddMinutes(1), now));
    }

    [Fact]
    public async Task ExecuteAsync_ReleasesAlreadyAcquiredClosureLeasesWhenALaterArtifactCannotBeLeased()
    {
        var inner = new InMemoryWorkflowExecutableStore();
        var child = Executable("artifact-a");
        var root = Executable("artifact-z", child);
        await inner.SaveAsync(child);
        await inner.SaveAsync(root);
        var now = _timeProvider.GetUtcNow();
        var blockingGuard = await inner.TryBeginDeletionAsync("artifact-z", "gc-z", now.AddMinutes(1), now);
        var store = new RecordingExecutableStore(inner);
        var manager = NewManager(store);
        var wrote = false;

        await Assert.ThrowsAsync<WorkflowExecutableRootWriteLeaseUnavailableException>(() =>
            manager.ExecuteAsync(root.Identity, "writer", _ =>
            {
                wrote = true;
                return ValueTask.CompletedTask;
            }).AsTask());

        Assert.NotNull(blockingGuard);
        Assert.False(wrote);
        Assert.Equal(["artifact-a", "artifact-z"], store.AcquiredArtifactIds);
        Assert.NotNull(await inner.TryBeginDeletionAsync("artifact-a", "gc-a", now.AddMinutes(1), now));
    }

    [Fact]
    public async Task ExecuteAsync_ReportsASuccessfulWriteAsSuccessWhenALeaseReleaseFails()
    {
        var inner = new InMemoryWorkflowExecutableStore();
        var child = Executable("artifact-a");
        var root = Executable("artifact-z", child);
        await inner.SaveAsync(child);
        await inner.SaveAsync(root);
        var store = new RecordingExecutableStore(inner) { FailReleases = true };
        var logger = new RecordingLogger<WorkflowExecutableRootWriteLeaseManager>();
        var manager = NewManager(store, logger);
        var wrote = false;

        await manager.ExecuteAsync(root.Identity, "writer", _ =>
        {
            wrote = true;
            return ValueTask.CompletedTask;
        });

        Assert.True(wrote);
        Assert.Equal(2, store.ReleaseAttempts);
        Assert.Collection(
            logger.Entries,
            entry => Assert.Equal((LogLevel.Warning, typeof(InvalidOperationException)), (entry.Level, entry.Exception?.GetType())),
            entry => Assert.Equal((LogLevel.Warning, typeof(InvalidOperationException)), (entry.Level, entry.Exception?.GetType())));
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("artifact-a", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("artifact-z", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_SurfacesTheWriteFailureUnchangedWhenAReleaseAlsoFails()
    {
        var store = new RecordingExecutableStore(new InMemoryWorkflowExecutableStore()) { FailReleases = true };
        await store.SaveAsync(Executable("artifact-1"));
        var manager = NewManager(store, new RecordingLogger<WorkflowExecutableRootWriteLeaseManager>());
        var writeFailure = new InvalidDataException("The write failed.");

        var thrown = await Assert.ThrowsAsync<InvalidDataException>(() =>
            manager.ExecuteAsync("artifact-1", "writer", _ => throw writeFailure).AsTask());

        Assert.Same(writeFailure, thrown);
        Assert.Equal(1, store.ReleaseAttempts);
    }

    [Fact]
    public async Task ExecuteAsync_CancelsTheWriteAndSurfacesTheLostLeaseWhenARenewalIsRefused()
    {
        var store = new RecordingExecutableStore(new InMemoryWorkflowExecutableStore()) { RefuseRenewals = true };
        await store.SaveAsync(Executable("artifact-1"));
        var manager = NewManager(store);
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writeCancelled = false;

        var writeTask = manager.ExecuteAsync("artifact-1", "writer", async cancellationToken =>
        {
            writeStarted.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                writeCancelled = true;
                throw;
            }
        }).AsTask();

        await writeStarted.Task.WaitAsync(AwaitTimeout);
        await _timeProvider.TimerCreated.WaitAsync(AwaitTimeout);
        _timeProvider.AdvanceAndFire(RenewalCadence);

        await Assert.ThrowsAsync<WorkflowExecutableRootWriteLeaseLostException>(() => writeTask.WaitAsync(AwaitTimeout));
        Assert.True(writeCancelled);
    }

    [Fact]
    public async Task Atomic_root_set_leases_the_distinct_union_once_in_ordinal_order()
    {
        var inner = new InMemoryWorkflowExecutableStore();
        var shared = Executable("artifact-a"); var first = Executable("artifact-z", shared);
        var second = Executable("artifact-b", shared);
        foreach (var executable in new[] { shared, first, second }) await inner.SaveAsync(executable);
        var store = new RecordingExecutableStore(inner); var manager = NewManager(store);
        var now = _timeProvider.GetUtcNow(); var wrote = false;
        await manager.ExecuteAsync([first.Identity, second.Identity, first.Identity], "cohort:unique-attempt", async _ =>
        {
            wrote = true;
            foreach (var id in new[] { "artifact-a", "artifact-b", "artifact-z" })
                Assert.Null(await store.TryBeginDeletionAsync(id, "gc", now.AddMinutes(1), now));
        });
        Assert.True(wrote);
        Assert.Equal(["artifact-a", "artifact-b", "artifact-z"], store.AcquiredArtifactIds);
        Assert.Equal(3, store.ReleaseAttempts);
        foreach (var id in store.AcquiredArtifactIds)
            Assert.NotNull(await store.TryBeginDeletionAsync(id, "gc", now.AddMinutes(1), now));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Atomic_root_set_validates_all_full_pins_before_any_acquisition(bool changeMetadataOnly)
    {
        var inner = new InMemoryWorkflowExecutableStore(); var first = Executable("artifact-a");
        var second = Executable("artifact-z");
        await inner.SaveAsync(first); await inner.SaveAsync(second);
        var store = new RecordingExecutableStore(inner); var wrote = false;
        var forged = changeMetadataOnly ? second.Identity with { DefinitionVersionId = "another-version" } :
            second.Identity with { ArtifactHash = "another-hash" };
        await Assert.ThrowsAsync<WorkflowExecutableDependencyGraphException>(() =>
            NewManager(store).ExecuteAsync([first.Identity, forged], "writer", _ =>
            { wrote = true; return ValueTask.CompletedTask; }).AsTask());
        Assert.False(wrote); Assert.Empty(store.AcquiredArtifactIds);
    }

    [Fact]
    public async Task Atomic_root_set_releases_partial_acquisitions_when_deletion_wins()
    {
        var inner = new InMemoryWorkflowExecutableStore(); var first = Executable("artifact-a");
        var second = Executable("artifact-z"); await inner.SaveAsync(first); await inner.SaveAsync(second);
        var now = _timeProvider.GetUtcNow();
        Assert.NotNull(await inner.TryBeginDeletionAsync("artifact-z", "gc", now.AddMinutes(1), now));
        var store = new RecordingExecutableStore(inner); var wrote = false;
        await Assert.ThrowsAsync<WorkflowExecutableRootWriteLeaseUnavailableException>(() =>
            NewManager(store).ExecuteAsync([first.Identity, second.Identity], "writer", _ =>
            { wrote = true; return ValueTask.CompletedTask; }).AsTask());
        Assert.False(wrote); Assert.Equal(1, store.ReleaseAttempts);
        Assert.NotNull(await inner.TryBeginDeletionAsync("artifact-a", "gc", now.AddMinutes(1), now));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Atomic_root_set_refuses_expired_grants_before_write(bool duringRenewal, bool expire)
    {
        var inner = new InMemoryWorkflowExecutableStore(); var first = Executable("artifact-a");
        var second = Executable("artifact-z"); await inner.SaveAsync(first); await inner.SaveAsync(second);
        var hook = new DelayedRootStore(inner, _timeProvider, duringRenewal,
            expire ? LeaseDuration + TimeSpan.FromTicks(1) : LeaseDuration / 2);
        var wrote = false;
        var call = NewManager(hook).ExecuteAsync([first.Identity, second.Identity], "writer", async _ =>
        {
            wrote = true;
            var now = _timeProvider.GetUtcNow();
            foreach (var id in new[] { "artifact-a", "artifact-z" })
                Assert.Null(await inner.TryBeginDeletionAsync(id, "gc", now.AddMinutes(1), now));
        }).AsTask();
        if (expire) await Assert.ThrowsAsync<WorkflowExecutableRootWriteLeaseLostException>(() => call);
        else await call;
        Assert.Equal(!expire, wrote); Assert.Equal(2, hook.Releases);
        var after = _timeProvider.GetUtcNow();
        foreach (var id in new[] { "artifact-a", "artifact-z" })
            Assert.NotNull(await inner.TryBeginDeletionAsync(id, "gc", after.AddMinutes(1), after));
    }

    [Fact]
    public async Task Legacy_manager_refuses_atomic_set_without_invoking_its_old_fallback()
    {
        var legacy = new LegacyRootManager(); IWorkflowExecutableRootWriteLeaseManager manager = legacy;
        var wrote = false;
        await Assert.ThrowsAsync<NotSupportedException>(() => manager.ExecuteAsync([Executable("artifact").Identity],
            "writer", _ => { wrote = true; return ValueTask.CompletedTask; }).AsTask());
        Assert.False(wrote); Assert.Equal(0, legacy.Calls);
        await manager.ExecuteAsync("artifact", "ordinary", _ => { wrote = true; return ValueTask.CompletedTask; });
        Assert.True(wrote); Assert.Equal(1, legacy.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Atomic_root_set_renews_every_grant_and_surfaces_nonfirst_loss(bool loseLast)
    {
        var inner = new InMemoryWorkflowExecutableStore(); var shared = Executable("artifact-a");
        var first = Executable("artifact-z", shared); var second = Executable("artifact-b", shared);
        foreach (var executable in new[] { shared, first, second }) await inner.SaveAsync(executable);
        var store = new PeriodicSetStore(inner, loseLast);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = false;
        var call = NewManager(store).ExecuteAsync([first.Identity, second.Identity], "writer", async token =>
        {
            started.SetResult();
            try { await finish.Task.WaitAsync(token); }
            catch (OperationCanceledException) { cancelled = true; throw; }
        }).AsTask();
        await started.Task.WaitAsync(AwaitTimeout); await _timeProvider.TimerCreated.WaitAsync(AwaitTimeout);
        _timeProvider.AdvanceAndFire(RenewalCadence);
        await store.PeriodicPass.Task.WaitAsync(AwaitTimeout);
        if (loseLast)
        {
            await Assert.ThrowsAsync<WorkflowExecutableRootWriteLeaseLostException>(() => call.WaitAsync(AwaitTimeout));
            Assert.True(cancelled);
        }
        else
        {
            _timeProvider.Advance(LeaseDuration - RenewalCadence / 2);
            var now = _timeProvider.GetUtcNow();
            foreach (var id in new[] { "artifact-a", "artifact-b", "artifact-z" })
                Assert.Null(await inner.TryBeginDeletionAsync(id, "gc", now.AddMinutes(1), now));
            finish.SetResult(); await call.WaitAsync(AwaitTimeout); Assert.False(cancelled);
        }
        Assert.Equal(["artifact-a", "artifact-b", "artifact-z"], store.PeriodicIds);
        Assert.Equal(3, store.Releases);
    }

    [Fact]
    public async Task Atomic_root_set_captures_the_callers_collection_before_async_graph_reads()
    {
        var inner = new InMemoryWorkflowExecutableStore(); var first = Executable("artifact-a");
        var second = Executable("artifact-b"); var replacement = Executable("artifact-z");
        foreach (var executable in new[] { first, second, replacement }) await inner.SaveAsync(executable);
        WorkflowExecutableIdentity[] roots = [first.Identity, second.Identity];
        var changing = new ChangingRootStore(inner, () => roots[1] = replacement.Identity);
        var recorded = new RecordingExecutableStore(changing);
        await NewManager(recorded).ExecuteAsync(roots, "writer", _ => ValueTask.CompletedTask);
        Assert.Equal(["artifact-a", "artifact-b"], recorded.AcquiredArtifactIds);
    }

    private sealed class ChangingRootStore(IWorkflowExecutableStore inner, Action change) : WorkflowExecutableStoreDecorator(inner)
    {
        private bool changed;
        public override ValueTask<RuntimeStorePage<WorkflowExecutable>> ListPageAsync(RuntimeStorePageRequest request,
            CancellationToken cancellationToken = default)
        { if (!changed) { changed = true; change(); } return base.ListPageAsync(request, cancellationToken); }
    }

    private sealed class PeriodicSetStore(IWorkflowExecutableStore inner, bool loseLast) : WorkflowExecutableStoreDecorator(inner)
    {
        private readonly Dictionary<string, int> counts = new(StringComparer.Ordinal);
        public List<string> PeriodicIds { get; } = [];
        public TaskCompletionSource PeriodicPass { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Releases { get; private set; }
        public override async ValueTask<bool> RenewRootWriteLeaseAsync(WorkflowExecutableRootWriteLease lease,
            DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            var count = counts.GetValueOrDefault(lease.ArtifactId) + 1; counts[lease.ArtifactId] = count;
            var periodic = count > 1;
            if (periodic) PeriodicIds.Add(lease.ArtifactId);
            var renewed = !(periodic && loseLast && lease.ArtifactId == "artifact-z") &&
                await base.RenewRootWriteLeaseAsync(lease, expiresAt, now, cancellationToken);
            if (periodic && lease.ArtifactId == "artifact-z") PeriodicPass.TrySetResult();
            return renewed;
        }
        public override ValueTask ReleaseRootWriteLeaseAsync(WorkflowExecutableRootWriteLease lease,
            CancellationToken cancellationToken = default)
        { Releases++; return base.ReleaseRootWriteLeaseAsync(lease, cancellationToken); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Atomic_root_set_requested_cancellation_precedes_acquisition_or_legacy_fallback(bool legacy)
    {
        var store = new RecordingExecutableStore(new InMemoryWorkflowExecutableStore());
        var old = new LegacyRootManager();
        IWorkflowExecutableRootWriteLeaseManager manager = legacy ? old : NewManager(store);
        var token = new CancellationToken(canceled: true); var wrote = false;
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            manager.ExecuteAsync([Executable("artifact").Identity], "writer", _ =>
            { wrote = true; return ValueTask.CompletedTask; }, token).AsTask());
        Assert.Equal(token, exception.CancellationToken); Assert.False(wrote);
        Assert.Empty(store.AcquiredArtifactIds); Assert.Equal(0, old.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Atomic_root_set_deadline_cancels_write_while_periodic_provider_is_blocked(bool expire)
    {
        var clock = new DeadlineClock(_timeProvider.GetUtcNow()); var inner = new InMemoryWorkflowExecutableStore();
        var first = Executable("artifact-a"); var last = Executable("artifact-z");
        await inner.SaveAsync(first); await inner.SaveAsync(last);
        var store = new BlockedPeriodicStore(inner);
        var manager = new WorkflowExecutableRootWriteLeaseManager(store, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), clock);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken activeToken = default;
        var call = manager.ExecuteAsync([first.Identity, last.Identity], "writer", async token =>
        {
            activeToken = token; started.SetResult();
            await finish.Task.WaitAsync(token);
        }).AsTask();
        await started.Task.WaitAsync(AwaitTimeout); clock.AdvanceAndFire(RenewalCadence);
        await store.Blocked.Task.WaitAsync(AwaitTimeout);
        try
        {
            clock.AdvanceAndFire(expire ? LeaseDuration - RenewalCadence + TimeSpan.FromTicks(1) : TimeSpan.FromSeconds(1));
            Assert.Equal(expire, activeToken.IsCancellationRequested);
            Assert.False(store.Release.Task.IsCompleted); // Cancellation precedes any response from the blocked provider.
        }
        finally { store.Release.TrySetResult(); finish.TrySetResult(); }
        if (expire) await Assert.ThrowsAsync<WorkflowExecutableRootWriteLeaseLostException>(() => call.WaitAsync(AwaitTimeout));
        else await call.WaitAsync(AwaitTimeout);
        Assert.Equal(2, store.Releases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Atomic_root_set_provider_outage_cancels_active_write_and_preserves_cause(bool providerCancellation)
    {
        var clock = new DeadlineClock(_timeProvider.GetUtcNow()); var inner = new InMemoryWorkflowExecutableStore();
        var first = Executable("artifact-a"); var last = Executable("artifact-z");
        await inner.SaveAsync(first); await inner.SaveAsync(last);
        Exception outage = providerCancellation ? new OperationCanceledException("unrequested lease provider cancellation") :
            new IOException("lease provider unavailable");
        var store = new BlockedPeriodicStore(inner, outage);
        var manager = new WorkflowExecutableRootWriteLeaseManager(store, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), clock);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var call = manager.ExecuteAsync([first.Identity, last.Identity], "writer", async token =>
        {
            started.SetResult();
            try { await finish.Task.WaitAsync(token); }
            finally { if (token.IsCancellationRequested) cancelled.TrySetResult(); }
        }).AsTask();
        await started.Task.WaitAsync(AwaitTimeout); clock.AdvanceAndFire(RenewalCadence);
        try { await cancelled.Task.WaitAsync(AwaitTimeout); }
        finally { finish.TrySetResult(); }
        Assert.Same(outage, await Assert.ThrowsAnyAsync<Exception>(() => call.WaitAsync(AwaitTimeout)));
        Assert.Equal(2, store.Releases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Atomic_root_set_checks_deadline_at_completion_even_if_timer_dispatch_is_delayed(bool expire)
    {
        var clock = new DeadlineClock(_timeProvider.GetUtcNow()); var inner = new InMemoryWorkflowExecutableStore();
        var first = Executable("artifact-a"); var last = Executable("artifact-z");
        await inner.SaveAsync(first); await inner.SaveAsync(last);
        var recorded = new RecordingExecutableStore(inner);
        var manager = new WorkflowExecutableRootWriteLeaseManager(recorded,
            Options.Create(new WorkflowExecutableGarbageCollectionOptions()), clock);
        var call = manager.ExecuteAsync([first.Identity, last.Identity], "writer", _ =>
        {
            clock.AdvanceWithoutFiring(expire ? LeaseDuration + TimeSpan.FromTicks(1) : TimeSpan.FromSeconds(1));
            return ValueTask.CompletedTask;
        }).AsTask();
        if (expire) await Assert.ThrowsAsync<WorkflowExecutableRootWriteLeaseLostException>(() => call);
        else await call;
        Assert.Equal(2, recorded.ReleaseAttempts);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Atomic_root_set_contains_throwing_cancellation_registration_and_preserves_both_causes(bool outage, bool delayOutage)
    {
        var clock = new DeadlineClock(_timeProvider.GetUtcNow()); var inner = new InMemoryWorkflowExecutableStore();
        var first = Executable("artifact-a"); var last = Executable("artifact-z");
        await inner.SaveAsync(first); await inner.SaveAsync(last);
        var providerFailure = new IOException("renewal unavailable");
        var registrationFailure = new InvalidOperationException("cancellation observer failed");
        var store = new BlockedPeriodicStore(inner, outage ? providerFailure : null, delayOutage);
        var manager = new WorkflowExecutableRootWriteLeaseManager(store,
            Options.Create(new WorkflowExecutableGarbageCollectionOptions()), clock);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var call = manager.ExecuteAsync([first.Identity, last.Identity], "writer", async token =>
        {
            var pending = finish.Task.WaitAsync(token);
            using var registration = token.Register(() =>
            { observed.TrySetResult(); throw registrationFailure; });
            started.SetResult(); await pending;
        }).AsTask();
        await started.Task.WaitAsync(AwaitTimeout);
        try
        {
            clock.AdvanceAndFire(RenewalCadence);
            await store.Blocked.Task.WaitAsync(AwaitTimeout);
            if (!outage || delayOutage) clock.AdvanceAndFire(LeaseDuration - RenewalCadence + TimeSpan.FromTicks(1));
            await observed.Task.WaitAsync(AwaitTimeout);
        }
        finally { store.Release.TrySetResult(); finish.TrySetResult(); }
        var thrown = await Assert.ThrowsAsync<AggregateException>(() => call.WaitAsync(AwaitTimeout));
        var causes = thrown.Flatten().InnerExceptions;
        Assert.Contains(registrationFailure, causes);
        if (outage) Assert.Contains(providerFailure, causes);
        if (!outage || delayOutage) Assert.Contains(causes, cause => cause is WorkflowExecutableRootWriteLeaseLostException);
        if (delayOutage) Assert.Equal(3, causes.Count);
        Assert.Equal(2, store.Releases);
    }

    private sealed class BlockedPeriodicStore(IWorkflowExecutableStore inner, Exception? outage = null, bool delayOutage = false) : WorkflowExecutableStoreDecorator(inner)
    {
        private int lastRenewals;
        public TaskCompletionSource Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Releases { get; private set; }
        public override async ValueTask<bool> RenewRootWriteLeaseAsync(WorkflowExecutableRootWriteLease lease,
            DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            if (lease.ArtifactId == "artifact-z" && ++lastRenewals > 1)
            {
                Blocked.TrySetResult(); if (outage is not null && !delayOutage) throw outage;
                await Release.Task; // Deliberately ignores cancellation to expose the independent deadline guard.
                if (outage is not null) throw outage;
            }
            return await base.RenewRootWriteLeaseAsync(lease, expiresAt, now, cancellationToken);
        }
        public override ValueTask ReleaseRootWriteLeaseAsync(WorkflowExecutableRootWriteLease lease,
            CancellationToken cancellationToken = default)
        { Releases++; return base.ReleaseRootWriteLeaseAsync(lease, cancellationToken); }
    }

    private sealed class DeadlineClock(DateTimeOffset initial) : TimeProvider
    {
        private readonly object gate = new();
        private DateTimeOffset now = initial;
        private readonly List<ClockTimer> timers = [];
        public override DateTimeOffset GetUtcNow() { lock (gate) return now; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (gate) { var timer = new ClockTimer(this, callback, state); timers.Add(timer); timer.Change(dueTime, period); return timer; }
        }
        public void AdvanceWithoutFiring(TimeSpan delta) { lock (gate) now += delta; }
        public void AdvanceAndFire(TimeSpan delta)
        {
            ClockTimer[] due;
            lock (gate) { now += delta; due = timers.Where(timer => timer.Due <= now).ToArray(); foreach (var timer in due) timer.Due = DateTimeOffset.MaxValue; }
            foreach (var timer in due) timer.Fire();
        }
        private sealed class ClockTimer(DeadlineClock clock, TimerCallback callback, object? state) : ITimer
        {
            private bool disposed;
            public DateTimeOffset Due { get; set; } = DateTimeOffset.MaxValue;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            { lock (clock.gate) { if (disposed) return false; Due = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : clock.now + dueTime; return true; } }
            public void Fire() { lock (clock.gate) { if (disposed) return; } callback(state); }
            public void Dispose() { lock (clock.gate) { disposed = true; Due = DateTimeOffset.MaxValue; } }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private sealed class LegacyRootManager : IWorkflowExecutableRootWriteLeaseManager
    {
        public int Calls { get; private set; }
        public ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write,
            CancellationToken cancellationToken = default) { Calls++; return write(cancellationToken); }
    }

    private sealed class DelayedRootStore(IWorkflowExecutableStore inner, ManualTimerTimeProvider clock,
        bool duringRenewal, TimeSpan delay) : WorkflowExecutableStoreDecorator(inner)
    {
        public int Releases { get; private set; }
        public override ValueTask<WorkflowExecutableRootWriteLease?> TryAcquireRootWriteLeaseAsync(string artifactId,
            string leaseId, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            if (!duringRenewal && artifactId == "artifact-z") clock.Advance(delay);
            return base.TryAcquireRootWriteLeaseAsync(artifactId, leaseId, expiresAt, now, cancellationToken);
        }
        public override async ValueTask<bool> RenewRootWriteLeaseAsync(WorkflowExecutableRootWriteLease lease,
            DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            var renewed = await base.RenewRootWriteLeaseAsync(lease, expiresAt, now, cancellationToken);
            if (duringRenewal && lease.ArtifactId == "artifact-z") clock.Advance(delay);
            return renewed;
        }
        public override ValueTask ReleaseRootWriteLeaseAsync(WorkflowExecutableRootWriteLease lease,
            CancellationToken cancellationToken = default)
        { Releases++; return base.ReleaseRootWriteLeaseAsync(lease, cancellationToken); }
    }

    private WorkflowExecutableRootWriteLeaseManager NewManager(
        IWorkflowExecutableStore store,
        ILogger<WorkflowExecutableRootWriteLeaseManager>? logger = null) =>
        new(
            store,
            Options.Create(new WorkflowExecutableGarbageCollectionOptions()),
            _timeProvider,
            logger);

    private WorkflowExecutable Executable(string artifactId, params WorkflowExecutable[] dependencies) =>
        new(
            identity: new WorkflowExecutableIdentity(artifactId, "definition-1", "version-1", "1.0.0", Hash(artifactId)),
            rootActivity: new ExecutableNode(
                executableNodeId: "node-root",
                authoredActivityId: "activity-root",
                activityType: "Test.Root",
                activityTypeVersion: "1.0.0",
                descriptor: new RuntimeActivityDescriptor(
                    "Test",
                    RuntimeActivityDescriptor.InitialSchemaVersion,
                    JsonSerializer.SerializeToElement(new { })),
                inputBindings: new Dictionary<string, RuntimeInputBinding>(),
                metadata: new Dictionary<string, string>()),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: _timeProvider.GetUtcNow(),
            compatibilityMetadata: new Dictionary<string, string>(),
            inputContract: null,
            dependencies: dependencies.Select(dependency => new WorkflowExecutableDependency(
                dependency.Identity.ArtifactId,
                dependency.Identity.ArtifactHash,
                ["node-root"])).ToArray(),
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);

    private static string Hash(string artifactId) => $"sha256:{artifactId}";

    private sealed class RecordingExecutableStore(IWorkflowExecutableStore inner) : IWorkflowExecutableStore
    {
        private readonly TaskCompletionSource _firstRenewalObserved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> AcquiredArtifactIds { get; } = [];

        /// <summary>Makes every release throw, as a store whose database is unreachable would.</summary>
        public bool FailReleases { get; init; }

        /// <summary>Makes every renewal report the lease lost.</summary>
        public bool RefuseRenewals { get; init; }

        public int ReleaseAttempts { get; private set; }

        /// <summary>Completes once a renewal has been applied, so the test can safely move the clock again.</summary>
        public Task FirstRenewalObserved => _firstRenewalObserved.Task;

        public ValueTask SaveAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(executable, cancellationToken);

        public ValueTask<bool> DeleteAsync(string artifactId, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(artifactId, cancellationToken);

        public ValueTask<WorkflowExecutableRootWriteLease?> TryAcquireRootWriteLeaseAsync(
            string artifactId,
            string leaseId,
            DateTimeOffset expiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            AcquiredArtifactIds.Add(artifactId);
            return inner.TryAcquireRootWriteLeaseAsync(artifactId, leaseId, expiresAt, now, cancellationToken);
        }

        public async ValueTask<bool> RenewRootWriteLeaseAsync(
            WorkflowExecutableRootWriteLease lease,
            DateTimeOffset expiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            var renewed = !RefuseRenewals && await inner.RenewRootWriteLeaseAsync(lease, expiresAt, now, cancellationToken);
            _firstRenewalObserved.TrySetResult();
            return renewed;
        }

        public ValueTask ReleaseRootWriteLeaseAsync(WorkflowExecutableRootWriteLease lease, CancellationToken cancellationToken = default)
        {
            ReleaseAttempts++;
            return FailReleases
                ? throw new InvalidOperationException("The lease store is unreachable.")
                : inner.ReleaseRootWriteLeaseAsync(lease, cancellationToken);
        }

        public ValueTask<WorkflowExecutableDeletionGuard?> TryBeginDeletionAsync(
            string artifactId,
            string operationId,
            DateTimeOffset expiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            inner.TryBeginDeletionAsync(artifactId, operationId, expiresAt, now, cancellationToken);

        public ValueTask<bool> CancelDeletionAsync(WorkflowExecutableDeletionGuard guard, CancellationToken cancellationToken = default) =>
            inner.CancelDeletionAsync(guard, cancellationToken);

        public ValueTask<bool> DeleteAsync(WorkflowExecutableDeletionGuard guard, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(guard, now, cancellationToken);

        public ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default) =>
            inner.FindAsync(artifactId, cancellationToken);

        public ValueTask<RuntimeStorePage<WorkflowExecutable>> ListPageAsync(
            RuntimeStorePageRequest request,
            CancellationToken cancellationToken = default) =>
            inner.ListPageAsync(request, cancellationToken);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, exception, formatter(state, exception)));
    }
}
