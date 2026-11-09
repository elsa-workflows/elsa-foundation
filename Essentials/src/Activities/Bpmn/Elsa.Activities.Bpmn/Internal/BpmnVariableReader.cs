using Bpmn.Model;
using Bpmn.Semantics;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

internal sealed class BpmnVariableReader(IRuntimeActivityExecutionContext context) : IBpmnVariableReader
{
    public bool TryRead(string name, out BpmnValue value)
    {
        value = BpmnValue.Absent;
        if (!context.TryReadScopedVariableValue(name, out var envelope) || envelope is null)
            return false;
        value = envelope.Presence switch
        {
            ValuePresence.Absent => BpmnValue.Absent,
            ValuePresence.ExplicitNull => BpmnValue.Null,
            ValuePresence.Withheld => BpmnValue.Refused(SecretBindingDiagnostics.WithheldInputCode,
                SecretBindingDiagnostics.WithheldVariableNotResolved(name).Message),
            _ => envelope.InlineValue is { } json
                ? BpmnValue.From(json, envelope.Type.Alias)
                : new BpmnValue(BpmnValuePresence.StoredExternally, envelope.Type.Alias)
        };
        return true;
    }
}
