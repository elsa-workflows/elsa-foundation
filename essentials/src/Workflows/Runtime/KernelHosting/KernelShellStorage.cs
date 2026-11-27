using System.Text.Json;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.EntityFramework;
using Kernel.Foundation.Contracts;
using Kernel.Foundation.Hosting;
using Kernel.Foundation.Postgres;
using Kernel.Foundation.Sqlite;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.KernelHosting;

internal abstract class KernelShellStorage
{
    private enum Profile { Shipped, ManagedSchedules, PreparedStarts, InlineStarts, EventStarts, EventStartReceipts, EventStartScan, SharedActivation, DirectTestRuns, EphemeralTestRuns, PublicEventHost, AttachedTestRuns, HeldEphemeralHttp }

    public abstract bool TakesSelfSends { get; }
    public abstract bool TakesTimers { get; }
    public abstract bool TakesChildren { get; }
    public abstract bool TakesHttpWaits { get; }
    public abstract bool TakesManagedSchedules { get; }
    public abstract ValueTask<KernelScheduleProvider> OpenSchedulesAsync(BoundScope scope, CancellationToken token);
    public abstract bool TakesPreparedStarts { get; }
    internal abstract bool TakesDirectTestRuns { get; }
    internal abstract bool TakesEphemeralTestRuns { get; }
    internal abstract bool TakesAttachedTestRuns { get; }
    internal abstract bool TakesHeldEphemeralHttp { get; }
    internal virtual bool TakesEventStarts => false;
    internal virtual bool RetainsEventStartReceipts => false;
    internal virtual bool TakesOrderedEventScan => false;
    internal virtual IEventStartAdmissions BindEventStarts(BoundScope scope) =>
        throw new InvalidOperationException("The store does not take Event starts.");
    public abstract bool TakesInlineStarts { get; }
    public abstract IProtectedRootStarts BindPublishedStarts(BoundScope scope);
    public abstract Task InstallAsync(CancellationToken token, ProtectRootStartAsync? protectRootStart = null);
    public abstract Task<RootStartProof> ConfirmPreparedStartAsync(BoundScope scope, ActorRef actor, string issuance,
        CancellationToken token);
    public abstract Task<int> PrunePreparedStartsAsync(BoundScope scope, CancellationToken token);
    public abstract ValueTask<KernelShellFoundation> OpenAsync(CancellationToken token);
    public abstract Task<IIngress> OpenOperatorsAsync(Func<BoundScope, ActorRef, string, bool> authorize, CancellationToken token);
    public abstract ValueTask<long> ReadClockAsync(CancellationToken token);
    public abstract ValueTask<FoundationChildBirthProof> ConfirmChildBirthAsync(BoundScope scope, ActorRef actor, string issuance,
        CancellationToken token);
    public abstract (IHostedService Service, IAsyncDisposable Lifetime) CreateOwner(IActorType actor, ILoggerFactory logs,
        Func<ActorHeader, TimeSpan?> retentionWindow, IActorType? schedule = null,
        ProtectChildBirthsAsync? protectChildBirths = null, InlineRootRequests? inlineStarts = null);

    public static KernelShellStorage Create(string provider, string? sqlitePath, string? postgresConnectionString) =>
        Create(provider, sqlitePath, postgresConnectionString, Profile.Shipped);

    internal static KernelShellStorage CreateManagedSchedulesQualification(string provider, string? sqlitePath, string? postgresConnectionString) =>
        Create(provider, sqlitePath, postgresConnectionString, Profile.ManagedSchedules);

    internal static KernelShellStorage CreatePreparedStartsQualification(string provider, string? sqlitePath, string? postgresConnectionString) =>
        Create(provider, sqlitePath, postgresConnectionString, Profile.PreparedStarts);

    internal static KernelShellStorage CreateEventStartsQualification(string provider, string? sqlitePath, string? postgresConnectionString) =>
        Create(provider, sqlitePath, postgresConnectionString, Profile.EventStarts);

    internal static KernelShellStorage CreateEventStartReceiptsQualification(string provider, string? sqlitePath, string? postgresConnectionString) =>
        Create(provider, sqlitePath, postgresConnectionString, Profile.EventStartReceipts);

