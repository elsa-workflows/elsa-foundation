using System.Text.Json;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.EntityFramework;
using Kernel.Foundation.Contracts;
using Kernel.Foundation.Postgres;
using Kernel.Foundation.Sqlite;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.KernelHosting;

internal abstract class KernelShellStorage
{
    public abstract bool TakesSelfSends { get; }
    public abstract bool TakesTimers { get; }
    public abstract bool TakesChildren { get; }
    public abstract bool TakesHttpWaits { get; }
    public abstract bool TakesManagedSchedules { get; }
    public abstract ValueTask<KernelScheduleProvider> OpenSchedulesAsync(BoundScope scope, CancellationToken token);
    public abstract Task InstallAsync(CancellationToken token);
    public abstract ValueTask<KernelShellFoundation> OpenAsync(CancellationToken token);
    public abstract ValueTask<long> ReadClockAsync(CancellationToken token);
    public abstract (IHostedService Service, IAsyncDisposable Lifetime) CreateOwner(IActorType actor, ILoggerFactory logs,
        Func<ActorHeader, TimeSpan?> retentionWindow, IActorType? schedule = null);

    public static KernelShellStorage Create(string provider, string? sqlitePath, string? postgresConnectionString) =>
        Create(provider, sqlitePath, postgresConnectionString, qualifySchedules: false);

    internal static KernelShellStorage CreateManagedSchedulesQualification(string provider, string? sqlitePath, string? postgresConnectionString) =>
        Create(provider, sqlitePath, postgresConnectionString, qualifySchedules: true);

