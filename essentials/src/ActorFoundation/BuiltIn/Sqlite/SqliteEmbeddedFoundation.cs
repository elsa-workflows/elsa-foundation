using Kernel.EntityFramework;
using Kernel.Runtime.Hosting;
using Microsoft.Extensions.Logging;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Sqlite;

/// <summary>Composes one shipped Embedded owner and its caller interface over a dedicated SQLite file.</summary>
/// <remarks>Install the file with <see cref="InstallShippedAsync"/> on this instance before starting the owner or using the interfaces.</remarks>
public sealed class SqliteEmbeddedFoundation : Contract.IActorRetentionPolicies
{
    private readonly string path;
    private readonly string node;
    private readonly string actorType;
    private readonly StoreFormat? qualificationFormat;
    private readonly Lazy<SqliteEmbeddedHosting.SqliteDirectRequests> requests;
    private readonly SemaphoreSlim installation = new(1, 1);
    private string? installedPath;

    /// <summary>Binds the database, owner identity and actor type for the shipped Embedded composition.</summary>
    public SqliteEmbeddedFoundation(string databasePath, string node, string actorType)
        : this(databasePath, node, actorType, qualificationFormat: null) { }

    /// <summary>Composes the dedicated preview format for qualifying retained terminal output.</summary>
    public static SqliteEmbeddedFoundation ForTerminalOutputQualification(string databasePath, string node, string actorType,
        bool withTimers = false) =>
        new(databasePath, node, actorType, withTimers ? StoreFormat.RootS6TerminalOutputQualification : StoreFormat.RootTerminalOutputQualification);

    internal static SqliteEmbeddedFoundation ForChildQualification(string databasePath, string node, string actorType) =>
        new(databasePath, node, actorType, StoreFormat.SqliteRootChildrenQualification);

    internal static SqliteEmbeddedFoundation ForLiveEventQualification(string databasePath, string node, string actorType) =>
        new(databasePath, node, actorType, StoreFormat.RootLiveEventQualification);

    /// <summary>Composes the dedicated preview format for qualifying indexed playback.</summary>
    public static SqliteEmbeddedFoundation ForPlaybackQualification(string databasePath, string node, string actorType) =>
        new(databasePath, node, actorType, StoreFormat.RootPlaybackQualification);

    /// <summary>Composes the dedicated preview format for qualifying descendant playback.</summary>
    public static SqliteEmbeddedFoundation ForPlaybackDescendantsQualification(string databasePath, string node, string actorType) =>
        new(databasePath, node, actorType, StoreFormat.RootPlaybackDescendantsQualification);

    private SqliteEmbeddedFoundation(string databasePath, string node, string actorType, StoreFormat? qualificationFormat)
    {
        path = Path.GetFullPath(databasePath);
        this.node = node;
        this.actorType = actorType;
        this.qualificationFormat = qualificationFormat;
        requests = new(() => SqliteEmbeddedHosting.CreateRequests(InstalledPath, node, actorType,
            qualificationFormat: qualificationFormat), LazyThreadSafetyMode.PublicationOnly);
    }

    /// <summary>Installs or verifies the file and binds future operations to its resolved path.</summary>
    public async Task InstallShippedAsync(CancellationToken cancellationToken = default)
    {
        await installation.WaitAsync(cancellationToken);
        try
        {
            var verified = await SqliteEmbeddedHosting.InstallShippedFileAsync(installedPath ?? path, cancellationToken, qualificationFormat);
            Volatile.Write(ref installedPath, verified);
        }
        finally { installation.Release(); }
    }

    private string InstalledPath => Volatile.Read(ref installedPath) ??
        throw new InvalidOperationException("Install the shipped SQLite foundation before using it.");

    /// <summary>Creates the host service whose reply edge also serves <see cref="OpenIngressAsync"/>.</summary>
    public SqliteEmbeddedHostedService CreateHostedService(Contract.IActorType actor, Action<string> endProcess,
        LoopOptions? options = null, bool fenceInBatch = true, ILogger<SqliteEmbeddedHostedService>? logger = null) =>
        CreateHostedServiceWithRetention(actor, endProcess, null, options, fenceInBatch, logger);

    /// <summary>Creates a hosted owner that rechecks retention eligibility under the supplied host policy.</summary>
    public SqliteEmbeddedHostedService CreateHostedServiceWithRetention(Contract.IActorType actor, Action<string> endProcess,
        Func<Contract.ActorHeader, TimeSpan?>? retentionWindow, LoopOptions? options = null, bool fenceInBatch = true,
        ILogger<SqliteEmbeddedHostedService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.Type != actorType) throw new ArgumentException("The actor type must match the Embedded foundation.", nameof(actor));
        return SqliteEmbeddedHostedService.ForShipped(node, actor, endProcess, options, fenceInBatch, logger,
            () => (InstalledPath, requests.Value), retentionWindow);
    }

    /// <summary>Opens durable ingress and held requests routed to this foundation's owner.</summary>
    public Task<Contract.IIngress> OpenIngressAsync(CancellationToken cancellationToken = default) =>
        SqliteEmbeddedHosting.OpenFoundationWithRequestsAsync(InstalledPath, actorType, requests.Value, cancellationToken);

    internal Task<Contract.IIngress> OpenIngressWithOperatorsAsync(Func<Contract.BoundScope, Contract.ActorRef, string, bool> authorize,
        CancellationToken cancellationToken) =>
        SqliteEmbeddedHosting.OpenFoundationWithOperatorsAsync(InstalledPath, actorType, authorize, requests.Value, cancellationToken);

    /// <summary>Submits a projected terminal selection from the trusted host retention worker.</summary>
    public ValueTask<Contract.Admission> AdmitRetentionDeleteAsync(Contract.BoundScope scope, Contract.ActorRef actor,
        string selection, CancellationToken cancellationToken = default) =>
        SqliteEmbeddedHosting.AdmitRetentionDeleteAsync(InstalledPath, scope, actor, selection, requests.Value.DeduplicationWindow,
            cancellationToken, qualificationFormat);

    /// <summary>Creates a paged reader of committed roots, inboxes and journals.</summary>
    ValueTask<Contract.ActorRetentionPolicy> Contract.IActorRetentionPolicies.ReadRetentionPolicyAsync(
        Contract.BoundScope scope, CancellationToken cancellationToken) =>
        SqliteEmbeddedHosting.RunRetentionPolicyAsync(InstalledPath, qualificationFormat,
            store => store.ReadRootRetentionPolicyAsync(scope, actorType, cancellationToken), cancellationToken);

    ValueTask<Contract.ActorRetentionPolicyChange> Contract.IActorRetentionPolicies.TryApplyRetentionPolicyAsync(
        Contract.BoundScope scope, long expectedRevision, Contract.ActorRetentionWindows proposed, long automaticNewlyEligibleLimit,
        string? confirmation, CancellationToken cancellationToken) =>
        SqliteEmbeddedHosting.RunRetentionPolicyAsync(InstalledPath, qualificationFormat,
            store => store.TryApplyRootRetentionPolicyAsync(scope, actorType, expectedRevision, proposed, automaticNewlyEligibleLimit, confirmation, cancellationToken),
            cancellationToken);

    public Contract.IActorReads CreateReads(int pageSize = 100) => SqliteEmbeddedHosting.CreateFoundationReads(InstalledPath, pageSize, qualificationFormat);

    /// <summary>Reads the store clock used to set held-request deadlines.</summary>
    public ValueTask<long> ReadClockAsync(CancellationToken cancellationToken = default) => requests.Value.ReadClockAsync(cancellationToken);
}