    internal static KernelShellStorage CreateEventStartScanQualification(string provider, string? sqlitePath, string? postgresConnectionString) =>
        Create(provider, sqlitePath, postgresConnectionString, Profile.EventStartScan);

    internal static KernelShellStorage CreateInlineStartsQualification(string provider, string? sqlitePath, string? postgresConnectionString) =>
        Create(provider, sqlitePath, postgresConnectionString, Profile.InlineStarts);

    internal static KernelShellStorage CreateSharedActivationQualification(string provider, string? sqlitePath, string? postgresConnectionString) =>
        Create(provider, sqlitePath, postgresConnectionString, Profile.SharedActivation);

    internal static KernelShellStorage CreatePublicEventHostQualification(string provider, string? sqlitePath, string? postgresConnectionString) =>
        Create(provider, sqlitePath, postgresConnectionString, Profile.PublicEventHost);

    internal static KernelShellStorage CreateDirectTestRunQualification(string provider, string? sqlitePath, string? postgresConnectionString) =>
        Create(provider, sqlitePath, postgresConnectionString, Profile.DirectTestRuns);

    internal static KernelShellStorage CreateEphemeralTestRunQualification(string provider, string? sqlitePath, string? postgresConnectionString) =>
        Create(provider, sqlitePath, postgresConnectionString, Profile.EphemeralTestRuns);

    internal static KernelShellStorage CreateAttachedTestRunQualification(string provider, string? sqlitePath, string? postgresConnectionString) =>
        Create(provider, sqlitePath, postgresConnectionString, Profile.AttachedTestRuns);

    internal static KernelShellStorage CreateHeldEphemeralHttpQualification(string provider, string? sqlitePath, string? postgresConnectionString) =>
        Create(provider, sqlitePath, postgresConnectionString, Profile.HeldEphemeralHttp);

    private static KernelShellStorage Create(string provider, string? sqlitePath, string? postgresConnectionString, Profile profile)
    {
        var node = $"foundation-kernel-{Environment.ProcessId}-{Guid.NewGuid():N}";
        return provider?.Trim().ToLowerInvariant() switch
        {
            "sqlite" when sqlitePath is not null && Path.IsPathFullyQualified(sqlitePath) && string.IsNullOrWhiteSpace(postgresConnectionString) =>
                new Sqlite(node, sqlitePath, profile),
            "postgresql" when !string.IsNullOrWhiteSpace(postgresConnectionString) && string.IsNullOrWhiteSpace(sqlitePath) =>
                new Postgres(node, postgresConnectionString, profile),
            "sqlite" => throw new InvalidOperationException("Set SqlitePath to an absolute path to a dedicated database and leave PostgresConnectionString empty."),
            "postgresql" => throw new InvalidOperationException("Set PostgresConnectionString for the dedicated kernel database and leave SqlitePath empty."),
            _ => throw new InvalidOperationException("Use SQLite or PostgreSQL for WorkflowsRuntimeKernelHosting's Provider.")
        };
    }

    private static KernelScheduleProvider BindSchedules(BoundScope scope, IScheduleRootControls controls, IScheduleOccurrenceAdmissions occurrences)
    {
        return new(ApplyAsync,
            (occurrence, start, token) => occurrences.AdmitAsync(Select(occurrence), start, token),
            (occurrence, actor, token) =>
            {
                var start = KernelScheduleAuthority.ReadPinnedStart(occurrence.PinnedStart, scope.Tenant, occurrence.Executable,
                    occurrence.TriggerNode, occurrence.ScheduleKey) ?? throw new InvalidDataException("The accepted workflow start is invalid.");
                return occurrences.ConfirmRetiredAsync(Select(occurrence), start, actor, token);
            });

        async ValueTask<Admission> ApplyAsync(RuntimeKernelScheduleControl control, ScheduleHeader? header, CancellationToken token)
        {
            var result = await controls.ApplyScheduleControlAsync(scope, control.DefinitionId, control.ScheduleKey, control.Revision,
                header is null ? ScheduleControlKind.Stop : ScheduleControlKind.Set,
                header is null ? null : JsonSerializer.Serialize(header), token);
            return result.Outcome switch
            {
                ScheduleControlOutcome.Applied => new(AdmissionOutcome.Accepted, result.Actor),
                ScheduleControlOutcome.Duplicate => new(AdmissionOutcome.Duplicate, result.Actor),
                ScheduleControlOutcome.Stale => new(AdmissionOutcome.Refused, result.Actor, RefusalReason.StaleSelection),
                ScheduleControlOutcome.Conflict => new(AdmissionOutcome.Refused, result.Actor, RefusalReason.Conflict),
                _ => throw new InvalidDataException("The provider returned an unknown schedule control outcome.")
            };
        }
    }

