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
    internal const int StoreSchemaVersion = 2;
    internal const int MessageProtocolVersion = 1;
    internal const int JournalCodecVersion = 1;

    public int Id { get; set; }
    public int StoreSchema { get; set; }
    public int MessageProtocol { get; set; }
    public int JournalCodec { get; set; }
}

// The same parameters are part of every relational node upsert. A readable family currently has one
// version, represented in invariant decimal. Additional versions use ascending comma-separated values.
internal static class NodeAdvertisement
{
    internal const string Columns = "\"ReadableStoreSchema\", \"ReadableMessageProtocol\", \"ReadableJournalCodec\"";
    internal const string Parameters = "{1}, {2}, {3}";
    internal const string Update = "\"ReadableStoreSchema\" = {1}, \"ReadableMessageProtocol\" = {2}, \"ReadableJournalCodec\" = {3}";

    private static readonly string Schema = StoreVersionEntity.StoreSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private static readonly string Protocol = StoreVersionEntity.MessageProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private static readonly string Codec = StoreVersionEntity.JournalCodecVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);

    internal static object[] Arguments(string node, long? lease = null) => lease is { } ms
        ? [node, Schema, Protocol, Codec, ms]
        : [node, Schema, Protocol, Codec];
}

public sealed partial class EfStore
{
    internal static async Task RequireStoreVersionsAsync(KernelDb db, CancellationToken cancellationToken)
    {
        StoreVersionEntity? stored;
        try
        {
            stored = await db.StoreVersions.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == StoreVersionEntity.MarkerId, cancellationToken);
        }
        catch (Exception error) when (MissingVersionTable(error))
        {
            throw new StoreFormatException("The kernel store version marker table is missing.", error);
        }

        if (stored is null)
            throw new StoreFormatException("The kernel store version marker is missing.");

        if (stored.StoreSchema != StoreVersionEntity.StoreSchemaVersion ||
            stored.MessageProtocol != StoreVersionEntity.MessageProtocolVersion ||
            stored.JournalCodec != StoreVersionEntity.JournalCodecVersion)
            throw new StoreFormatException(
                $"Unsupported kernel store versions. Stored schema={stored.StoreSchema}, message protocol={stored.MessageProtocol}, journal codec={stored.JournalCodec}. " +
                $"This build reads schema={StoreVersionEntity.StoreSchemaVersion}, message protocol={StoreVersionEntity.MessageProtocolVersion}, journal codec={StoreVersionEntity.JournalCodecVersion}.");
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
