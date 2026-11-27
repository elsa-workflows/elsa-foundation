using System.Text.Json;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Foundation.Sqlite;
using Kernel.Runtime.Hosting;
using Kernel.WorkflowActor;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;
using WorkflowActorType = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Native.Run;

internal static class ShippedScheduleOwnerModeChecks
{
    internal static async Task<int> RunAsync(bool postgres, bool managed)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var token = timeout.Token;
        var directory = postgres ? null : Directory.CreateTempSubdirectory("shipped-schedule-owner-mode-");
        try
        {
            await VerifyAsync(postgres, managed, directory?.FullName, token);
            return 0;
        }
        finally { directory?.Delete(recursive: true); }
    }

    private static async Task VerifyAsync(bool postgres, bool managed, string? directory, CancellationToken token)
    {
        const string node = "schedule-mode-first";
        const string definition = "mode-proof";
        const string key = "schedule:mode-proof:trigger";
        var bound = EmbeddedTenant.Bind("mode-fixture");
        var path = directory is null ? null : Path.Combine(directory, "foundation.db");
        var options = new DbContextOptionsBuilder<KernelDb>();
        if (postgres)
        {
            options.UseNpgsql(Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
                ?? throw new InvalidOperationException("Supply an empty dedicated PostgreSQL fixture."));
            await using (var db = Open())
                Check(await PostgresFixtureDatabase.IsEmptyAsync(db, token), "the PostgreSQL fixture is empty");
            await PostgresPreviewHosting.InstallShippedAsync(options.Options, token);
        }
        else
        {
            options.UseSqlite(new SqliteConnectionStringBuilder { DataSource = path!, ForeignKeys = true, Pooling = false }.ToString());
        }

        var workflow = Wf.Workflow("ordinary-root", Wf.WriteLine("step", "ordinary root finished"));
        var primary = new WorkflowActorType([new NativeExecutionModel(new WorkflowRegistry().Add(workflow))]);
        var schedule = new ScheduleActor(null, new AcceptsOffers());
        var start = WorkflowActorType.Start(NativeExecutionModel.Start(workflow));
        var sqlite = path is null ? null : new SqliteEmbeddedFoundation(path, node, primary.Type);
        if (sqlite is not null) await sqlite.InstallShippedAsync(token);
        var requests = postgres ? PostgresPreviewHosting.CreateRequests(Open, node, primary.Type) : null;
        var pg = postgres ? await PostgresPreviewHosting.OpenFoundationAsync(Open, primary.Type, requests, cancellationToken: token) : null;
        Contract.IIngress ingress = pg is null ? await sqlite!.OpenIngressAsync(token) : pg;
        Contract.IActorReads reads = pg is null ? sqlite!.CreateReads() : pg;
        var controls = sqlite?.BindScheduleControls(schedule.Type) ?? PostgresPreviewHosting.BindScheduleControls(Open, schedule.Type);
        var occurrences = sqlite?.BindScheduleOccurrences(bound, schedule.Type) ??
            await PostgresPreviewHosting.OpenScheduleOccurrencesAsync(Open, bound, primary.Type, schedule.Type, requests, token);
        var header = JsonSerializer.Serialize(new ScheduleHeader(new(RecurringSchedule.Interval, "PT1H"), "unused", 1) { Authority = "mode-proof" });
        var candidate = new Contract.AcceptedScheduleOccurrence(definition, key, new(new string('A', 64)), 1,
            "~" + new string('B', 64), 1_702_000_000_000);

        await MustRefuseAsync(() => SetAsync().AsTask(), "before admitting schedule work");
        await MustRefuseAsync(() => occurrences.AdmitAsync(candidate, start, token).AsTask(), "before admitting schedule work");
        Check(await CountControlsAsync() == 0, "unbound schedule control and occurrence writes leave the store empty");

        await using (var first = await StartOwnerAsync(node, managed))
        {
            if (postgres)
                await MustRefuseAsync(() => StartOwnerAsync("schedule-mode-contender", !managed), "different schedule owner mode");
            var ordinary = await ingress.SendAsync(bound, new Contract.Target.Start(start), "ordinary", Contract.Kinds.Start, cancellationToken: token);
            Check(ordinary is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "either mode admits an ordinary workflow");
            await WaitAsync(ordinary.Actor!.Value, Contract.ActorStatus.Finished);
            if (managed)
            {
                var admitted = await SetAsync();
                Check(admitted is { Outcome: Contract.ScheduleControlOutcome.Applied, Actor: not null }, "the bound managed owner can create the schedule");
                await WaitAsync(admitted.Actor!.Value, Contract.ActorStatus.Running);
                Check(await SetAsync() == admitted with { Outcome = Contract.ScheduleControlOutcome.Duplicate }, "the managed control replays its exact receipt");
                var occurrence = await occurrences.AdmitAsync(candidate with { Schedule = admitted.Actor.Value }, start, token);
                Check(occurrence is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "the bound managed owner admits the scheduled occurrence");
                await WaitAsync(occurrence.Actor!.Value, Contract.ActorStatus.Finished);
            }
            else
            {
                await MustRefuseAsync(() => SetAsync().AsTask(), "different schedule owner mode");
                await MustRefuseAsync(() => occurrences.AdmitAsync(candidate, start, token).AsTask(), "different schedule owner mode");
                Check(await CountControlsAsync() == 0, "a live standalone owner cannot be given managed schedule work");
            }
        }

        await MustRefuseAsync(() => StartOwnerAsync("schedule-mode-other", !managed), "different schedule owner mode");
        await using (var restarted = await StartOwnerAsync("schedule-mode-restarted", managed)) { }
        Check(true, "the mode survives stopping all owners and permits only a matching restart");

        KernelDb Open() => postgres ? PostgresPreviewHosting.CreateShippedContext(options.Options) :
            KernelDb.ForSqliteRootShipped(options.Options);

        Task<RuntimeNode> StartOwnerAsync(string ownerNode, bool wantsManaged)
        {
            if (sqlite is not null)
                return SqliteEmbeddedHosting.StartFoundationAsync(path!, ownerNode, primary, Escalate,
                    cancellationToken: token, auxiliaryActor: wantsManaged ? schedule : null);
            var transport = ownerNode == node ? requests! : PostgresPreviewHosting.CreateRequests(Open, ownerNode, primary.Type);
            return PostgresPreviewHosting.StartAsync(Open, ownerNode, primary, Escalate, null, token, null, transport,
                fenceInBatch: true, auxiliaryActor: wantsManaged ? schedule : null);
        }

        ValueTask<Contract.ScheduleControlResult> SetAsync() => controls.ApplyScheduleControlAsync(bound, definition, key,
            1, Contract.ScheduleControlKind.Set, header, token);

        async Task<long> CountControlsAsync()
        {
            await using var db = Open();
            return await db.Database.SqlQueryRaw<long>("""
                SELECT (SELECT count(*) FROM "FoundationScheduleControls") + (SELECT count(*) FROM "FoundationScheduleRoots")
                    AS "Value"
                """).SingleAsync(token);
        }

        async Task WaitAsync(Contract.ActorRef actor, Contract.ActorStatus status)
        {
            while ((await reads.GetAsync(bound, actor, token))?.Header.Status != status)
                await Task.Delay(25, token);
        }
    }

    private static async Task MustRefuseAsync(Func<Task> operation, string diagnosis)
    {
        try { await operation(); }
        catch (NotSupportedException error) when (error.Message.Contains(diagnosis, StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException($"Expected a schedule owner mode refusal containing '{diagnosis}'.");
    }

    private static void Escalate(string reason) => throw new InvalidOperationException(reason);

    private sealed class AcceptsOffers : IScheduleOccurrenceAcceptor
    {
        public ValueTask<bool> TryAcceptAsync(ScheduleOccurrence occurrence, CancellationToken cancellationToken) => ValueTask.FromResult(true);
    }

    private static void Check(bool result, string description)
    {
        if (!result) throw new InvalidOperationException(description);
        Console.WriteLine("ok " + description);
    }
}
