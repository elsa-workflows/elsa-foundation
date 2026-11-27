using System.Collections.Concurrent;
using System.Text.Json;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Foundation.Sqlite;
using Kernel.WorkflowActor;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Contract = Kernel.Foundation.Contracts;
using Control = Kernel.Foundation.Contracts.ScheduleControlKind;
using Outcome = Kernel.Foundation.Contracts.ScheduleControlOutcome;

namespace Kernel.Native.Run;

internal static class RootScheduleControlChecks
{
    internal static async Task<int> RunAsync(bool postgres, bool inBatch, bool managedProfile = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var directory = postgres ? null : Directory.CreateTempSubdirectory("root-schedule-controls-");
        try
        {
            await RunCoreAsync(postgres, inBatch, managedProfile, directory?.FullName, timeout.Token);
            return 0;
        }
        finally { directory?.Delete(recursive: true); }
    }

    private static async Task RunCoreAsync(bool postgres, bool inBatch, bool managedProfile, string? directory, CancellationToken token)
    {
        const string node = "schedule-controls-owner";
        const string definition = "workflow-with-schedule";
        const string key = "schedule:workflow-with-schedule:timer";
        const string raceKey = "schedule:workflow-with-schedule:race";
        var scope = EmbeddedTenant.Bind("schedule-fixture");
        var path = directory is null ? null : Path.Combine(directory, "actors.db");
        var options = new DbContextOptionsBuilder<KernelDb>();
        var sqlite = path is null ? null : managedProfile
            ? SqliteEmbeddedFoundation.ForManagedSchedulesQualification(path, node, WorkflowActor.WorkflowActor.ActorType)
            : SqliteEmbeddedFoundation.ForScheduleControlsQualification(path, node, WorkflowActor.WorkflowActor.ActorType);
        if (postgres)
        {
            options.UseNpgsql(Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
                ?? throw new InvalidOperationException("Use a dedicated empty PostgreSQL qualification database."));
            await using var db = Open();
            Require(await PostgresFixtureDatabase.IsEmptyAsync(db, token), "the PostgreSQL qualification database is empty");
            if (managedProfile)
                await PostgresPreviewHosting.InstallManagedSchedulesQualificationAsync(options.Options, token);
            else
                await PostgresPreviewHosting.InstallScheduleControlsQualificationAsync(options.Options, token);
        }
        else
        {
            options.UseSqlite(new SqliteConnectionStringBuilder { DataSource = path!, ForeignKeys = true, Pooling = false }.ConnectionString);
            await sqlite!.InstallShippedAsync(token);
        }

        if (managedProfile)
        {
            await using var db = Open();
            Require(db.RequiredFormat == StoreFormat.RootManagedSchedulesQualification && db.UsesRootPlaybackDescendants &&
                db.UsesRootScheduleControls && db.UsesRootScheduleLineage, "managed schedules compose all shipped workflow capabilities");
        }

        Contract.IActorType primary = managedProfile ? new HeldEventActor() : new WorkflowActor.WorkflowActor([]);
        var acceptor = new RecordingAcceptor();
        var schedule = new ScheduleActor(null, acceptor);
        var writes = sqlite?.BindScheduleControls(schedule.Type) ?? PostgresPreviewHosting.BindScheduleControls(Open, schedule.Type);
        var requests = postgres ? PostgresPreviewHosting.CreateRequests(Open, node, primary.Type) : null;
        var pg = postgres ? await PostgresPreviewHosting.OpenFoundationAsync(Open, primary.Type, requests, cancellationToken: token) : null;
        Contract.IIngress ingress = pg is null ? await sqlite!.OpenIngressAsync(token) : pg;
        Contract.IActorReads reads = pg is null ? sqlite!.CreateReads() : pg;
        if (managedProfile)
            Require(reads is Contract.IActorSequencedJournalReads { SupportsSequencedJournal: true } &&
                reads is Contract.IActorPlaybackReads { SupportsPlayback: true, SupportsDescendantPlayback: true },
                "the managed profile exposes sequenced journal and descendant playback through public reads");

        Require(await Apply(3, Control.Stop) is { Outcome: Outcome.Applied, Actor: null }, "Stop before first Set persists without a root");
        Require(await Apply(3, Control.Stop) is { Outcome: Outcome.Duplicate, Actor: null }, "identical Stop retries the unbound receipt");
        Require(await Apply(2, Control.Set, Header(2, "too-old")) is { Outcome: Outcome.Stale, Actor: null },
            "a delayed Set cannot resurrect an earlier stopped schedule");
        Require(await Apply(3, Control.Set, Header(3, "same-revision")) is { Outcome: Outcome.Conflict, Actor: null },
            "a different operation cannot reuse the current revision");
        await using (var db = Open())
            Require(!await db.Messages.AnyAsync(token), "Stop before the first Set and its stale replays enqueue nothing");

        var firstHeader = Header(4, "first");
        var first = await Apply(4, Control.Set, firstHeader);
        Require(first is { Outcome: Outcome.Applied, Actor: not null }, "a newer Set atomically creates the schedule and control watermark");
        var reference = first.Actor!.Value;
        Require(await Apply(4, Control.Set, firstHeader) == new Contract.ScheduleControlResult(Outcome.Duplicate, reference),
            "identical Set bytes replay the exact root");
        Require(await Apply(4, Control.Set, firstHeader + " ") == new Contract.ScheduleControlResult(Outcome.Conflict, reference),
            "different bytes at the same revision are a conflict even with the same parsed header");
        try
        {
            await Apply(5, Control.Set, Header(6, "wrong-revision"));
            throw new InvalidOperationException("The writer accepted a header at an unrelated revision.");
        }
        catch (ArgumentException) { Console.WriteLine("ok mismatched header revision is rejected before writing"); }
        var contenders = await Task.WhenAll(
            Apply(1, Control.Set, Header(1, "race-a", "PT1H"), raceKey).AsTask(),
            Apply(1, Control.Set, Header(1, "race-b", "PT1H"), raceKey).AsTask());
        Require(contenders.Count(result => result.Outcome == Outcome.Applied) == 1 &&
            contenders.Count(result => result.Outcome == Outcome.Conflict) == 1 && contenders[0].Actor == contenders[1].Actor,
            "simultaneous different Set bytes share one watermark and one creator");

        IHostedService owner = sqlite is not null
            ? sqlite.CreateHostedServiceWithAuxiliaryActor(primary, schedule, Escalate, fenceInBatch: inBatch)
            : inBatch
                ? PostgresPreviewHostedService.CreateWithAuxiliaryActor(Open, node, primary, schedule, Escalate, requests: requests)
                : new PostgresPreviewHostedService(Open, node, primary, Escalate, requests: requests)
                {
                    StartNode = (fault, stop) => PostgresPreviewHosting.StartAsync(Open, node, primary, Escalate, null,
                        stop, fault, requests, fenceInBatch: false, auxiliaryActor: schedule)
                };
        await using var lifetime = (IAsyncDisposable)owner;
        await owner.StartAsync(token);
        if (managedProfile)
        {
            var started = await ingress.SendAsync(scope, new Contract.Target.Start(new(primary.Type, "{}")),
                "managed-profile-held-start", Contract.Kinds.Start, cancellationToken: token);
            Require(started is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "the managed profile admits the primary actor");
            await UntilAsync(async () =>
            {
                await using var db = Open();
                return await db.Subscriptions.AnyAsync(subscription => subscription.Wait == HeldEventActor.WaitName, token);
            });
            var now = postgres ? await requests!.ReadClockAsync(token) : await sqlite!.ReadClockAsync(token);
            var reply = await ingress.RequestAsync(scope, new Contract.Target.Event(HeldEventActor.EventKey),
                "managed-profile-held-event", Contract.Kinds.Event, null, null,
                new Contract.RequestOptions(null, now + 5000), token);
            Require(reply is { Channel: Request.Response, Error: null } && reply.Item?.ToString() == "accepted",
                $"the managed profile replies to a public held event (channel {reply.Channel}, error {reply.Error}, status {reply.Status}, item {reply.Item})");
        }
        await UntilAsync(() => Task.FromResult(acceptor.Offers.Any(offer => offer.Schedule == reference && offer.Authority == "first")));
        Require(await Apply(5, Control.Set, Header(5, "second")) == new Contract.ScheduleControlResult(Outcome.Applied, reference),
            "authenticated Set enqueues to the exact existing auxiliary root");
        var raw = await ingress.SendAsync(scope, new Contract.Target.Ref(reference), "outside-schedule-set",
            ScheduleActor.SetKind, Header(100, "untrusted"), cancellationToken: token);
        Require(raw.Outcome == Contract.AdmissionOutcome.Accepted, "ordinary ingress can only enqueue an untrusted envelope");
        await UntilAsync(() => Task.FromResult(acceptor.Offers.Any(offer => offer.Schedule == reference && offer.Authority == "second")));

        Require(await Apply(7, Control.Stop) == new Contract.ScheduleControlResult(Outcome.Applied, reference) &&
            await Apply(6, Control.Set, Header(6, "delayed")) == new Contract.ScheduleControlResult(Outcome.Stale, reference) &&
            await Apply(7, Control.Stop) == new Contract.ScheduleControlResult(Outcome.Duplicate, reference),
            "authenticated Stop advances one receipt and fences delayed updates");
        await UntilAsync(StoppedAsync);
        Require(await reads.GetAsync(scope, reference, token) is { Header.TypeData: var saved } &&
            JsonSerializer.Deserialize<ScheduleHeader>(saved) is { Revision: 5, Authority: "second" } &&
            !acceptor.Offers.Any(offer => offer.Authority is "untrusted" or "delayed"),
            "the real schedule actor stops and preserves its trusted header despite the ordinary lookalike");

        Require(await Apply(8, Control.Set, Header(8, "rearmed")) == new Contract.ScheduleControlResult(Outcome.Applied, reference),
            "a later Set re-arms the same stopped root");
        await UntilAsync(() => Task.FromResult(acceptor.Offers.Any(offer => offer.Schedule == reference && offer.Authority == "rearmed")));
        Require(await Apply(9, Control.Stop) == new Contract.ScheduleControlResult(Outcome.Applied, reference),
            "the restored schedule still accepts a newer authenticated Stop");
        await UntilAsync(StoppedAsync);
        await owner.StopAsync(token);
        Require((owner as SqliteEmbeddedHostedService)?.LastError is null && (owner as PostgresPreviewHostedService)?.LastError is null,
            "the installed owner drains both actor types cleanly");

        try
        {
            if (postgres)
            {
                await using var legacy = KernelDb.ForPostgresRootScheduleSettlementQualification(options.Options);
                await EfStore.RequireStoreVersionsAsync(legacy, token);
            }
            else
                await SqliteEmbeddedFoundation.ForScheduleQualification(path!, "legacy-profile", primary.Type).InstallShippedAsync(token);
            throw new InvalidOperationException("The older schedule profile opened the new control store.");
        }
        catch (StoreFormatException) { Console.WriteLine("ok the frozen settlement profile refuses the managed control store"); }

        ValueTask<Contract.ScheduleControlResult> Apply(long revision, Control kind, string? header = null, string scheduleKey = key) =>
            writes.ApplyScheduleControlAsync(scope, definition, scheduleKey, revision, kind, header, token);

        KernelDb Open()
        {
            if (postgres)
                return managedProfile ? PostgresPreviewHosting.CreateManagedSchedulesQualificationContext(options.Options)
                    : PostgresPreviewHosting.CreateScheduleControlsQualificationContext(options.Options);
            var format = managedProfile ? StoreFormat.RootManagedSchedulesQualification : StoreFormat.RootScheduleControlsQualification;
            return KernelDb.ForSqliteRootRuntime(options.Options, new SqliteAdmissionDefinitions(), format);
        }

        async Task<bool> StoppedAsync()
        {
            await using var db = Open();
            return await db.Database.SqlQuery<bool>($"""
                SELECT NOT EXISTS(SELECT 1 FROM "Messages" m JOIN "FoundationRootBirths" b ON b."Actor" = m."Target"
                    WHERE b."Tenant" = {scope.Tenant} AND b."Reference" = {reference.Id}
                    AND m."Kind" IN ({ScheduleActor.FireKind}, {ScheduleActor.SetKind}, {ScheduleActor.StopKind}) AND NOT m."Done") AS "Value"
                """).SingleAsync(token);
        }

        async Task UntilAsync(Func<Task<bool>> reached)
        {
            while (!await reached())
            {
                if (((owner as SqliteEmbeddedHostedService)?.LastError ?? (owner as PostgresPreviewHostedService)?.LastError) is { } fault)
                    throw new InvalidOperationException("The shared schedule owner faulted.", fault);
                await Task.Delay(25, token);
            }
        }
    }

