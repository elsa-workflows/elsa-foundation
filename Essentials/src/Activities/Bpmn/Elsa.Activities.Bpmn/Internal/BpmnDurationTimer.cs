using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>One duration listener's retained deadline and exact committed delivery source.</summary>
internal sealed record BpmnDurationTimer(int FormatVersion, PersistenceScope Scope, string ExecutionId,
    WorkflowExecutableIdentity Executable, string MaterialIncarnationId, string BindingId, string InvocationId,
    string AttemptId, DateTimeOffset DueAt, string SourceOutboxItemId)
{
    internal const string Kind = "Elsa.Bpmn.DurationTimer.v1";
    [JsonIgnore] public string IntentId => "bpmn-duration:" + BpmnModelIdentity.Create(Scope.Value, ExecutionId, InvocationId, AttemptId);
    [JsonIgnore] public string InputId => "timer-elapsed:" + BpmnModelIdentity.Create(Scope.Value, ExecutionId, InvocationId, AttemptId);

    internal bool Matches(RuntimeModelExecutionState state) => FormatVersion == 1 && Scope == state.Scope && ExecutionId == state.ExecutionId &&
        state.Protocol == new RuntimeExecutionProtocol(BpmnModelProtocol.Key, BpmnModelProtocol.Version) &&
        MaterialIncarnationId == state.MaterialIncarnationId && WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(Executable, state.Executable) &&
        !string.IsNullOrWhiteSpace(BindingId) && !string.IsNullOrWhiteSpace(InvocationId) && !string.IsNullOrWhiteSpace(AttemptId) &&
        !string.IsNullOrWhiteSpace(SourceOutboxItemId) && SourceOutboxItemId.EndsWith(":" + IntentId, StringComparison.Ordinal) &&
        DueAt != default && DueAt.Offset == TimeSpan.Zero;

    internal static TimeSpan Duration(BpmnElement element)
    {
        if (element.ElementType != BpmnElementTypes.BoundaryEvent || !element.CancelActivity ||
            element.EventDefinitions is not { Count: 1 } || element.EventDefinitions.Single() is not { Type: BpmnEventDefinitionTypes.Timer } timer ||
            timer.Properties.Count != 1 || !timer.Properties.TryGetValue(BpmnEventDefinitionProperties.Interval, out var interval))
            throw new BpmnModelValidationException("The duration profile requires one interrupting boundary timer with an interval.");
        try
        {
            var duration = XmlConvert.ToTimeSpan(interval);
            if (duration <= TimeSpan.Zero) throw new BpmnModelValidationException("A duration timer requires a positive interval.");
            return duration;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentNullException)
        { throw new BpmnModelValidationException("The duration timer interval is not representable.", exception); }
    }
}

