using Elsa.Activities.DispatchWorkflow.Runtime;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Activities.DispatchWorkflow.Tests;

/// <summary>Full-feature in-process enrichment outage controls; fresh-provider recovery remains T045.</summary>
public sealed class WorkflowControlEnrichmentAvailabilityTests
{
    [Theory]
    [InlineData(75)]
    [InlineData(90)]
    [InlineData(100)]
    [InlineData(101)]
    public async Task Unavailable_control_enrichment_keeps_its_uncommitted_source_and_retries_without_faulting(int order)
    {
        var fault = new FaultPoint(order);
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(authoredNotificationChild: true,
            terminalAfterNotification: order >= 100, configureServices: services => Decorate(services, fault));
        var run = await fixture.StartParentAsync("enrichment-" + order, "enrichment-parent-" + order, "correlation",
            waitForCompletion: true, controlConsumerVersion: 1);
        fault.WorkflowId = run.Dispatch.ChildWorkflowExecutionId;
        await fixture.SweepAsync();
        var activities = fixture.Services.GetRequiredService<IActivityExecutionStateStore>();
        var childRoot = Assert.Single(await activities.ListAllAsync(fault.WorkflowId), state => state.ParentActivityExecutionId is null);
        Assert.Equal(1, fault.Failures);
        // The root completion precedes the separate terminal workflow checkpoint. That prior commit stays intact.
        Assert.Equal(order >= 100 ? ActivityExecutionStatus.Completed : ActivityExecutionStatus.Running, childRoot.Status);
        Assert.Equal(0, childRoot.FaultCount);
        Assert.Null(childRoot.Fault);
        Assert.Equal(order >= 100 ? 0L : (long?)null, childRoot.ProducedControl?.Ordinal);
        Assert.Equal(WorkflowExecutionStatus.Running, (await fixture.FindWorkflowAsync(fault.WorkflowId))!.Status);
        Assert.Empty(await fixture.Services.GetRequiredService<IIncidentStateStore>().ListAsync(fault.WorkflowId));
        Assert.Empty(await fixture.Services.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(fault.WorkflowId));
        var queue = fixture.Services.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var source = Assert.Single(await queue.ListAllAsync(fault.WorkflowId),
            work => work.CommandKind == (order >= 100 ? WorkflowExecutionCommandKind.Checkpoint : WorkflowExecutionCommandKind.InvokeActivity));
        var beforeCall = (await activities.FindAsync(run.Dispatch.ParentWorkflowExecutionId, run.Dispatch.ParentActivityExecutionId))!;
        Assert.False(beforeCall.ChildControlInbox?.IsTerminalConsumed ?? false);
        var beforeRetry = childRoot.ProducedControl?.Ordinal;
        await fixture.SweepAsync();
        Assert.Equal(1, fault.Failures);
        Assert.Contains(await queue.ListAllAsync(fault.WorkflowId), item => item.WorkItemId == source.WorkItemId);
        Assert.Equal(beforeRetry, (await activities.FindAsync(fault.WorkflowId, childRoot.InvocationId))!.ProducedControl?.Ordinal);
        Assert.Equal(WorkflowExecutionStatus.Running, (await fixture.FindWorkflowAsync(fault.WorkflowId))!.Status);
        fixture.AdvanceTime(TimeSpan.FromSeconds(6));
        for (var attempt = 0; attempt < 4; attempt++) await fixture.SweepAsync();
        childRoot = Assert.Single(await activities.ListAllAsync(fault.WorkflowId), state => state.ParentActivityExecutionId is null);
        Assert.Equal(0, childRoot.FaultCount);
        Assert.Null(childRoot.Fault);
        Assert.Equal(order >= 100 ? 1 : 0, childRoot.ProducedControl!.Ordinal);
        Assert.DoesNotContain(await queue.ListAllAsync(fault.WorkflowId), item => item.WorkItemId == source.WorkItemId);
        var call = (await activities.FindAsync(run.Dispatch.ParentWorkflowExecutionId, run.Dispatch.ParentActivityExecutionId))!;
        Assert.Equal(order >= 100 ? 2 : 1, call.ChildControlInbox!.NextOrdinal);
        Assert.Equal(order >= 100, call.ChildControlInbox.IsTerminalConsumed);
        Assert.Empty(await fixture.Services.GetRequiredService<IIncidentStateStore>().ListAsync(fault.WorkflowId));
        Assert.Empty(await fixture.Services.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(fault.WorkflowId));
    }

