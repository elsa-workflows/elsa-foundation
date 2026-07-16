namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Provider-neutral durable keys for values owned by an activity boundary execution.</summary>
public static class ActivityExecutionScopeKeys
{
    public static string Input(string activityExecutionId, string referenceKey) => Key(activityExecutionId, "input", referenceKey);
    public static string Variable(string activityExecutionId, string referenceKey) => Key(activityExecutionId, "variable", referenceKey);
    public static string Output(string activityExecutionId, string referenceKey) => Key(activityExecutionId, "output", referenceKey);

    public static string Boundary(string activityExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        return $"activity-scope:{activityExecutionId.Length}:{activityExecutionId}:boundary";
    }

    private static string Key(string activityExecutionId, string role, string referenceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceKey);
        return $"activity-scope:{activityExecutionId.Length}:{activityExecutionId}:{role}:{referenceKey.Length}:{referenceKey}";
    }
}
