using System.Collections.Concurrent;
using Elsa.ActorFoundation.Hosting;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Foundation.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Contract = Kernel.Foundation.Contracts;
using ActorType = Kernel.Foundation.Contracts.IActorType;

namespace Kernel.Native.Run;

internal static class RootAuxiliaryOwnerChecks
{
    internal static async Task<int> RunAsync(bool postgres, bool fenceInBatch)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var token = timeout.Token;
        var directory = postgres ? null : Directory.CreateTempSubdirectory("root-auxiliary-owner-");
        try
        {
            await RunOwnerAsync(postgres, fenceInBatch, directory?.FullName, token);
            return 0;
        }
        finally { directory?.Delete(recursive: true); }
    }

    private static async Task RunOwnerAsync(bool postgres, bool fenceInBatch, string? directory, CancellationToken token)
    {
        const string node = "auxiliary-root-owner";
        const string tenant = "owner-fixture";
        var scope = EmbeddedTenant.Bind(tenant);
        var primary = new TimedActor(Kernel.WorkflowActor.WorkflowActor.ActorType);
        var auxiliary = new TimedActor(Kernel.WorkflowActor.ScheduleActor.ActorType);
        var path = directory is null ? null : Path.Combine(directory, "actors.db");
        var options = new DbContextOptionsBuilder<KernelDb>();
        var definitions = new SqliteAdmissionDefinitions();
        var sqlite = path is null ? null : SqliteEmbeddedFoundation.ForScheduleQualification(path, node, primary.Type);
        if (postgres)
        {
            options.UseNpgsql(Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
                ?? throw new InvalidOperationException("Use a dedicated empty PostgreSQL qualification database."));
            await using var db = Open();
            Require(await PostgresFixtureDatabase.IsEmptyAsync(db, token), "the PostgreSQL qualification database is empty");
            var store = new EfStore(Open, postgres: true);
            await store.CreateAsync(2);
            await store.InstallPreviewRootSchemaAsync(token);
        }
        else
        {
            options.UseSqlite(new SqliteConnectionStringBuilder { DataSource = path!, ForeignKeys = true, Pooling = false }.ConnectionString);
            await sqlite!.InstallShippedAsync(token);
        }

        var requests = postgres ? PostgresPreviewHosting.CreateRequests(Open, node, primary.Type) : null;
        var pgFoundation = postgres
            ? await PostgresPreviewHosting.OpenFoundationAsync(Open, primary.Type, requests, cancellationToken: token)
            : null;
        Contract.IIngress ingress = pgFoundation is null ? await sqlite!.OpenIngressAsync(token) : pgFoundation;
        Contract.IActorReads reads = pgFoundation is null ? sqlite!.CreateReads() : pgFoundation;
        var primaryStart = await ingress.SendAsync(scope, new Contract.Target.Start(new(primary.Type, "primary")), "ordinary-start",
            Contract.Kinds.Start, cancellationToken: token);
        Require(primaryStart is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "public ingress accepts its primary type");
        try
        {
            await ingress.SendAsync(scope, new Contract.Target.Start(new(auxiliary.Type, "external")), "invalid-start",
                Contract.Kinds.Start, cancellationToken: token);
            throw new InvalidOperationException("Public ingress should refuse an auxiliary start.");
        }
        catch (NotSupportedException) { Console.WriteLine("ok public ingress refuses an auxiliary start"); }

        var privateStart = await AdmitAsync(auxiliary.Type, "expected", "auxiliary");
        Require(privateStart is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "the private inventory admits the auxiliary creator");
        var external = await ingress.SendAsync(scope, new Contract.Target.Ref(privateStart.Actor!.Value), "invalid-input", TimedActor.TimerKind,
            "external", cancellationToken: token);
        Require(external.Outcome == Contract.AdmissionOutcome.Accepted, "the ordinary envelope reaches the owner rejection boundary");
        var expiring = await AdmitAsync(auxiliary.Type, "deadline", TimedActor.Expires);
        Require(expiring is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "the private inventory admits the deadline witness");
        var unknown = await AdmitAsync("unregistered-model", "unregistered", "unknown");
        Require(unknown is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "an incompatible private producer queues its creator");

        IHostedService owner = sqlite is not null
            ? sqlite.CreateHostedServiceWithAuxiliaryActor(primary, auxiliary, Escalate, fenceInBatch: fenceInBatch)
            : fenceInBatch
                ? PostgresPreviewHostedService.CreateWithAuxiliaryActor(Open, node, primary, auxiliary, Escalate, requests: requests)
                : new PostgresPreviewHostedService(Open, node, primary, Escalate, requests: requests)
                {
                    StartNode = (fault, stop) => PostgresPreviewHosting.StartAsync(Open, node, primary, Escalate, null,
                        stop, fault, requests, fenceInBatch: false, auxiliaryActor: auxiliary)
                };
        await using var lifetime = (IAsyncDisposable)owner;
        await owner.StartAsync(token);
        var primaryReference = primaryStart.Actor!.Value;
        var auxiliaryReference = privateStart.Actor!.Value;
        var expiryReference = expiring.Actor!.Value;
        while (true)
        {
            var primaryPoint = await reads.GetAsync(scope, primaryReference, token);
            var auxiliaryPoint = await reads.GetAsync(scope, auxiliaryReference, token);
            var expiryPoint = await reads.GetAsync(scope, expiryReference, token);
            if (((owner as SqliteEmbeddedHostedService)?.LastError ?? (owner as PostgresPreviewHostedService)?.LastError) is { } fault)
                throw new InvalidOperationException("The shared owner faulted.", fault);
            foreach (var point in new[] { primaryPoint, auxiliaryPoint, expiryPoint })
                if (point?.Header.Status == Contract.ActorStatus.Faulted)
                    throw new InvalidOperationException($"The {point.Header.Type} root faulted: {point.Header.Fault}");
            await using var db = Open();
            var inbox = await db.Messages.AsNoTracking().CountAsync(row => !row.Done, token);
            if (primaryPoint?.Header.Status == Contract.ActorStatus.Finished &&
                auxiliaryPoint?.Header.Status == Contract.ActorStatus.Finished &&
                expiryPoint?.Header.Status == Contract.ActorStatus.Finished && inbox == 0)
                break;
            await Task.Delay(25, token);
        }
        Require(primary.Completed.Distinct().SequenceEqual([primaryReference]) &&
            auxiliary.Completed.Distinct().SequenceEqual([auxiliaryReference]),
            "one owner executes and commits both timed callbacks under their exact physical actor references");
        var auxiliaryDeliveries = auxiliary.Deliveries.Where(delivery => delivery.Actor == auxiliaryReference).ToArray();
        Require(auxiliaryDeliveries.Select(delivery => delivery.Kind).Distinct()
                .SequenceEqual([Contract.Kinds.Start, TimedActor.TimerKind, Contract.Kinds.JobFinished]),
            "the refused external envelope does not join the auxiliary actor's authentic timer or job result");
        Require(auxiliary.TimedOut.Distinct().SequenceEqual([expiryReference]), "the auxiliary deadline settles when the callback cannot return");
        Require(await reads.GetAsync(scope, unknown.Actor!.Value, token) is null, "the owner refuses the unknown model without creating a checkpoint");
        await owner.StopAsync(token);
        Require((owner as SqliteEmbeddedHostedService)?.LastError is null && (owner as PostgresPreviewHostedService)?.LastError is null,
            "the shared owner drains cleanly");

        KernelDb Open() => postgres
            ? KernelDb.ForPostgresRootScheduleSettlementQualification(options.Options)
            : KernelDb.ForSqliteRootRuntime(options.Options, definitions, StoreFormat.RootScheduleSettlementQualification);

        Task<Contract.Admission> AdmitAsync(string actorType, string key, string typeData) => postgres
            ? new PostgresRootAdmission(Open, TimeSpan.FromMinutes(1), scheduleActorType: actorType)
                .AdmitScheduleAsync(tenant, new("auxiliary-fixture", key), typeData, token)
            : new SqliteRootAdmission(Open, TimeSpan.FromMinutes(1), definitions, scheduleActorType: actorType)
                .AdmitScheduleAsync(tenant, new("auxiliary-fixture", key), typeData, token);
    }

    private static void Escalate(string reason) => throw new InvalidOperationException(reason);

    private static void Require(bool result, string description)
    {
        if (!result) throw new InvalidOperationException(description);
        Console.WriteLine("ok " + description);
    }

    private sealed class TimedActor(string type) : ActorType
    {
        internal const string TimerKind = "fixture.timer";
        internal const string Expires = "expires";
        private const string JobKey = "fixture.job";
        private const string TimerPayload = "self";
        private readonly ConcurrentQueue<Contract.ActorRef> completed = new();
        private readonly ConcurrentQueue<Contract.ActorRef> timedOut = new();
        private readonly ConcurrentQueue<(Contract.ActorRef Actor, string Kind)> deliveries = new();
        internal IReadOnlyCollection<Contract.ActorRef> Completed => completed.ToArray();
        internal IReadOnlyCollection<Contract.ActorRef> TimedOut => timedOut.ToArray();
        internal IReadOnlyCollection<(Contract.ActorRef Actor, string Kind)> Deliveries => deliveries.ToArray();

        public string Type => type;

        public bool Withholds(Contract.ActorHeader header)
        {
            if (header.Type != Type) throw new InvalidOperationException("Withholding used the wrong actor type.");
            return false;
        }

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            if (turn.Header.Type != Type) throw new InvalidOperationException("A root was routed to the wrong actor type.");
            foreach (var message in turn.Inbox)
            {
                deliveries.Enqueue((turn.Header.Ref, message.Kind));
                var expires = turn.Header.TypeData == Expires;
                if (message.Kind == Contract.Kinds.Start)
                    turn.Send(turn.Header.Ref, TimerKind, TimerPayload, checked(turn.Now + 100));
                else if (message.Kind == TimerKind && message.Payload == TimerPayload && message.Sender == turn.Header.Ref)
                    turn.StartJob(JobKey, Args(turn.Header.Ref, expires), expires ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(10));
                else if (message.Kind == Contract.Kinds.JobFinished &&
                    System.Text.Json.JsonSerializer.Deserialize<Contract.JobFinished>(message.Payload!) is { Key: JobKey } result)
                {
                    if (expires && result.Error == "timeout") timedOut.Enqueue(turn.Header.Ref);
                    else if (expires || result.Error is not null || result.Result != "true")
                        throw new InvalidOperationException("The actor received an unexpected job result.");
                    turn.End(Contract.ActorStatus.Finished);
                }
                else
                    throw new InvalidOperationException("An untrusted delivery reached the actor.");
            }
            return ValueTask.CompletedTask;
        }

        public async ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken)
        {
            var expires = job.Arguments == Args(job.Actor, true);
            if (job.Key != JobKey || job.Arguments != Args(job.Actor, expires) || job.Deadline is null)
                throw new InvalidOperationException("The owner bound the job to the wrong actor, arguments or deadline.");
            if (expires) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            completed.Enqueue(job.Actor);
            return true;
        }

        private static string Args(Contract.ActorRef actor, bool expires) => expires ? "expires/" + actor.Id : actor.Id;

        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
