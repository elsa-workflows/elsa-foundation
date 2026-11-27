using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Update;
using Npgsql;

namespace Kernel.EntityFramework;

public sealed class PartitionEntity
{
    public int Id { get; set; }
    public string? Owner { get; set; }
    public long Epoch { get; set; }
    public long? WindowStart { get; set; }
    public long? WindowEnd { get; set; }
    public long Position { get; set; }
    public bool Died { get; set; }          // the last take found another owner's lease expired
    internal int Backlog { get; set; }
}

public sealed class NodeEntity
{
    public string Id { get; set; } = "";
    public string ReadableStoreSchema { get; set; } = "";
    public string ReadableMessageProtocol { get; set; } = "";
    public string ReadableJournalCodec { get; set; } = "";
    public long SeenAt { get; set; }        // database time of the last heartbeat
    public long LeaseUntil { get; set; }    // K1b, the node lease: database time until which no other process may take this one's partitions
}

public sealed class InstanceEntity
{
    public string Id { get; set; } = "";
    public int Partition { get; set; }
    public string Model { get; set; } = "";
    public string Definition { get; set; } = "";
    public InstanceStatus Status { get; set; }
    public string State { get; set; } = "";
    public string? Jobs { get; set; }
    public long? NextJobGeneration { get; set; }
    public string? CustomHandles { get; set; }
    public long Revision { get; set; }
    public string? Parent { get; set; }
    public string? Fault { get; set; }
    public History History { get; set; }
    public bool Suspended { get; set; }
    public string? Children { get; set; }
    public int StateVersion { get; set; }
    public bool Retired { get; set; }
}

public sealed class ProjectionEntity
{
    public int Partition { get; set; }
    public long Position { get; set; }
}

public sealed class ViewEntity
{
    public string Instance { get; set; } = "";
    public int Partition { get; set; }
    public long Revision { get; set; }
    public int Commits { get; set; }
}

public sealed class MessageEntity
{
    public long JobGeneration { get; set; }
    public long InboxScope { get; set; }
    public byte[] IssuedWire { get; set; } = [];
    public byte[]? EventMatch { get; set; }
    // Only used while preparing this commit. EF maps public properties by convention.
    internal int SendOrdinal { get; set; }
    public string Id { get; set; } = "";
    public long Seq { get; set; }
    public int Partition { get; set; }
    public string Target { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? Payload { get; set; }
    public string? Sender { get; set; }
    public string? Start { get; set; }
    public long Due { get; set; }           // 0 when the message is due at once
    public long Seen { get; set; }
    public int Attempts { get; set; }
    public long Trial { get; set; }         // the epoch at which it was last put on trial; 0 when never
    public long? KeepUntil { get; set; }
    public bool Done { get; set; }
    public string? ReplyTo { get; set; }    // a caller waiting on another node for what the turn that consumes it emits
}

public sealed class SubscriptionEntity
{
    public string Key { get; set; } = "";
    public string Instance { get; set; } = "";
    public string Wait { get; set; } = "";
    public int Partition { get; set; }
    public string? Start { get; set; }      // a subscription of kind start: the spec of the instance an event creates
}

public sealed class EventEntity
{
    public string Key { get; set; } = "";
    public string Id { get; set; } = "";
    public string? Payload { get; set; }
    public long KeepUntil { get; set; }
}

public sealed class JournalEntity
{
    public int Partition { get; set; }
    public long Position { get; set; }
    public string Instance { get; set; } = "";
    public long Revision { get; set; }
    public string Entries { get; set; } = "";
}

public class KernelDb : DbContext
{
    // One context type for every profile: a store format and the root capabilities it carries (RootProfiles). EF keys
    // its model cache by both (KernelModelCacheKeyFactory). Fresh opt-in databases seed their marker in the
    // same creation transaction as the schema. An existing database keeps its original marker.
    public KernelDb(DbContextOptions<KernelDb> options) : this(options, StoreFormat.Current, RootCapabilities.None) { }

    private KernelDb(DbContextOptions<KernelDb> options, StoreFormat format, RootCapabilities capabilities,
        bool sqliteShippedInstall = false, SqliteAdmissionDefinitions? sqliteRuntimeDefinitions = null,
        RootPublicEventHost? publicEventHost = null) : base(options)
    {
        RequiredFormat = format;
        Capabilities = capabilities;
        SqliteShippedInstall = sqliteShippedInstall;
        SqliteRuntimeDefinitions = sqliteRuntimeDefinitions;
        RootStateFamily = RootStateFamily.Of(capabilities);
        PublicEventHost = publicEventHost;
    }

