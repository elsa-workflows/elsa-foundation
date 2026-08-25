using System.Globalization;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Builds the current, injective physical identity for a workflow-scoped runtime row.</summary>
/// <remarks>
/// Each component is length-prefixed so delimiters and component boundaries cannot forge another identity.
/// The admitted projection and envelope limits are checked together here so every workflow-scoped runtime
/// store shares one physical identity contract.
/// </remarks>
internal static class GroundworkV2CompositeIdentityCodec
{
    public static string From(string workflowExecutionId, string logicalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalId);
        EnsureProjectionLength(workflowExecutionId, nameof(workflowExecutionId));
        EnsureProjectionLength(logicalId, nameof(logicalId));

        var physicalId = string.Concat(
            workflowExecutionId.Length.ToString(CultureInfo.InvariantCulture),
            ":",
            workflowExecutionId,
            logicalId.Length.ToString(CultureInfo.InvariantCulture),
            ":",
            logicalId);
        if (physicalId.Length > ElsaRuntimeV2StorageManifest.IdMaximumLength)
        {
            throw new InvalidOperationException(
                $"Groundwork runtime physical identity exceeds the admitted ID length of {ElsaRuntimeV2StorageManifest.IdMaximumLength}.");
        }

        return physicalId;
    }

    private static void EnsureProjectionLength(string value, string parameterName)
    {
        if (value.Length > ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"Groundwork runtime identity parts cannot exceed {ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength} characters.");
        }
    }
}