    private static void Decorate(IServiceCollection services, FaultPoint fault)
    {
        var boundary = services.Single(descriptor => descriptor.ServiceType == typeof(IRuntimeWorkflowControlBoundary));
        services.Replace(ServiceDescriptor.Scoped<IRuntimeWorkflowControlBoundary>(provider => new Boundary(
            (IRuntimeWorkflowControlBoundary)ActivatorUtilities.CreateInstance(provider, boundary.ImplementationType!), fault)));
        services.Replace(ServiceDescriptor.Scoped<IPostCommitOutboxLookupStore>(provider => new Lookup(
            provider.GetRequiredService<Elsa.Workflows.Runtime.Services.Checkpoints.InMemoryRuntimeCheckpointCommitStore>(), fault)));
        foreach (var descriptor in services.Where(descriptor => descriptor.ServiceType == typeof(IRuntimeCheckpointCommitEnricher) &&
                     descriptor.ImplementationType?.Assembly == typeof(DispatchWorkflowRuntimeFeature).Assembly).ToArray())
        {
            services.Remove(descriptor);
            services.AddScoped<IRuntimeCheckpointCommitEnricher>(provider => new Enricher(
                (IRuntimeCheckpointCommitEnricher)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!), fault));
        }
    }

    private sealed class FaultPoint(int selectedOrder)
    {
        internal readonly AsyncLocal<int?> ActiveOrder = new();
        internal string WorkflowId { get; set; } = "not-armed";
        internal int Failures { get; private set; }
        internal bool IsLookup => selectedOrder == 101;
        internal void ThrowLookupOnce()
        {
            if (!IsLookup || ActiveOrder.Value != 100 || Failures != 0 || WorkflowId == "not-armed") return;
            Failures++;
            throw new IOException("One-shot terminal source outbox read outage.");
        }
        internal void ThrowOnce(string workflowId, string rootId)
        {
            if (workflowId != WorkflowId || ActiveOrder.Value != selectedOrder || Failures != 0) return;
            Failures++;
            throw new WorkflowControlBoundaryException(workflowId, rootId, new IOException("One-shot enrichment store outage."), isUnavailable: true);
        }
    }

    private sealed class Boundary(IRuntimeWorkflowControlBoundary inner, FaultPoint fault) : IRuntimeWorkflowControlBoundary
    {
        public async ValueTask<RuntimeChildControlTarget?> ResolveAsync(string workflowExecutionId, string rootActivityExecutionId,
            CancellationToken cancellationToken = default)
        {
            var target = await inner.ResolveAsync(workflowExecutionId, rootActivityExecutionId, cancellationToken);
            if (target is not null) fault.ThrowOnce(workflowExecutionId, rootActivityExecutionId);
            return target;
        }
    }

    private sealed class Lookup(IPostCommitOutboxLookupStore inner, FaultPoint fault) : IPostCommitOutboxLookupStore
    {
        public ValueTask<RuntimePostCommitOutboxItem?> FindAsync(string outboxItemId, CancellationToken cancellationToken = default)
        {
            fault.ThrowLookupOnce();
            return inner.FindAsync(outboxItemId, cancellationToken);
        }
    }

    private sealed class Enricher(IRuntimeCheckpointCommitEnricher inner, FaultPoint fault) : IRuntimeCheckpointCommitEnricher
    {
        public int Order => inner.Order;
        public async ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
        {
            var previous = fault.ActiveOrder.Value;
            fault.ActiveOrder.Value = Order;
            try { return await inner.EnrichAsync(commit, cancellationToken); }
            finally { fault.ActiveOrder.Value = previous; }
        }
    }
}
