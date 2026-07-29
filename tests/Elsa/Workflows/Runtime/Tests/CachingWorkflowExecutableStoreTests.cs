using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class CachingWorkflowExecutableStoreTests
{
    private const string MeterName = "Elsa.Workflows.Runtime.ExecutableCache";
    private const string RequestCounterName = "elsa.workflow_executable_cache.requests";
    private const string EvictionCounterName = "elsa.workflow_executable_cache.evictions";
    private const string ProviderLoadDurationName = "elsa.workflow_executable_cache.provider_load.duration";

    [Fact]
    public void Options_HaveBoundedDefaults_AndConditionalValidation()
    {
        var defaults = new WorkflowExecutableCacheOptions();

        Assert.True(defaults.Enabled);
        Assert.Equal(256, defaults.Capacity);
        defaults.Validate();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorkflowExecutableCacheOptions { Enabled = true, Capacity = 0 }.Validate());
        new WorkflowExecutableCacheOptions { Enabled = false, Capacity = 0 }.Validate();
    }

    [Fact]
    public async Task ConstructorAndPublicMethods_ValidateArguments()
    {
        var provider = new ControllableStore();
        var enabled = new WorkflowExecutableCacheOptions();

        Assert.Throws<ArgumentNullException>(() => new CachingWorkflowExecutableStore(null!, enabled));
        Assert.Throws<ArgumentNullException>(() => new CachingWorkflowExecutableStore(provider, null!));
        Assert.Throws<ArgumentException>(() => new CachingWorkflowExecutableStore(
            provider,
            new WorkflowExecutableCacheOptions { Enabled = false, Capacity = 0 }));
        var sharedState = new WorkflowExecutableCache(enabled);
        Assert.Throws<ArgumentNullException>(() =>
            new InvalidatingWorkflowExecutableStore(null!, sharedState, "tenant-a"));
        Assert.Throws<ArgumentNullException>(() =>
            new InvalidatingWorkflowExecutableStore(provider, null!, "tenant-a"));
        Assert.Throws<ArgumentException>(() =>
            new InvalidatingWorkflowExecutableStore(provider, sharedState, " "));

        var cache = NewCache(provider);
        await Assert.ThrowsAsync<ArgumentNullException>(() => cache.SaveAsync(null!).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => cache.SaveAsync(NewExecutable(string.Empty)).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => cache.DeleteAsync(" ").AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => cache.FindAsync(" ").AsTask());
    }

    [Fact]
    public async Task RepeatedFind_ReusesOnePositiveProviderLoad()
    {
        var expected = NewExecutable("artifact-a");
        var provider = new ControllableStore(expected);
        var cache = NewCache(provider);

        var first = await cache.FindAsync(expected.Identity.ArtifactId);
        var second = await cache.FindAsync(expected.Identity.ArtifactId);

        Assert.Same(expected, first);
        Assert.Same(expected, second);
        Assert.Equal(1, provider.FindCalls(expected.Identity.ArtifactId));
    }

    [Fact]
    public async Task SharedState_ReusesEntriesAcrossAdaptersInTheSamePartition()
    {
        var expected = NewExecutable("artifact-a");
        var provider = new ControllableStore(expected);
        var state = new WorkflowExecutableCache(new WorkflowExecutableCacheOptions());
        var first = new CachingWorkflowExecutableStore(
            provider,
            state,
            "tenant-a",
            provider.FindAsync);
        var second = new CachingWorkflowExecutableStore(
            provider,
            state,
            "tenant-a",
            provider.FindAsync);

        Assert.Same(expected, await first.FindAsync(expected.Identity.ArtifactId));
        Assert.Same(expected, await second.FindAsync(expected.Identity.ArtifactId));
        Assert.Equal(1, provider.FindCalls(expected.Identity.ArtifactId));
    }

    [Fact]
    public async Task SharedState_DoesNotReuseEntriesAcrossPersistencePartitions()
    {
        var expected = NewExecutable("artifact-a");
        var provider = new ControllableStore(expected);
        var state = new WorkflowExecutableCache(new WorkflowExecutableCacheOptions());
        var first = new CachingWorkflowExecutableStore(
            provider,
            state,
            "tenant-a",
            provider.FindAsync);
        var second = new CachingWorkflowExecutableStore(
            provider,
            state,
            "tenant-b",
            provider.FindAsync);

        Assert.Same(expected, await first.FindAsync(expected.Identity.ArtifactId));
        Assert.Same(expected, await second.FindAsync(expected.Identity.ArtifactId));
        Assert.Equal(2, provider.FindCalls(expected.Identity.ArtifactId));
    }

    [Fact]
    public async Task SharedState_AllPartitionInvalidation_RemovesEveryArtifactCopy()
    {
        var expected = NewExecutable("artifact-a");
        var provider = new ControllableStore(expected);
        var state = new WorkflowExecutableCache(new WorkflowExecutableCacheOptions());
        var first = new CachingWorkflowExecutableStore(
            provider,
            state,
            "tenant-a",
            provider.FindAsync);
        var second = new CachingWorkflowExecutableStore(
            provider,
            state,
            "tenant-b",
            provider.FindAsync);

        Assert.Same(expected, await first.FindAsync(expected.Identity.ArtifactId));
        Assert.Same(expected, await second.FindAsync(expected.Identity.ArtifactId));

        var invalidatingStore = new InvalidatingWorkflowExecutableStore(provider, state, partition: null);
        await invalidatingStore.SaveAsync(expected);

        Assert.Same(expected, await first.FindAsync(expected.Identity.ArtifactId));
        Assert.Same(expected, await second.FindAsync(expected.Identity.ArtifactId));
        Assert.Equal(4, provider.FindCalls(expected.Identity.ArtifactId));
    }

    [Fact]
    public async Task ConcurrentSameKeyMisses_CoalesceOneProviderLoad()
    {
        var expected = NewExecutable("artifact-a");
        var release = NewCompletionSource<WorkflowExecutable?>();
        var provider = new ControllableStore
        {
            FindHandler = (_, _) => new ValueTask<WorkflowExecutable?>(release.Task)
        };
        var cache = NewCache(provider);

        var callers = Enumerable.Range(0, 32)
            .Select(_ => cache.FindAsync(expected.Identity.ArtifactId).AsTask())
            .ToArray();
        await WaitUntilAsync(() => provider.FindCalls(expected.Identity.ArtifactId) == 1);

        Assert.Equal(1, provider.FindCalls(expected.Identity.ArtifactId));
        release.SetResult(expected);
        var results = await Task.WhenAll(callers);

        Assert.All(results, result => Assert.Same(expected, result));
        Assert.Equal(1, provider.FindCalls(expected.Identity.ArtifactId));
    }

    [Fact]
    public async Task CancellingOneWaiter_DoesNotCancelOrPoisonSharedLoad()
    {
        var expected = NewExecutable("artifact-a");
        var release = NewCompletionSource<WorkflowExecutable?>();
        CancellationToken providerToken = default;
        var provider = new ControllableStore
        {
            FindHandler = (_, token) =>
            {
                providerToken = token;
                return new ValueTask<WorkflowExecutable?>(release.Task);
            }
        };
        var cache = NewCache(provider);
        using var cancelled = new CancellationTokenSource();

        var first = cache.FindAsync(expected.Identity.ArtifactId, cancelled.Token).AsTask();
        var second = cache.FindAsync(expected.Identity.ArtifactId).AsTask();
        await WaitUntilAsync(() => provider.FindCalls(expected.Identity.ArtifactId) == 1);
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.False(providerToken.IsCancellationRequested);
        release.SetResult(expected);
        Assert.Same(expected, await second);
        Assert.Same(expected, await cache.FindAsync(expected.Identity.ArtifactId));
        Assert.Equal(1, provider.FindCalls(expected.Identity.ArtifactId));
    }

    [Fact]
    public async Task SynchronouslyCompletedLoad_IsCachedWithoutLeavingCompletedInFlightState()
    {
        var expected = NewExecutable("artifact-a");
        var provider = new ControllableStore(expected);
        var cache = NewCache(provider);

        Assert.True(cache.FindAsync(expected.Identity.ArtifactId).IsCompletedSuccessfully);
        Assert.Same(expected, await cache.FindAsync(expected.Identity.ArtifactId));
        Assert.Equal(1, provider.FindCalls(expected.Identity.ArtifactId));
    }

    [Fact]
    public async Task NullLoad_IsRetried()
    {
        var expected = NewExecutable("artifact-a");
        var provider = new ControllableStore();
        provider.FindHandler = (_, _) => ValueTask.FromResult<WorkflowExecutable?>(
            provider.TotalFindCalls == 1 ? null : expected);
        var cache = NewCache(provider);

        Assert.Null(await cache.FindAsync(expected.Identity.ArtifactId));
        Assert.Same(expected, await cache.FindAsync(expected.Identity.ArtifactId));
        Assert.Equal(2, provider.FindCalls(expected.Identity.ArtifactId));
    }

    [Fact]
    public async Task ProviderFailure_IsPropagatedAndRetried()
    {
        var expected = NewExecutable("artifact-a");
        var provider = new ControllableStore();
        provider.FindHandler = (_, _) => provider.TotalFindCalls == 1
            ? ValueTask.FromException<WorkflowExecutable?>(new ExpectedException())
            : ValueTask.FromResult<WorkflowExecutable?>(expected);
        var cache = NewCache(provider);

        await Assert.ThrowsAsync<ExpectedException>(() => cache.FindAsync(expected.Identity.ArtifactId).AsTask());
        Assert.Same(expected, await cache.FindAsync(expected.Identity.ArtifactId));
        Assert.Equal(2, provider.FindCalls(expected.Identity.ArtifactId));
    }

    [Fact]
    public async Task ProviderCancellation_IsPropagatedAndRetried()
    {
        var expected = NewExecutable("artifact-a");
        using var providerCancellation = new CancellationTokenSource();
        providerCancellation.Cancel();
        var provider = new ControllableStore();
        provider.FindHandler = (_, _) => provider.TotalFindCalls == 1
            ? ValueTask.FromCanceled<WorkflowExecutable?>(providerCancellation.Token)
            : ValueTask.FromResult<WorkflowExecutable?>(expected);
        var cache = NewCache(provider);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.FindAsync(expected.Identity.ArtifactId).AsTask());
        Assert.Same(expected, await cache.FindAsync(expected.Identity.ArtifactId));
        Assert.Equal(2, provider.FindCalls(expected.Identity.ArtifactId));
    }

    [Fact]
    public async Task CapacityEviction_IsDeterministicLeastRecentlyUsed()
    {
        var a = NewExecutable("artifact-a");
        var b = NewExecutable("artifact-b");
        var c = NewExecutable("artifact-c");
        var provider = new ControllableStore(a, b, c);
        var cache = NewCache(provider, capacity: 2);

        await cache.FindAsync(a.Identity.ArtifactId);
        await cache.FindAsync(b.Identity.ArtifactId);
        await cache.FindAsync(a.Identity.ArtifactId); // Promote A; B is now least recent.
        await cache.FindAsync(c.Identity.ArtifactId);
        await cache.FindAsync(a.Identity.ArtifactId);
        await cache.FindAsync(b.Identity.ArtifactId);

        Assert.Equal(1, provider.FindCalls(a.Identity.ArtifactId));
        Assert.Equal(2, provider.FindCalls(b.Identity.ArtifactId));
        Assert.Equal(1, provider.FindCalls(c.Identity.ArtifactId));
    }

    [Fact]
    public async Task CapacityOne_AlternatingKeysAlwaysEvictsPriorEntry()
    {
        var a = NewExecutable("artifact-a");
        var b = NewExecutable("artifact-b");
        var provider = new ControllableStore(a, b);
        var cache = NewCache(provider, capacity: 1);

        await cache.FindAsync(a.Identity.ArtifactId);
        await cache.FindAsync(b.Identity.ArtifactId);
        await cache.FindAsync(a.Identity.ArtifactId);

        Assert.Equal(2, provider.FindCalls(a.Identity.ArtifactId));
        Assert.Equal(1, provider.FindCalls(b.Identity.ArtifactId));
    }

    [Fact]
    public async Task SuccessfulSave_InvalidatesAndReloadsProviderAuthoritativeValue()
    {
        var durable = NewExecutable("artifact-a", "durable");
        var callerSupplied = NewExecutable("artifact-a", "caller");
        var provider = new ControllableStore(durable);
        var cache = NewCache(provider);
        Assert.Same(durable, await cache.FindAsync(durable.Identity.ArtifactId));

        await cache.SaveAsync(callerSupplied);
        var reloaded = await cache.FindAsync(durable.Identity.ArtifactId);

        Assert.Same(durable, reloaded);
        Assert.Equal(2, provider.FindCalls(durable.Identity.ArtifactId));
        Assert.Equal(1, provider.SaveCalls);
    }

    [Fact]
    public async Task FailedSave_LeavesPriorResidentValue()
    {
        var durable = NewExecutable("artifact-a", "durable");
        var provider = new ControllableStore(durable)
        {
            SaveHandler = (_, _) => ValueTask.FromException(new ExpectedException())
        };
        var cache = NewCache(provider);
        await cache.FindAsync(durable.Identity.ArtifactId);

        await Assert.ThrowsAsync<ExpectedException>(() => cache.SaveAsync(NewExecutable("artifact-a", "caller")).AsTask());

        Assert.Same(durable, await cache.FindAsync(durable.Identity.ArtifactId));
        Assert.Equal(1, provider.FindCalls(durable.Identity.ArtifactId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NonThrowingDelete_EvictsResidentAndPreservesProviderResult(bool providerResult)
    {
        var expected = NewExecutable("artifact-a");
        var provider = new ControllableStore(expected)
        {
            DeleteHandler = (_, _) => ValueTask.FromResult(providerResult)
        };
        var cache = NewCache(provider);
        await cache.FindAsync(expected.Identity.ArtifactId);

        Assert.Equal(providerResult, await cache.DeleteAsync(expected.Identity.ArtifactId));
        await cache.FindAsync(expected.Identity.ArtifactId);

        Assert.Equal(2, provider.FindCalls(expected.Identity.ArtifactId));
    }

    [Fact]
    public async Task FailedDelete_LeavesPriorResidentValue()
    {
        var expected = NewExecutable("artifact-a");
        var provider = new ControllableStore(expected)
        {
            DeleteHandler = (_, _) => ValueTask.FromException<bool>(new ExpectedException())
        };
        var cache = NewCache(provider);
        await cache.FindAsync(expected.Identity.ArtifactId);

        await Assert.ThrowsAsync<ExpectedException>(() => cache.DeleteAsync(expected.Identity.ArtifactId).AsTask());

        Assert.Same(expected, await cache.FindAsync(expected.Identity.ArtifactId));
        Assert.Equal(1, provider.FindCalls(expected.Identity.ArtifactId));
    }

    [Fact]
    public async Task DeleteDuringInflightLoad_PreventsStaleReadmission()
    {
        var stale = NewExecutable("artifact-a", "stale");
        var release = NewCompletionSource<WorkflowExecutable?>();
        var provider = new ControllableStore
        {
            DeleteHandler = (_, _) => ValueTask.FromResult(true)
        };
        provider.FindHandler = (_, _) => provider.TotalFindCalls == 1
            ? new ValueTask<WorkflowExecutable?>(release.Task)
            : ValueTask.FromResult<WorkflowExecutable?>(null);
        var cache = NewCache(provider);

        var inFlight = cache.FindAsync(stale.Identity.ArtifactId).AsTask();
        await WaitUntilAsync(() => provider.FindCalls(stale.Identity.ArtifactId) == 1);
        await cache.DeleteAsync(stale.Identity.ArtifactId);
        release.SetResult(stale);
        Assert.Same(stale, await inFlight);

        Assert.Null(await cache.FindAsync(stale.Identity.ArtifactId));
        Assert.Equal(2, provider.FindCalls(stale.Identity.ArtifactId));
    }

    [Fact]
    public async Task SaveDuringInflightLoad_PreventsPreSaveReadmission()
    {
        var stale = NewExecutable("artifact-a", "stale");
        var durable = NewExecutable("artifact-a", "durable");
        var release = NewCompletionSource<WorkflowExecutable?>();
        var provider = new ControllableStore(durable);
        provider.FindHandler = (_, _) => provider.TotalFindCalls == 1
            ? new ValueTask<WorkflowExecutable?>(release.Task)
            : ValueTask.FromResult<WorkflowExecutable?>(durable);
        var cache = NewCache(provider);

        var inFlight = cache.FindAsync(stale.Identity.ArtifactId).AsTask();
        await WaitUntilAsync(() => provider.FindCalls(stale.Identity.ArtifactId) == 1);
        await cache.SaveAsync(NewExecutable("artifact-a", "caller"));
        release.SetResult(stale);
        Assert.Same(stale, await inFlight);

        Assert.Same(durable, await cache.FindAsync(stale.Identity.ArtifactId));
        Assert.Equal(2, provider.FindCalls(stale.Identity.ArtifactId));
    }

    [Fact]
    public async Task CallerStartingAfterDelete_DoesNotJoinPreDeleteLoad()
    {
        var stale = NewExecutable("artifact-a", "stale");
        var release = NewCompletionSource<WorkflowExecutable?>();
        var call = 0;
        int providerCall() => Interlocked.Increment(ref call);
        var provider = new ControllableStore
        {
            FindHandler = (_, _) => providerCall() == 1
                ? new ValueTask<WorkflowExecutable?>(release.Task)
                : ValueTask.FromResult<WorkflowExecutable?>(null),
            DeleteHandler = (_, _) => ValueTask.FromResult(true)
        };
        var cache = NewCache(provider);

        var beforeDelete = cache.FindAsync(stale.Identity.ArtifactId).AsTask();
        await WaitUntilAsync(() => provider.FindCalls(stale.Identity.ArtifactId) == 1);
        await cache.DeleteAsync(stale.Identity.ArtifactId);
        var afterDelete = cache.FindAsync(stale.Identity.ArtifactId).AsTask();
        await WaitUntilAsync(() => provider.FindCalls(stale.Identity.ArtifactId) == 2);

        Assert.Null(await afterDelete);
        release.SetResult(stale);
        Assert.Same(stale, await beforeDelete);
        Assert.Equal(2, provider.FindCalls(stale.Identity.ArtifactId));
    }

    [Fact]
    public async Task CallerStartingAfterSave_DoesNotJoinPreSaveLoad()
    {
        var stale = NewExecutable("artifact-a", "stale");
        var durable = NewExecutable("artifact-a", "durable");
        var release = NewCompletionSource<WorkflowExecutable?>();
        var provider = new ControllableStore(durable);
        provider.FindHandler = (_, _) => provider.TotalFindCalls == 1
            ? new ValueTask<WorkflowExecutable?>(release.Task)
            : ValueTask.FromResult<WorkflowExecutable?>(durable);
        var cache = NewCache(provider);

        var beforeSave = cache.FindAsync(stale.Identity.ArtifactId).AsTask();
        await WaitUntilAsync(() => provider.FindCalls(stale.Identity.ArtifactId) == 1);
        await cache.SaveAsync(NewExecutable("artifact-a", "caller"));
        var afterSave = cache.FindAsync(stale.Identity.ArtifactId).AsTask();
        await WaitUntilAsync(() => provider.FindCalls(stale.Identity.ArtifactId) == 2);

        Assert.Same(durable, await afterSave);
        release.SetResult(stale);
        Assert.Same(stale, await beforeSave);
        Assert.Same(durable, await cache.FindAsync(stale.Identity.ArtifactId));
        Assert.Equal(2, provider.FindCalls(stale.Identity.ArtifactId));
    }

    [Fact]
    public async Task MutationOfDifferentKey_DoesNotPreventInflightLoadAdmission()
    {
        var a = NewExecutable("artifact-a");
        var b = NewExecutable("artifact-b");
        var release = NewCompletionSource<WorkflowExecutable?>();
        var provider = new ControllableStore(a, b);
        provider.FindHandler = (artifactId, _) => artifactId == a.Identity.ArtifactId && provider.FindCalls(artifactId) == 1
            ? new ValueTask<WorkflowExecutable?>(release.Task)
            : ValueTask.FromResult(provider.Executable(artifactId));
        var cache = NewCache(provider);

        var loadingA = cache.FindAsync(a.Identity.ArtifactId).AsTask();
        await WaitUntilAsync(() => provider.FindCalls(a.Identity.ArtifactId) == 1);
        await cache.SaveAsync(b);
        release.SetResult(a);
        Assert.Same(a, await loadingA);

        Assert.Same(a, await cache.FindAsync(a.Identity.ArtifactId));
        Assert.Equal(1, provider.FindCalls(a.Identity.ArtifactId));
    }

    [Fact]
    public async Task ListPage_DelegatesWithoutPopulatingOrChangingResidentRecency()
    {
        var a = NewExecutable("artifact-a");
        var b = NewExecutable("artifact-b");
        var c = NewExecutable("artifact-c");
        var provider = new ControllableStore(a, b, c)
        {
            ListHandler = (request, _) => ValueTask.FromResult(
                new RuntimeStorePage<WorkflowExecutable>(request, [a, b, c]))
        };
        var cache = NewCache(provider, capacity: 2);
        await cache.FindAsync(a.Identity.ArtifactId);

        Assert.Equal(3, (await cache.ListPageAsync(new RuntimeStorePageRequest())).Items.Count);
        await cache.FindAsync(b.Identity.ArtifactId);
        await cache.FindAsync(c.Identity.ArtifactId);
        await cache.FindAsync(a.Identity.ArtifactId);

        Assert.Equal(1, provider.ListCalls);
        Assert.Equal(2, provider.FindCalls(a.Identity.ArtifactId));
    }

    [Fact]
    public async Task RetentionTransitionsDelegateAndGuardedDeleteInvalidatesResidentArtifact()
    {
        var executable = NewExecutable("artifact-retention");
        var provider = new InMemoryWorkflowExecutableStore();
        var cache = NewCache(provider);
        var now = DateTimeOffset.UnixEpoch;
        var expiresAt = now.AddMinutes(1);
        await cache.SaveAsync(executable);
        Assert.Same(executable, await cache.FindAsync(executable.Identity.ArtifactId));

        var lease = await cache.TryAcquireRootWriteLeaseAsync(
            executable.Identity.ArtifactId,
            "lease-1",
            expiresAt,
            now);
        Assert.NotNull(lease);
        Assert.True(await cache.RenewRootWriteLeaseAsync(lease!, expiresAt.AddMinutes(1), now));
        await cache.ReleaseRootWriteLeaseAsync(lease);

        var cancelledGuard = await cache.TryBeginDeletionAsync(
            executable.Identity.ArtifactId,
            "delete-cancelled",
            expiresAt,
            now);
        Assert.NotNull(cancelledGuard);
        Assert.True(await cache.CancelDeletionAsync(cancelledGuard!));
        var deletionGuard = await cache.TryBeginDeletionAsync(
            executable.Identity.ArtifactId,
            "delete-final",
            expiresAt,
            now);
        Assert.NotNull(deletionGuard);

        Assert.True(await cache.DeleteAsync(deletionGuard!, now));
        Assert.Null(await cache.FindAsync(executable.Identity.ArtifactId));
    }

    [Fact]
    public async Task UnsuccessfulGuardedDeletePreservesResidentArtifact()
    {
        var executable = NewExecutable("artifact-retained");
        var provider = new ControllableStore(executable)
        {
            GuardedDeleteHandler = (_, _, _) => ValueTask.FromResult(false)
        };
        var cache = NewCache(provider);
        var guard = new WorkflowExecutableDeletionGuard(
            executable.Identity.ArtifactId,
            "delete-rejected",
            "guard-version");
        Assert.Same(executable, await cache.FindAsync(executable.Identity.ArtifactId));

        Assert.False(await cache.DeleteAsync(guard, DateTimeOffset.UnixEpoch));

        Assert.Same(executable, await cache.FindAsync(executable.Identity.ArtifactId));
        Assert.Equal(1, provider.FindCalls(executable.Identity.ArtifactId));
    }

    [Fact]
    public async Task Telemetry_UsesOnlyStableInstrumentsAndBoundedTags()
    {
        var found = NewExecutable("artifact-found");
        var other = NewExecutable("artifact-other");
        var provider = new ControllableStore(found, other);
        using var telemetry = new TelemetryCapture();
        var cache = NewCache(provider, capacity: 1);

        await cache.FindAsync(found.Identity.ArtifactId); // miss/found
        await cache.FindAsync(found.Identity.ArtifactId); // hit
        await cache.FindAsync("artifact-missing"); // miss/not_found
        await cache.FindAsync(other.Identity.ArtifactId); // miss/found + capacity eviction
        await cache.DeleteAsync(other.Identity.ArtifactId); // delete eviction

        telemetry.AssertInstrumentObserved(RequestCounterName);
        telemetry.AssertInstrumentObserved(EvictionCounterName);
        telemetry.AssertInstrumentObserved(ProviderLoadDurationName);
        telemetry.AssertOnlyBoundedTags(
            allowedValues: ["hit", "miss", "capacity", "delete", "save", "found", "not_found", "failed", "cancelled"]);
    }

    [Fact]
    public async Task ProviderLoadTelemetry_ReportsFailedAndCancelledWithoutSensitiveValues()
    {
        using var providerCancellation = new CancellationTokenSource();
        providerCancellation.Cancel();
        var provider = new ControllableStore();
        provider.FindHandler = (artifactId, _) => artifactId switch
        {
            "artifact-failed" => ValueTask.FromException<WorkflowExecutable?>(new ExpectedException()),
            "artifact-cancelled" => ValueTask.FromCanceled<WorkflowExecutable?>(providerCancellation.Token),
            _ => ValueTask.FromResult<WorkflowExecutable?>(null)
        };
        using var telemetry = new TelemetryCapture();
        var cache = NewCache(provider);

        await Assert.ThrowsAsync<ExpectedException>(() => cache.FindAsync("artifact-failed").AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.FindAsync("artifact-cancelled").AsTask());

        telemetry.AssertTagValueObserved("failed");
        telemetry.AssertTagValueObserved("cancelled");
        telemetry.AssertOnlyBoundedTags(
            allowedValues: ["hit", "miss", "capacity", "delete", "save", "found", "not_found", "failed", "cancelled"]);
    }

    [Fact]
    public async Task ThrowingTelemetryListener_DoesNotChangeStoreBehavior()
    {
        var expected = NewExecutable("artifact-a");
        var provider = new ControllableStore(expected);
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == MeterName)
                    meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, _, _) => throw new ExpectedException());
        listener.SetMeasurementEventCallback<double>((_, _, _, _) => throw new ExpectedException());
        listener.Start();
        var cache = NewCache(provider);

        var result = await cache.FindAsync(expected.Identity.ArtifactId);

        Assert.Same(expected, result);
    }

    private static CachingWorkflowExecutableStore NewCache(IWorkflowExecutableStore provider, int capacity = 256) =>
        new(provider, new WorkflowExecutableCacheOptions { Enabled = true, Capacity = capacity });

    private static WorkflowExecutable NewExecutable(string artifactId, string nodeSuffix = "root")
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        return new WorkflowExecutable(
            new WorkflowExecutableIdentity(artifactId, "definition", "version", "1", $"sha256:{artifactId}"),
            new ExecutableNode(
                $"node-{nodeSuffix}",
                $"authored-{nodeSuffix}",
                "test/activity",
                "1.0.0",
                "test",
                document.RootElement,
                new Dictionary<string, RuntimeInputBinding>(),
                new Dictionary<string, string>(),
                outputCaptures: new Dictionary<string, RuntimeOutputCapture>()),
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            DateTimeOffset.UnixEpoch,
            new Dictionary<string, string>(),
            IncidentStrategyBuiltIns.FaultReference);
    }

    private static TaskCompletionSource<T> NewCompletionSource<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class ExpectedException : Exception;

    private sealed class ControllableStore(params WorkflowExecutable[] executables) : IWorkflowExecutableStore
    {
        private readonly ConcurrentDictionary<string, WorkflowExecutable> _executables = new(
            executables.ToDictionary(x => x.Identity.ArtifactId, StringComparer.Ordinal));
        private readonly ConcurrentDictionary<string, int> _findCalls = new(StringComparer.Ordinal);
        private int _saveCalls;
        private int _deleteCalls;
        private int _listCalls;

        public Func<string, CancellationToken, ValueTask<WorkflowExecutable?>>? FindHandler { get; set; }
        public Func<WorkflowExecutable, CancellationToken, ValueTask>? SaveHandler { get; set; }
        public Func<string, CancellationToken, ValueTask<bool>>? DeleteHandler { get; set; }
        public Func<WorkflowExecutableDeletionGuard, DateTimeOffset, CancellationToken, ValueTask<bool>>? GuardedDeleteHandler { get; set; }
        public Func<RuntimeStorePageRequest, CancellationToken, ValueTask<RuntimeStorePage<WorkflowExecutable>>>?
            ListHandler { get; set; }

        public int TotalFindCalls => _findCalls.Values.Sum();
        public int SaveCalls => Volatile.Read(ref _saveCalls);
        public int DeleteCalls => Volatile.Read(ref _deleteCalls);
        public int ListCalls => Volatile.Read(ref _listCalls);
        public int FindCalls(string artifactId) => _findCalls.GetValueOrDefault(artifactId);
        public WorkflowExecutable? Executable(string artifactId) => _executables.GetValueOrDefault(artifactId);

        public ValueTask SaveAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _saveCalls);
            if (SaveHandler is not null)
                return SaveHandler(executable, cancellationToken);

            _executables.TryAdd(executable.Identity.ArtifactId, executable);
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> DeleteAsync(string artifactId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _deleteCalls);
            return DeleteHandler?.Invoke(artifactId, cancellationToken)
                   ?? ValueTask.FromResult(_executables.TryRemove(artifactId, out _));
        }

        public ValueTask<WorkflowExecutableRootWriteLease?> TryAcquireRootWriteLeaseAsync(
            string artifactId,
            string leaseId,
            DateTimeOffset expiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowExecutableRootWriteLease?>(null);

        public ValueTask<bool> RenewRootWriteLeaseAsync(
            WorkflowExecutableRootWriteLease lease,
            DateTimeOffset expiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);

        public ValueTask ReleaseRootWriteLeaseAsync(
            WorkflowExecutableRootWriteLease lease,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<WorkflowExecutableDeletionGuard?> TryBeginDeletionAsync(
            string artifactId,
            string operationId,
            DateTimeOffset expiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowExecutableDeletionGuard?>(null);

        public ValueTask<bool> CancelDeletionAsync(
            WorkflowExecutableDeletionGuard guard,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);

        public ValueTask<bool> DeleteAsync(
            WorkflowExecutableDeletionGuard guard,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            GuardedDeleteHandler?.Invoke(guard, now, cancellationToken)
            ?? ValueTask.FromResult(false);

        public ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default)
        {
            _findCalls.AddOrUpdate(artifactId, 1, static (_, count) => count + 1);
            return FindHandler?.Invoke(artifactId, cancellationToken)
                   ?? ValueTask.FromResult(_executables.GetValueOrDefault(artifactId));
        }

        public ValueTask<RuntimeStorePage<WorkflowExecutable>> ListPageAsync(
            RuntimeStorePageRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _listCalls);
            return ListHandler?.Invoke(request, cancellationToken)
                   ?? ValueTask.FromResult(new RuntimeStorePage<WorkflowExecutable>(
                       request,
                       _executables.Values.Take(request.Limit).ToArray()));
        }
    }

    private sealed class TelemetryCapture : IDisposable
    {
        private readonly ConcurrentQueue<Measurement> _measurements = new();
        private readonly MeterListener _listener = new();

        public TelemetryCapture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MeterName)
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
                _measurements.Enqueue(new Measurement(instrument.Name, value, tags.ToArray())));
            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
                _measurements.Enqueue(new Measurement(instrument.Name, value, tags.ToArray())));
            _listener.Start();
        }

        public void AssertInstrumentObserved(string name) =>
            Assert.Contains(_measurements, measurement => measurement.InstrumentName == name);

        public void AssertTagValueObserved(string value) =>
            Assert.Contains(_measurements, measurement =>
                measurement.Tags.Any(tag => StringComparer.Ordinal.Equals(tag.Value, value)));

        public void AssertOnlyBoundedTags(IReadOnlyCollection<string> allowedValues)
        {
            Assert.NotEmpty(_measurements);
            foreach (var measurement in _measurements)
            {
                Assert.All(measurement.Tags, tag => Assert.Contains(Assert.IsType<string>(tag.Value), allowedValues));
                Assert.DoesNotContain(measurement.Tags, tag =>
                    tag.Value?.ToString()?.Contains("artifact-", StringComparison.Ordinal) == true ||
                    tag.Value?.ToString()?.Contains("definition", StringComparison.Ordinal) == true);
            }
        }

        public void Dispose() => _listener.Dispose();

        private sealed record Measurement(
            string InstrumentName,
            double Value,
            KeyValuePair<string, object?>[] Tags);
    }
}