    private static AcceptedScheduleOccurrence Select(RuntimeKernelScheduleOccurrence occurrence) =>
        new(occurrence.Executable.DefinitionId, occurrence.ScheduleKey, new(occurrence.ScheduleReference),
            occurrence.ArmGeneration, occurrence.TimerDeliveryId, occurrence.Due.ToUnixTimeMilliseconds());

    private sealed class Sqlite(string node, string path, Profile profile) : KernelShellStorage
    {
        private SqliteEmbeddedFoundation? _foundation = profile switch
        {
            Profile.ManagedSchedules => SqliteEmbeddedFoundation.ForManagedSchedulesQualification(path, node, WorkflowActor.ActorType),
            Profile.PreparedStarts or Profile.InlineStarts or Profile.EventStarts or Profile.EventStartReceipts or Profile.EventStartScan or
                Profile.SharedActivation or Profile.DirectTestRuns or Profile.EphemeralTestRuns or Profile.PublicEventHost or Profile.AttachedTestRuns or
                Profile.HeldEphemeralHttp => null,
            Profile.Shipped when SqliteEmbeddedHosting.TakesPreparedStarts => null,
            _ => new(path, node, WorkflowActor.ActorType)
        };

        private SqliteEmbeddedFoundation Foundation => _foundation ??
            throw new InvalidOperationException("Install the SQLite prepared-start store before opening it.");

        public override bool TakesSelfSends => SqliteEmbeddedHosting.TakesSelfSends;
        public override bool TakesTimers => SqliteEmbeddedHosting.TakesTimers;
        public override bool TakesChildren => SqliteEmbeddedHosting.TakesChildren;
        public override bool TakesHttpWaits => SqliteEmbeddedHosting.TakesHttpWaits;
        public override bool TakesManagedSchedules => TakesPreparedStarts || Foundation.TakesManagedSchedules;
        public override bool TakesPreparedStarts => profile is Profile.PreparedStarts or Profile.InlineStarts or Profile.DirectTestRuns || TakesEventStarts ||
            profile == Profile.Shipped && SqliteEmbeddedHosting.TakesPreparedStarts;
        internal override bool TakesDirectTestRuns => profile == Profile.DirectTestRuns || TakesEphemeralTestRuns;
        internal override bool TakesEphemeralTestRuns => profile == Profile.EphemeralTestRuns || TakesAttachedTestRuns;
        internal override bool TakesAttachedTestRuns => profile == Profile.AttachedTestRuns || TakesHeldEphemeralHttp;
        internal override bool TakesHeldEphemeralHttp => profile == Profile.HeldEphemeralHttp;
        internal override bool TakesEventStarts => profile == Profile.Shipped
            ? SqliteEmbeddedHosting.TakesEventStarts
            : profile == Profile.EventStarts || RetainsEventStartReceipts;
        internal override bool RetainsEventStartReceipts => profile == Profile.Shipped
            ? SqliteEmbeddedHosting.RetainsEventStartReceipts
            : profile == Profile.EventStartReceipts || TakesOrderedEventScan;
        internal override bool TakesOrderedEventScan => profile == Profile.Shipped
            ? SqliteEmbeddedHosting.TakesOrderedEventScan
            : profile is Profile.EventStartScan or Profile.SharedActivation or Profile.PublicEventHost || TakesEphemeralTestRuns;
        internal override IEventStartAdmissions BindEventStarts(BoundScope scope) => Foundation.BindEventStarts(scope);
        public override bool TakesInlineStarts => profile == Profile.Shipped
            ? SqliteEmbeddedHosting.TakesInlineStarts
            : profile is Profile.InlineStarts or Profile.SharedActivation or Profile.PublicEventHost || TakesEphemeralTestRuns;
        public override IProtectedRootStarts BindPublishedStarts(BoundScope scope) => Foundation.BindProtectedRootStarts(scope);

