using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowsRuntimeApiFeatureTests
{
    [Fact]
    public void RegistersRuntimeExecutionServicesAndRequestHandlers()
    {
        var services = new ServiceCollection();

        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowExecutableStore) &&
            descriptor.ImplementationType == typeof(InMemoryWorkflowExecutableStore));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowSchedulerWorkQueue) &&
            descriptor.ImplementationType == typeof(InMemoryWorkflowSchedulerWorkQueue));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IActivityExecutionStateStore) &&
            descriptor.ImplementationType == typeof(InMemoryActivityExecutionStateStore));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowExecutionCommandProcessor) &&
            descriptor.ImplementationType == typeof(WorkflowSchedulerCommandProcessor));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowSchedulerDrainer) &&
            descriptor.ImplementationType == typeof(WorkflowSchedulerDrainer));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowSchedulerDrainPolicy) &&
            descriptor.ImplementationType == typeof(ImmediateWorkflowSchedulerDrainPolicy));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRuntimeCheckpointPersistencePolicy) &&
            descriptor.ImplementationType == typeof(ImmediateRuntimeCheckpointPersistencePolicy));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRuntimeCheckpointWriter) &&
            descriptor.ImplementationType == typeof(InMemoryRuntimeCheckpointWriter));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRuntimePostCommitIntentDispatcher) &&
            descriptor.ImplementationType == typeof(NoopRuntimePostCommitIntentDispatcher));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(RuntimeCheckpointCommitter) &&
            descriptor.ImplementationType == typeof(RuntimeCheckpointCommitter));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRuntimeActivityInputMaterializer) &&
            descriptor.ImplementationType == typeof(RuntimeActivityInputMaterializer));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowSchedulerDrainObserver) &&
            descriptor.ImplementationType == typeof(NoopWorkflowSchedulerDrainObserver));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowSchedulerWorkHandler) &&
            descriptor.ImplementationType == typeof(WorkflowStartSchedulerWorkHandler));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowSchedulerWorkHandler) &&
            descriptor.ImplementationType == typeof(WorkflowScheduleActivitySchedulerWorkHandler));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowSchedulerWorkHandler) &&
            descriptor.ImplementationType == typeof(WorkflowStartActivitySchedulerWorkHandler));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowSchedulerWorkHandler) &&
            descriptor.ImplementationType == typeof(WorkflowCompleteActivitySchedulerWorkHandler));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowSchedulerWorkHandler) &&
            descriptor.ImplementationType == typeof(WorkflowCheckpointSchedulerWorkHandler));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowSchedulerWorkHandler) &&
            descriptor.ImplementationType == typeof(MissingActivityInvocationSchedulerWorkHandler));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowSchedulerWorkHandler) &&
            descriptor.ImplementationType == typeof(NoopWorkflowSchedulerWorkHandler));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowExecutionAgentProvider) &&
            descriptor.ImplementationType == typeof(InProcessWorkflowExecutionAgentProvider));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRuntimeExecutionIdGenerator) &&
            descriptor.ImplementationType == typeof(GuidRuntimeExecutionIdGenerator) &&
            descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowExecutionStartDispatcher) &&
            descriptor.ImplementationType == typeof(WorkflowExecutionStartDispatcher) &&
            descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IWorkflowExecutor));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRequestHandler));

        using var provider = services.BuildServiceProvider();

        Assert.IsType<InProcessWorkflowExecutionAgentProvider>(provider.GetRequiredService<IWorkflowExecutionAgentProvider>());
        Assert.IsType<WorkflowSchedulerCommandProcessor>(provider.GetRequiredService<IWorkflowExecutionCommandProcessor>());
        Assert.IsType<InMemoryWorkflowSchedulerWorkQueue>(provider.GetRequiredService<IWorkflowSchedulerWorkQueue>());
        Assert.IsType<InMemoryActivityExecutionStateStore>(provider.GetRequiredService<IActivityExecutionStateStore>());
        Assert.IsType<WorkflowSchedulerDrainer>(provider.GetRequiredService<IWorkflowSchedulerDrainer>());
        Assert.IsType<ImmediateWorkflowSchedulerDrainPolicy>(provider.GetRequiredService<IWorkflowSchedulerDrainPolicy>());
        Assert.IsType<ImmediateRuntimeCheckpointPersistencePolicy>(provider.GetRequiredService<IRuntimeCheckpointPersistencePolicy>());
        Assert.IsType<InMemoryRuntimeCheckpointWriter>(provider.GetRequiredService<IRuntimeCheckpointWriter>());
        Assert.IsType<NoopRuntimePostCommitIntentDispatcher>(provider.GetRequiredService<IRuntimePostCommitIntentDispatcher>());
        Assert.IsType<RuntimeCheckpointCommitter>(provider.GetRequiredService<RuntimeCheckpointCommitter>());
        Assert.IsType<RuntimeActivityInputMaterializer>(provider.GetRequiredService<IRuntimeActivityInputMaterializer>());
        Assert.IsType<GuidRuntimeExecutionIdGenerator>(provider.GetRequiredService<IRuntimeExecutionIdGenerator>());
        Assert.IsType<WorkflowExecutionStartDispatcher>(provider.GetRequiredService<IWorkflowExecutionStartDispatcher>());
        Assert.Contains(provider.GetServices<IWorkflowSchedulerDrainObserver>(), observer => observer is NoopWorkflowSchedulerDrainObserver);
        var schedulerWorkHandlers = provider.GetServices<IWorkflowSchedulerWorkHandler>().ToArray();
        Assert.Contains(schedulerWorkHandlers, handler => handler is WorkflowStartSchedulerWorkHandler);
        Assert.Contains(schedulerWorkHandlers, handler => handler is WorkflowScheduleActivitySchedulerWorkHandler);
        Assert.Contains(schedulerWorkHandlers, handler => handler is WorkflowStartActivitySchedulerWorkHandler);
        Assert.Contains(schedulerWorkHandlers, handler => handler is WorkflowCompleteActivitySchedulerWorkHandler);
        Assert.Contains(schedulerWorkHandlers, handler => handler is WorkflowCheckpointSchedulerWorkHandler);
        Assert.Contains(schedulerWorkHandlers, handler => handler is MissingActivityInvocationSchedulerWorkHandler);
        Assert.Contains(schedulerWorkHandlers, handler => handler is NoopWorkflowSchedulerWorkHandler);
        Assert.Contains(schedulerWorkHandlers, handler => handler is IFallbackWorkflowSchedulerWorkHandler);
        Assert.True(
            Array.FindIndex(schedulerWorkHandlers, handler => handler is WorkflowStartSchedulerWorkHandler) <
            Array.FindIndex(schedulerWorkHandlers, handler => handler is WorkflowScheduleActivitySchedulerWorkHandler));
        Assert.True(
            Array.FindIndex(schedulerWorkHandlers, handler => handler is WorkflowScheduleActivitySchedulerWorkHandler) <
            Array.FindIndex(schedulerWorkHandlers, handler => handler is WorkflowStartActivitySchedulerWorkHandler));
        Assert.True(
            Array.FindIndex(schedulerWorkHandlers, handler => handler is WorkflowStartActivitySchedulerWorkHandler) <
            Array.FindIndex(schedulerWorkHandlers, handler => handler is WorkflowCompleteActivitySchedulerWorkHandler));
        Assert.True(
            Array.FindIndex(schedulerWorkHandlers, handler => handler is WorkflowCompleteActivitySchedulerWorkHandler) <
            Array.FindIndex(schedulerWorkHandlers, handler => handler is WorkflowCheckpointSchedulerWorkHandler));
        Assert.True(
            Array.FindIndex(schedulerWorkHandlers, handler => handler is WorkflowCheckpointSchedulerWorkHandler) <
            Array.FindIndex(schedulerWorkHandlers, handler => handler is MissingActivityInvocationSchedulerWorkHandler));
        Assert.True(
            Array.FindIndex(schedulerWorkHandlers, handler => handler is MissingActivityInvocationSchedulerWorkHandler) <
            Array.FindIndex(schedulerWorkHandlers, handler => handler is NoopWorkflowSchedulerWorkHandler));
    }
}
