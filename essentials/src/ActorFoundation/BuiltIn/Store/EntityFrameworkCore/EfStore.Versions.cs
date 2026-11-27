using System.Runtime.CompilerServices;
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
    internal static StoreFormat SqliteParkArrivalsQualification => Current with { MessageProtocol = 85 };
    internal static StoreFormat PostgresRootChildrenQualification => Current with { MessageProtocol = 111 };
    internal static StoreFormat RootDueBacklogQualification => Current with { MessageProtocol = 110 };
    internal static StoreFormat RootTerminalOutputQualification => new(3, 112, 1);
    internal static StoreFormat RootS6TerminalOutputQualification => new(3, 113, 1);
    internal static StoreFormat SqliteRootChildrenQualification => new(3, 114, 1);
    internal static StoreFormat RootSequencedJournalQualification => new(3, 115, 1);
    internal static StoreFormat RootPlaybackQualification => new(3, 116, 1);
    internal static StoreFormat RootLiveEventQualification => new(3, 117, 1);
    internal static StoreFormat RootPlaybackDescendantsQualification => new(3, 118, 1);
    internal static StoreFormat RootScheduleInventoryQualification => new(3, 119, 1);
    internal static StoreFormat RootScheduleSettlementQualification => new(3, 120, 1);
    internal static StoreFormat RootScheduleControlsQualification => new(3, 121, 1);
    internal static StoreFormat RootScheduleLineageQualification => new(3, 122, 1);
    internal static StoreFormat RootManagedSchedulesQualification => new(3, 123, 1);
    internal static StoreFormat RootPreparedStartsQualification => new(3, 124, 1);
    internal static StoreFormat RootAuthoredEventsQualification => new(3, 126, 1);
    internal static StoreFormat RootEventStartsQualification => new(3, 127, 1);
    internal static StoreFormat RootEventStartReceiptsQualification => new(3, 128, 1);
    internal static StoreFormat SqliteParkRecoveryQualification => Current with { MessageProtocol = 84 };
    internal static StoreFormat RootSelfTimedQualification => Current with { MessageProtocol = 83 };
    internal static StoreFormat RootSelfTimedParkQualification => Current with { MessageProtocol = 90 };
    internal static StoreFormat RootTimerRearmQualification => Current with { MessageProtocol = 91 };

    // S7: the shipped format, composed of every capability qualified together (RootProfiles.Shipped). The formats above are
    // qualification profiles, kept for their checks until K14. A widening takes the next shared protocol number.
    // A change to an engine's definitions advances its own StoreDefinitionsRevision.
    internal static StoreFormat RootShipped => Current with { MessageProtocol = 35 };

    public override string ToString() =>
        $"schema={StoreSchema}, message protocol={MessageProtocol}, journal codec={JournalCodec}";
}

