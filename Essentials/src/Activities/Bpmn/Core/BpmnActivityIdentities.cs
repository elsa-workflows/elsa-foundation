namespace Elsa.Activities.Bpmn.Core;

/// <summary>Frozen native activity and structure identities shared by BPMN authoring and execution.</summary>
public static class BpmnActivityIdentities
{
    public const string ProcessType = "Elsa.Activities.Bpmn.Activities.BpmnProcess";
    public const string ProcessAlias = "Elsa.BpmnProcess";
    public const string DecisionType = "Elsa.Activities.Bpmn.Activities.BpmnDecision";
    public const string RunType = "Elsa.Activities.Bpmn.Activities.RunBpmn";
    public const string StructureKind = "elsa.bpmn.structure";
    public const string StructureSchemaVersion = "1.0.0";
    public const string ActivitiesSlotName = "Bpmn.Activities";
    public const string CanStartWorkflowInput = "CanStartWorkflow";
    public const string DecisionOutcomeInput = "Outcome";
    public const string ModelInput = "Model";
}
