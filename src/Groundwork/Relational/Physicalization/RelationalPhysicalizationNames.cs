using Groundwork.Core.Manifests;
using Groundwork.Core.Physicalization;

namespace Groundwork.Relational.Physicalization;

public static class RelationalPhysicalizationNames
{
    private const int MaxIdentifierLength = 63;
    private const string TablePrefix = "groundwork_physicalized_";
    private const string ColumnPrefix = "p_";

    public static string TableName(StorageUnit unit) => $"{TablePrefix}{PhysicalizationNameEncoder.Encode(unit.Identity.Value, MaxIdentifierLength - TablePrefix.Length)}";

    public static string ColumnName(PhysicalizedFieldPlan field) => $"{ColumnPrefix}{PhysicalizationNameEncoder.Encode(field.Name, MaxIdentifierLength - ColumnPrefix.Length)}";

    public static string IndexName(StorageUnit unit, PhysicalizedFieldPlan field, bool unique)
    {
        var prefix = unique ? "ux" : "ix";
        var encoded = PhysicalizationNameEncoder.Encode($"{unit.Identity.Value}_{field.Name}", MaxIdentifierLength - prefix.Length - 1);
        return $"{prefix}_{encoded}";
    }

}