// What a store format selects in the generated-root protocol. A later protocol in one chain carries the earlier
// capabilities of that chain, so a set is always written out closed. Marker and refusal checks compare formats.
[Flags]
internal enum RootCapabilities : long
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
    DueBacklog = 1 << 21,
    TimerRearm = 1 << 22,
    Children = 1 << 23,
    TerminalOutput = 1 << 24,
    SequencedJournal = 1 << 25,
    LiveEventWaits = 1 << 26,
    Playback = 1 << 27,
    RetentionPolicy = 1 << 28,
    PlaybackDescendants = 1 << 29,
    ScheduleInventory = 1 << 30,
    ScheduleSettlement = 1L << 31,
    ScheduleControls = 1L << 32,
    ScheduleLineage = 1L << 33,
    PreparedStarts = 1L << 34,
    AuthoredEvents = 1L << 36,
    EventStarts = 1L << 37,
    EventStartReceipts = 1L << 38
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

    /// <summary>
    /// The operator, timed job and held-reply capabilities that the composed Park profiles build on.
    /// </summary>
    internal const RootCapabilities ParkCompositionBase = Retention | RootCapabilities.OperatorReplies | TimedJobSettlement |
        RootCapabilities.TimedJobPreview | RootCapabilities.ReplyProtocol | ParkInspection;

    // Park is qualified separately before the self and timed envelope union can ship.
    internal const RootCapabilities SelfTimedQualification = StableBody | TimedJobSettlement | RootCapabilities.TimedJobPreview |
        RootCapabilities.SelfProtocol | RootCapabilities.Timers;

    /// <summary>
    /// The shared shipped format includes owned children, durable self-sends and timers, one-page Park Resume,
    /// fenced due pressure, sequenced workflow history, live event waits, tenant retention policy and ranked workflow Playback.
    /// The growth rule: each widening of this set takes a new protocol number, and a database installed with an older set
    /// is refused. Before GA a preview database is recreated; after GA a widening migrates, with a rollback floor (K12).
    /// </summary>
    private const RootCapabilities Shipped29 = TimerRearmQualification | RootCapabilities.DueBacklog | RootCapabilities.TerminalOutput;
    private const RootCapabilities Shipped31 = Shipped29 | RootCapabilities.Children | RootCapabilities.SequencedJournal;
    private const RootCapabilities Shipped33 = Shipped31 | RootCapabilities.LiveEventWaits | RootCapabilities.RetentionPolicy;
    private const RootCapabilities Shipped34 = Shipped33 | RootCapabilities.Playback | RootCapabilities.PlaybackDescendants;
    internal const RootCapabilities Shipped = PreparedStartsQualification;
    // Freeze this private profile to the base it qualified. Playback has its own qualification profile.
    internal const RootCapabilities ScheduleInventoryQualification = Shipped31 | RootCapabilities.LiveEventWaits |
        RootCapabilities.RetentionPolicy | RootCapabilities.ScheduleInventory;
    internal const RootCapabilities ScheduleSettlementQualification = ScheduleInventoryQualification | RootCapabilities.ScheduleSettlement;
    internal const RootCapabilities ScheduleControlsQualification = ScheduleSettlementQualification | RootCapabilities.ScheduleControls;
    internal const RootCapabilities ScheduleLineageQualification = ScheduleSettlementQualification | RootCapabilities.ScheduleLineage;
    internal const RootCapabilities ManagedSchedulesQualification = Shipped34 | ScheduleControlsQualification | ScheduleLineageQualification;
    internal const RootCapabilities PreparedStartsQualification = ManagedSchedulesQualification | RootCapabilities.PreparedStarts;
    internal const RootCapabilities AuthoredEventsQualification = PreparedStartsQualification | RootCapabilities.AuthoredEvents;
    internal const RootCapabilities EventStartsQualification = AuthoredEventsQualification | RootCapabilities.EventStarts;
    internal const RootCapabilities EventStartReceiptsQualification = EventStartsQualification | RootCapabilities.EventStartReceipts;
    internal const RootCapabilities SequencedJournalQualification = Shipped29 | RootCapabilities.SequencedJournal;
    internal const RootCapabilities PlaybackQualification = Shipped31 | RootCapabilities.Playback;
    internal const RootCapabilities PlaybackDescendantsQualification = Shipped34;
    internal const RootCapabilities LiveEventQualification = Shipped31 | RootCapabilities.LiveEventWaits;

    internal const RootCapabilities ParkRecoveryComposition = ParkCompositionBase | RootCapabilities.ParkArrivals | RootCapabilities.ParkRecovery;
    internal const RootCapabilities SelfTimedParkQualification = ParkRecoveryComposition | RootCapabilities.SelfProtocol | RootCapabilities.Timers;
    internal const RootCapabilities TimerRearmQualification = SelfTimedParkQualification | RootCapabilities.TimerRearm;
    internal const RootCapabilities ChildQualification = TimerRearmQualification | RootCapabilities.Children;
    // Protocol 112 was qualified before the S6 timers and rearming capability.
    internal const RootCapabilities TerminalOutputQualification = ParkRecoveryComposition | RootCapabilities.TerminalOutput;
    internal const RootCapabilities S6TerminalOutputQualification = TimerRearmQualification | RootCapabilities.TerminalOutput;

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
        [StoreFormat.RootShipped] = Shipped
    };

    internal static RootCapabilities CapabilitiesOf(StoreFormat format) =>
        Formats.TryGetValue(format, out var capabilities) ? capabilities : throw new ArgumentOutOfRangeException(nameof(format), format, "No kernel store profile has this format.");

    // What each capability needs, as the shipped chains carry it. Every shipped set is closed under this map
    // (profile-capabilities checks it), and an unshipped composition must be closed too.
    internal static IReadOnlyDictionary<RootCapabilities, RootCapabilities> Prerequisites { get; } = new Dictionary<RootCapabilities, RootCapabilities>
    {
        [RootCapabilities.Timers] = RootCapabilities.SelfProtocol,
        [RootCapabilities.Termination] = RootCapabilities.OperatorProtocol,
        [RootCapabilities.StableBody] = RootCapabilities.Termination,
        [RootCapabilities.TerminalSelection] = RootCapabilities.StableBody,
        [RootCapabilities.Retirement] = RootCapabilities.TerminalSelection,
        [RootCapabilities.TerminateThenDelete] = RootCapabilities.Retirement,
        [RootCapabilities.Retention] = RootCapabilities.TerminateThenDelete,
        // A held reply needs a selected command. Admission also refuses a held Delete without retirement and a held
        // retention delete, so the shipped Retention chain beneath format 17 is not a prerequisite.
        [RootCapabilities.OperatorReplies] = RootCapabilities.TerminalSelection,
        [RootCapabilities.JobPreview] = RootCapabilities.JobProtocol,
        [RootCapabilities.TimedJobIssuance] = RootCapabilities.JobProtocol,
        [RootCapabilities.TimedJobSettlement] = RootCapabilities.JobProtocol,
        [RootCapabilities.TimedJobPreview] = RootCapabilities.TimedJobSettlement,
        [RootCapabilities.ParkInspection] = RootCapabilities.ParkOpening,
        [RootCapabilities.ParkArrivals] = RootCapabilities.ParkInspection,
        [RootCapabilities.ParkRecovery] = RootCapabilities.ParkArrivals | RootCapabilities.OperatorProtocol,
        [RootCapabilities.TimerRearm] = ParkCompositionBase | RootCapabilities.Timers | RootCapabilities.ParkRecovery,
        [RootCapabilities.TerminalOutput] = RootCapabilities.TerminalSelection | RootCapabilities.Retention,
        [RootCapabilities.SequencedJournal] = RootCapabilities.Retention,
        [RootCapabilities.LiveEventWaits] = RootCapabilities.ReplyProtocol | RootCapabilities.ParkRecovery,
        [RootCapabilities.AuthoredEvents] = RootCapabilities.LiveEventWaits | RootCapabilities.SelfProtocol,
        [RootCapabilities.EventStarts] = RootCapabilities.AuthoredEvents | RootCapabilities.PreparedStarts,
        [RootCapabilities.EventStartReceipts] = RootCapabilities.EventStarts,
        [RootCapabilities.Playback] = RootCapabilities.SequencedJournal,
        [RootCapabilities.PlaybackDescendants] = RootCapabilities.Playback,
        [RootCapabilities.RetentionPolicy] = RootCapabilities.Retention | RootCapabilities.TerminalSelection | RootCapabilities.ParkInspection,
        [RootCapabilities.ScheduleInventory] = RootCapabilities.Timers | RootCapabilities.ParkOpening,
        [RootCapabilities.ScheduleSettlement] = RootCapabilities.ScheduleInventory | RootCapabilities.JobProtocol,
        [RootCapabilities.ScheduleControls] = RootCapabilities.ScheduleInventory | RootCapabilities.TimedJobSettlement,
        [RootCapabilities.ScheduleLineage] = RootCapabilities.ScheduleSettlement | RootCapabilities.Children | RootCapabilities.Retirement
    };

    // The prerequisites of the set's members that the set lacks. None means the set is closed.
    internal static RootCapabilities MissingPrerequisites(RootCapabilities capabilities) => Prerequisites
        .Where(entry => (capabilities & entry.Key) != 0)
        .Aggregate(RootCapabilities.None, (missing, entry) => missing | (entry.Value & ~capabilities));
}

