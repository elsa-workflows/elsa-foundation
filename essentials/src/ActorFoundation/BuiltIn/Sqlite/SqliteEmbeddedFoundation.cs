using Kernel.Foundation.Hosting;
using Kernel.EntityFramework;
using Kernel.Runtime.Hosting;
using Microsoft.Extensions.Logging;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Sqlite;

/// <summary>Composes one shipped Embedded owner and its caller interface over a dedicated SQLite file.</summary>
/// <remarks>Install the file with <see cref="InstallShippedAsync"/> on this instance before starting the owner or using the interfaces.</remarks>
public sealed partial class SqliteEmbeddedFoundation : Contract.IActorRetentionPolicies
{
    private readonly string path;
    private readonly string node;
    private readonly string actorType;
    private readonly StoreFormat? qualificationFormat;
    private readonly RootPublicEventHost? publicEventHost;
    private readonly Lazy<SqliteEmbeddedHosting.SqliteDirectRequests> requests;
    private readonly SemaphoreSlim installation = new(1, 1);
    private string? installedPath;

    /// <summary>Binds the database, owner identity and actor type for the shipped Embedded composition.</summary>
    public SqliteEmbeddedFoundation(string databasePath, string node, string actorType)
        : this(databasePath, node, actorType, qualificationFormat: null) { }

    /// <summary>Binds the shipped Embedded composition with trusted protection for held root starts.</summary>
    public static SqliteEmbeddedFoundation WithRootStartProtection(string databasePath, string node, string actorType,
        ProtectRootStartAsync protectRootStart)
    {
        ArgumentNullException.ThrowIfNull(protectRootStart);
        return new(databasePath, node, actorType, qualificationFormat: null, protectRootStart);
    }

    /// <summary>Composes the dedicated preview format for qualifying retained terminal output.</summary>
    public static SqliteEmbeddedFoundation ForTerminalOutputQualification(string databasePath, string node, string actorType,
        bool withTimers = false) =>
        new(databasePath, node, actorType, withTimers ? StoreFormat.RootS6TerminalOutputQualification : StoreFormat.RootTerminalOutputQualification);

    internal static SqliteEmbeddedFoundation ForChildQualification(string databasePath, string node, string actorType) =>
        new(databasePath, node, actorType, StoreFormat.SqliteRootChildrenQualification);

    internal static SqliteEmbeddedFoundation ForLiveEventQualification(string databasePath, string node, string actorType) =>
        new(databasePath, node, actorType, StoreFormat.RootLiveEventQualification);

    internal static SqliteEmbeddedFoundation ForEventStartsQualification(string databasePath, string node, string actorType) =>
        new(databasePath, node, actorType, StoreFormat.RootEventStartsQualification);

    internal SqliteEmbeddedHosting.SqliteDirectRequests EventStarts =>
        qualificationFormat == StoreFormat.RootEventStartsQualification || qualificationFormat == StoreFormat.RootEventStartReceiptsQualification ||
        qualificationFormat == StoreFormat.RootEventStartScanQualification || qualificationFormat == StoreFormat.RootSharedActivationQualification ||
        qualificationFormat == StoreFormat.RootPublicEventStartsQualification || qualificationFormat == StoreFormat.RootPublicEventHostQualification ||
        qualificationFormat == StoreFormat.RootPagedPublicEventsQualification ||
