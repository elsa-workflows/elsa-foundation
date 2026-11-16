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

/// <summary>
/// The metadata of a durable value an activity boundary execution owns. The boundary that writes its inputs and
/// variables, and a capture that writes one of its variables, both build it here, so the row shape has one owner.
/// </summary>
public static class ActivityExecutionScopeValues
{
    public const string InputRole = "input";
    public const string VariableRole = "variable";
    public const string OutputRole = "output";

    public static Dictionary<string, string> Metadata(
        string boundaryActivityExecutionId,
        string role,
        string referenceKey,
        string storageDriverKey,
        string? inputName = null,
        string? variableName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boundaryActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageDriverKey);

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Constants.RuntimeMetadataKeys.BoundaryExecutionScopeId] = boundaryActivityExecutionId,
            [Constants.RuntimeMetadataKeys.BoundaryValueRole] = role,
            [Constants.RuntimeMetadataKeys.BoundaryReferenceKey] = referenceKey,
            [Constants.RuntimeMetadataKeys.StorageDriverKey] = storageDriverKey
        };
        if (inputName is not null)
            metadata[Constants.RuntimeMetadataKeys.BoundaryInputName] = inputName;
        if (variableName is not null)
            metadata[Constants.RuntimeMetadataKeys.BoundaryVariableName] = variableName;
        return metadata;
    }
}
