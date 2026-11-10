using System.Runtime.ExceptionServices;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WhileActivity = Elsa.Activities.While.Activities.While;

namespace Elsa.Activities.Http.IntegrationTests;

public sealed partial class LongDrainCancellationTests
{
    [Fact]
    public async Task Graceful_host_stop_joins_the_detached_redrives_real_ownership_release_before_teardown()
    {
        var lifetime = new RedriveLifetimeProbe();
        var probe = new LoopProbe
        {
            Continued = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var fixture = await HttpEndpointHostFixture.StartDurableSqliteAsync(PersistenceMode.Ephemeral, 50, services =>
        {
            ReferenceWorkflowRuns.ComposeHost(services);
            services.AddSingleton(probe);
            // The host stops in reverse registration order. This witness runs after the runtime lifecycle hook.
            services.Insert(0, ServiceDescriptor.Singleton<IHostedService>(lifetime));
            var selected = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRuntimeExecutionOwnershipService));
            services.Remove(selected);
            services.Add(new ServiceDescriptor(typeof(IRuntimeExecutionOwnershipService), provider =>
            {
                var inner = (IRuntimeExecutionOwnershipService)(selected.ImplementationInstance ??
                    selected.ImplementationFactory?.Invoke(provider) ??
                    ActivatorUtilities.CreateInstance(provider, selected.ImplementationType!));
                return new HeldReleaseOwnership(inner, lifetime);
            }, selected.Lifetime));
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        Task? start = null;
        Task? cancel = null;
        Task? stopping = null;
        Exception? bodyFailure = null;
        Exception? cleanupFailure = null;
        try
        {
            var body = ReferenceWorkflows.Clr("body", typeof(ReplaySafeBody), []);
            var loop = ReferenceWorkflows.Clr("loop", typeof(WhileActivity), [("Condition", true)],
                childSlots: [new ExecutableChildSlot(WhileActivity.BodySlotName, [body])],
                structure: new ExecutableActivityStructure(WhileActivity.StructureKind, WhileActivity.StructureSchemaVersion,
                    JsonSerializer.SerializeToElement(new { body = "body" })));
            var executable = ReferenceWorkflows.Executable("shutdown-redrive", loop);
            await fixture.PublishAsync(executable);
            const string executionId = "shutdown-redrive";
            const string startKey = "shutdown-start";
            start = fixture.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(
                new(executable.Identity.ArtifactId, "test", workflowExecutionId: executionId, idempotencyKey: startKey),
                cancellationToken: timeout.Token).AsTask();
            await probe.Entered.Task.WaitAsync(timeout.Token);
            var actor = await fixture.Services.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(
                new(executionId, WorkflowExecutionActorActivationReason.SchedulerWork, DateTimeOffset.UtcNow, "test",
                    WorkflowExecutionActorCapabilities.InProcessMailbox), timeout.Token);
            var duplicate = actor.EnqueueAsync(NewCancel(executionId, startKey), timeout.Token).AsTask();
            probe.Release.TrySetResult();
            Assert.Equal(WorkflowExecutionCommandDispatchStatus.Duplicate, (await duplicate.WaitAsync(timeout.Token)).Status);
            await start.WaitAsync(timeout.Token);
            await probe.Continued!.Task.WaitAsync(timeout.Token);
            Assert.Equal(2, Volatile.Read(ref lifetime.Acquisitions));

            lifetime.HoldRelease = true;
            cancel = actor.EnqueueAsync(NewCancel(executionId, "shutdown-cancel"), timeout.Token).AsTask();
            probe.Stop.TrySetResult();
            await lifetime.ReleaseEntered.Task.WaitAsync(timeout.Token);
            // ReleaseAsync is actually in progress with CancellationToken.None, while its persistence scope is alive.
            Assert.False(lifetime.ReleaseToken.CanBeCanceled);
            stopping = fixture.StopAsync(timeout.Token);
            var witness = await Task.WhenAny(lifetime.Cancelled.Task, lifetime.HostStopped.Task).WaitAsync(timeout.Token);
            Assert.Same(lifetime.Cancelled.Task, witness);
            Assert.False(stopping.IsCompleted);
            Assert.False(lifetime.HostStopped.Task.IsCompleted);
            Assert.False(lifetime.ReleaseFinished);

            lifetime.Release.TrySetResult();
            await stopping.WaitAsync(timeout.Token);
            await cancel.WaitAsync(timeout.Token);
            Assert.True(lifetime.ReleaseFinished);
            Assert.True(lifetime.HostStopped.Task.IsCompletedSuccessfully);
            Assert.Equal(2, probe.Calls);
            Assert.False(probe.SafetyLimitReached);
        }
        catch (Exception exception)
        {
            bodyFailure = exception;
        }
        finally
        {
            lifetime.Release.TrySetResult();
            probe.Release.TrySetResult();
            probe.Stop.TrySetResult();
            // Let an already queued real Cancel consume the yield; withdrawing it here would strand it again.
            if (cancel is null)
                await timeout.CancelAsync();
            var cleanupFailures = new List<Exception>();
            foreach (var operation in new[] { start, cancel, stopping }.OfType<Task>())
            {
                try { await operation.WaitAsync(timeout.Token); }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
                catch (Exception exception) { cleanupFailures.Add(exception); }
            }
            await timeout.CancelAsync();
            try { await fixture.DisposeAsync(); }
            catch (Exception exception) { cleanupFailures.Add(exception); }
            lifetime.Cancellation.Dispose();
            cleanupFailure = cleanupFailures.Count switch
            {
                0 => null,
                1 => cleanupFailures[0],
                _ => new AggregateException("Multiple lifetime cleanup operations failed.", cleanupFailures)
            };
        }
        if (bodyFailure is not null && cleanupFailure is not null)
            throw new AggregateException("Both the lifetime assertion and fixture cleanup failed.", bodyFailure, cleanupFailure);
        if (bodyFailure is not null) ExceptionDispatchInfo.Capture(bodyFailure).Throw();
        if (cleanupFailure is not null) ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
    }

    private sealed class RedriveLifetimeProbe : IHostedService
    {
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource HostStopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenRegistration Cancellation;
        public int Acquisitions;
        public CancellationToken ReleaseToken;
        public volatile bool HoldRelease;
        public volatile bool ReleaseFinished;
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken)
        {
            HostStopped.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class HeldReleaseOwnership(IRuntimeExecutionOwnershipService inner, RedriveLifetimeProbe lifetime) :
        IRuntimeExecutionOwnershipService
    {
        public async ValueTask<RuntimeExecutionLease> AcquireAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
        {
            var lease = await inner.AcquireAsync(workflowExecutionId, cancellationToken);
            if (Interlocked.Increment(ref lifetime.Acquisitions) == 2)
                lifetime.Cancellation = cancellationToken.Register(() => lifetime.Cancelled.TrySetResult());
            return lease;
        }
        public ValueTask<RuntimeExecutionOwnershipTransitionResult> HeartbeatAsync(RuntimeExecutionLease lease, CancellationToken cancellationToken = default) =>
            inner.HeartbeatAsync(lease, cancellationToken);
        public ValueTask EnsureCurrentAsync(string workflowExecutionId, long fencingToken, CancellationToken cancellationToken = default) =>
            inner.EnsureCurrentAsync(workflowExecutionId, fencingToken, cancellationToken);
        public async ValueTask<RuntimeExecutionOwnershipTransitionResult> ReleaseAsync(RuntimeExecutionLease lease, CancellationToken cancellationToken = default)
        {
            if (!lifetime.HoldRelease)
                return await inner.ReleaseAsync(lease, cancellationToken);
            lifetime.ReleaseToken = cancellationToken;
            lifetime.ReleaseEntered.TrySetResult();
            await lifetime.Release.Task.WaitAsync(cancellationToken);
            var result = await inner.ReleaseAsync(lease, cancellationToken);
            Assert.True(result.Succeeded || result.Status == RuntimeExecutionOwnershipTransitionStatus.AlreadyApplied);
            lifetime.ReleaseFinished = true;
            return result;
        }
    }
}