// The state family of a capability set: the prefix its qualified root writers put before each checkpoint state, which
// its reads require, and the statuses besides Finished that end a root of the family. Termination ends Cancelled
// roots and timed-job settlement ends Faulted ones. A set with at most one family capability keeps that family's
// shipped prefix bytes. A set with several writes one composed prefix that names each, so it carries all their
// terminal statuses and no shipped reader accepts it.
internal sealed record RootStateFamily(string Prefix, bool EndsCancelled, bool EndsFaulted)
{
    internal const string Public = StoredState.Family + "4:public-root:";
    internal const string Values = StoredState.Family + "5:public-root-values:";
    internal const string TimedJobs = StoredState.Family + "6:public-root-timed-jobs:";
    internal const string Replies = StoredState.Family + "7:public-root-replies:";
    internal const string Termination = StoredState.Family + "11:public-root-termination:";
    internal const string StableBody = StoredState.Family + "12:public-root-stable-body:";
    internal const string Composed = StoredState.Family + "13:public-root-composed:";

    internal static RootStateFamily Of(RootCapabilities capabilities)
    {
        bool Has(RootCapabilities capability) => (capabilities & capability) != 0;
        var stableBody = Has(RootCapabilities.StableBody);
        // The order is the shipped selection order: the first present family won when a set could carry only one.
        (RootCapabilities Capability, string Prefix, string Name)[] families =
        [
            (RootCapabilities.ValueProtocol, Values, "values"),
            (RootCapabilities.TimedJobSettlement, TimedJobs, "timed-jobs"),
            (RootCapabilities.Termination, stableBody ? StableBody : Termination, stableBody ? "stable-body" : "termination"),
            (RootCapabilities.ReplyProtocol, Replies, "replies")
        ];
        var present = families.Where(family => Has(family.Capability)).ToArray();
        var prefix = present switch
        {
            [] => Public,
            [var only] => only.Prefix,
            _ => Composed + string.Join("+", present.Select(family => family.Name)) + ":"
        };
        return new(prefix, Has(RootCapabilities.Termination), Has(RootCapabilities.TimedJobSettlement));
    }

