using Groundwork.Core.Text;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Maps an unbounded Elsa logical identity to a portable Groundwork document identity.
/// </summary>
/// <remarks>
/// Short identities remain unchanged. Longer identities use a versioned, ordinal hash projection while
/// the owning document retains the complete logical identity for round-tripping and collision detection.
/// </remarks>
internal static class GroundworkPhysicalDocumentId
{
    private const string HashedIdentityPrefix = "elsa-logical-id:v1:";

    public static string FromLogicalId(string logicalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalId);
        if (logicalId.Length <= PortableStringComparison.MaximumIdentityCodeUnits)
            return logicalId;

        var identity = PortableStringComparison.ProjectIdentity(logicalId, PortableStringComparisonPolicy.Ordinal);
        return $"{HashedIdentityPrefix}{identity.LookupKey}";
    }
}