        public override ValueTask<KernelScheduleProvider> OpenSchedulesAsync(BoundScope scope, CancellationToken token) =>
            ValueTask.FromResult(BindSchedules(scope, Foundation.BindScheduleControls(ScheduleActor.ActorType),
                Foundation.BindScheduleOccurrences(scope, ScheduleActor.ActorType)));
        public override Task InstallAsync(CancellationToken token, ProtectRootStartAsync? protectRootStart = null)
        {
            if (TakesPreparedStarts)
            {
                ArgumentNullException.ThrowIfNull(protectRootStart);
                _foundation ??= profile switch
                {
                    Profile.EventStarts => SqliteEmbeddedFoundation.ForEventStartsQualification(path, node, WorkflowActor.ActorType, protectRootStart),
                    Profile.EventStartReceipts => SqliteEmbeddedFoundation.ForEventStartReceiptsQualification(path, node, WorkflowActor.ActorType, protectRootStart),
                    Profile.EventStartScan => SqliteEmbeddedFoundation.ForEventStartScanQualification(path, node, WorkflowActor.ActorType, protectRootStart),
                    Profile.SharedActivation => SqliteEmbeddedFoundation.ForSharedActivationQualification(path, node, WorkflowActor.ActorType, protectRootStart),
                    Profile.PublicEventHost => SqliteEmbeddedFoundation.ForPublicEventHostQualification(path, node, WorkflowActor.ActorType, protectRootStart),
                    Profile.InlineStarts => SqliteEmbeddedFoundation.ForInlineStartsQualification(path, node, WorkflowActor.ActorType, protectRootStart),
                    Profile.PreparedStarts => SqliteEmbeddedFoundation.ForPreparedStartsQualification(path, node, WorkflowActor.ActorType, protectRootStart),
                    Profile.DirectTestRuns => SqliteEmbeddedFoundation.ForDirectTestRunQualification(path, node, WorkflowActor.ActorType, protectRootStart),
                    Profile.EphemeralTestRuns => SqliteEmbeddedFoundation.ForEphemeralTestRunQualification(path, node, WorkflowActor.ActorType, protectRootStart),
                    Profile.AttachedTestRuns => SqliteEmbeddedFoundation.ForAttachedTestRunQualification(path, node, WorkflowActor.ActorType, protectRootStart),
                    Profile.HeldEphemeralHttp => SqliteEmbeddedFoundation.ForHeldEphemeralHttpQualification(path, node, WorkflowActor.ActorType, protectRootStart),
                    _ => SqliteEmbeddedFoundation.WithRootStartProtection(path, node, WorkflowActor.ActorType, protectRootStart)
                };
            }
            return Foundation.InstallShippedAsync(token);
        }

        public override Task<RootStartProof> ConfirmPreparedStartAsync(BoundScope scope, ActorRef actor, string issuance,
            CancellationToken token) => Foundation.ConfirmPreparedStartAsync(scope, actor, issuance, token);

        public override Task<int> PrunePreparedStartsAsync(BoundScope scope, CancellationToken token) =>
            Foundation.PrunePreparedStartsAsync(scope, token);
        public override ValueTask<long> ReadClockAsync(CancellationToken token) => Foundation.ReadClockAsync(token);

        public override Task<IIngress> OpenOperatorsAsync(Func<BoundScope, ActorRef, string, bool> authorize, CancellationToken token) =>
            Foundation.OpenIngressWithOperatorsAsync(authorize, token);

        public override async ValueTask<KernelShellFoundation> OpenAsync(CancellationToken token) =>
            new(await Foundation.OpenIngressAsync(token), Foundation.CreateReads(), Foundation, Foundation.AdmitRetentionDeleteAsync);

