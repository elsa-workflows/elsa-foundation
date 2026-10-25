namespace Elsa.Activities.Bpmn.Models;

/// <summary>Data retained in the existing element property bag, without changing the authored envelope.</summary>
public static class BpmnRetainedAuthoring
{
    /// <summary>
    /// A flow id appended to this key identifies its serialized typed condition expression on the source
    /// element. It is retained for edit/export only; the current Foundation host refuses this execution profile.
    /// </summary>
    public const string ConditionExpressionPrefix = "elsa.bpmn.retained-condition:";
}
