namespace Elsa.Workflows.Design.Persistence.Core.Constants;

/// <summary>Provider-neutral logical units that a workflow-design mutation may change.</summary>
public static class DesignPersistenceUnitNames
{
    public const string Definitions = "workflowDefinition";
    public const string Versions = "workflowDefinitionVersion";
    public const string Drafts = "workflowDefinitionDraft";
    public const string DraftLayouts = "workflowDefinitionDraftLayout";
    public const string VersionLayouts = "workflowDefinitionVersionLayout";
}
