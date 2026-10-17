using Bpmn.Model;
using Bpmn.Semantics;

namespace UnifiedExecution.BpmnProof;

internal sealed record StoredHostCommand(
    string Kind,
    string? BindingRef = null,
    string? ElementId = null,
    string? Handle = null,
    string? IterationId = null,
    IReadOnlyDictionary<string, BpmnValue>? IterationValues = null,
    IReadOnlyDictionary<string, string>? Correlation = null,
    string? SchedulingCause = null,
    string? StartElementHint = null,
    string? Reason = null,
    string? Code = null,
    string? PayloadJson = null,
    string WorkKind = "activity",
    long? TimerDurationTicks = null);

internal sealed record PendingCommand(long Id, string CommandId, string ScopeId, string Json);

internal sealed record WorkRecord(
    string Handle,
    string ScopeId,
    string BindingRef,
    string ElementId,
    string? IterationId,
    string WorkKind,
    string Status,
    long? DueAtTicks,
    string CommandId);

internal sealed record ScopeRecord(
    string ScopeId,
    string ProcessId,
    string? DefinitionFingerprint,
    string StateJson,
    string ContinuationKind,
    string? ContinuationJson,
    string CorrelationJson,
    bool HasParent,
    int NextHandle,
    long ClockTicks,
    int Revision)
{
    public IReadOnlyDictionary<string, string> Correlation =>
        System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(CorrelationJson)
        ?? new Dictionary<string, string>(StringComparer.Ordinal);
}

internal sealed record HostTraceEvent(
    string Kind,
    string? BindingRef = null,
    string? ElementId = null,
    string? Handle = null,
    string? Outcome = null,
    string? Message = null,
    string? Reason = null,
    string? Disposition = null,
    string? Continuation = null,
    int? Revision = null);

internal sealed record IncidentRecord(string ScopeId, string BindingRef, string Handle, string Message, int Revision);

internal sealed class UnsupportedAlterationException(string operation)
    : Exception($"Unsupported BPMN alteration '{operation}'. Only modeled BPMN events and interpreter commands are supported here.")
{
    public string Operation { get; } = operation;
}

internal static class HostCommandMapper
{
    public static StoredHostCommand Map(BpmnHostCommand command, BpmnGraph graph)
    {
        return command switch
        {
            BpmnHostCommand.StartWork start => MapStart(start, graph),
            BpmnHostCommand.CancelWorkSubtree cancel => new StoredHostCommand(
                "cancel-work-subtree", ElementId: cancel.ElementId, Handle: cancel.Handle, Reason: cancel.Reason),
            BpmnHostCommand.SignalEnclosingScope signal => new StoredHostCommand(
                "signal-enclosing-scope", Code: signal.Code,
                PayloadJson: signal.Payload is { } payload ? payload.GetRawText() : null),
            _ => throw new NotSupportedException($"Unknown BPMN host command type '{command.GetType().FullName}'.")
        };
    }

    private static StoredHostCommand MapStart(BpmnHostCommand.StartWork start, BpmnGraph graph)
    {
        var element = graph.GetRequiredElement(start.ElementId);
        var timerDefinition = element.EventDefinitions.FirstOrDefault(definition => definition.Type == BpmnEventDefinitionTypes.Timer);
        var messageDefinition = element.EventDefinitions.FirstOrDefault(definition => definition.Type == BpmnEventDefinitionTypes.Message);
        var intervalText = timerDefinition?.Properties.GetValueOrDefault(BpmnEventDefinitionProperties.Interval);
        var duration = intervalText is null ? (TimeSpan?)null : System.Xml.XmlConvert.ToTimeSpan(intervalText);
        var workKind = duration is not null ? "timer" : messageDefinition is not null ? "message-listener" : element.ElementType switch
        {
            BpmnElementTypes.UserTask or BpmnElementTypes.ManualTask => "human",
            _ => "activity"
        };

        return new StoredHostCommand(
            "start-work",
            BindingRef: start.BindingRef,
            ElementId: start.ElementId,
            IterationId: start.IterationScope?.IterationId,
            IterationValues: start.IterationScope?.Values,
            Correlation: start.Correlation,
            SchedulingCause: start.SchedulingCause,
            StartElementHint: start.StartElementHint,
            WorkKind: workKind,
            TimerDurationTicks: duration?.Ticks);
    }
}
