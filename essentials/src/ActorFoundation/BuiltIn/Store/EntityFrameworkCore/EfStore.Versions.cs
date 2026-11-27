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
    internal static StoreFormat RootInlinePromotionQualification => new(3, 125, 1);
    internal static StoreFormat RootAuthoredEventsQualification => new(3, 126, 1);
    internal static StoreFormat RootEventStartsQualification => new(3, 127, 1);
    internal static StoreFormat RootEventStartReceiptsQualification => new(3, 128, 1);
    internal static StoreFormat RootEventStartScanQualification => new(3, 129, 1);
    internal static StoreFormat RootSharedActivationQualification => new(3, 130, 1);
    internal static StoreFormat RootDirectTestRunQualification => new(3, 131, 1);
    internal static StoreFormat RootPublicEventStartsQualification => new(3, 132, 1);
    internal static StoreFormat RootKeyedStartsQualification => new(3, 133, 1);
    internal static StoreFormat RootPagedPublicEventsQualification => new(3, 137, 1);
    internal static StoreFormat RootEphemeralTestRunQualification => new(3, 134, 1);
    internal static StoreFormat RootPublicEventHostQualification => new(3, 135, 1);
    internal static StoreFormat SqliteParkRecoveryQualification => Current with { MessageProtocol = 84 };
    internal static StoreFormat RootSelfTimedQualification => Current with { MessageProtocol = 83 };
    internal static StoreFormat RootSelfTimedParkQualification => Current with { MessageProtocol = 90 };
    internal static StoreFormat RootTimerRearmQualification => Current with { MessageProtocol = 91 };

    // S7: the shipped format, composed of every capability qualified together (RootProfiles.Shipped). The formats above are
    // qualification profiles, kept for their checks until K14. A widening takes the next shared protocol number.
    // A change to an engine's definitions advances its own StoreDefinitionsRevision.
    internal static StoreFormat RootShipped => Current with { MessageProtocol = 36 };

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
    InlinePromotion = 1L << 35,
    AuthoredEvents = 1L << 36,
    EventStarts = 1L << 37,
    EventStartReceipts = 1L << 38,
    EventStartScan = 1L << 39,
    PublicEventStarts = 1L << 40,
    DirectTestRuns = 1L << 41,
    KeyedStarts = 1L << 43,
    PublicEventHost = 1L << 44,
    PagedPublicEvents = 1L << 45
