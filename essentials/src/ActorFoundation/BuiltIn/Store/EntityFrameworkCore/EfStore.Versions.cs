using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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

    internal static StoreFormat RootTimers => Current with { MessageProtocol = 4 };
    internal static StoreFormat RootOperators => Current with { MessageProtocol = 5 };
    internal static StoreFormat RootJobs => Current with { MessageProtocol = 6 };
    // These qualification formats are separate profiles.
    internal static StoreFormat RootTimedJobIssuance => Current with { MessageProtocol = 7 };
    internal static StoreFormat RootValues => Current with { MessageProtocol = 8 };
    internal static StoreFormat RootTimedJobSettlement => Current with { MessageProtocol = 9 };

    internal static StoreFormat RootReplies => Current with { MessageProtocol = 10 };
    internal static StoreFormat RootTerminations => Current with { MessageProtocol = 11 };
    internal static StoreFormat RootStableBody => Current with { MessageProtocol = 12 };
    internal static StoreFormat RootTerminalSelection => Current with { MessageProtocol = 13 };
    internal static StoreFormat RootRetirement => Current with { MessageProtocol = 14 };
    internal static StoreFormat RootTerminateThenDelete => Current with { MessageProtocol = 15 };
    internal static StoreFormat RootRetention => Current with { MessageProtocol = 16 };
    internal static StoreFormat RootOperatorReplies => Current with { MessageProtocol = 17 };

    internal static StoreFormat RootTimedJobPreview => Current with { MessageProtocol = 18 };
    internal static StoreFormat RootParkOpening => Current with { MessageProtocol = 19 };
    internal static StoreFormat RootParkInspection => Current with { MessageProtocol = 20 };
    internal static StoreFormat RootParkArrivals => Current with { MessageProtocol = 21 };
    internal static StoreFormat RootParkRecovery => Current with { MessageProtocol = 22 };
    internal static StoreFormat RootParkDrain => Current with { MessageProtocol = 23 };

    public override string ToString() =>
        $"schema={StoreSchema}, message protocol={MessageProtocol}, journal codec={JournalCodec}";
}

// What a store format selects in the generated-root protocol. A later protocol in one chain carries the earlier
// capabilities of that chain, so a set is always written out closed. Marker and refusal checks compare formats.
[Flags]
internal enum RootCapabilities
{
    None = 0,
    SelfProtocol = 1 << 0,
    Timers = 1 << 1,
    OperatorProtocol = 1 << 2,
    Termination = 1 << 3,
    StableBody = 1 << 4,
    TerminalSelection = 1 << 5,
    Retirement = 1 << 6,
    TerminateThenDelete = 1 << 7,
    Retention = 1 << 8,
    OperatorReplies = 1 << 9,
    JobProtocol = 1 << 10,
    JobPreview = 1 << 11,
    TimedJobIssuance = 1 << 12,
    TimedJobSettlement = 1 << 13,
    TimedJobPreview = 1 << 14,
    ValueProtocol = 1 << 15,
    ReplyProtocol = 1 << 16,
    ParkOpening = 1 << 17,
    ParkInspection = 1 << 18,
    ParkArrivals = 1 << 19,
    ParkRecovery = 1 << 20,
    ParkDrain = 1 << 21
}

internal static class RootProfiles
{
    private const RootCapabilities Operators = RootCapabilities.OperatorProtocol;
    private const RootCapabilities Terminations = Operators | RootCapabilities.Termination;
    private const RootCapabilities StableBody = Terminations | RootCapabilities.StableBody;
    private const RootCapabilities TerminalSelection = StableBody | RootCapabilities.TerminalSelection;
    private const RootCapabilities Retirement = TerminalSelection | RootCapabilities.Retirement;
    private const RootCapabilities TerminateThenDelete = Retirement | RootCapabilities.TerminateThenDelete;
    private const RootCapabilities Retention = TerminateThenDelete | RootCapabilities.Retention;
    private const RootCapabilities TimedJobSettlement = RootCapabilities.JobProtocol | RootCapabilities.TimedJobSettlement;
    private const RootCapabilities ParkInspection = RootCapabilities.ParkOpening | RootCapabilities.ParkInspection;

