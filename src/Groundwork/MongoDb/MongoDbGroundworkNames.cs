using Groundwork.Core.Manifests;
using Groundwork.Core.Physicalization;

namespace Groundwork.MongoDb;

public static class MongoDbGroundworkNames
{
    public const int MaxEncodedIdentityLength = 120;
    public const int MaxPhysicalizedFieldNameLength = 120;
    public const string SchemaHistoryCollection = "groundwork_schema_history";

    public static string CollectionName(StorageUnit unit) => $"groundwork_{PhysicalizationNameEncoder.Encode(unit.Identity.Value, MaxEncodedIdentityLength)}";

    public static string PhysicalizedFieldName(PhysicalizedFieldPlan field) => PhysicalizationNameEncoder.Encode(field.Name, MaxPhysicalizedFieldNameLength);
}