    // Whether a non-running status ends a root of this family. A Cancelled or Faulted row also needs the family's prefix.
    internal bool Ends(InstanceStatus status) => status switch
    {
        InstanceStatus.Finished => true,
        InstanceStatus.Cancelled => EndsCancelled,
        InstanceStatus.Faulted => EndsFaulted,
        _ => false
    };
}

// EF caches one model per key. Profiles seed different markers, so the key carries the format and the capabilities.
internal sealed class KernelModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => context is KernelDb db
        ? (context.GetType(), db.RequiredFormat, db.Capabilities, db.SqliteShippedInstall, designTime)
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

    internal static void RequireSnapshotFormat(KernelDb db, int? schema, int? protocol, int? codec, string? definitionStamp)
    {
        if (schema is null || protocol is null || codec is null)
            throw new StoreFormatException("The kernel store version marker is missing.");
        var stored = new StoreFormat(schema.Value, protocol.Value, codec.Value);
        if (stored != db.RequiredFormat)
            throw new StoreFormatException($"Unsupported kernel store versions. Stored {stored}. This build reads {db.RequiredFormat}.");
        StoreDefinitionsRevision.Require(db, definitionStamp);
    }

    private static async Task RequireStoreVersionsAsync(KernelDb db, CancellationToken cancellationToken,
        FormatLock formatLock)
    {
        RefuseSqliteShippedRuntime(db);
        await RequireInstalledStoreVersionsAsync(db, formatLock, cancellationToken);
    }

    private static async Task RequireInstalledStoreVersionsAsync(KernelDb db, FormatLock formatLock, CancellationToken token)
    {
        var (stored, definitions) = await ReadVersionMarkersAsync(db, formatLock, token);
        // A locked read can wait for a concurrent change. Re-sample both rows after the lock on the same transaction.
        if (db.SeparateRootFormatLock &&
            formatLock != FormatLock.None && db.Database.IsNpgsql())
            (stored, definitions) = await ReadVersionMarkersAsync(db, FormatLock.None, token);
        RequireSnapshotFormat(db, stored.StoreSchema, stored.MessageProtocol, stored.JournalCodec,
            definitions is null ? null : StoreDefinitionsRevision.Stamp(definitions));
    }

    private static async Task<StoreFormat> ReadStoreFormatAsync(KernelDb db, FormatLock formatLock, CancellationToken cancellationToken)
    {
        var (stored, _) = await ReadVersionMarkersAsync(db, formatLock, cancellationToken);
        return stored.Format;
    }

    private static async Task<(StoreVersionEntity Format, StoreVersionEntity? Definitions)> ReadVersionMarkersAsync(
        KernelDb db, FormatLock formatLock, CancellationToken cancellationToken)
    {
        var withDefinitions = StoreDefinitionsRevision.Seed(db) is not null;
        List<StoreVersionEntity> rows;
        try
        {
            var sql = FormatReadSql(db, formatLock, withDefinitions);
            rows = sql is null
                ? await db.StoreVersions.AsNoTracking().Where(row => row.Id == StoreVersionEntity.MarkerId ||
                    withDefinitions && row.Id == StoreDefinitionsRevision.MarkerId).ToListAsync(cancellationToken)
                : await db.StoreVersions.FromSqlRaw(sql, StoreVersionEntity.MarkerId).AsNoTracking()
                    .ToListAsync(cancellationToken);
        }
        catch (Exception error) when (MissingVersionTable(error))
        {
            throw new StoreFormatException("The kernel store version marker table is missing.", error);
        }

        var stored = rows.SingleOrDefault(row => row.Id == StoreVersionEntity.MarkerId)
            ?? throw new StoreFormatException("The kernel store version marker is missing.");
        return (stored, rows.SingleOrDefault(row => row.Id == StoreDefinitionsRevision.MarkerId));
    }

    // Ownership and the marker advance take their locks in the same order. SQLite holds its writer transaction.
    private static string? FormatReadSql(KernelDb db, FormatLock formatLock, bool withDefinitions)
    {
        if (formatLock == FormatLock.None || db.Database.IsSqlite()) return null;
        var exclusive = formatLock == FormatLock.Exclusive;
        if (db.Database.IsSqlServer())
            return $"""SELECT * FROM "KernelStoreVersions" WITH ({(exclusive ? "XLOCK, " : "")}HOLDLOCK, ROWLOCK) WHERE "Id" = {"{0}"}""";
        var predicate = withDefinitions
            ? FormattableString.Invariant($$""" "Id" IN ({0}, {{StoreDefinitionsRevision.MarkerId}}) ORDER BY "Id" """)
            : """ "Id" = {0} """;
        var sql = $"""SELECT * FROM "KernelStoreVersions" WHERE {predicate} FOR {(exclusive ? "UPDATE" : "SHARE")}""";
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

// Row 1 retains the shared protocol marker. Row 2 uses the three existing fields for engine, revision and reserved zero.
internal static class StoreDefinitionsRevision
{
    internal const int MarkerId = 2;
    private const int PostgreSql = 1;
    private const int Sqlite = 2;
    private const int LegacyPostgreSqlRevision = 1;
    private const int PostgreSqlRevision = 10;
    private const int PostgreSqlChildrenRevision = 10;
    private const int SqliteRevision = 13;
    private const int SqliteChildrenRevision = 13;
    private const int PostgreSqlSequencedJournalRevision = 10;
    private const int SqliteSequencedJournalRevision = 14;
    private const int PostgreSqlLiveEventRevision = 11;
    private const int SqliteLiveEventRevision = 14;
    private const int PostgreSqlShippedRevision = 20;
    private const int SqliteShippedRevision = 23;
    private const int PostgreSqlPlaybackRevision = 12;
    private const int SqlitePlaybackRevision = 15;
    private const int PostgreSqlPlaybackDescendantsRevision = 14;
    private const int SqlitePlaybackDescendantsRevision = 17;
    private const int PostgreSqlScheduleInventoryRevision = 15;
    private const int SqliteScheduleInventoryRevision = 18;
    private const int PostgreSqlScheduleControlsRevision = 16;
    private const int SqliteScheduleControlsRevision = 19;
    private const int PostgreSqlScheduleLineageRevision = 17;
    private const int SqliteScheduleLineageRevision = 20;
    private const int PostgreSqlManagedSchedulesRevision = 18;
    private const int SqliteManagedSchedulesRevision = 21;
    private const int PostgreSqlPreparedStartsRevision = 19;
    private const int SqlitePreparedStartsRevision = 22;
    private const int SqliteParkRecoverRevision = 5;
    private const int PostgreSqlDueBacklogRevision = 5;
    private const int SqliteDueBacklogRevision = 10;
    private const int SqliteTerminalOutputRevision = 10;
    private const int PostgreSqlTerminalOutputRevision = 6;
    private const int SqliteS6TerminalOutputRevision = 11;
    private const int PostgreSqlS6TerminalOutputRevision = 7;

    internal static StoreVersionEntity? Seed(KernelDb db)
    {
        var sqliteQualification = db.UsesSqliteShippedInstall &&
            (db.RequiredFormat == StoreFormat.SqliteParkArrivalsQualification || db.RequiredFormat == StoreFormat.SqliteParkRecoveryQualification ||
                db.RequiredFormat == StoreFormat.RootSelfTimedQualification || db.RequiredFormat == StoreFormat.RootSelfTimedParkQualification ||
                db.RequiredFormat == StoreFormat.RootTimerRearmQualification);
        if (!db.SeparateRootFormatLock && !sqliteQualification) return null;
        var sqlite = db.Database.IsSqlite();
        return new StoreVersionEntity
        {
            Id = MarkerId,
            StoreSchema = sqlite ? Sqlite : PostgreSql,
            MessageProtocol = db.RequiredFormat switch
            {
                _ when sqliteQualification => db.RequiredFormat == StoreFormat.SqliteParkRecoveryQualification ? SqliteParkRecoverRevision : 1,
                _ when db.RequiredFormat == StoreFormat.RootDueBacklogQualification => sqlite ? SqliteDueBacklogRevision : PostgreSqlDueBacklogRevision,
                _ when db.RequiredFormat == StoreFormat.PostgresRootChildrenQualification => PostgreSqlChildrenRevision,
                _ when db.RequiredFormat == StoreFormat.SqliteRootChildrenQualification => SqliteChildrenRevision,
                _ when db.RequiredFormat == StoreFormat.RootSequencedJournalQualification =>
                    sqlite ? SqliteSequencedJournalRevision : PostgreSqlSequencedJournalRevision,
                _ when db.RequiredFormat == StoreFormat.RootPlaybackQualification =>
                    sqlite ? SqlitePlaybackRevision : PostgreSqlPlaybackRevision,
                _ when db.RequiredFormat == StoreFormat.RootPlaybackDescendantsQualification =>
                    sqlite ? SqlitePlaybackDescendantsRevision : PostgreSqlPlaybackDescendantsRevision,
                _ when db.RequiredFormat == StoreFormat.RootPreparedStartsQualification =>
                    sqlite ? SqlitePreparedStartsRevision : PostgreSqlPreparedStartsRevision,
                _ when db.RequiredFormat == StoreFormat.RootManagedSchedulesQualification =>
                    sqlite ? SqliteManagedSchedulesRevision : PostgreSqlManagedSchedulesRevision,
                _ when db.RequiredFormat == StoreFormat.RootScheduleLineageQualification =>
                    sqlite ? SqliteScheduleLineageRevision : PostgreSqlScheduleLineageRevision,
                _ when db.RequiredFormat == StoreFormat.RootScheduleInventoryQualification ||
                    db.RequiredFormat == StoreFormat.RootScheduleSettlementQualification =>
                    sqlite ? SqliteScheduleInventoryRevision : PostgreSqlScheduleInventoryRevision,
                _ when db.RequiredFormat == StoreFormat.RootScheduleControlsQualification =>
                    sqlite ? SqliteScheduleControlsRevision : PostgreSqlScheduleControlsRevision,
                _ when db.RequiredFormat == StoreFormat.RootTerminalOutputQualification => sqlite ? SqliteTerminalOutputRevision : PostgreSqlTerminalOutputRevision,
                _ when db.RequiredFormat == StoreFormat.RootS6TerminalOutputQualification => sqlite ? SqliteS6TerminalOutputRevision : PostgreSqlS6TerminalOutputRevision,
                _ when db.RequiredFormat == StoreFormat.RootLiveEventQualification =>
                    sqlite ? SqliteLiveEventRevision : PostgreSqlLiveEventRevision,
                // Protocol 126 adds a wire and write path, using the same physical definitions as protocol 35.
                _ when db.RequiredFormat == StoreFormat.RootAuthoredEventsQualification => sqlite ? 23 : 20,
                _ when db.RequiredFormat == StoreFormat.RootEventStartsQualification => sqlite ? 26 : 23,
                _ when db.RequiredFormat == StoreFormat.RootEventStartReceiptsQualification => sqlite ? 27 : 24,
                _ when db.RequiredFormat == StoreFormat.RootShipped =>
                    sqlite ? SqliteShippedRevision : PostgreSqlShippedRevision,
                _ => sqlite ? SqliteRevision : PostgreSqlRevision
            },
            JournalCodec = 0
        };
    }

    internal static string Stamp(StoreVersionEntity row) =>
        FormattableString.Invariant($"{row.StoreSchema}/{row.MessageProtocol}/{row.JournalCodec}");

    internal static void Require(KernelDb db, string? stored)
    {
        if (Seed(db) is not { } expected) return;
        var actual = stored ?? (db.Database.IsNpgsql() ? FormattableString.Invariant($"{PostgreSql}/{LegacyPostgreSqlRevision}/0") : null);
        if (!string.Equals(actual, Stamp(expected), StringComparison.Ordinal))
            throw new StoreFormatException($"Incompatible kernel store definitions revision. Stored {actual ?? "missing"}. " +
                $"This build reads {Stamp(expected)}. Recreate or migrate the database.");
    }

    // Query the stamp inside the same statement as the format, so a reader cannot combine two store revisions.
    private static readonly string PostgresStampSql = FormattableString.Invariant($"""
        SELECT d."StoreSchema"::text || '/' || d."MessageProtocol"::text || '/' || d."JournalCodec"::text
        FROM /*physical-relation*/"KernelStoreVersions" d WHERE d."Id" = {MarkerId}
        """);

    internal static string AddPostgresStampSql(string sql, bool lockDefinitions = false, bool only = false) =>
        sql.Replace("/*definitions-stamp*/", "(" + PostgresStampSql.Replace("/*physical-relation*/", only ? "ONLY " : "", StringComparison.Ordinal)
            + (lockDefinitions ? " FOR SHARE OF d)" : ")"), StringComparison.Ordinal);

    internal static FormattableString AddPostgresStamp(FormattableString sql) =>
        FormattableStringFactory.Create(AddPostgresStampSql(sql.Format), sql.GetArguments());
}