    private static string Header(long revision, string authority, string interval = "PT1S") =>
        JsonSerializer.Serialize(new ScheduleHeader(new(RecurringSchedule.Interval, interval), "unused", revision) { Authority = authority });

    private static void Escalate(string reason) => throw new InvalidOperationException(reason);

    private static void Require(bool result, string description)
    {
        if (!result) throw new InvalidOperationException(description);
        Console.WriteLine("ok " + description);
    }

    private sealed class HeldEventActor : Contract.IActorType
    {
        internal const string EventKey = "managed-profile-event";
        internal const string WaitName = "managed-profile-held-wait";
        public string Type => WorkflowActor.WorkflowActor.ActorType;
        public bool Withholds(Contract.ActorHeader header) => false;

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == Contract.Kinds.Start)
                {
                    turn.State = "{}";
                    turn.Subscribe(EventKey, WaitName);
                }
                else if (message.Event is not null)
                    turn.Emit(Request.Response, "accepted");
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) =>
            ValueTask.FromException<object?>(new InvalidOperationException("The primary fixture actor has no background jobs."));
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }

    private sealed class RecordingAcceptor : IScheduleOccurrenceAcceptor
    {
        internal ConcurrentQueue<ScheduleOccurrence> Offers { get; } = new();

        public ValueTask<bool> TryAcceptAsync(ScheduleOccurrence occurrence, CancellationToken cancellationToken)
        {
            Offers.Enqueue(occurrence);
            return ValueTask.FromResult(true);
        }
    }
}