    private static KernelShellStorage Create(string provider, string? sqlitePath, string? postgresConnectionString, bool qualifySchedules)
    {
        var node = $"foundation-kernel-{Environment.ProcessId}-{Guid.NewGuid():N}";
        return provider?.Trim().ToLowerInvariant() switch
        {
            "sqlite" when sqlitePath is not null && Path.IsPathFullyQualified(sqlitePath) && string.IsNullOrWhiteSpace(postgresConnectionString) =>
                new Sqlite(node, sqlitePath, qualifySchedules),
            "postgresql" when !string.IsNullOrWhiteSpace(postgresConnectionString) && string.IsNullOrWhiteSpace(sqlitePath) =>
                new Postgres(node, postgresConnectionString, qualifySchedules),
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

    private sealed class Sqlite(string node, string path, bool qualifySchedules) : KernelShellStorage
    {
        private readonly SqliteEmbeddedFoundation _foundation = qualifySchedules
            ? SqliteEmbeddedFoundation.ForManagedSchedulesQualification(path, node, WorkflowActor.ActorType)
            : new(path, node, WorkflowActor.ActorType);

        public override bool TakesSelfSends => SqliteEmbeddedHosting.TakesSelfSends;
        public override bool TakesTimers => SqliteEmbeddedHosting.TakesTimers;
        public override bool TakesChildren => SqliteEmbeddedHosting.TakesChildren;
        public override bool TakesHttpWaits => SqliteEmbeddedHosting.TakesHttpWaits;
        public override bool TakesManagedSchedules => _foundation.TakesManagedSchedules;

        public override ValueTask<KernelScheduleProvider> OpenSchedulesAsync(BoundScope scope, CancellationToken token) =>
            ValueTask.FromResult(BindSchedules(scope, _foundation.BindScheduleControls(ScheduleActor.ActorType),
                _foundation.BindScheduleOccurrences(scope, ScheduleActor.ActorType)));
        public override Task InstallAsync(CancellationToken token) => _foundation.InstallShippedAsync(token);
        public override ValueTask<long> ReadClockAsync(CancellationToken token) => _foundation.ReadClockAsync(token);

        public override async ValueTask<KernelShellFoundation> OpenAsync(CancellationToken token) =>
            new(await _foundation.OpenIngressAsync(token), _foundation.CreateReads(), _foundation, _foundation.AdmitRetentionDeleteAsync);

        public override (IHostedService, IAsyncDisposable) CreateOwner(IActorType actor, ILoggerFactory logs,
            Func<ActorHeader, TimeSpan?> retentionWindow, IActorType? schedule = null)
        {
            var logger = logs.CreateLogger<SqliteEmbeddedHostedService>();
            var owner = schedule is null
                ? _foundation.CreateHostedServiceWithRetention(actor, Environment.FailFast, retentionWindow, logger: logger)
                : _foundation.CreateHostedServiceWithAuxiliaryActor(actor, schedule, Environment.FailFast, retentionWindow, logger: logger);
            return (owner, owner);
        }
    }

    private sealed class Postgres : KernelShellStorage
    {
        private readonly DbContextOptions<KernelDb> _options;
        private readonly PostgresDirectRequests _requests;
        private readonly string _node;
        private readonly bool _qualifySchedules;

        public Postgres(string node, string connectionString, bool qualifySchedules)
        {
            _node = node;
            _qualifySchedules = qualifySchedules;
            _options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connectionString).Options;
            _requests = PostgresPreviewHosting.CreateRequests(OpenDatabase, node, WorkflowActor.ActorType);
        }

        public override bool TakesSelfSends => PostgresPreviewHosting.TakesSelfSends(OpenDatabase);
        public override bool TakesTimers => PostgresPreviewHosting.TakesTimers(OpenDatabase);
        public override bool TakesChildren => PostgresPreviewHosting.TakesChildren(OpenDatabase);
        public override bool TakesHttpWaits => PostgresPreviewHosting.TakesHttpWaits(OpenDatabase);
        public override bool TakesManagedSchedules => PostgresPreviewHosting.TakesManagedSchedules(OpenDatabase);
        public override Task InstallAsync(CancellationToken token) => _qualifySchedules
            ? PostgresPreviewHosting.InstallManagedSchedulesQualificationAsync(_options, token)
            : PostgresPreviewHosting.InstallShippedAsync(_options, token);

        public override async ValueTask<KernelScheduleProvider> OpenSchedulesAsync(BoundScope scope, CancellationToken token)
        {
            var occurrences = await PostgresPreviewHosting.OpenScheduleOccurrencesAsync(OpenDatabase, scope, WorkflowActor.ActorType,
                ScheduleActor.ActorType, _requests, token);
            return BindSchedules(scope, PostgresPreviewHosting.BindScheduleControls(OpenDatabase, ScheduleActor.ActorType), occurrences);
        }
        public override ValueTask<long> ReadClockAsync(CancellationToken token) => _requests.ReadClockAsync(token);

        public override async ValueTask<KernelShellFoundation> OpenAsync(CancellationToken token)
        {
            var foundation = await PostgresPreviewHosting.OpenFoundationAsync(OpenDatabase, WorkflowActor.ActorType, _requests,
                cancellationToken: token);
            return new(foundation, foundation, foundation, foundation.AdmitRetentionDeleteAsync);
        }

        public override (IHostedService, IAsyncDisposable) CreateOwner(IActorType actor, ILoggerFactory logs,
            Func<ActorHeader, TimeSpan?> retentionWindow, IActorType? schedule = null)
        {
            var logger = logs.CreateLogger<PostgresPreviewHostedService>();
            var owner = schedule is null
                ? new PostgresPreviewHostedService(OpenDatabase, _node, actor, Environment.FailFast, logger: logger, requests: _requests)
                    { RetentionWindow = retentionWindow }
                : PostgresPreviewHostedService.CreateWithAuxiliaryActorAndRetention(OpenDatabase, _node, actor, schedule,
                    Environment.FailFast, retentionWindow, logger: logger, requests: _requests);
            return (owner, owner);
        }

        private KernelDb OpenDatabase() => _qualifySchedules
            ? PostgresPreviewHosting.CreateManagedSchedulesQualificationContext(_options)
            : PostgresPreviewHosting.CreateShippedContext(_options);
    }
}

internal sealed record KernelShellFoundation(IIngress Ingress, IActorReads Reads, IActorRetentionPolicies RetentionPolicies,
    Func<BoundScope, ActorRef, string, CancellationToken, ValueTask<Admission>> AdmitRetentionDelete);
