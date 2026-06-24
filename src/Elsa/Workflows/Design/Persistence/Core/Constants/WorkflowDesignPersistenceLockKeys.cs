namespace Elsa.Workflows.Design.Persistence.Core.Constants;

public static class WorkflowDesignPersistenceLockKeys
{
    public static string DraftKey(string draftId) => $"workflow-draft:{draftId}";
}
