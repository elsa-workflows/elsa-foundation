using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Elsa.ActorFoundation.TestSupport;
using Kernel.EntityFramework;
using Kernel.Native.Preview;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
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
        var key = RandomNumberGenerator.GetBytes(32);
        var planModel = new MigrationPlanModel(new MigrationPlanAuthority(key), (model, from, to) =>
            model is NativeExecutionModel.ModelKey or OtherModel.ModelKey && from == Source && to == Target);
        var registry = new WorkflowRegistry()
            .Add(Wf.Workflow(Source, Wf.If("finish-or-wait", Binding.Expr("input.complete"),
                Wf.Sequence("finished"), Wf.Node<PostgresRootTerminateChecks.InvocationWait>("wait"))))
            .Add(Wf.Workflow(Target, Wf.Node<PostgresRootTerminateChecks.InvocationWait>("wait")));
        var actorType = new PublicActor([new NativeExecutionModel(registry), new OtherModel(), planModel]);

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

        var service = Operator(reads);
        var exact = await StartPlan(service, "exact-native", NativeExecutionModel.ModelKey);
        var runner = service.Open(blue, exact);
        Check(await runner.AdvanceAsync(stop) == MigrationPlanStep.SelectionAdmitted,
            "the real operator admits a signed selection through PostgreSQL ingress");
        Check((await Batches(reads, exact)).Count == 0,
            "admission alone does not expose the uncommitted selection in the public plan journal");
        var selected = await DriveBatch(reads, exact, 1);
        Check(selected is { AllSelected: true, After: null } && selected.Actors.SequenceEqual([eligible]),
            "a Native plan selects exactly the eligible reference and excludes the wrong model, definition, status and tenant");
        var afterCommit = await Operator(reads).Open(blue, exact).ReadOutcomesAsync(stop);
        Check(afterCommit is { Closed: false } && afterCommit.Pending.SequenceEqual([eligible]),
            "a fresh operator reports the exact committed selection as pending from PostgreSQL journal reads");

        // A one-row list has two source rows. Target the later row's model so the first
        // page commits an empty batch, which can advance without migration dispatch.
        var pageReads = await store.OpenPreviewRootReadsAsync(pageSize: 1, cancellationToken: stop);
        var query = new Contract.ActorQuery(PublicActor.ActorType, Contract.ActorStatus.Running,
            new Dictionary<string, string> { ["definition"] = Source });
        var first = await pageReads.ListAsync(blue, query, cancellationToken: stop);
        Check(first is { Items: [var earlier], Next: not null } &&
            (earlier.Header.Ref == eligible || earlier.Header.Ref == colliding), "the real PostgreSQL list issues a continuation for the two source rows");
        var firstRef = first.Items.Single().Header.Ref;
        var selectedRef = firstRef == eligible ? colliding : eligible;
        var modelForLater = selectedRef == eligible ? NativeExecutionModel.ModelKey : OtherModel.ModelKey;
        var paginated = await StartPlan(Operator(pageReads), "resumed-selection", modelForLater);
        Check(await Operator(pageReads).Open(blue, paginated).AdvanceAsync(stop) == MigrationPlanStep.SelectionAdmitted,
            "a signed selection is admitted even when the first page has no eligible actors");
        var emptyPage = await DriveBatch(pageReads, paginated, 1);
        Check(emptyPage is { Actors: [], AllSelected: false, After.Value.Length: > 0 and <= 1_024 },
            "the real plan persists the opaque continuation for its empty selection page");
        var bookmarkRejected = false;
        try { await pageReads.ListAsync(red, query, emptyPage.After, stop); }
        catch (ArgumentException error) when (error.ParamName == "from") { bookmarkRejected = true; }
        Check(bookmarkRejected, "the committed list continuation cannot be reused in another tenant");
        var resumedReads = await Store().OpenPreviewRootReadsAsync(pageSize: 1, cancellationToken: stop);
        var resumed = Operator(resumedReads).Open(blue, paginated);
        Check(await resumed.AdvanceAsync(stop) == MigrationPlanStep.SelectionAdmitted,
            "a fresh store reader and operator advance using the committed continuation");
        var last = await DriveBatch(resumedReads, paginated, 2);
        Check(last is { AllSelected: true, After: null } && last.Actors.SequenceEqual([selectedRef]) &&
            await resumed.ReadOutcomesAsync(stop) is { Closed: false } outcome && outcome.Pending.SequenceEqual([selectedRef]),
            "the resumed plan reaches its last page and reports only the selected opaque actor reference");
        return 0;

        MigrationPlanOperator Operator(Contract.IActorReads sourceReads) =>
            new(ingress, sourceReads, planModel, new MigrationPlanAuthority(key), scope => ReferenceEquals(scope, blue), () => true);

        static PublicModel.WorkflowStart NativeSource(bool complete) =>
            NativeExecutionModel.Start(Source, input: new JsonObject { ["complete"] = complete });

        async Task<Contract.ActorRef> Start(Contract.BoundScope scope, string id, PublicModel.WorkflowStart start) =>
            (await runtime.StartAsync(scope, id, start, cancellationToken: stop)).Actor
            ?? throw new InvalidOperationException("The fixture actor was not admitted.");

        async Task<Contract.ActorRef> StartPlan(MigrationPlanOperator service, string id, string model)
        {
            var clock = (await reads.GetAsync(blue, eligible, stop))!.AsOf;
            var actor = (await service.StartAsync(blue, id, new(Source, Target, clock + 120_000, model), stop)).Actor
                ?? throw new InvalidOperationException("The fixture plan was not admitted.");
            await Drive(blue, actor, Contract.ActorStatus.Running);
            return actor;
        }

        async Task Drive(Contract.BoundScope scope, Contract.ActorRef actor, Contract.ActorStatus status)
        {
            for (var pass = 0; pass < 24; pass++)
            {
                if ((await reads.GetAsync(scope, actor, stop))?.Header.Status == status) return;
                await Next();
            }
            throw new InvalidOperationException($"The fixture actor did not reach {status}.");
        }

        async Task<MigrationPlanJournal.Selected> DriveBatch(Contract.IActorReads reader, Contract.ActorRef plan, long number)
        {
            for (var pass = 0; pass < 24; pass++)
            {
                if ((await Batches(reader, plan)).SingleOrDefault(batch => batch.Number == number) is { } selected) return selected;
                await Next();
            }
            throw new InvalidOperationException($"The fixture plan did not commit selection batch {number}.");
        }

        async Task Next()
        {
            await owner.WokenAsync(null, stop);
            await owner.RunAsync(stop);
        }

        async Task<List<MigrationPlanJournal.Selected>> Batches(Contract.IActorReads reader, Contract.ActorRef plan)
        {
            var batches = new List<MigrationPlanJournal.Selected>();
            Contract.PageToken? after = null;
            while (true)
            {
                var page = await reader.TailAsync(blue, plan, after, stop);
                if (page.Items.Count == 0) return batches;
                batches.AddRange(page.Items.Select(row => MigrationPlanJournal.Read(row.Entry)).OfType<MigrationPlanJournal.Selected>());
                if (page.Next is null || page.Next == after) throw new InvalidOperationException("The fixture journal did not advance.");
                after = page.Next;
            }
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
