using System.Text.Json;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Foundation.Sqlite;
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

        if (postgres)
        {
            await using var db = Open();
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "FoundationScheduleOwnerMode" SET UNLOGGED""", token);
            await MustRefuseAsync(() => StartOwnerAsync("schedule-mode-unlogged", managed), "selected physical root store");
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "FoundationScheduleOwnerMode" SET LOGGED""", token);
            Check(true, "PostgreSQL refuses an unlogged replacement of the durable mode table");

            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "FoundationScheduleOwnerMode" ENABLE ROW LEVEL SECURITY""", token);
            await MustRefuseAsync(() => StartOwnerAsync("schedule-mode-row-security", managed), "selected physical root store");
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "FoundationScheduleOwnerMode" DISABLE ROW LEVEL SECURITY""", token);
            Check(true, "PostgreSQL refuses schedule storage whose owner record could be hidden by policy");
        }

        var scheduled = await CountControlsAsync();
        await using (var db = Open())
        {
            await db.Database.ExecuteSqlRawAsync("""DELETE FROM "FoundationScheduleOwnerMode" WHERE "Singleton" = 1""", token);
            if (postgres) await PostgresPreviewHosting.InstallShippedAsync(options.Options, token);
            else await sqlite!.InstallShippedAsync(token);

            await MustRefuseAsync(() => StartOwnerAsync("schedule-mode-incomplete", wantsManaged: false), "missing from the installed root store");
            await MustRefuseAsync(() => StartOwnerAsync("schedule-mode-incomplete", wantsManaged: true), "missing from the installed root store");
            var modes = await db.Database.SqlQueryRaw<long>("""SELECT count(*) AS "Value" FROM "FoundationScheduleOwnerMode" """).SingleAsync(token);
            Check(modes == 0 && await CountControlsAsync() == scheduled, "a missing owner mode refuses either owner even after reinstall");
        }

        KernelDb Open() => postgres ? PostgresPreviewHosting.CreateShippedContext(options.Options) :
            KernelDb.ForSqliteRootShipped(options.Options);

        async Task<IAsyncDisposable> StartOwnerAsync(string ownerNode, bool wantsManaged)
        {
            if (sqlite is not null)
            {
                var service = wantsManaged ? sqlite.CreateHostedServiceWithAuxiliaryActor(primary, schedule, Escalate) :
                    sqlite.CreateHostedService(primary, Escalate);
                try
                {
                    await service.StartAsync(token);
                    return service;
                }
                catch
                {
                    await service.DisposeAsync();
                    throw;
                }
            }
            var transport = ownerNode == node ? requests! : PostgresPreviewHosting.CreateRequests(Open, ownerNode, primary.Type);
            return await PostgresPreviewHosting.StartAsync(Open, ownerNode, primary, Escalate, null, token, null, transport,
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
        catch (Exception error) when (error is NotSupportedException or StoreFormatException &&
            error.Message.Contains(diagnosis, StringComparison.Ordinal)) { return; }
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
