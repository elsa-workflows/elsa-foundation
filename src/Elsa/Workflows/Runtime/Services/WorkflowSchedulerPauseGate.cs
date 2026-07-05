using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class WorkflowSchedulerPauseGate : IWorkflowSchedulerPauseGate
{
    private static readonly JsonSerializerOptions PayloadJsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IRuntimePauseDecisionProvider _pauseDecisionProvider;
    private readonly TimeProvider _timeProvider;

    public WorkflowSchedulerPauseGate(
        IRuntimePauseDecisionProvider pauseDecisionProvider,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(pauseDecisionProvider);

        _pauseDecisionProvider = pauseDecisionProvider;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<SchedulerPauseDecision?> EvaluateAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        var request = CreateRequest(workItem);
        if (request is null)
            return null;

        return await _pauseDecisionProvider.DecideAsync(request, cancellationToken);
    }

    private RuntimePauseDecisionRequest? CreateRequest(RuntimeSchedulerWorkItem workItem) =>
        workItem.CommandKind switch
        {
            WorkflowExecutionCommandKind.StartActivity => new RuntimePauseDecisionRequest(
                boundary: RuntimePauseBoundary.BeforeActivityExecutionStart,
                evaluatedAt: _timeProvider.GetUtcNow(),
                workflowExecutionId: workItem.WorkflowExecutionId,
                activityExecutionId: TryReadPayload<RuntimeStartActivityCommandPayload>(workItem)?.ActivityExecutionId,
                metadata: CreateMetadata(workItem)),

            WorkflowExecutionCommandKind.InvokeActivity => new RuntimePauseDecisionRequest(
                boundary: RuntimePauseBoundary.BeforeActivityExecutionStart,
                evaluatedAt: _timeProvider.GetUtcNow(),
                workflowExecutionId: workItem.WorkflowExecutionId,
                activityExecutionId: TryReadPayload<RuntimeInvokeActivityCommandPayload>(workItem)?.ActivityExecutionId,
                metadata: CreateMetadata(workItem)),

            WorkflowExecutionCommandKind.GeneratedEvent => new RuntimePauseDecisionRequest(
                boundary: RuntimePauseBoundary.BeforeGeneratorEmission,
                evaluatedAt: _timeProvider.GetUtcNow(),
                workflowExecutionId: workItem.WorkflowExecutionId,
                activityExecutionId: TryReadPayload<SchedulerGeneratedEventWorkItem>(workItem)?.GeneratorActivityExecutionId,
                generatorId: TryReadGeneratorId(workItem),
                metadata: CreateMetadata(workItem)),

            _ => null
        };

    private static T? TryReadPayload<T>(RuntimeSchedulerWorkItem workItem)
    {
        if (workItem.Payload is not { } payload)
            return default;

        try
        {
            return payload.Deserialize<T>(PayloadJsonSerializerOptions);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            return default;
        }
    }

    private static string? TryReadGeneratorId(RuntimeSchedulerWorkItem workItem) =>
        workItem.CommandMetadata.TryGetValue("GeneratorId", out var generatorId) && !string.IsNullOrWhiteSpace(generatorId)
            ? generatorId
            : null;

    private static IReadOnlyDictionary<string, string> CreateMetadata(RuntimeSchedulerWorkItem workItem) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["runtime.schedulerWorkItemId"] = workItem.WorkItemId,
            ["runtime.schedulerCommandKind"] = workItem.CommandKind.ToString()
        };
}
