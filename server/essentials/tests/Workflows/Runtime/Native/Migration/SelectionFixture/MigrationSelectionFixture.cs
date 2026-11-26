using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Kernel.WorkflowActor;
using Contract = Kernel.Foundation.Contracts;
using PublicActor = Kernel.WorkflowActor.WorkflowActor;

namespace Elsa.Workflows.Runtime.Native.Migration.SelectionFixture;

/// <summary>Runs plan selection against the public foundation ports supplied by a provider fixture.</summary>
public sealed class MigrationSelectionFixture
{
    private readonly MigrationPlanAuthority _authority = new(RandomNumberGenerator.GetBytes(32));
    private readonly MigrationPlanModel _plan;
    private readonly string source;
    private readonly string target;
    private readonly string nativeModel;
    private readonly string otherModel;

    public MigrationSelectionFixture(string source, string target, string nativeModel, string otherModel)
    {
        this.source = source;
        this.target = target;
        this.nativeModel = nativeModel;
        this.otherModel = otherModel;
        _plan = new(_authority, (model, from, to) => (model == nativeModel || model == otherModel) && from == source && to == target);
    }

    public Kernel.Model.Contracts.IExecutionModel PlanModel => _plan;

    public async Task RunAsync(Contract.IIngress ingress, Func<int, CancellationToken, ValueTask<Contract.IActorReads>> openReads,
        Contract.BoundScope blue, Contract.BoundScope red, Contract.ActorRef eligible, Contract.ActorRef colliding,
        Func<CancellationToken, Task> pass, Func<Func<Task>, Task> withoutOwnerBody, CancellationToken stop)
    {
        var reads = await openReads(16, stop);
        // These guards run on copies of the persisted header. The timer profile projects
        // only born roots and refuses ephemeral starts, so it cannot supply these rows.
        var eligibleView = (await reads.GetAsync(blue, eligible, stop))!;
        Check(PublicActor.CanSelectForMigration(eligibleView, nativeModel, source),
            "the persisted candidate header is eligible before exercising the guard controls");
        Check(!PublicActor.CanSelectForMigration(WithField(eligibleView, "Version", null), nativeModel, source),
            "the header eligibility guard rejects a missing workflow version");
        Check(!PublicActor.CanSelectForMigration(WithField(eligibleView, "Ephemeral", JsonValue.Create(true)), nativeModel, source),
            "the header eligibility guard rejects an ephemeral workflow");
        Check(!PublicActor.CanSelectForMigration(eligibleView with { Header = eligibleView.Header with { Type = "other" } }, nativeModel, source),
            "the header eligibility guard rejects another actor type");

        var service = Operator(reads);
        var exact = await StartPlan(service, "exact-native", nativeModel);
        var runner = service.Open(blue, exact);
        await withoutOwnerBody(async () => Check(await runner.AdvanceAsync(stop) == MigrationPlanStep.SelectionAdmitted,
            "the real operator admits a signed selection with the owner body unavailable"));
        Check((await Batches(reads, exact)).Count == 0,
            "admission alone does not expose the uncommitted selection in the public plan journal");
        var selected = await DriveBatch(reads, exact, 1);
        Check(selected is { AllSelected: true, After: null } && selected.Actors.SequenceEqual([eligible]),
            "a Native plan selects exactly the eligible reference and excludes the wrong model, definition, status and tenant");
        var afterCommit = await Operator(reads).Open(blue, exact).ReadOutcomesAsync(stop);
        Check(afterCommit is { Closed: false } && afterCommit.Pending.SequenceEqual([eligible]),
            "a fresh operator reports the exact committed selection as pending from the public journal");

        // A one-row list has two source rows. Target the later row's model so the first
        // page commits an empty batch, which can advance without migration dispatch.
        var pageReads = await openReads(1, stop);
        var query = new Contract.ActorQuery(PublicActor.ActorType, Contract.ActorStatus.Running,
            new Dictionary<string, string> { ["definition"] = source });
        var first = await pageReads.ListAsync(blue, query, cancellationToken: stop);
        Check(first is { Items: [var earlier], Next: not null } &&
            (earlier.Header.Ref == eligible || earlier.Header.Ref == colliding), "the public actor list issues a continuation for the two source rows");
        var firstRef = first.Items.Single().Header.Ref;
        var selectedRef = firstRef == eligible ? colliding : eligible;
        var modelForLater = selectedRef == eligible ? nativeModel : otherModel;
        var paginated = await StartPlan(Operator(pageReads), "resumed-selection", modelForLater);
        await withoutOwnerBody(async () => Check(await Operator(pageReads).Open(blue, paginated).AdvanceAsync(stop) == MigrationPlanStep.SelectionAdmitted,
            "a signed selection is admitted even when the first page has no eligible actors"));
        var emptyPage = await DriveBatch(pageReads, paginated, 1);
        Check(emptyPage is { Actors: [], AllSelected: false, After.Value.Length: > 0 and <= 1_024 },
            "the real plan persists the opaque continuation for its empty selection page");
        var bookmarkRejected = false;
        try { await pageReads.ListAsync(red, query, emptyPage.After, stop); }
        catch (ArgumentException error) when (error.ParamName == "from") { bookmarkRejected = true; }
        Check(bookmarkRejected, "the committed list continuation cannot be reused in another tenant");
        var resumedReads = await openReads(1, stop);
        var resumed = Operator(resumedReads).Open(blue, paginated);
        await withoutOwnerBody(async () => Check(await resumed.AdvanceAsync(stop) == MigrationPlanStep.SelectionAdmitted,
            "a fresh store reader and operator advance using the committed continuation"));
        var last = await DriveBatch(resumedReads, paginated, 2);
        Check(last is { AllSelected: true, After: null } && last.Actors.SequenceEqual([selectedRef]) &&
            await resumed.ReadOutcomesAsync(stop) is { Closed: false } outcome && outcome.Pending.SequenceEqual([selectedRef]),
            "the resumed plan reaches its last page and reports only the selected opaque actor reference");

        MigrationPlanOperator Operator(Contract.IActorReads sourceReads) =>
            new(ingress, sourceReads, _plan, _authority, scope => ReferenceEquals(scope, blue), () => true);

        async Task<Contract.ActorRef> StartPlan(MigrationPlanOperator service, string id, string model)
        {
            var clock = (await reads.GetAsync(blue, eligible, stop))!.AsOf;
            var actor = (await service.StartAsync(blue, id, new(source, target, clock + 120_000, model), stop)).Actor
                ?? throw new InvalidOperationException("The fixture plan was not admitted.");
            await DrivePlan(blue, actor);
            return actor;
        }

        async Task DrivePlan(Contract.BoundScope scope, Contract.ActorRef actor)
        {
            for (var attempt = 0; attempt < 24; attempt++)
            {
                if ((await reads.GetAsync(scope, actor, stop))?.Header.Status == Contract.ActorStatus.Running) return;
                await pass(stop);
            }
            throw new InvalidOperationException("The fixture plan did not start.");
        }

        async Task<MigrationPlanJournal.Selected> DriveBatch(Contract.IActorReads reader, Contract.ActorRef plan, long number)
        {
            for (var attempt = 0; attempt < 24; attempt++)
            {
                if ((await Batches(reader, plan)).SingleOrDefault(batch => batch.Number == number) is { } selected) return selected;
                await pass(stop);
            }
            throw new InvalidOperationException($"The fixture plan did not commit selection batch {number}.");
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

    private static Contract.ActorView WithField(Contract.ActorView view, string name, JsonNode? value)
    {
        var typeData = JsonNode.Parse(view.Header.TypeData)!.AsObject();
        typeData[name] = value;
        return view with { Header = view.Header with { TypeData = typeData.ToJsonString() } };
    }

    private static void Check(bool pass, string description)
    {
        if (!pass) throw new InvalidOperationException(description);
        Console.WriteLine($"ok {description}");
    }
}
