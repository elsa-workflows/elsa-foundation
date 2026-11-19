using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Tests;

public sealed partial class WorkflowsRuntimeDistributedFeatureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_local_mailbox_hands_off_in_both_feature_registration_orders(bool distributionFirst)
    {
        var services = BuildBaselineServices();
        if (distributionFirst) new WorkflowsRuntimeDistributedFeature().ConfigureServices(services);
        services.AddWorkflowRuntime();
        if (!distributionFirst) new WorkflowsRuntimeDistributedFeature().ConfigureServices(services);
        services.AddSingleton<YieldObservationExecutor>();
        services.Replace(ServiceDescriptor.Singleton<IWorkflowExecutionCommandExecutor>(provider => provider.GetRequiredService<YieldObservationExecutor>()));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var local = provider.GetRequiredService<InProcessWorkflowExecutionActorProvider>();
        var processor = provider.GetRequiredService<YieldObservationExecutor>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var now = DateTimeOffset.UtcNow;
        const string executionId = "same-execution";
        var actor = await local.GetAgentAsync(new(executionId, WorkflowExecutionActorActivationReason.SchedulerWork,
            now, "test", WorkflowExecutionActorCapabilities.InProcessMailbox), timeout.Token);
        var work = new WorkflowExecutionCommandEnvelope("run", executionId,
            new("run", executionId, WorkflowExecutionCommandKind.RunSchedulerWork, now, null, new Dictionary<string, string>()),
            "run", WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now);
        var running = actor.EnqueueAsync(work, timeout.Token).AsTask();
        await processor.Entered.Task.WaitAsync(timeout.Token);
        var cancel = actor.EnqueueAsync(new("cancel", executionId,
            new("cancel", executionId, WorkflowExecutionCommandKind.Cancel, now, null, new Dictionary<string, string>()),
            "cancel", WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now), timeout.Token).AsTask();
        processor.Continue.TrySetResult();
        await Task.WhenAll(running, cancel).WaitAsync(timeout.Token);
        Assert.Equal(RuntimeSchedulerDrainStopReason.Yielded, processor.StopReason);
    }

    private sealed class YieldObservationExecutor(IServiceScopeFactory scopes) : IWorkflowExecutionCommandExecutor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public RuntimeSchedulerDrainStopReason? StopReason;
        public async ValueTask<WorkflowExecutionCommandProcessResult> ProcessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (envelope.Command.Kind == WorkflowExecutionCommandKind.Cancel) return WorkflowExecutionCommandProcessResult.NoDrain;
            Entered.TrySetResult();
            await Continue.Task.WaitAsync(cancellationToken);
            await using var scope = scopes.CreateAsyncScope();
            var drain = await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerDrainer>().DrainAsync(new(envelope.WorkflowExecutionId), cancellationToken);
            StopReason = drain.StopReason;
            return WorkflowExecutionCommandProcessResult.FromDrain(drain);
        }
    }
}
