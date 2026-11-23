using MongoDB.Bson;
using MongoDB.Driver;

namespace Kernel.Mongo;

internal readonly record struct MongoStoreFormat(int StoreSchema, int MessageProtocol, int JournalCodec)
{
    public bool Matches(BsonDocument? marker)
    {
        return MatchesField("storeSchema", StoreSchema) && MatchesField("messageProtocol", MessageProtocol) && MatchesField("journalCodec", JournalCodec);
        bool MatchesField(string field, int version) => marker?.GetValue(field, BsonNull.Value) is BsonInt32 value && value.Value == version;
    }

    public override string ToString() => $"schema={StoreSchema}, message protocol={MessageProtocol}, journal codec={JournalCodec}";
}

public sealed partial class MongoStore
{
    internal static MongoStoreFormat CurrentFormat => new(1, 1, 1);

    internal Task<bool> UpgradeJobGenerationFormatAsync(CancellationToken token = default) =>
        AdvanceStoreFormatAsync(CurrentFormat, JobGenerationFormat, cancellationToken: token);

    // A coordinated migration supplies its database writes through this session. A transaction retry
    // can call migrateData again. Callers must keep all of its effects inside the transaction.
    internal async Task<bool> AdvanceStoreFormatAsync(MongoStoreFormat expected, MongoStoreFormat next,
        Func<IClientSessionHandle, CancellationToken, Task>? migrateData = null, CancellationToken cancellationToken = default)
    {
        if (expected != CurrentFormat && !(_options.ScopedJobRoots && expected == JobGenerationFormat))
            throw new StoreFormatException($"This migrator reads source {CurrentFormat}. Requested source {expected}.");
        if (next == expected || next.StoreSchema < expected.StoreSchema || next.MessageProtocol < expected.MessageProtocol ||
            next.JournalCodec < expected.JournalCodec)
            throw new ArgumentException("A store format migration must advance at least one family.", nameof(next));

        using var session = await _client.StartSessionAsync(cancellationToken: cancellationToken);
        return await session.WithTransactionAsync(async (transaction, token) =>
        {
            var marker = await _meta.Find(transaction, PartitionMarker).FirstOrDefaultAsync(token);
            if (marker is not null && next.Matches(marker) && marker.GetValue("count", BsonNull.Value).IsInt32)
                return false;
            RequireVersions(marker, expected);

            var live = new BsonDocument("$expr", new BsonDocument("$gte", new BsonArray { "$leaseUntil", NowMs }));
            using var nodes = await _nodes.Find(transaction, live).Project(new BsonDocument
            {
                { "readableStoreSchema", 1 }, { "readableMessageProtocol", 1 }, { "readableJournalCodec", 1 }
            }).ToCursorAsync(token);
            while (await nodes.MoveNextAsync(token))
                foreach (var node in nodes.Current)
                    if (!Reads(node, "readableStoreSchema", next.StoreSchema) || !Reads(node, "readableMessageProtocol", next.MessageProtocol)
                        || !Reads(node, "readableJournalCodec", next.JournalCodec))
                        throw new StoreFormatException($"Live node '{node["_id"]}' cannot read the proposed kernel store format {next}.");

            // Renewal of an existing grant contends on that node. A cold registration, including
            // a name absent from our snapshot, contends on the marker. A retry checks all nodes again.
            await _nodes.UpdateManyAsync(transaction, new BsonDocument("formatGrant", new BsonDocument("$exists", true)),
                Builders<BsonDocument>.Update.Unset("formatGrant"), cancellationToken: token);
            var changed = await _meta.UpdateOneAsync(transaction, PartitionMarker, Builders<BsonDocument>.Update
                .Set("storeSchema", next.StoreSchema).Set("messageProtocol", next.MessageProtocol).Set("journalCodec", next.JournalCodec),
                cancellationToken: token);
            if (changed.ModifiedCount != 1) throw new StoreFormatException("The kernel store version marker did not advance.");
            if (migrateData is not null) await migrateData(transaction, token);
            return true;

        }, Transaction, cancellationToken);
    }

    private static bool Reads(BsonDocument node, string field, int version) =>
        node.GetValue(field, BsonNull.Value) is BsonArray versions && versions.Any(v => v is BsonInt32 n && n.Value == version);
}
