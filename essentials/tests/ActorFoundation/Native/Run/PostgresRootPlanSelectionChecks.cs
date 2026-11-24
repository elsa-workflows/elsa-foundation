using System.Text.Json.Nodes;
using Elsa.ActorFoundation.TestSupport;
using Elsa.Workflows.Runtime.Native.Migration.SelectionFixture;
using Kernel.EntityFramework;
using Kernel.Native.Preview;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;
using PublicModel = Kernel.Model.Contracts;
using PublicActor = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Native.Run;

internal static class PostgresRootPlanSelectionChecks
{
    private const string Source = "selection-source";
    private const string Target = "selection-target";

    internal static async Task<int> RunAsync(bool owned, bool inBatch)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use an empty dedicated PostgreSQL database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => KernelDb.ForPostgresRootTimers(options);
        EfStore Store() => new(Open, postgres: true, fenceInBatch: inBatch)
            { CommitSql = owned ? CommitSql.Owned : CommitSql.EntityFramework };
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var stop = deadline.Token;
        await using (var db = Open())
            Check(await PostgresFixtureDatabase.IsEmptyAsync(db, stop), "the plan selection fixture uses an empty store");

        var store = Store();
        await store.CreateAsync(1);
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        await admission.InstallAsync(stop);
        var blue = ContractScopes.Bind("plan-blue");
        var red = ContractScopes.Bind("plan-red");
        // The timer qualification profile uses the same PostgreSQL ingress as the preview host.
        var ingress = new PostgresRootIngress(admission, PublicActor.ActorType);
        var reads = await store.OpenPreviewRootReadsAsync(pageSize: 16, cancellationToken: stop);
        var runtime = new WorkflowRuntime(ingress, reads);
        var fixture = new MigrationSelectionFixture(Source, Target, NativeExecutionModel.ModelKey, OtherModel.ModelKey);
        var registry = new WorkflowRegistry()
            .Add(Wf.Workflow(Source, Wf.If("finish-or-wait", Binding.Expr("input.complete"),
                Wf.Sequence("finished"), Wf.Node<PostgresRootTerminateChecks.InvocationWait>("wait"))))
            .Add(Wf.Workflow(Target, Wf.Node<PostgresRootTerminateChecks.InvocationWait>("wait")));
        var actorType = new PublicActor([new NativeExecutionModel(registry), new OtherModel(), fixture.PlanModel]);

        var eligible = await Start(blue, "eligible", NativeSource(complete: false));
        var colliding = await Start(blue, "other-model", new(OtherModel.ModelKey, Source));
        var ended = await Start(blue, "ended", NativeSource(complete: true));
        var differentDefinition = await Start(blue, "different-definition", NativeExecutionModel.Start(Target));
        var foreign = await Start(red, "foreign-eligible", NativeSource(complete: false));

        var ownerStore = Store();
        await ownerStore.EnableRootSelfSendsAsync(stop);
        var roots = (await ownerStore.BindWorkerPartitionAsync(0, stop)).Scope;
        var host = new DurableHost([new Worker("plan-selection-owner", 0, ownerStore,
            new ActorAdapter(new EfStore.RootHeaders(roots), actorType, ownerStore.RootSelfIssuer), new WorkerOptions { Partitions = 1 })]);
        await host.HeartbeatAsync(Defaults.Lease, stop);
        await using var owner = await host.ActivateAsync(0, stop)
            ?? throw new InvalidOperationException("The fixture owner could not take its partition.");
        foreach (var actor in new[] { eligible, colliding, differentDefinition })
            await Drive(blue, actor, Contract.ActorStatus.Running);
        await Drive(red, foreign, Contract.ActorStatus.Running);
        await Drive(blue, ended, Contract.ActorStatus.Finished);

