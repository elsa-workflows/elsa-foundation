using System.Collections.Concurrent;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class RuntimeResumptionServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentRedrive_UsesBoundDistinctScopes_PreservesResultOrder_AndPurgesTerminalResidue(bool fail)
    {
        var queue = await QueueWithBacklogAsync("a", "b", "c", "terminal");
        var states = new InMemoryWorkflowExecutionStateStore();
        foreach (var id in new[] { "a", "b", "c" }) await states.SaveAsync(NewState(id, WorkflowExecutionStatus.Suspended));
        await states.SaveAsync(NewState("terminal", WorkflowExecutionStatus.Completed));
        var observations = new ConcurrentRecoveryObservations { Fail = fail };
        await using var provider = ConcurrentRecoveryProvider(queue, states, observations);
        var factory = provider.GetRequiredService<IPersistenceOperationScopeFactory>();
        await using (var sweep = await factory.CreateAsync(new PersistenceScope("tenant-blue")))
        {
            var service = sweep.ServiceProvider.GetRequiredService<RuntimeResumptionService>();
            var run = service.SweepAsync(new RuntimeResumptionSweepRequest { MaxConcurrentDispatches = 2 }).AsTask();
            try
            {
                await observations.Ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(2, observations.Calls.Count);
                Assert.Equal(4, provider.GetRequiredService<IRuntimeAdmissionLoadSignal>().InFlightDispatches);
            }
            finally { observations.Release.TrySetResult(); }
            var result = await run;
            Assert.Equal(["a", "b", "c"], result.Dispatches.Select(x => x.WorkflowExecutionId));
            Assert.All(result.Dispatches, x => Assert.Equal(fail && x.WorkflowExecutionId == "b"
                ? RuntimeResumptionDispatchOutcome.Faulted : RuntimeResumptionDispatchOutcome.Accepted, x.Outcome));
            Assert.Equal((1, 1), (result.TerminalExecutionsPurged, result.PurgedWorkItemCount));
            Assert.Empty((await queue.ListAsync(new RuntimeSchedulerWorkQuery("terminal"))).Items);
            Assert.Equal(2, observations.Peak);
            Assert.Equal(3, observations.Calls.Select(x => x.Scope).Distinct().Count());
            Assert.Empty(observations.Errors);
            Assert.Equal(0, provider.GetRequiredService<IRuntimeAdmissionLoadSignal>().InFlightDispatches);
        }
        Assert.Equal(observations.Created.Order(), observations.Disposed.Order());
    }

    [Fact]
    public async Task ConcurrentRedrive_CancellationDisposesScopesAndCharges_AndDoesNotCommitDiscoveryCursor()
    {
        var queue = await QueueWithBacklogAsync("a", "b", "c");
        var observations = new ConcurrentRecoveryObservations();
        await using var provider = ConcurrentRecoveryProvider(queue, new InMemoryWorkflowExecutionStateStore(), observations);
        await using var sweep = await provider.GetRequiredService<IPersistenceOperationScopeFactory>().CreateAsync(new PersistenceScope("tenant-blue"));
        using var shutdown = new CancellationTokenSource();
        var request = new RuntimeResumptionSweepRequest { MaxConcurrentDispatches = 2 };
        var before = provider.GetRequiredService<RuntimeResumptionDiscoveryStateStore>().Get("tenant-blue");
        var run = sweep.ServiceProvider.GetRequiredService<RuntimeResumptionService>().SweepAsync(request, shutdown.Token).AsTask();
        try { await observations.Ready.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { await shutdown.CancelAsync(); observations.Release.TrySetResult(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(2, observations.Calls.Count);
        Assert.Equal(0, provider.GetRequiredService<IRuntimeAdmissionLoadSignal>().InFlightDispatches);
        await sweep.DisposeAsync();
        Assert.Equal(observations.Created.Order(), observations.Disposed.Order());
        Assert.Equal(before, provider.GetRequiredService<RuntimeResumptionDiscoveryStateStore>().Get("tenant-blue"));
        // An interrupted walk leaves all candidate backlog available for the next sweep.
        Assert.Equal(3, (await queue.ListPendingWorkflowExecutionIdsAsync(100)).Count);
    }

    private static ServiceProvider ConcurrentRecoveryProvider(IWorkflowSchedulerWorkQueue queue,
        IWorkflowExecutionStateStore states, ConcurrentRecoveryObservations observations)
    {
        var services = new ServiceCollection();
        services.AddPersistenceCore("tenant-blue");
        services.AddWorkflowRuntime();
        services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        services.RemoveAll<IWorkflowSchedulerWorkQueue>(); services.AddSingleton(queue);
        services.RemoveAll<IWorkflowExecutionStateStore>(); services.AddSingleton(states);
        services.RemoveAll<IRuntimeRecoveryScanner>(); services.AddSingleton<IRuntimeRecoveryScanner>(new FakeRecoveryScanner());
        services.RemoveAll<IWorkflowExecutionActorProvider>(); services.AddScoped<IWorkflowExecutionActorProvider, ConcurrentRecoveryActors>();
        services.AddSingleton(observations);
        services.AddScoped<RuntimeResumptionService>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private sealed class ConcurrentRecoveryObservations
    {
        public bool Fail;
        public int Active, Peak;
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<(string Execution, Guid Scope)> Calls { get; } = new();
        public ConcurrentBag<Guid> Created { get; } = [];
        public ConcurrentBag<Guid> Disposed { get; } = [];
        public ConcurrentBag<string> Errors { get; } = [];
    }

    private sealed class ConcurrentRecoveryActors : IWorkflowExecutionActorProvider, IDisposable
    {
        private readonly ConcurrentRecoveryObservations _observations;
        private readonly IPersistenceAccessContextAccessor _access;
        private readonly IRuntimeAdmissionLoadSignal _signal;
        private readonly Guid _scope = Guid.NewGuid();
        public ConcurrentRecoveryActors(ConcurrentRecoveryObservations observations, IPersistenceAccessContextAccessor access, IRuntimeAdmissionLoadSignal signal)
        {
            _observations = observations; _access = access; _signal = signal;
            observations.Created.Add(_scope);
        }
        public WorkflowExecutionActorCapabilities Capabilities => WorkflowExecutionActorCapabilities.InProcessMailbox;
        public ValueTask<IWorkflowExecutionActor> GetAgentAsync(WorkflowExecutionActorActivationRequest request, CancellationToken cancellationToken = default) =>
            new(new ConcurrentRecoveryActor(this));
        public ValueTask PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public void Dispose() => _observations.Disposed.Add(_scope);

        private sealed class ConcurrentRecoveryActor(ConcurrentRecoveryActors owner) : IWorkflowExecutionActor
        {
            public WorkflowExecutionActorDescriptor Descriptor => throw new NotSupportedException();
            public async ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
            {
                var obs = owner._observations;
                if (owner._access.Current.RequireScope().Value != "tenant-blue") obs.Errors.Add("wrong scope");
                if (!owner._signal.HasAmbientCharge) obs.Errors.Add("missing load charge");
                owner._signal.RecordDispatch();
                var active = Interlocked.Increment(ref obs.Active);
                int previous;
                do { previous = obs.Peak; } while (previous < active && Interlocked.CompareExchange(ref obs.Peak, active, previous) != previous);
                obs.Calls.Enqueue((envelope.WorkflowExecutionId, owner._scope));
                if (obs.Calls.Count >= 2) obs.Ready.TrySetResult();
                try
                {
                    await obs.Release.Task.WaitAsync(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (obs.Fail && envelope.WorkflowExecutionId == "b") throw new InvalidOperationException("redrive failed");
                    return new WorkflowExecutionCommandDispatchResult(envelope.EnvelopeId, envelope.WorkflowExecutionId, WorkflowExecutionCommandDispatchStatus.Accepted, Now);
                }
                finally { Interlocked.Decrement(ref obs.Active); }
            }
        }
    }
}