        public override ValueTask<FoundationChildBirthProof> ConfirmChildBirthAsync(BoundScope scope, ActorRef actor, string issuance,
            CancellationToken token) => Foundation.ConfirmChildBirthAsync(scope, actor, issuance, token);

        public override (IHostedService, IAsyncDisposable) CreateOwner(IActorType actor, ILoggerFactory logs,
            Func<ActorHeader, TimeSpan?> retentionWindow, IActorType? schedule = null, ProtectChildBirthsAsync? protectChildBirths = null,
            InlineRootRequests? inlineStarts = null)
        {
            var logger = logs.CreateLogger<SqliteEmbeddedHostedService>();
            if (inlineStarts is not null)
            {
                var inlineOwner = Foundation.CreateInlineQualification(actor, schedule, protectChildBirths!, inlineStarts,
                    Environment.FailFast, retentionWindow, logger);
                return (inlineOwner, inlineOwner);
            }
            if (protectChildBirths is not null)
            {
                var protectedOwner = Foundation.CreateHostedServiceWithChildBirthProtection(actor, protectChildBirths,
                    Environment.FailFast, retentionWindow, logger: logger, auxiliaryActor: schedule);
                return (protectedOwner, protectedOwner);
            }
            var owner = schedule is null
                ? Foundation.CreateHostedServiceWithRetention(actor, Environment.FailFast, retentionWindow, logger: logger)
                : Foundation.CreateHostedServiceWithAuxiliaryActor(actor, schedule, Environment.FailFast, retentionWindow, logger: logger);
            return (owner, owner);
        }
    }

    private sealed class Postgres : KernelShellStorage
    {
        private readonly DbContextOptions<KernelDb> _options;
        private PostgresDirectRequests? _requests;
        private readonly string _node;
        private readonly Profile _profile;

        public Postgres(string node, string connectionString, Profile profile)
        {
            _node = node;
            _profile = profile;
            _options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connectionString).Options;
            if (!TakesPreparedStarts) _requests = PostgresPreviewHosting.CreateRequests(OpenDatabase, node, WorkflowActor.ActorType);
        }

        public override bool TakesSelfSends => PostgresPreviewHosting.TakesSelfSends(OpenDatabase);
        public override bool TakesTimers => PostgresPreviewHosting.TakesTimers(OpenDatabase);
        public override bool TakesChildren => PostgresPreviewHosting.TakesChildren(OpenDatabase);
        public override bool TakesHttpWaits => PostgresPreviewHosting.TakesHttpWaits(OpenDatabase);
        public override bool TakesManagedSchedules => PostgresPreviewHosting.TakesManagedSchedules(OpenDatabase);
        public override bool TakesPreparedStarts => PostgresPreviewHosting.TakesPreparedStarts(OpenDatabase);
        internal override bool TakesDirectTestRuns => _profile == Profile.DirectTestRuns || TakesEphemeralTestRuns;
        internal override bool TakesEphemeralTestRuns => _profile == Profile.EphemeralTestRuns || TakesAttachedTestRuns;
        internal override bool TakesAttachedTestRuns => _profile == Profile.AttachedTestRuns || TakesHeldEphemeralHttp;
        internal override bool TakesHeldEphemeralHttp => _profile == Profile.HeldEphemeralHttp;
        internal override bool TakesEventStarts => PostgresPreviewHosting.TakesEventStarts(OpenDatabase);
        internal override bool RetainsEventStartReceipts => PostgresPreviewHosting.RetainsEventStartReceipts(OpenDatabase);
        internal override bool TakesOrderedEventScan => PostgresPreviewHosting.TakesOrderedEventScan(OpenDatabase);
        internal override IEventStartAdmissions BindEventStarts(BoundScope scope) => PostgresPreviewHosting.BindEventStarts(OpenDatabase, scope, Requests);
        public override bool TakesInlineStarts => PostgresPreviewHosting.TakesInlineStarts(OpenDatabase);
        public override IProtectedRootStarts BindPublishedStarts(BoundScope scope) => Requests.BindProtectedRootStarts(scope);
        private PostgresDirectRequests Requests => _requests ??
            throw new InvalidOperationException("Install the PostgreSQL prepared-start store before opening it.");

