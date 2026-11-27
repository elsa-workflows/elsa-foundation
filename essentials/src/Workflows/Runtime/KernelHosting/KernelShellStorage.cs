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
    private enum Profile { Shipped, ManagedSchedules, PreparedStarts }

    public abstract bool TakesSelfSends { get; }
    public abstract bool TakesTimers { get; }
    public abstract bool TakesChildren { get; }
    public abstract bool TakesHttpWaits { get; }
    public abstract bool TakesManagedSchedules { get; }
    public abstract ValueTask<KernelScheduleProvider> OpenSchedulesAsync(BoundScope scope, CancellationToken token);
    public abstract bool TakesPreparedStarts { get; }
    public abstract Task InstallAsync(CancellationToken token, ProtectRootStartAsync? protectRootStart = null);
    public abstract Task<RootStartProof> ConfirmPreparedStartAsync(BoundScope scope, ActorRef actor, string issuance,
        CancellationToken token);
    public abstract Task<int> PrunePreparedStartsAsync(BoundScope scope, CancellationToken token);
    public abstract ValueTask<KernelShellFoundation> OpenAsync(CancellationToken token);
    public abstract ValueTask<long> ReadClockAsync(CancellationToken token);
    public abstract ValueTask<FoundationChildBirthProof> ConfirmChildBirthAsync(BoundScope scope, ActorRef actor, string issuance,
        CancellationToken token);
    public abstract (IHostedService Service, IAsyncDisposable Lifetime) CreateOwner(IActorType actor, ILoggerFactory logs,
        Func<ActorHeader, TimeSpan?> retentionWindow, IActorType? schedule = null,
        ProtectChildBirthsAsync? protectChildBirths = null);

    public static KernelShellStorage Create(string provider, string? sqlitePath, string? postgresConnectionString) =>
        Create(provider, sqlitePath, postgresConnectionString, Profile.Shipped);

    internal static KernelShellStorage CreateManagedSchedulesQualification(string provider, string? sqlitePath, string? postgresConnectionString) =>
        Create(provider, sqlitePath, postgresConnectionString, Profile.ManagedSchedules);

    internal static KernelShellStorage CreatePreparedStartsQualification(string provider, string? sqlitePath, string? postgresConnectionString) =>
        Create(provider, sqlitePath, postgresConnectionString, Profile.PreparedStarts);

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
            Profile.PreparedStarts => null,
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
        public override bool TakesPreparedStarts => profile == Profile.PreparedStarts ||
            profile == Profile.Shipped && SqliteEmbeddedHosting.TakesPreparedStarts;

        public override ValueTask<KernelScheduleProvider> OpenSchedulesAsync(BoundScope scope, CancellationToken token) =>
            ValueTask.FromResult(BindSchedules(scope, Foundation.BindScheduleControls(ScheduleActor.ActorType),
                Foundation.BindScheduleOccurrences(scope, ScheduleActor.ActorType)));
        public override Task InstallAsync(CancellationToken token, ProtectRootStartAsync? protectRootStart = null)
        {
            if (TakesPreparedStarts)
            {
                ArgumentNullException.ThrowIfNull(protectRootStart);
                _foundation ??= profile == Profile.PreparedStarts
                    ? SqliteEmbeddedFoundation.ForPreparedStartsQualification(path, node, WorkflowActor.ActorType, protectRootStart)
                    : SqliteEmbeddedFoundation.WithRootStartProtection(path, node, WorkflowActor.ActorType, protectRootStart);
            }
            return Foundation.InstallShippedAsync(token);
        }

        public override Task<RootStartProof> ConfirmPreparedStartAsync(BoundScope scope, ActorRef actor, string issuance,
            CancellationToken token) => Foundation.ConfirmPreparedStartAsync(scope, actor, issuance, token);

        public override Task<int> PrunePreparedStartsAsync(BoundScope scope, CancellationToken token) =>
            Foundation.PrunePreparedStartsAsync(scope, token);
        public override ValueTask<long> ReadClockAsync(CancellationToken token) => Foundation.ReadClockAsync(token);

        public override async ValueTask<KernelShellFoundation> OpenAsync(CancellationToken token) =>
            new(await Foundation.OpenIngressAsync(token), Foundation.CreateReads(), Foundation, Foundation.AdmitRetentionDeleteAsync);

        public override ValueTask<FoundationChildBirthProof> ConfirmChildBirthAsync(BoundScope scope, ActorRef actor, string issuance,
            CancellationToken token) => Foundation.ConfirmChildBirthAsync(scope, actor, issuance, token);

        public override (IHostedService, IAsyncDisposable) CreateOwner(IActorType actor, ILoggerFactory logs,
            Func<ActorHeader, TimeSpan?> retentionWindow, IActorType? schedule = null, ProtectChildBirthsAsync? protectChildBirths = null)
        {
            var logger = logs.CreateLogger<SqliteEmbeddedHostedService>();
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
        private PostgresDirectRequests Requests => _requests ??
            throw new InvalidOperationException("Install the PostgreSQL prepared-start store before opening it.");

        public override async Task InstallAsync(CancellationToken token, ProtectRootStartAsync? protectRootStart = null)
        {
            switch (_profile)
            {
                case Profile.PreparedStarts:
                    await PostgresPreviewHosting.InstallPreparedStartsQualificationAsync(_options, token);
                    break;
                case Profile.ManagedSchedules:
                    await PostgresPreviewHosting.InstallManagedSchedulesQualificationAsync(_options, token);
                    break;
                default:
                    await PostgresPreviewHosting.InstallShippedAsync(_options, token);
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

        public override async ValueTask<KernelShellFoundation> OpenAsync(CancellationToken token)
        {
            var foundation = await PostgresPreviewHosting.OpenFoundationAsync(OpenDatabase, WorkflowActor.ActorType, Requests,
                cancellationToken: token);
            return new(foundation, foundation, foundation, foundation.AdmitRetentionDeleteAsync);
        }

        public override ValueTask<FoundationChildBirthProof> ConfirmChildBirthAsync(BoundScope scope, ActorRef actor, string issuance,
            CancellationToken token) => PostgresPreviewHostedService.ConfirmChildBirthAsync(OpenDatabase, scope, actor, issuance, token);

        public override (IHostedService, IAsyncDisposable) CreateOwner(IActorType actor, ILoggerFactory logs,
            Func<ActorHeader, TimeSpan?> retentionWindow, IActorType? schedule = null, ProtectChildBirthsAsync? protectChildBirths = null)
        {
            var logger = logs.CreateLogger<PostgresPreviewHostedService>();
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
            _ => PostgresPreviewHosting.CreateShippedContext(_options)
        };
    }
}

internal sealed record KernelShellFoundation(IIngress Ingress, IActorReads Reads, IActorRetentionPolicies RetentionPolicies,
    Func<BoundScope, ActorRef, string, CancellationToken, ValueTask<Admission>> AdmitRetentionDelete);