/// <summary>Uses the existing delayed outbox and protected model queue; acknowledgment requires its exact input receipt.</summary>
internal sealed class BpmnDurationTimerDelivery(BpmnNativeWorkBoundary sources, IPostCommitOutboxLookupStore outbox,
    IWorkflowSchedulerWorkQueue queue, IPersistenceAccessContextAccessor access, IWorkflowExecutionActorProvider actors,
    TimeProvider clock, IRuntimeModelExecutionReader? models = null) : IRuntimePostCommitDurableSourceIntentHandler
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    internal async ValueTask ValidateAsync(RuntimeModelExecutionState model, RuntimeModelExecutionCommand command, CancellationToken cancellationToken)
    {
        var timer = command.Payload.Deserialize<BpmnDurationTimer>(Web) ?? throw new BpmnModelValidationException("The duration timer address is missing.");
        access.Current.EnsureScope(model.Scope); access.Current.EnsureTenantScope(model.TenantId);
        if (!timer.Matches(model) || command.InputId != timer.InputId)
            throw new BpmnModelValidationException("The duration timer does not address this exact model invocation.");
        var source = await outbox.FindDurableAsync(timer.SourceOutboxItemId, cancellationToken)
            ?? throw Pending("The committed duration timer source is unavailable.");
        if (source.StimulusDeliverySourceScope != model.Scope || source.Intent.Kind != BpmnDurationTimer.Kind ||
            source.Intent.IntentId != timer.IntentId || source.Intent.WorkflowExecutionId != model.ExecutionId ||
            source.Intent.NotBefore != timer.DueAt || source.Intent.Payload is not { } payload || !JsonElement.DeepEquals(payload, command.Payload))
            throw new BpmnModelValidationException("The duration timer differs from its committed deadline and source.");
        if (clock.GetUtcNow() < timer.DueAt) throw Pending("The duration timer deadline has not arrived.");
    }

    public async ValueTask HandleAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default)
    {
        try { await HandleCoreAsync(source, cancellationToken); }
        catch (JsonException exception) { throw InvalidOwnedJson(source, exception); }
        catch (BpmnModelValidationException exception) when (exception.InnerException is JsonException cause)
        { throw InvalidOwnedJson(source, cause); }
    }

    private static BpmnModelValidationException InvalidOwnedJson(PostCommitDeliveryContext source, JsonException exception) =>
        new($"BPMN delivery '{source.Intent.Kind}' for execution '{source.Intent.WorkflowExecutionId}', " +
            $"outbox source '{source.SourceOutboxItemId}' in scope '{source.Scope.Value}' has invalid owned JSON.", exception);

    private async ValueTask HandleCoreAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken)
    {
        await sources.RevalidateSourceAsync(source, cancellationToken);
        if (source.Intent.Kind != BpmnDurationTimer.Kind || source.Intent.Payload is not { } payload)
            throw new BpmnModelValidationException("The duration timer delivery is missing its typed source.");
        var timer = payload.Deserialize<BpmnDurationTimer>(Web) ?? throw new BpmnModelValidationException("The duration timer address is missing.");
        var reader = models ?? throw new NotSupportedException("Duration timers require qualified model state persistence.");
        var model = await reader.FindAsync(timer.ExecutionId, cancellationToken) ?? throw Pending("The duration timer destination is unavailable.");
        if (timer.SourceOutboxItemId != source.SourceOutboxItemId || source.Scope != model.Scope)
            throw new BpmnModelValidationException("The duration timer delivery has a different physical source.");
        var command = new RuntimeModelExecutionCommand(model.Executable, model.MaterialIncarnationId, timer.InputId,
            "timer-elapsed", payload, model.Authority, model.TenantId);
        await ValidateAsync(model, command, cancellationToken);
        var fingerprint = RuntimeCheckpointCommitFingerprint.ComputeModelInput(command);
        if (await reader.FindInputAsync(model.ExecutionId, timer.InputId, cancellationToken) is { } receipt)
        {
            if (receipt.ContentFingerprint != fingerprint) throw new RuntimeCheckpointReplayConflictException(timer.InputId);
            if (receipt.InvocationId != timer.InvocationId || receipt.AttemptId != timer.AttemptId)
                throw new BpmnModelValidationException("The duration timer receipt belongs to another invocation.");
            return;
        }
        if (actors is not InProcessWorkflowExecutionActorProvider || !queue.SupportsClaimTransitions || !queue.SupportsClaimValidation)
            throw new NotSupportedException("Duration timers require the qualified built-in model mailbox and protected queue.");
        var id = "bpmn-duration-delivery:" + timer.InputId;
        await queue.EnqueueAsync(new(RuntimeModelExecutionCommand.CreateSchedulerWorkItemId(timer.InputId), model.ExecutionId, timer.InputId,
            WorkflowExecutionCommandKind.ExecuteModel, id, id, source.Intent.RecordedAt, clock.GetUtcNow(),
            payload: JsonSerializer.SerializeToElement(command, Web)), cancellationToken);
        throw Pending("The elapsed duration timer remains owed until its exact model input is consumed.");
    }

    private static RuntimePostCommitDeliveryException Pending(string message) =>
        new(PostCommitFailureKind.Transient, "bpmn.duration.pending", message);
}
