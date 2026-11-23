using MongoDB.Bson;
using MongoDB.Driver;

namespace Kernel.Mongo;

public sealed partial class MongoStore
{
    private static readonly BsonDocument PartitionMarker = new("_id", "partitions");
    private const int StoreSchema = 1, MessageProtocol = 1, JournalCodec = 1;

    private async Task OpenVersionsAsync(IReadOnlyCollection<string> collections, int partitions)
    {
        var marker = await _meta.Find(PartitionMarker).FirstOrDefaultAsync();
        if (marker is null)
        {
            // Initializing 'meta' can leave an empty collection before the first document lands.
            // Every other kernel collection, even an empty one, belongs to a store that already started.
            if (collections.Intersect(DataCollections).Any() || await _meta.Find(FilterDefinition<BsonDocument>.Empty).AnyAsync())
            {
                // A concurrently initializing host can have installed the marker since our first read.
                marker = await _meta.Find(PartitionMarker).FirstOrDefaultAsync();
            }
            else
            {
                // Count and formats share the first durable write. A retry at the same count can finish
                // creating collections and rows, and a racing open never overwrites an existing marker.
                var initial = new BsonDocument
                {
                    { "count", partitions }, { "storeSchema", StoreSchema },
                    { "messageProtocol", MessageProtocol }, { "journalCodec", JournalCodec }
                };
                marker = await _meta.FindOneAndUpdateAsync(PartitionMarker, new BsonDocument("$setOnInsert", initial),
                    new FindOneAndUpdateOptions<BsonDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After });
            }
        }

        var recorded = RequireVersions(marker);
        if (recorded != partitions)
            throw new StoreException(Failure.Definite, $"The database has {recorded} partitions and was opened with {partitions}. The count is fixed at creation: recreate the database.");
    }

    private async Task RequireVersionsAsync(IClientSessionHandle session, CancellationToken cancellationToken, FindOptions? options = null) =>
        RequireVersions(await _meta.Find(session, PartitionMarker, options).FirstOrDefaultAsync(cancellationToken));

    private static int RequireVersions(BsonDocument? marker)
    {
        if (marker is null)
            throw new StoreFormatException("The kernel store version marker is missing.");
        if (!Reads("storeSchema", StoreSchema) || !Reads("messageProtocol", MessageProtocol) || !Reads("journalCodec", JournalCodec))
            throw new StoreFormatException($"Unsupported kernel store versions. Stored schema={Value("storeSchema")}, message protocol={Value("messageProtocol")}, journal codec={Value("journalCodec")}. This build reads schema={StoreSchema}, message protocol={MessageProtocol}, journal codec={JournalCodec}.");

        if (!marker.TryGetValue("count", out var count) || !count.IsInt32)
            throw new StoreFormatException("The kernel store partition count is missing or malformed.");
        return count.AsInt32;

        bool Reads(string field, int version) => marker.TryGetValue(field, out var value) && value.IsInt32 && value.AsInt32 == version;
        string Value(string field) => marker.TryGetValue(field, out var value) && value.IsInt32 ? value.AsInt32.ToString(System.Globalization.CultureInfo.InvariantCulture) : "missing or malformed";
    }
}
