using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using Npgsql;

namespace Kernel.EntityFramework;

internal sealed class StoreVersionEntity
{
    internal const string Table = "KernelStoreVersions";
    internal const int MarkerId = 1;
    internal const int StoreSchemaVersion = 3;
    internal const int MessageProtocolVersion = 2;
    internal const int JournalCodecVersion = 1;

    public int Id { get; set; }
    public int StoreSchema { get; set; }
    public int MessageProtocol { get; set; }
    public int JournalCodec { get; set; }

    internal StoreFormat Format => new(StoreSchema, MessageProtocol, JournalCodec);
}

internal readonly record struct StoreFormat(int StoreSchema, int MessageProtocol, int JournalCodec)
{
    internal static StoreFormat Current { get; } = new(StoreVersionEntity.StoreSchemaVersion,
        StoreVersionEntity.MessageProtocolVersion, StoreVersionEntity.JournalCodecVersion);

    // The dedicated PostgreSQL continuation profile retains its own message protocol.
    internal static StoreFormat RootSelf => Current with { MessageProtocol = 3 };

    public override string ToString() =>
        $"schema={StoreSchema}, message protocol={MessageProtocol}, journal codec={JournalCodec}";
}

// The same parameters are part of every relational node upsert. A readable family currently has one
// version, represented in invariant decimal. Additional versions use ascending comma-separated values.
internal static class NodeAdvertisement
{
    internal const string Columns = "\"ReadableStoreSchema\", \"ReadableMessageProtocol\", \"ReadableJournalCodec\"";
    internal const string Parameters = "{1}, {2}, {3}";
    internal const string Update = "\"ReadableStoreSchema\" = {1}, \"ReadableMessageProtocol\" = {2}, \"ReadableJournalCodec\" = {3}";

    private static readonly string Schema = StoreVersionEntity.StoreSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private static readonly string Codec = StoreVersionEntity.JournalCodecVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);

    internal static object[] Arguments(string node, long? lease = null, int messageProtocol = StoreVersionEntity.MessageProtocolVersion)
    {
        var protocol = messageProtocol.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return lease is { } ms ? [node, Schema, protocol, Codec, ms] : [node, Schema, protocol, Codec];
    }
}

public sealed partial class EfStore
{
    private enum FormatLock { None, Shared, Exclusive }

    internal static Task RequireStoreVersionsAsync(KernelDb db, CancellationToken cancellationToken)
        => RequireStoreVersionsAsync(db, cancellationToken, FormatLock.None);

    private static async Task RequireStoreVersionsAsync(KernelDb db, CancellationToken cancellationToken,
        FormatLock formatLock)
    {
        var stored = await ReadStoreFormatAsync(db, formatLock, cancellationToken);
        if (stored != db.RequiredFormat)
            throw new StoreFormatException($"Unsupported kernel store versions. Stored {stored}. This build reads {db.RequiredFormat}.");
    }

    private static async Task<StoreFormat> ReadStoreFormatAsync(KernelDb db, FormatLock formatLock, CancellationToken cancellationToken)
    {
        StoreVersionEntity? stored;
        try
        {
            var sql = FormatReadSql(db, formatLock);
            stored = sql is null
                ? await db.StoreVersions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == StoreVersionEntity.MarkerId, cancellationToken)
                : (await db.StoreVersions.FromSqlRaw(sql, StoreVersionEntity.MarkerId).AsNoTracking()
                    .ToListAsync(cancellationToken)).SingleOrDefault();
        }
        catch (Exception error) when (MissingVersionTable(error))
        {
            throw new StoreFormatException("The kernel store version marker table is missing.", error);
        }

        if (stored is null)
            throw new StoreFormatException("The kernel store version marker is missing.");
        return stored.Format;
    }

    // Ownership and the marker advance take their locks in the same order. SQLite holds its writer transaction.
    private static string? FormatReadSql(KernelDb db, FormatLock formatLock)
    {
        if (formatLock == FormatLock.None || db.Database.IsSqlite()) return null;
        var exclusive = formatLock == FormatLock.Exclusive;
        if (db.Database.IsSqlServer())
            return $"""SELECT * FROM "KernelStoreVersions" WITH ({(exclusive ? "XLOCK, " : "")}HOLDLOCK, ROWLOCK) WHERE "Id" = {"{0}"}""";
        var sql = $"""SELECT * FROM "KernelStoreVersions" WHERE "Id" = {"{0}"} FOR {(exclusive ? "UPDATE" : "SHARE")}""";
        return db.Database.IsNpgsql() ? sql : sql.Replace('"', '`');
    }

    private static bool MissingVersionTable(Exception error) => error switch
    {
        PostgresException { SqlState: PostgresErrorCodes.UndefinedTable } => true,
        SqlException { Number: 208 } => true,
        MySqlException { Number: 1146 } => true,
        // SQLite reports both missing tables and ordinary SQL errors as SQLITE_ERROR.
        SqliteException { SqliteErrorCode: 1 } sqlite =>
            sqlite.Message.Contains("no such table: " + StoreVersionEntity.Table, StringComparison.Ordinal),
        _ => false
    };
}
