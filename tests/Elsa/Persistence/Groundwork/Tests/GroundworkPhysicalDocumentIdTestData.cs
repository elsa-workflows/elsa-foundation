using Groundwork.Core.Text;

namespace Elsa.Persistence.Groundwork.Tests;

internal static class GroundworkPhysicalDocumentIdTestData
{
    public static string PhysicalAliasFor(string logicalId)
    {
        if (logicalId.Length <= PortableStringComparison.MaximumIdentityCodeUnits)
            throw new ArgumentException("Collision test data requires an over-limit logical identity.", nameof(logicalId));

        var identity = PortableStringComparison.ProjectIdentity(logicalId, PortableStringComparisonPolicy.Ordinal);
        return $"elsa-logical-id:v1:{identity.LookupKey}";
    }
}