        public override async Task InstallAsync(CancellationToken token, ProtectRootStartAsync? protectRootStart = null)
        {
            switch (_profile)
            {
                case Profile.EventStarts:
                    await PostgresPreviewHosting.InstallEventStartsQualificationAsync(_options, token);
                    break;
                case Profile.EventStartReceipts:
                    await PostgresPreviewHosting.InstallEventStartReceiptsQualificationAsync(_options, token);
                    break;
                case Profile.EventStartScan:
                    await PostgresPreviewHosting.InstallEventStartScanQualificationAsync(_options, token);
                    break;
                case Profile.SharedActivation:
                    await PostgresPreviewHosting.InstallSharedActivationQualificationAsync(_options, token);
                    break;
                case Profile.PublicEventHost:
                    await PostgresPreviewHosting.InstallPublicEventHostQualificationAsync(_options, WorkflowActor.ActorType, protectsStarts: true, token);
                    break;
                case Profile.InlineStarts:
                    await PostgresPreviewHosting.InstallInlineStartsQualificationAsync(_options, token);
                    break;
                case Profile.DirectTestRuns:
                    await PostgresPreviewHosting.InstallDirectTestRunQualificationAsync(_options, token);
                    break;
                case Profile.EphemeralTestRuns:
                    await PostgresPreviewHosting.InstallEphemeralTestRunQualificationAsync(_options, token);
                    break;
                case Profile.AttachedTestRuns:
                    await PostgresPreviewHosting.InstallAttachedTestRunQualificationAsync(_options, token);
                    break;
                case Profile.HeldEphemeralHttp:
                    await PostgresPreviewHosting.InstallHeldEphemeralHttpQualificationAsync(_options, token);
                    break;
                case Profile.PreparedStarts:
                    await PostgresPreviewHosting.InstallPreparedStartsQualificationAsync(_options, token);
                    break;
                case Profile.ManagedSchedules:
                    await PostgresPreviewHosting.InstallManagedSchedulesQualificationAsync(_options, token);
                    break;
                default:
                    await PostgresPreviewHosting.InstallShippedAsync(_options, WorkflowActor.ActorType, protectsStarts: true, token);
                    break;
            }
            if (TakesPreparedStarts)
            {
                ArgumentNullException.ThrowIfNull(protectRootStart);
                _requests ??= PostgresPreviewHosting.CreateRequestsWithRootStartProtection(OpenDatabase, _node,
                    WorkflowActor.ActorType, protectRootStart);
            }
        }

        public override Task<RootStartProof> ConfirmPreparedStartAsync(BoundScope scope, ActorRef actor, string issuance,
            CancellationToken token) => PostgresPreviewHosting.ConfirmPreparedStartAsync(OpenDatabase, scope, actor, issuance, token);

        public override Task<int> PrunePreparedStartsAsync(BoundScope scope, CancellationToken token) =>
            PostgresPreviewHosting.PrunePreparedStartsAsync(OpenDatabase, scope, token);

        public override async ValueTask<KernelScheduleProvider> OpenSchedulesAsync(BoundScope scope, CancellationToken token)
        {
            var occurrences = await PostgresPreviewHosting.OpenScheduleOccurrencesAsync(OpenDatabase, scope, WorkflowActor.ActorType,
                ScheduleActor.ActorType, Requests, token);
            return BindSchedules(scope, PostgresPreviewHosting.BindScheduleControls(OpenDatabase, ScheduleActor.ActorType), occurrences);
        }
        public override ValueTask<long> ReadClockAsync(CancellationToken token) => Requests.ReadClockAsync(token);

        public override async Task<IIngress> OpenOperatorsAsync(Func<BoundScope, ActorRef, string, bool> authorize, CancellationToken token) =>
            await PostgresPreviewHosting.OpenFoundationWithOperatorsAsync(OpenDatabase, WorkflowActor.ActorType, authorize, Requests,
                cancellationToken: token);

