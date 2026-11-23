using MongoDB.Bson;
using MongoDB.Driver;

namespace Kernel.Mongo;

public sealed partial class MongoStore
{
    private static readonly BsonDocument PartitionMarker = new("_id", "partitions");
    private readonly MongoStoreFormat _format;
    private static MongoStoreFormat JobGenerationFormat => new(2, 2, 1);
    private static MongoStoreFormat ScopedJobRootFormat => new(3, 3, 1);

    private static MongoStoreFormat JobResultFormat => new(4, 4, 1);

    internal Task<bool> UpgradeScopedJobResultsAsync(CancellationToken token = default) =>
        AdvanceStoreFormatAsync(ScopedJobRootFormat, JobResultFormat, cancellationToken: token);

    internal Task<bool> UpgradeScopedJobRootFormatAsync(CancellationToken token = default) =>
        AdvanceStoreFormatAsync(JobGenerationFormat, ScopedJobRootFormat, cancellationToken: token);

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
                    { "count", partitions }, { "storeSchema", _format.StoreSchema },
                    { "messageProtocol", _format.MessageProtocol }, { "journalCodec", _format.JournalCodec }
                };
                marker = await _meta.FindOneAndUpdateAsync(PartitionMarker, new BsonDocument("$setOnInsert", initial),
                    new FindOneAndUpdateOptions<BsonDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After });
            }
        }

        var recorded = RequireVersions(marker, _format);
        if (recorded != partitions)
            throw new StoreException(Failure.Definite, $"The database has {recorded} partitions and was opened with {partitions}. The count is fixed at creation: recreate the database.");
    }

    private async Task<int> RequireVersionsAsync(CancellationToken cancellationToken) =>
        RequireVersions(await _meta.Find(PartitionMarker).FirstOrDefaultAsync(cancellationToken), _format);

    private async Task RequireVersionsAsync(IClientSessionHandle session, CancellationToken cancellationToken) =>
        RequireVersions(await _meta.Find(session, PartitionMarker).FirstOrDefaultAsync(cancellationToken), _format);

    private async Task RequireReadOnlyVersionsAsync(CancellationToken token, IClientSessionHandle? session = null)
    {
        var marker = await (session is null ? _meta.Find(PartitionMarker) : _meta.Find(session, PartitionMarker)).FirstOrDefaultAsync(token);
        // The staged reader understands the earlier checkpoint and inbox representation.
        var readable = _options.JobGenerations && CurrentFormat.Matches(marker) ? CurrentFormat
            : _options.ScopedJobRoots && JobGenerationFormat.Matches(marker) ? JobGenerationFormat
            : _options.ScopedJobResults && ScopedJobRootFormat.Matches(marker) ? ScopedJobRootFormat : _format;
        RequireVersions(marker, readable);
    }

    private static int RequireVersions(BsonDocument? marker, MongoStoreFormat expected)
    {
        if (marker is null)
            throw new StoreFormatException("The kernel store version marker is missing.");
        if (!expected.Matches(marker))
            throw new StoreFormatException($"Unsupported kernel store versions. Stored schema={Value("storeSchema")}, message protocol={Value("messageProtocol")}, journal codec={Value("journalCodec")}. This build reads {expected}.");

        if (!marker.TryGetValue("count", out var count) || !count.IsInt32)
            throw new StoreFormatException("The kernel store partition count is missing or malformed.");
        return count.AsInt32;

        string Value(string field) => marker.TryGetValue(field, out var value) && value.IsInt32 ? value.AsInt32.ToString(System.Globalization.CultureInfo.InvariantCulture) : "missing or malformed";
    }
}
