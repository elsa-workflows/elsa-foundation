using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;

namespace Groundwork.Core.Physicalization;

public static class PhysicalizationProjection
{
    public static IReadOnlyList<PhysicalizedFieldPlan> EligibleFields(StorageUnit unit)
    {
        if (unit.Physicalization.Kind != PhysicalizationKind.Optimized)
            return [];

        return unit.Indexes
            .Where(IsEligible)
            .Select(index => new PhysicalizedFieldPlan(
                index.Identity,
                index.Fields[0].Path,
                index.ValueKind,
                index.IsUnique,
                index.IsSortable))
            .ToList();
    }

    public static bool IsEligible(StorageUnit unit, string indexName) =>
        EligibleFields(unit).Any(field => field.Name == indexName);

    private static bool IsEligible(IndexDeclaration index) =>
        index.Fields.Count == 1 &&
        index.MissingValueBehavior == MissingValueBehavior.Excluded &&
        index.SupportedOperations.Contains(PortableQueryOperation.Equal);
}