        public override async ValueTask<KernelShellFoundation> OpenAsync(CancellationToken token)
        {
            var foundation = await PostgresPreviewHosting.OpenFoundationAsync(OpenDatabase, WorkflowActor.ActorType, Requests,
                cancellationToken: token);
            return new(foundation, foundation, foundation, foundation.AdmitRetentionDeleteAsync);
        }

        public override ValueTask<FoundationChildBirthProof> ConfirmChildBirthAsync(BoundScope scope, ActorRef actor, string issuance,
            CancellationToken token) => PostgresPreviewHostedService.ConfirmChildBirthAsync(OpenDatabase, scope, actor, issuance, token);

        public override (IHostedService, IAsyncDisposable) CreateOwner(IActorType actor, ILoggerFactory logs,
            Func<ActorHeader, TimeSpan?> retentionWindow, IActorType? schedule = null, ProtectChildBirthsAsync? protectChildBirths = null,
            InlineRootRequests? inlineStarts = null)
        {
            var logger = logs.CreateLogger<PostgresPreviewHostedService>();
            if (inlineStarts is not null)
            {
                var inlineOwner = PostgresPreviewHostedService.CreateInlineQualification(OpenDatabase, _node, actor, schedule,
                    Requests, protectChildBirths!, inlineStarts, Environment.FailFast, retentionWindow, logger);
                return (inlineOwner, inlineOwner);
            }
            if (protectChildBirths is not null)
            {
                var protectedOwner = PostgresPreviewHostedService.CreateWithChildBirthProtection(OpenDatabase, _node, actor,
                    protectChildBirths, Environment.FailFast, retentionWindow, logger: logger, requests: Requests, auxiliaryActor: schedule);
                return (protectedOwner, protectedOwner);
            }
            var owner = schedule is null
                ? new PostgresPreviewHostedService(OpenDatabase, _node, actor, Environment.FailFast, logger: logger, requests: Requests)
                    { RetentionWindow = retentionWindow }
                : PostgresPreviewHostedService.CreateWithAuxiliaryActorAndRetention(OpenDatabase, _node, actor, schedule,
                    Environment.FailFast, retentionWindow, logger: logger, requests: Requests);
            return (owner, owner);
        }

        private KernelDb OpenDatabase() => _profile switch
        {
            Profile.ManagedSchedules => PostgresPreviewHosting.CreateManagedSchedulesQualificationContext(_options),
            Profile.PreparedStarts => PostgresPreviewHosting.CreatePreparedStartsQualificationContext(_options),
            Profile.EventStarts => PostgresPreviewHosting.CreateEventStartsQualificationContext(_options),
            Profile.EventStartReceipts => PostgresPreviewHosting.CreateEventStartReceiptsQualificationContext(_options),
            Profile.EventStartScan => PostgresPreviewHosting.CreateEventStartScanQualificationContext(_options),
            Profile.SharedActivation => PostgresPreviewHosting.CreateSharedActivationQualificationContext(_options),
            Profile.PublicEventHost => PostgresPreviewHosting.CreatePublicEventHostQualificationContext(_options, WorkflowActor.ActorType, protectsStarts: true),
            Profile.InlineStarts => PostgresPreviewHosting.CreateInlineStartsQualificationContext(_options),
            Profile.DirectTestRuns => PostgresPreviewHosting.CreateDirectTestRunQualificationContext(_options),
            Profile.EphemeralTestRuns => PostgresPreviewHosting.CreateEphemeralTestRunQualificationContext(_options),
            Profile.AttachedTestRuns => PostgresPreviewHosting.CreateAttachedTestRunQualificationContext(_options),
            Profile.HeldEphemeralHttp => PostgresPreviewHosting.CreateHeldEphemeralHttpQualificationContext(_options),
            _ => PostgresPreviewHosting.CreateShippedContext(_options, WorkflowActor.ActorType, protectsStarts: true)
        };
    }
}

internal sealed record KernelShellFoundation(IIngress Ingress, IActorReads Reads, IActorRetentionPolicies RetentionPolicies,
    Func<BoundScope, ActorRef, string, CancellationToken, ValueTask<Admission>> AdmitRetentionDelete);
