using System.Data.Common;
using System.Diagnostics;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Diagnostics;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Services.Coalescing;
using CShells.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Http.IntegrationTests;

internal sealed class ResponseReplayDbCommandInterceptor(ResponseReplayObservation observation) : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        observation.RecordCommandStart(eventData.CommandId, Activity.Current, $"Reader|{eventData.CommandSource}");
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        observation.RecordCommandStart(eventData.CommandId, Activity.Current, $"Reader|{eventData.CommandSource}");
        return ValueTask.FromResult(result);
    }

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        observation.RecordCommandOutcome(eventData.CommandId, ResponseReplayAttemptOutcome.Succeeded);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        observation.RecordCommandOutcome(eventData.CommandId, ResponseReplayAttemptOutcome.Succeeded);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        observation.RecordCommandStart(eventData.CommandId, Activity.Current, $"Scalar|{eventData.CommandSource}");
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        observation.RecordCommandStart(eventData.CommandId, Activity.Current, $"Scalar|{eventData.CommandSource}");
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        observation.RecordCommandOutcome(eventData.CommandId, ResponseReplayAttemptOutcome.Succeeded);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        observation.RecordCommandOutcome(eventData.CommandId, ResponseReplayAttemptOutcome.Succeeded);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        observation.RecordCommandStart(eventData.CommandId, Activity.Current, $"NonQuery|{eventData.CommandSource}");
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        observation.RecordCommandStart(eventData.CommandId, Activity.Current, $"NonQuery|{eventData.CommandSource}");
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        observation.RecordCommandOutcome(eventData.CommandId, ResponseReplayAttemptOutcome.Succeeded);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        observation.RecordCommandOutcome(eventData.CommandId, ResponseReplayAttemptOutcome.Succeeded);
        return ValueTask.FromResult(result);
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) =>
        observation.RecordCommandOutcome(eventData.CommandId, ResponseReplayAttemptOutcome.Failed);

    public override Task CommandFailedAsync(
        DbCommand command,
        CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        observation.RecordCommandOutcome(eventData.CommandId, ResponseReplayAttemptOutcome.Failed);
        return Task.CompletedTask;
    }

    public override void CommandCanceled(DbCommand command, CommandEndEventData eventData) =>
        observation.RecordCommandOutcome(eventData.CommandId, ResponseReplayAttemptOutcome.Canceled);

    public override Task CommandCanceledAsync(
        DbCommand command,
        CommandEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        observation.RecordCommandOutcome(eventData.CommandId, ResponseReplayAttemptOutcome.Canceled);
        return Task.CompletedTask;
    }
}

internal sealed class ResponseReplayObservedCheckpointCommitStore(
    IRuntimeCheckpointCommitStore inner,
    ResponseReplayObservation observation) : IRuntimeCheckpointCommitStore
{
    public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointPersistenceDecision decision,
        CancellationToken cancellationToken = default)
    {
        var attempt = observation.BeginLogicalCheckpoint(commit, decision);
        try
        {
            var result = await inner.CommitAsync(commit, decision, cancellationToken);
            observation.CompleteLogicalCheckpoint(attempt, ResponseReplayAttemptOutcome.Succeeded);
            return result;
        }
        catch (OperationCanceledException)
        {
            observation.CompleteLogicalCheckpoint(attempt, ResponseReplayAttemptOutcome.Canceled);
            throw;
        }
        catch
        {
            observation.CompleteLogicalCheckpoint(attempt, ResponseReplayAttemptOutcome.Failed);
            throw;
        }
    }
}

internal sealed class ResponseReplayObservedDurableCheckpointCommitStore(
    IRuntimeCheckpointCommitStore inner,
    IRuntimeCoalescingSessionAccessor sessionAccessor,
    ResponseReplayObservation observation) : IRuntimeCheckpointCommitStore
{
    public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointPersistenceDecision decision,
        CancellationToken cancellationToken = default)
    {
        var session = sessionAccessor.Current;
        var hasBufferedSegment = session is { IsActive: true } &&
                                 session.AppliesTo(commit.WorkflowExecutionId) &&
                                 session.HasBufferedChanges;
        var attempt = observation.BeginDurableCheckpoint(commit, decision, hasBufferedSegment);
        try
        {
            var result = await inner.CommitAsync(commit, decision, cancellationToken);
            observation.CompleteDurableCheckpoint(attempt, ResponseReplayAttemptOutcome.Succeeded);
            return result;
        }
        catch (OperationCanceledException)
        {
            observation.CompleteDurableCheckpoint(attempt, ResponseReplayAttemptOutcome.Canceled);
            throw;
        }
        catch
        {
            observation.CompleteDurableCheckpoint(attempt, ResponseReplayAttemptOutcome.Failed);
            throw;
        }
    }
}

