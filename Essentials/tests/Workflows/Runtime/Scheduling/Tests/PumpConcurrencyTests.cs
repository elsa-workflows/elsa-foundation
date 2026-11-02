using System.Collections.Concurrent;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Scheduling.Options;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Triggers;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Scheduling.Tests;

public sealed class PumpConcurrencyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Timers_BoundConcurrency_PreserveExecutionOrder_AndIsolateRenewalScopes()
    {
        var store = new InMemoryDurableTimerStore();
        foreach (var (id, execution) in new[] { ("1", "a"), ("2", "a"), ("3", "b"), ("4", "c") })
            await store.SaveAsync(new DurableTimer(id, execution, "timer", id, Now, Now));
        var observations = new Observations(2);
        var clock = new FakeTimeProvider(Now);
        var services = Services(observations);
        services.AddScoped<IDurableTimerStore>(sp => new ScopedTimerStore(store, sp.GetRequiredService<ScopeProbe>(), observations));
        services.AddScoped<IBookmarkResumeDispatcher, Dispatcher>();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var signal = provider.GetRequiredService<IRuntimeAdmissionLoadSignal>();
        var pump = new DurableTimerPumpTask(provider.GetRequiredService<IPersistenceScopeRunner>(),
            Microsoft.Extensions.Options.Options.Create(new DurableTimerPumpOptions { MaxConcurrentDispatches = 2 }), clock, NullLogger<DurableTimerPumpTask>.Instance);

        var run = pump.ExecuteAsync(CancellationToken.None);
        try
        {
            await observations.Ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(2, observations.Calls.Count);
            Assert.Equal(4, signal.InFlightDispatches); // two seeds and two dispatch units
            Assert.Null(signal.TryOpenCharge(1, out var observedLoad));
            Assert.Equal(4, observedLoad);
            Assert.DoesNotContain(observations.Calls, x => x.Id == "2");
            clock.Advance(TimeSpan.FromSeconds(20));
            await observations.RenewedWhileDispatching.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { observations.Release.TrySetResult(); }
        await run;

        Assert.Equal(2, observations.Peak);
        Assert.Equal(["1", "2"], observations.Calls.Where(x => x.Execution == "a").Select(x => x.Id));
        Assert.Equal(4, observations.Calls.Count);
        Assert.Equal(4, observations.Calls.Select(x => x.Scope).Distinct().Count());
        Assert.Empty(observations.Errors);
        Assert.Equal(observations.RenewalScopes.Count, observations.RenewalScopes.Distinct().Count());
        Assert.Equal(0, signal.InFlightDispatches);
        Assert.False(signal.HasAmbientCharge);
        Assert.Empty(await store.ListDueAsync(Now.AddHours(1), 100));
        Assert.Equal(observations.Created.Order(), observations.Disposed.Order());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimerDispatch_FaultOrCancellation_DisposesChargeAndOperationScope(bool cancel)
    {
        var store = new InMemoryDurableTimerStore();
        await store.SaveAsync(new DurableTimer("1", "a", "timer", "1", Now, Now));
        var observations = new Observations(1) { Throw = !cancel };
        var services = Services(observations);
        services.AddSingleton<IDurableTimerStore>(store);
        services.AddScoped<IBookmarkResumeDispatcher, Dispatcher>();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var pump = new DurableTimerPumpTask(provider.GetRequiredService<IPersistenceScopeRunner>(),
            Microsoft.Extensions.Options.Options.Create(new DurableTimerPumpOptions { MaxConcurrentDispatches = 4 }), new FakeTimeProvider(Now), NullLogger<DurableTimerPumpTask>.Instance);
        using var shutdown = new CancellationTokenSource();
        var run = pump.ExecuteAsync(shutdown.Token);
        try { await observations.Ready.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally
        {
            if (cancel) await shutdown.CancelAsync();
            observations.Release.TrySetResult();
        }
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        else await run;
        Assert.Equal(0, provider.GetRequiredService<IRuntimeAdmissionLoadSignal>().InFlightDispatches);
        Assert.NotNull(await store.FindAsync("a", "1"));
        Assert.Equal(observations.Created.Order(), observations.Disposed.Order());
    }

    [Fact]
    public async Task WaitingTimerClaims_ReclaimedByPeer_AreNotDispatchedOrSettledByOldPump()
    {
        var store = new InMemoryDurableTimerStore();
        for (var i = 0; i < 4; i++)
            await store.SaveAsync(new DurableTimer(i.ToString(), i.ToString(), "timer", i.ToString(), Now, Now));
        var observations = new Observations(2);
        var services = Services(observations);
        services.AddSingleton<IDurableTimerStore>(store);
        services.AddScoped<IBookmarkResumeDispatcher, Dispatcher>();
        await using var provider = services.BuildServiceProvider();
        var pump = new DurableTimerPumpTask(provider.GetRequiredService<IPersistenceScopeRunner>(),
            Microsoft.Extensions.Options.Options.Create(new DurableTimerPumpOptions { MaxConcurrentDispatches = 2 }), new FakeTimeProvider(Now), NullLogger<DurableTimerPumpTask>.Instance);
        var run = pump.ExecuteAsync(CancellationToken.None);
        IReadOnlyCollection<RuntimeDurableTimerClaim> peerClaims;
        try
        {
            await observations.Ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            peerClaims = await store.ClaimDueAsync(new RuntimeDurableTimerClaimRequest("peer", Now.AddMinutes(2), TimeSpan.FromMinutes(1), 100));
            Assert.Equal(4, peerClaims.Count);
        }
        finally { observations.Release.TrySetResult(); }
        await run;
        Assert.Equal(2, observations.Calls.Count);
        foreach (var claim in peerClaims)
            Assert.Equal(RuntimeDurableTimerClaimTransitionStatus.Succeeded, (await store.CompleteClaimAsync(claim)).Status);
        Assert.Equal(0, provider.GetRequiredService<IRuntimeAdmissionLoadSignal>().InFlightDispatches);
    }

    [Fact]
    public async Task RecurringStarts_BoundConcurrency_KeepPinnedOwnerBindings_AndChargeActualRoutes()
    {
        var store = new InMemoryRecurringTriggerScheduleStore();
        var bindings = new InMemoryWorkflowTriggerBindingStore();
        for (var i = 0; i < 4; i++)
        {
            var id = i.ToString();
            await store.SaveAsync(new RecurringTriggerSchedule(id, "artifact-" + id, "node-" + id, "timer", id,
                RecurringScheduleKind.Interval, "PT5M", Now, Now));
            await bindings.SaveAsync(new WorkflowTriggerBinding("binding-" + id, "artifact-" + id, "definition-" + id,
                "1.0.0", "hash", "node-" + id, "timer", id, null, new Dictionary<string, string>(), Now));
        }
        var observations = new Observations(2);
        var services = Services(observations);
        services.AddSingleton<IRecurringTriggerScheduleStore>(store);
        services.AddSingleton<IWorkflowTriggerBindingStore>(bindings);
        services.AddSingleton<IRecurringScheduleCalculator, RecurringScheduleCalculator>();
        services.AddScoped<IStimulusRouter, Router>();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var pump = new RecurringTriggerPumpTask(provider.GetRequiredService<IPersistenceScopeRunner>(),
            Microsoft.Extensions.Options.Options.Create(new RecurringTriggerPumpOptions { MaxConcurrentDispatches = 2 }), new FakeTimeProvider(Now), NullLogger<RecurringTriggerPumpTask>.Instance);
        var run = pump.ExecuteAsync(CancellationToken.None);
        try
        {
            await observations.Ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(2, observations.Calls.Count);
            Assert.Equal(4, provider.GetRequiredService<IRuntimeAdmissionLoadSignal>().InFlightDispatches);
        }
        finally { observations.Release.TrySetResult(); }
        await run;
        Assert.Equal(2, observations.Peak);
        Assert.Equal(4, observations.Calls.Count);
        Assert.Equal(4, observations.Calls.Select(x => x.Scope).Distinct().Count());
        Assert.Empty(observations.Errors);
        Assert.Empty(await store.ClaimDueAsync(new RecurringTriggerOccurrenceClaimRequest("after", Now, TimeSpan.FromMinutes(1), 100)));
        Assert.Equal(0, provider.GetRequiredService<IRuntimeAdmissionLoadSignal>().InFlightDispatches);
        Assert.Equal(observations.Created.Order(), observations.Disposed.Order());
    }

    private static ServiceCollection Services(Observations observations)
    {
        var services = new ServiceCollection();
        services.AddPersistenceCore("tenant-blue");
        services.AddSingleton(observations);
        services.AddScoped<ScopeProbe>();
        services.AddSingleton<IRuntimeAdmissionLoadSignal, DispatchRuntimeAdmissionLoadSignal>();
        return services;
    }

    private sealed class ScopeProbe : IDisposable
    {
        private readonly Observations observations;
        private readonly IPersistenceAccessContextAccessor access;
        public ScopeProbe(Observations observations, IPersistenceAccessContextAccessor access)
        {
            this.observations = observations;
            this.access = access;
            observations.Created.Add(Id);
        }
        public Guid Id { get; } = Guid.NewGuid();
        public void Observe()
        {
            if (access.Current.Scope?.Value != "tenant-blue" || access.Current.AccessPolicy != PersistenceAccessPolicy.Ordinary)
                observations.Errors.Add("incorrect persistence scope");
        }
        public void Dispose() => observations.Disposed.Add(Id);
    }

    private sealed class Observations(int readyCount)
    {
        private int _active;
        public int Peak;
        public bool Throw;
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RenewedWhileDispatching { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<(string Id, string Execution, Guid Scope)> Calls { get; } = new();
        public ConcurrentDictionary<Guid, byte> ActiveScopes { get; } = new();
        public ConcurrentBag<Guid> Created { get; } = [];
        public ConcurrentBag<Guid> Disposed { get; } = [];
        public ConcurrentBag<string> Errors { get; } = [];
        public ConcurrentDictionary<string, int> Renewals { get; } = new();
        public ConcurrentBag<Guid> RenewalScopes { get; } = [];
        public async ValueTask DispatchAsync(string id, string execution, ScopeProbe scope, IRuntimeAdmissionLoadSignal signal, CancellationToken ct)
        {
            scope.Observe();
            if (!signal.HasAmbientCharge) Errors.Add("dispatch has no ambient charge");
            signal.RecordDispatch();
            ActiveScopes.TryAdd(scope.Id, 0);
            var active = Interlocked.Increment(ref _active);
            int previous;
            do { previous = Peak; } while (previous < active && Interlocked.CompareExchange(ref Peak, active, previous) != previous);
            Calls.Enqueue((id, execution, scope.Id));
            if (Calls.Count >= readyCount) Ready.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(ct);
                ct.ThrowIfCancellationRequested();
                if (Throw) throw new InvalidOperationException("dispatch failed");
            }
            finally { ActiveScopes.TryRemove(scope.Id, out _); Interlocked.Decrement(ref _active); }
        }
    }

    private sealed class Dispatcher(Observations observations, ScopeProbe scope, IRuntimeAdmissionLoadSignal signal) : IBookmarkResumeDispatcher
    {
        public async ValueTask<BookmarkResumeDispatchResult> DispatchAsync(BookmarkResumeDispatchRequest request,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null, CancellationToken cancellationToken = default)
        {
            await observations.DispatchAsync(request.StimulusHash, request.WorkflowExecutionId!, scope, signal, cancellationToken);
            return new BookmarkResumeDispatchResult(BookmarkResumeDispatchStatus.Duplicate, request.WorkflowExecutionId!, reason: "already delivered");
        }
    }

    private sealed class Router(Observations observations, ScopeProbe scope, IRuntimeAdmissionLoadSignal signal) : IStimulusRouter
    {
        public async ValueTask<StimulusRoutingResult> RouteAsync(StimulusDispatchRequest request, CancellationToken cancellationToken = default)
        {
            var binding = Assert.Single(request.MatchedTriggerBindings!);
            Assert.Equal("artifact-" + request.StimulusHash, binding.ArtifactId);
            await observations.DispatchAsync(request.StimulusHash, binding.ArtifactId, scope, signal, cancellationToken);
            return new StimulusRoutingResult([], []);
        }
    }

    private sealed class ScopedTimerStore(InMemoryDurableTimerStore inner, ScopeProbe scope, Observations observations) : IDurableTimerStore
    {
        public bool SupportsClaimTransitions => true;
        public ValueTask<DurableTimer> SaveAsync(DurableTimer timer, CancellationToken cancellationToken = default) => inner.SaveAsync(timer, cancellationToken);
        public ValueTask<DurableTimer?> FindAsync(string workflowExecutionId, string timerId, CancellationToken cancellationToken = default) => inner.FindAsync(workflowExecutionId, timerId, cancellationToken);
        public ValueTask<IReadOnlyCollection<DurableTimer>> ListDueAsync(DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default) => inner.ListDueAsync(asOf, limit, cancellationToken);
        public ValueTask DeleteAsync(string workflowExecutionId, string timerId, CancellationToken cancellationToken = default) => inner.DeleteAsync(workflowExecutionId, timerId, cancellationToken);
        public ValueTask<IReadOnlyCollection<RuntimeDurableTimerClaim>> ClaimDueAsync(RuntimeDurableTimerClaimRequest request, CancellationToken cancellationToken = default) => inner.ClaimDueAsync(request, cancellationToken);
        public ValueTask<RuntimeDurableTimerClaimTransitionResult> RenewClaimAsync(RuntimeDurableTimerClaim claim, DateTimeOffset now, TimeSpan visibilityTimeout, CancellationToken cancellationToken = default)
        {
            scope.Observe();
            observations.RenewalScopes.Add(scope.Id);
            if (observations.ActiveScopes.ContainsKey(scope.Id)) observations.Errors.Add("renewal shares active dispatch scope");
            if (observations.Renewals.AddOrUpdate(claim.Timer.TimerId, 1, (_, count) => count + 1) > 1 && !observations.ActiveScopes.IsEmpty)
                observations.RenewedWhileDispatching.TrySetResult();
            return inner.RenewClaimAsync(claim, now, visibilityTimeout, cancellationToken);
        }
        public ValueTask<RuntimeDurableTimerClaimTransitionResult> CompleteClaimAsync(RuntimeDurableTimerClaim claim, CancellationToken cancellationToken = default) => inner.CompleteClaimAsync(claim, cancellationToken);
        public ValueTask<RuntimeDurableTimerClaimTransitionResult> ReleaseClaimAsync(RuntimeDurableTimerClaim claim, DateTimeOffset visibleAt, CancellationToken cancellationToken = default) => inner.ReleaseClaimAsync(claim, visibleAt, cancellationToken);
    }
}
