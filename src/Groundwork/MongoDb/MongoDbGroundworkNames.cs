using Groundwork.Core.Manifests;

namespace Groundwork.MongoDb;

internal static class MongoDbGroundworkNames
{
    public const string SchemaHistoryCollection = "groundwork_schema_history";

    public static string CollectionName(StorageUnit unit) => $"groundwork_{unit.Identity.Value}";
}