internal sealed class ResponseReplayObservedWorkflowEngineTracer(
    IWorkflowEngineTracer inner,
    ResponseReplayObservation observation) : IWorkflowEngineTracer
{
    public Activity? StartDrainCycle(RuntimeSchedulerDrainRequest request)
    {
        observation.RecordDrain(request);
        return EnsureExecutionActivity(inner.StartDrainCycle(request), WorkflowEngineTelemetry.DrainSpanName, request.WorkflowExecutionId);
    }

    public Activity? StartDispatch(RuntimeSchedulerWorkItem workItem)
    {
        observation.RecordDispatch(workItem);
        var activity = EnsureExecutionActivity(inner.StartDispatch(workItem), WorkflowEngineTelemetry.DispatchSpanName, workItem.WorkflowExecutionId);
        activity?.SetTag(WorkflowEngineTelemetry.WorkItemIdTag, workItem.WorkItemId);
        activity?.SetTag(WorkflowEngineTelemetry.CommandKindTag, workItem.CommandKind.ToString());
        return activity;
    }

    public Activity? StartActivityExecution(RuntimeSchedulerWorkItem workItem)
    {
        observation.RecordActivityExecution(workItem);
        var activity = EnsureExecutionActivity(inner.StartActivityExecution(workItem), WorkflowEngineTelemetry.ActivityExecuteSpanName, workItem.WorkflowExecutionId);
        activity?.SetTag(WorkflowEngineTelemetry.WorkItemIdTag, workItem.WorkItemId);
        activity?.SetTag(WorkflowEngineTelemetry.CommandKindTag, workItem.CommandKind.ToString());
        return activity;
    }

    public Activity? StartCheckpointCommit(RuntimeCheckpointCommit commit) =>
        EnsureExecutionActivity(inner.StartCheckpointCommit(commit), WorkflowEngineTelemetry.CheckpointCommitSpanName, commit.WorkflowExecutionId);

    private static Activity EnsureExecutionActivity(Activity? activity, string name, string executionId)
    {
        activity ??= new Activity(name).Start();
        activity.SetTag(WorkflowEngineTelemetry.WorkflowExecutionIdTag, executionId);
        return activity;
    }
}

[ShellFeature(
    name: "ResponseReplayObservation",
    DisplayName = "Response Replay Test Observation",
    Description = "Test-only bounded command and checkpoint counters for response replay comparison.",
    DependsOn = new object[] { "WorkflowsRuntimeCheckpointPersistence", "WorkflowsRuntimeEntityFrameworkCore" })]
public sealed class ResponseReplayObservationFeature : IShellFeature, IPostConfigureShellServices
{
    public string RequestPath { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string TargetArtifactId { get; set; } = string.Empty;
    public string TargetArtifactHash { get; set; } = string.Empty;
    public string TargetProfile { get; set; } = string.Empty;
    public string EndpointNodeId { get; set; } = string.Empty;
    public string ResponseNodeId { get; set; } = string.Empty;

    public void ConfigureServices(IServiceCollection services)
    {
        var options = new ResponseReplayMeasurementOptions(
            RequestPath, CorrelationId, TargetArtifactId, TargetArtifactHash, TargetProfile, EndpointNodeId, ResponseNodeId);
        if (string.IsNullOrWhiteSpace(options.RequestPath) ||
            string.IsNullOrWhiteSpace(options.CorrelationId) ||
            string.IsNullOrWhiteSpace(options.TargetArtifactId) ||
            string.IsNullOrWhiteSpace(options.TargetArtifactHash) ||
            string.IsNullOrWhiteSpace(options.EndpointNodeId) ||
            string.IsNullOrWhiteSpace(options.ResponseNodeId) ||
            options.TargetProfile is not ("External" or "ReplaySafe"))
            throw new InvalidOperationException("The response-replay observation feature requires an exact request and artifact identity/profile.");

        services.AddSingleton(options);
        var observation = new ResponseReplayObservation(options);
        var interceptor = new ResponseReplayDbCommandInterceptor(observation);
        services.AddSingleton(observation);
        services.AddSingleton(interceptor);
        services.ConfigureDbContext<RuntimeSqliteDbContext>(builder => builder.AddInterceptors(interceptor));
    }

    public void PostConfigureServices(IServiceCollection services)
    {
        var observation = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(ResponseReplayObservation))?.ImplementationInstance as ResponseReplayObservation
            ?? throw new InvalidOperationException("The response-replay observation feature did not register its counter.");
        DecorateCoalescedInner(services, observation);
        DecorateOuterStore(services, observation);
        DecorateWorkflowEngineTracer(services, observation);
    }