    private static KernelDb Profile(DbContextOptions<KernelDb> options, StoreFormat format) =>
        new(options, format, RootProfiles.CapabilitiesOf(format));

    // A capability set under a format number no profile ships, for database-backed composition guards only: a qualification
    // set, or the shipped capability set under a guard's own number. Its format must be unshipped, so that its marker is
    // refused by every shipped profile, and the set must hold every prerequisite of its members.
    internal static KernelDb ForUnshippedComposition(DbContextOptions<KernelDb> options, StoreFormat format, RootCapabilities capabilities) =>
        RootProfiles.Formats.ContainsKey(format)
            ? throw new ArgumentOutOfRangeException(nameof(format), format, "A shipped format carries its own capabilities.")
            : RootProfiles.MissingPrerequisites(capabilities) is not RootCapabilities.None and var missing
                ? throw new ArgumentException($"The capability set lacks the prerequisites {missing}.", nameof(capabilities))
                : new(options, format, capabilities);

    internal bool SqliteShippedInstall { get; }
    internal bool UsesSqliteShippedInstall => SqliteShippedInstall && Database.IsSqlite();
    internal SqliteAdmissionDefinitions? SqliteRuntimeDefinitions { get; }
    internal bool UsesSqliteRootRuntime => SqliteRuntimeDefinitions is not null && UsesSqliteShippedInstall;
    internal StoreFormat RequiredFormat { get; }
    internal RootCapabilities Capabilities { get; }
    // The one state family this set writes and reads, with its terminal statuses.
    internal RootStateFamily RootStateFamily { get; }
    private bool Has(RootCapabilities capability) => (Capabilities & capability) != 0;
    internal bool UsesRootSelfProtocol => Has(RootCapabilities.SelfProtocol);
    internal bool UsesRootTimers => Has(RootCapabilities.Timers);
    internal bool UsesRootJobProtocol => Has(RootCapabilities.JobProtocol);
    internal bool UsesRootJobPreview => Has(RootCapabilities.JobPreview);
    internal bool UsesRootTimedJobIssuance => Has(RootCapabilities.TimedJobIssuance);
    internal bool UsesRootTimedJobPreview => Has(RootCapabilities.TimedJobPreview);
    internal bool UsesRootTimedJobSettlement => Has(RootCapabilities.TimedJobSettlement);
    internal bool SupportsCorePreview => RequiredFormat == StoreFormat.Current || UsesRootJobPreview || UsesRootTimedJobPreview;
    internal static KernelDb ForPostgresRootTimedJobPreview(DbContextOptions<KernelDb> options) => Profile(options, StoreFormat.RootTimedJobPreview);
    internal bool UsesRootParkOpening => Has(RootCapabilities.ParkOpening);
    internal bool UsesRootParkInspection => Has(RootCapabilities.ParkInspection);
    internal bool UsesRootParkArrivals => Has(RootCapabilities.ParkArrivals);
    internal bool UsesRootParkRecovery => Has(RootCapabilities.ParkRecovery);
    internal bool UsesRootChildren => Has(RootCapabilities.Children);
    internal bool UsesRootDueBacklog => Has(RootCapabilities.DueBacklog);
    internal bool UsesRootPrivateParkInputs => UsesRootSelfProtocol && UsesRootParkArrivals && UsesRootComposedParkRecovery;
    internal bool UsesRootTimerRearm => UsesRootPrivateParkInputs && Has(RootCapabilities.TimerRearm);
    internal bool UsesRootTerminalOutput => Has(RootCapabilities.TerminalOutput);
    internal bool UsesRootSequencedJournal => Has(RootCapabilities.SequencedJournal);
    internal bool UsesRootLiveEventWaits => Has(RootCapabilities.LiveEventWaits);
    internal bool UsesRootAuthoredEvents => Has(RootCapabilities.AuthoredEvents);
    internal bool UsesRootEventStarts => Has(RootCapabilities.EventStarts);
    internal bool UsesRootEventStartReceipts => Has(RootCapabilities.EventStartReceipts);
    internal bool UsesRootEventStartScan => Has(RootCapabilities.EventStartScan);
    internal bool UsesRootPublicEventStarts => Has(RootCapabilities.PublicEventStarts);
    internal bool UsesRootPublicEventHost => Has(RootCapabilities.PublicEventHost);
    internal RootPublicEventHost? PublicEventHost { get; }
    internal bool UsesRootPagedPublicEvents => Has(RootCapabilities.PagedPublicEvents);
