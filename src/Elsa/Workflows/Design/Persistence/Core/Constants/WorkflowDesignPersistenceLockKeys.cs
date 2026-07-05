namespace Elsa.Workflows.Design.Persistence.Core.Constants;

public static class WorkflowDesignPersistenceLockKeys
{
    public static string DraftKey(string draftId) => $"workflow-draft:{draftId}";

    /// <summary>
    /// Serializes operations that read-compute-write against a definition's version list
    /// (e.g. publishing a new version, issue #404). Runtime lock name only — not a persisted identifier.
    /// </summary>
    public static string DefinitionKey(string definitionId) => $"workflow-definition:{definitionId}";
}