    private static void DecorateCoalescedInner(IServiceCollection services, ResponseReplayObservation observation)
    {
        var serviceType = typeof(CoalescingInner<IRuntimeCheckpointCommitStore>);
        var descriptor = services.LastOrDefault(candidate => candidate.ServiceType == serviceType)
            ?? throw new InvalidOperationException("The observation feature requires the registered Coalesced checkpoint inner store.");
        services.Remove(descriptor);
        services.Add(new ServiceDescriptor(serviceType, provider =>
        {
            var resolvedHolder = (CoalescingInner<IRuntimeCheckpointCommitStore>)
                ResponseReplayServiceDescriptorActivator.Instantiate(descriptor, provider);
            var resolvedInner = resolvedHolder.Value;
            if (resolvedInner is CoalescingRuntimeCheckpointCommitStore)
                throw new InvalidOperationException("The observed durable checkpoint inner resolved to another Coalesced decorator.");
            return new CoalescingInner<IRuntimeCheckpointCommitStore>(new ResponseReplayObservedDurableCheckpointCommitStore(
                resolvedInner,
                provider.GetRequiredService<IRuntimeCoalescingSessionAccessor>(),
                observation));
        }, descriptor.Lifetime));
    }

    private static void DecorateOuterStore(IServiceCollection services, ResponseReplayObservation observation)
    {
        var descriptor = services.LastOrDefault(candidate => candidate.ServiceType == typeof(IRuntimeCheckpointCommitStore))
            ?? throw new InvalidOperationException("The observation feature could not find the registered Coalesced checkpoint store.");
        services.Remove(descriptor);
        services.Add(new ServiceDescriptor(typeof(IRuntimeCheckpointCommitStore), provider =>
        {
            var resolvedInner = (IRuntimeCheckpointCommitStore)ResponseReplayServiceDescriptorActivator.Instantiate(descriptor, provider);
            if (resolvedInner is not CoalescingRuntimeCheckpointCommitStore)
                throw new InvalidOperationException($"The observation feature expected the Coalesced checkpoint store, found '{resolvedInner.GetType().FullName}'.");
            return new ResponseReplayObservedCheckpointCommitStore(resolvedInner, observation);
        }, descriptor.Lifetime));
    }

    private static void DecorateWorkflowEngineTracer(IServiceCollection services, ResponseReplayObservation observation)
    {
        var descriptor = services.LastOrDefault(candidate => candidate.ServiceType == typeof(IWorkflowEngineTracer))
            ?? throw new InvalidOperationException("The observation feature could not find the runtime tracer registration.");
        services.Remove(descriptor);
        services.Add(new ServiceDescriptor(typeof(IWorkflowEngineTracer), provider =>
        {
            var inner = (IWorkflowEngineTracer)ResponseReplayServiceDescriptorActivator.Instantiate(descriptor, provider);
            return new ResponseReplayObservedWorkflowEngineTracer(inner, observation);
        }, descriptor.Lifetime));
    }
}

internal static class ResponseReplayServiceDescriptorActivator
{
    public static object Instantiate(ServiceDescriptor descriptor, IServiceProvider serviceProvider)
    {
        if (descriptor.ImplementationInstance is { } instance)
            return instance;
        if (descriptor.ImplementationFactory is { } factory)
            return factory(serviceProvider);
        if (descriptor.ImplementationType is { } implementationType)
            return ActivatorUtilities.CreateInstance(serviceProvider, implementationType);

        throw new InvalidOperationException("A response-replay observation wrapper found an unresolvable service descriptor.");
    }
}
