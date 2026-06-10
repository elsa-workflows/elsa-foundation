using Groundwork.Core.Manifests;
using Groundwork.Core.Physicalization;

namespace Groundwork.Relational.Physicalization;

public static class RelationalPhysicalizationNames
{
    public static string TableName(StorageUnit unit) => $"groundwork_physicalized_{Sanitize(unit.Identity.Value)}";

    public static string ColumnName(PhysicalizedFieldPlan field) => $"p_{Sanitize(field.Name)}";

    public static string IndexName(StorageUnit unit, PhysicalizedFieldPlan field, bool unique)
    {
        var prefix = unique ? "ux" : "ix";
        return $"{prefix}_{TableName(unit)}_{ColumnName(field)}";
    }

    private static string Sanitize(string value)
    {
        var characters = value.Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_');
        return string.Concat(characters);
    }
}