        var ephemeralRefused = false;
        try { await runtime.StartAsync(blue, "ephemeral", NativeExecutionModel.Start(Source, Modes.Ephemeral), cancellationToken: stop); }
        catch (NotSupportedException) { ephemeralRefused = true; }
        Check(ephemeralRefused, "the PostgreSQL timer profile refuses an ephemeral workflow start");
        var clock = (await reads.GetAsync(blue, eligible, stop))!.AsOf;
        var future = await ingress.SendAsync(blue, new Contract.Target.Start(PublicActor.Start(NativeSource(complete: false))),
            "unborn-source", Contract.Kinds.Start, options: new(clock + 120_000), cancellationToken: stop);
        Check(future is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } futureRef } &&
            await reads.GetAsync(blue, futureRef, stop) is null, "an admitted source without a committed model version is absent from the projection");
        Check((await store.ReadFirstRootAsync(blue.Tenant, eligible.Id, stop))?.State is not null,
            "the candidate has an authoritative body before the read-path isolation");

        await fixture.RunAsync(ingress, (pageSize, token) => Store().OpenPreviewRootReadsAsync(pageSize, token),
            blue, red, eligible, colliding, Next, WithoutOwnerBody, stop);
        return 0;

        static PublicModel.WorkflowStart NativeSource(bool complete) =>
            NativeExecutionModel.Start(Source, input: new JsonObject { ["complete"] = complete });

        async Task<Contract.ActorRef> Start(Contract.BoundScope scope, string id, PublicModel.WorkflowStart start) =>
            (await runtime.StartAsync(scope, id, start, cancellationToken: stop)).Actor
            ?? throw new InvalidOperationException("The fixture actor was not admitted.");

        async Task Drive(Contract.BoundScope scope, Contract.ActorRef actor, Contract.ActorStatus status)
        {
            for (var pass = 0; pass < 24; pass++)
            {
                if ((await reads.GetAsync(scope, actor, stop))?.Header.Status == status) return;
                await Next(stop);
            }
            throw new InvalidOperationException($"The fixture actor did not reach {status}.");
        }

        async Task Next(CancellationToken token)
        {
            await owner.WokenAsync(null, token);
            await owner.RunAsync(token);
        }

        async Task WithoutOwnerBody(Func<Task> select)
        {
            try
            {
                await SetOwnerBodyHidden(true, stop);
                var ownerReadFailed = false;
                try { await store.ReadFirstRootAsync(blue.Tenant, eligible.Id, stop); }
                catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.UndefinedColumn) { ownerReadFailed = true; }
                Check(ownerReadFailed, "the real owner cannot hydrate the candidate while the selector runs");
                await select();
            }
            finally
            {
                using var restoration = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await SetOwnerBodyHidden(false, restoration.Token);
            }
        }

        async Task SetOwnerBodyHidden(bool hide, CancellationToken token)
        {
            // Keep the relation, identity and journal available. The scheduler is idle in this window.
            await using var db = Open();
            await using var change = await db.Database.BeginTransactionAsync(token);
            var visible = await CountOwnerColumns();
            if (visible != (hide ? 0 : 3))
            {
                await db.Database.ExecuteSqlRawAsync(hide ? """
                    ALTER TABLE "Instances" RENAME COLUMN "State" TO "FixtureSelectionState";
                    ALTER TABLE "Instances" RENAME COLUMN "StateVersion" TO "FixtureSelectionVersion";
                    ALTER TABLE "Instances" RENAME COLUMN "FoundationHeader" TO "FixtureSelectionHeader";
                    """ : """
                    ALTER TABLE "Instances" RENAME COLUMN "FixtureSelectionState" TO "State";
                    ALTER TABLE "Instances" RENAME COLUMN "FixtureSelectionVersion" TO "StateVersion";
                    ALTER TABLE "Instances" RENAME COLUMN "FixtureSelectionHeader" TO "FoundationHeader";
                    """, token);
                visible = await CountOwnerColumns();
            }
            if (visible != (hide ? 0 : 3)) throw new InvalidOperationException("The owner body schema did not reach the requested state.");
            await change.CommitAsync(token);

            Task<int> CountOwnerColumns() => db.Database.SqlQueryRaw<int>("""
                SELECT count(*)::int AS "Value" FROM pg_catalog.pg_attribute
                WHERE attrelid = to_regclass('"Instances"') AND NOT attisdropped
                  AND attname IN ('State', 'StateVersion', 'FoundationHeader')
                """).SingleAsync(token);
        }
    }

    private static void Check(bool pass, string description)
    {
        if (!pass) throw new InvalidOperationException(description);
        Console.WriteLine($"ok {description}");
    }

    private sealed class OtherModel : PublicModel.IExecutionModel
    {
        internal const string ModelKey = "other-selection-model";
        public string Key => ModelKey;
        public Type StateType => typeof(string);
        public int StateVersion => 1;
        public object CreateState(PublicModel.WorkflowStart start) => "other state";
        public ValueTask TurnAsync(PublicModel.ITurn turn, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public Contract.RestorePlan RestoreWaits(PublicModel.InstanceInfo instance, object state) => new([], [], []);
        public long TerminalGrowth(PublicModel.InstanceInfo instance, object state) => 0;
        public object UpgradeState(string state, int fromVersion) => throw new NotSupportedException();
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