    // Each format and the closed capability set its profile carries.
    internal static IReadOnlyDictionary<StoreFormat, RootCapabilities> Formats { get; } = new Dictionary<StoreFormat, RootCapabilities>
    {
        [StoreFormat.Current] = RootCapabilities.None,
        [StoreFormat.RootSelf] = RootCapabilities.SelfProtocol,
        [StoreFormat.RootTimers] = RootCapabilities.SelfProtocol | RootCapabilities.Timers,
        [StoreFormat.RootOperators] = Operators,
        [StoreFormat.RootJobs] = RootCapabilities.JobProtocol | RootCapabilities.JobPreview,
        [StoreFormat.RootTimedJobIssuance] = RootCapabilities.JobProtocol | RootCapabilities.TimedJobIssuance,
        [StoreFormat.RootValues] = RootCapabilities.ValueProtocol,
        [StoreFormat.RootTimedJobSettlement] = TimedJobSettlement,
        [StoreFormat.RootReplies] = RootCapabilities.ReplyProtocol,
        [StoreFormat.RootTerminations] = Terminations,
        [StoreFormat.RootStableBody] = StableBody,
        [StoreFormat.RootTerminalSelection] = TerminalSelection,
        [StoreFormat.RootRetirement] = Retirement,
        [StoreFormat.RootTerminateThenDelete] = TerminateThenDelete,
        [StoreFormat.RootRetention] = Retention,
        [StoreFormat.RootOperatorReplies] = Retention | RootCapabilities.OperatorReplies,
        [StoreFormat.RootTimedJobPreview] = TimedJobSettlement | RootCapabilities.TimedJobPreview,
        [StoreFormat.RootParkOpening] = RootCapabilities.ParkOpening,
        [StoreFormat.RootParkInspection] = ParkInspection,
        [StoreFormat.RootParkArrivals] = ParkInspection | RootCapabilities.ParkArrivals,
        // Recover composes with the Park capabilities and admits only its own operator occurrence.
        [StoreFormat.RootParkRecovery] = ParkInspection | RootCapabilities.ParkArrivals | RootCapabilities.ParkRecovery | RootCapabilities.OperatorProtocol,
        // The drain adds paging to recover: a kept set over one page opens a drain instead of refusing "page".
        [StoreFormat.RootParkDrain] = ParkInspection | RootCapabilities.ParkArrivals | RootCapabilities.ParkRecovery | RootCapabilities.OperatorProtocol |
            RootCapabilities.ParkDrain
    };

    internal static RootCapabilities CapabilitiesOf(StoreFormat format) =>
        Formats.TryGetValue(format, out var capabilities) ? capabilities : throw new ArgumentOutOfRangeException(nameof(format), format, "No kernel store profile has this format.");
}

// EF caches one model per key. Profiles seed different markers, so the key carries the format and the capabilities.
internal sealed class KernelModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => context is KernelDb db
        ? (context.GetType(), db.RequiredFormat, db.Capabilities, designTime)
        : (context.GetType(), designTime);
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

    internal static Task LockCompatibleStoreFormatAsync(KernelDb db, CancellationToken cancellationToken, bool exclusive = false)
        => RequireStoreVersionsAsync(db, cancellationToken, exclusive ? FormatLock.Exclusive : FormatLock.Shared);

    internal static void RequireSnapshotFormat(StoreFormat expected, int? schema, int? protocol, int? codec)
    {
        if (schema is null || protocol is null || codec is null)
            throw new StoreFormatException("The kernel store version marker is missing.");
        var stored = new StoreFormat(schema.Value, protocol.Value, codec.Value);
        if (stored != expected) throw new StoreFormatException($"Unsupported kernel store versions. Stored {stored}. This build reads {expected}.");
    }

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
