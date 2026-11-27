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
        qualificationFormat == StoreFormat.RootKeyedStartsQualification || qualificationFormat is { SupportsIsolatedInlineQualification: true } ||
        qualificationFormat is null && SqliteEmbeddedHosting.TakesEventStarts
            ? requests.Value :
            throw new InvalidOperationException("The selected SQLite profile does not support Event starts.");

    internal static SqliteEmbeddedFoundation ForAuthoredEventsQualification(string databasePath, string node, string actorType) =>
        new(databasePath, node, actorType, StoreFormat.RootAuthoredEventsQualification);

    internal static SqliteEmbeddedFoundation ForKeyedStartsQualification(string databasePath, string node, string actorType,
        ProtectRootStartAsync? protectRootStart = null) =>
        new(databasePath, node, actorType, StoreFormat.RootKeyedStartsQualification, protectRootStart);

    /// <summary>Composes the dedicated preview format for qualifying indexed playback.</summary>
    public static SqliteEmbeddedFoundation ForPlaybackQualification(string databasePath, string node, string actorType) =>
        new(databasePath, node, actorType, StoreFormat.RootPlaybackQualification);

    /// <summary>Composes the dedicated preview format for qualifying descendant playback.</summary>
    public static SqliteEmbeddedFoundation ForPlaybackDescendantsQualification(string databasePath, string node, string actorType) =>
        new(databasePath, node, actorType, StoreFormat.RootPlaybackDescendantsQualification);

    internal static SqliteEmbeddedFoundation ForScheduleQualification(string databasePath, string node, string actorType,
        bool trackLineage = false) => new(databasePath, node, actorType,
            trackLineage ? StoreFormat.RootScheduleLineageQualification : StoreFormat.RootScheduleSettlementQualification);

    /// <summary>Composes the dedicated preview format for qualifying trusted schedule controls.</summary>
    public static SqliteEmbeddedFoundation ForScheduleControlsQualification(string databasePath, string node, string actorType) =>
        new(databasePath, node, actorType, StoreFormat.RootScheduleControlsQualification);

    /// <summary>Composes the private profile for qualifying managed schedules with the complete shipped workflow runtime.</summary>
    public static SqliteEmbeddedFoundation ForManagedSchedulesQualification(string databasePath, string node, string actorType) =>
        new(databasePath, node, actorType, StoreFormat.RootManagedSchedulesQualification);

    /// <summary>Composes managed schedules and prepared held starts with their executable protection.</summary>
    public static SqliteEmbeddedFoundation ForPreparedStartsQualification(string databasePath, string node, string actorType,
        ProtectRootStartAsync protectRootStart)
    {
        ArgumentNullException.ThrowIfNull(protectRootStart);
        return new(databasePath, node, actorType, StoreFormat.RootPreparedStartsQualification, protectRootStart);
    }

    /// <summary>Composes the private SQLite profile for isolated root TestRuns with executable protection.</summary>
    public static SqliteEmbeddedFoundation ForDirectTestRunQualification(string databasePath, string node, string actorType,
        ProtectRootStartAsync protectRootStart)
    {
        ArgumentNullException.ThrowIfNull(protectRootStart);
        return new(databasePath, node, actorType, StoreFormat.RootDirectTestRunQualification, protectRootStart);
    }

    /// <summary>Binds the private SQLite Ephemeral Test Run format with its executable protection.</summary>
    public static SqliteEmbeddedFoundation ForEphemeralTestRunQualification(string databasePath, string node, string actorType,
        ProtectRootStartAsync protectRootStart)
    {
        ArgumentNullException.ThrowIfNull(protectRootStart);
        return new(databasePath, node, actorType, StoreFormat.RootEphemeralTestRunQualification, protectRootStart);
    }

    /// <summary>Binds the private SQLite format for qualifying attached children in isolated Test Runs.</summary>
    public static SqliteEmbeddedFoundation ForAttachedTestRunQualification(string databasePath, string node, string actorType,
        ProtectRootStartAsync protectRootStart)
    {
        ArgumentNullException.ThrowIfNull(protectRootStart);
        return new(databasePath, node, actorType, StoreFormat.RootAttachedTestRunQualification, protectRootStart);
    }

    /// <summary>Binds the private SQLite inline start qualification profile with its executable protection.</summary>
    public static SqliteEmbeddedFoundation ForInlineStartsQualification(string databasePath, string node, string actorType,
        ProtectRootStartAsync protectRootStart)
    {
        ArgumentNullException.ThrowIfNull(protectRootStart);
        return new(databasePath, node, actorType, StoreFormat.RootInlinePromotionQualification, protectRootStart);
    }

    private SqliteEmbeddedFoundation(string databasePath, string node, string actorType, StoreFormat? qualificationFormat,
        ProtectRootStartAsync? protectRootStart = null)
    {
        path = Path.GetFullPath(databasePath);
        this.node = node;
        this.actorType = actorType;
        this.qualificationFormat = qualificationFormat;
        if (qualificationFormat == StoreFormat.RootPublicEventHostQualification)
            publicEventHost = new(actorType, protectRootStart is not null);
        requests = new(() => SqliteEmbeddedHosting.CreateRequests(InstalledPath, node, actorType,
            qualificationFormat: qualificationFormat, protectRootStart: protectRootStart), LazyThreadSafetyMode.PublicationOnly);
    }

    /// <summary>Whether this profile supports managed schedule delivery and irreversible cohort retirement.</summary>
    public bool TakesManagedSchedules => qualificationFormat == StoreFormat.RootManagedSchedulesQualification ||
        qualificationFormat == StoreFormat.RootPreparedStartsQualification ||
        qualificationFormat == StoreFormat.RootDirectTestRunQualification ||
        qualificationFormat is { SupportsIsolatedInlineQualification: true } ||
        qualificationFormat == StoreFormat.RootInlinePromotionQualification ||
        qualificationFormat == StoreFormat.RootSharedActivationQualification ||
        qualificationFormat == StoreFormat.RootPublicEventStartsQualification || qualificationFormat == StoreFormat.RootPublicEventHostQualification ||
        qualificationFormat == StoreFormat.RootKeyedStartsQualification ||
        qualificationFormat is null && RootProfiles.Shipped.HasFlag(RootCapabilities.ScheduleControls | RootCapabilities.ScheduleLineage);

    /// <summary>Installs or verifies the file and binds future operations to its resolved path.</summary>
    public async Task InstallShippedAsync(CancellationToken cancellationToken = default)
    {
        await installation.WaitAsync(cancellationToken);
        try
        {
            var verified = await SqliteEmbeddedHosting.InstallShippedFileAsync(installedPath ?? path, cancellationToken, qualificationFormat, publicEventHost);
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
        ILogger<SqliteEmbeddedHostedService>? logger = null) =>
        CreateHostedServiceCore(actor, endProcess, retentionWindow, options, fenceInBatch, logger, auxiliaryActor: null);

    /// <summary>Creates one owner for the primary actor and a distinct auxiliary actor on a qualified store.</summary>
    public SqliteEmbeddedHostedService CreateHostedServiceWithAuxiliaryActor(Contract.IActorType actor, Contract.IActorType auxiliaryActor,
        Action<string> endProcess, Func<Contract.ActorHeader, TimeSpan?>? retentionWindow = null, LoopOptions? options = null,
        bool fenceInBatch = true, ILogger<SqliteEmbeddedHostedService>? logger = null)
    {
        Kernel.Durable.ActorAdapter.RequireAuxiliaryActor(actor, auxiliaryActor);
        return CreateHostedServiceCore(actor, endProcess, retentionWindow, options, fenceInBatch, logger, auxiliaryActor);
    }

    /// <summary>Creates an owner that protects every prepared child birth before committing its parent checkpoint.</summary>
    public SqliteEmbeddedHostedService CreateHostedServiceWithChildBirthProtection(Contract.IActorType actor,
        ProtectChildBirthsAsync protectChildBirths, Action<string> endProcess,
        Func<Contract.ActorHeader, TimeSpan?>? retentionWindow = null, LoopOptions? options = null,
        bool fenceInBatch = true, ILogger<SqliteEmbeddedHostedService>? logger = null, Contract.IActorType? auxiliaryActor = null)
    {
        ArgumentNullException.ThrowIfNull(protectChildBirths);
        if (auxiliaryActor is not null) Kernel.Durable.ActorAdapter.RequireAuxiliaryActor(actor, auxiliaryActor);
        return CreateHostedServiceCore(actor, endProcess, retentionWindow, options, fenceInBatch, logger, auxiliaryActor, protectChildBirths);
    }

    /// <summary>Creates the SQLite owner with inline start promotion.</summary>
    public SqliteEmbeddedHostedService CreateInlineQualification(Contract.IActorType actor, Contract.IActorType? auxiliaryActor,
        ProtectChildBirthsAsync protectChildren, InlineRootRequests inlineStarts, Action<string> endProcess,
        Func<Contract.ActorHeader, TimeSpan?> retentionWindow, ILogger<SqliteEmbeddedHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(protectChildren);
        ArgumentNullException.ThrowIfNull(inlineStarts);
        if (auxiliaryActor is not null) Kernel.Durable.ActorAdapter.RequireAuxiliaryActor(actor, auxiliaryActor);
        return CreateHostedServiceCore(actor, endProcess, retentionWindow, null, true, logger, auxiliaryActor, protectChildren, inlineStarts);
    }

    private SqliteEmbeddedHostedService CreateHostedServiceCore(Contract.IActorType actor, Action<string> endProcess,
        Func<Contract.ActorHeader, TimeSpan?>? retentionWindow, LoopOptions? options, bool fenceInBatch,
        ILogger<SqliteEmbeddedHostedService>? logger, Contract.IActorType? auxiliaryActor, ProtectChildBirthsAsync? protectChildBirths = null,
        InlineRootRequests? inlineStarts = null)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.Type != actorType) throw new ArgumentException("The actor type must match the Embedded foundation.", nameof(actor));
        return SqliteEmbeddedHostedService.ForShipped(node, actor, endProcess, options, fenceInBatch, logger,
            () => (InstalledPath, requests.Value), retentionWindow, auxiliaryActor, protectChildBirths, inlineStarts);
    }

    /// <summary>Confirms an exact protected child in this foundation's installed file.</summary>
    public ValueTask<FoundationChildBirthProof> ConfirmChildBirthAsync(Contract.BoundScope scope, Contract.ActorRef child,
        string issuance, CancellationToken cancellationToken = default) =>
        SqliteEmbeddedHosting.ConfirmChildBirthAsync(InstalledPath, qualificationFormat, scope, child, issuance, cancellationToken);

    /// <summary>Opens durable ingress and held requests routed to this foundation's owner.</summary>
    public Task<Contract.IIngress> OpenIngressAsync(CancellationToken cancellationToken = default) =>
        SqliteEmbeddedHosting.OpenFoundationWithRequestsAsync(InstalledPath, actorType, requests.Value, cancellationToken);

    /// <summary>Binds one-way protected starts to this installed SQLite file and the host's selected tenant.</summary>
    public IProtectedRootStarts BindProtectedRootStarts(Contract.BoundScope scope) => requests.Value.BindProtectedRootStarts(scope);

    /// <summary>Confirms an exact protected held start under the installed root source.</summary>
    public Task<RootStartProof> ConfirmPreparedStartAsync(Contract.BoundScope scope, Contract.ActorRef actor, string issuance,
        CancellationToken cancellationToken = default) =>
        requests.Value.ConfirmHeldStartAsync(scope, actor, issuance, cancellationToken);

    /// <summary>Removes one bounded page of expired held-root preparations for this scope.</summary>
    public Task<int> PrunePreparedStartsAsync(Contract.BoundScope scope, CancellationToken cancellationToken = default) =>
        requests.Value.PruneHeldStartsAsync(scope, cancellationToken);

    /// <summary>Opens installed ingress with the host's authorization for each exact operator target.</summary>
    public Task<Contract.IIngress> OpenIngressWithOperatorsAsync(Func<Contract.BoundScope, Contract.ActorRef, string, bool> authorize,
        CancellationToken cancellationToken) =>
        SqliteEmbeddedHosting.OpenFoundationWithOperatorsAsync(InstalledPath, actorType, authorize, requests.Value, cancellationToken);

    /// <summary>Binds the trusted control transport to the host's chosen auxiliary schedule actor.</summary>
    public Contract.IScheduleRootControls BindScheduleControls(string scheduleActorType)
    {
        RootScheduleInventory.RequireActorType(scheduleActorType);
        if (scheduleActorType == actorType)
            throw new ArgumentException("The schedule actor must be distinct from the primary actor.", nameof(scheduleActorType));
        return new ScheduleControls(this, scheduleActorType);
    }

    private sealed class ScheduleControls(SqliteEmbeddedFoundation foundation, string scheduleActorType) : Contract.IScheduleRootControls
    {
        public ValueTask<Contract.ScheduleControlResult> ApplyScheduleControlAsync(Contract.BoundScope scope,
            string definition, string scheduleKey, long revision, Contract.ScheduleControlKind kind, string? setHeader,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(scope);
            return SqliteEmbeddedHosting.ApplyScheduleControlAsync(foundation.InstalledPath, foundation.qualificationFormat,
                scope.Tenant, scheduleActorType, new(new(definition, scheduleKey), revision, kind, setHeader), cancellationToken);
        }
    }

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
