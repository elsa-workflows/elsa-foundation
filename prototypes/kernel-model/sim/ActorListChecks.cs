using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

/// <summary>FC-11 paged actor lists over real fenced root turns.</summary>
internal static class ActorListChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        const long stamp = 1_700_100_000_000;
        var now = stamp;
        var store = new InMemoryStore(1, () => now);
        var started = new List<string>();
        string Add(string tenant, string id, string status, string definition = "invoice", string model = "list")
        {
            var target = InstanceRef.In(0, tenant + "_" + id);
            return store.AdmitFoundationStart(tenant, new Message(tenant + "/" + id, target, status,
                Start: new StartSpec(model, definition, Creation: new(1, 0, id)), TargetCreation: 1))!;
        }

        for (var i = 0; i < 5; i++) started.Add(Add("acme", "match" + i, "finish"));
        Add("acme", "wrong-definition", "finish", definition: "other");
        Add("acme", "wrong-model", "finish", model: "other");
        var parked = Add("acme", "park", "park");
        var authoredFault = Add("acme", "authored-fault", "wait");
        Add("other", "elsewhere", "finish");

        Worker Owner(string name) => new(name, 0, store, new WorkflowInstance([new Probe("list"), new Probe("other")]),
            new WorkerOptions { Monotonic = () => 0 });

        var firstOwner = (await new DurableHost([Owner("list-owner")]).ActivateAsync(0))!;
        await Pump(firstOwner);
        now++;
        Add("acme", "at-end-bound", "finish");
        await Pump(firstOwner);

        var scope = new Contract.BoundScope("acme");
        var reader = new ActorList(store, 2);
        var labels = new Dictionary<string, string> { ["version"] = "1", ["definition"] = "invoice" };
        var query = new Contract.ActorQuery("list", Contract.ActorStatus.Finished, labels, stamp + 1, Parked: false);
        var source = store.ReadFoundationActor("acme", authoredFault).Actor!.Row;
        var claimed = source with { Revision = source.Revision + 1, Status = InstanceStatus.Faulted, Fault = "actor-owned outcome" };
        var custom = new Dictionary<string, string> { ["region"] = "west" };
        var headerUpdate = new TurnCommit(source.Id, claimed, source.Revision, [], [], [], [], [], "[\"ended\"]")
        { FoundationHeader = new("opaque data", custom) };
        var fence = store.Partitions[0];
        var batch = new CommitBatch(0, fence.Epoch, fence.Position, TimeSpan.FromSeconds(10), false, [headerUpdate], [], []);
        check((await store.CommitAsync(batch)).Landed, "the fenced checkpoint stores custom actor header data");
        custom["region"] = "later caller mutation";
        var listedCustom = await reader.ListAsync(scope, new(Labels: new Dictionary<string, string> { ["region"] = "west" }));
        check(listedCustom.Items is [{ Header.TypeData: "opaque data", Header.Status: Contract.ActorStatus.Faulted, Park: null }],
            "committed custom labels drive the list and an actor-authored fault has no Park mark");

        var tooMany = Enumerable.Range(1, 5).ToDictionary(index => index.ToString(), _ => "value");
        var invalid = headerUpdate with { Expected = claimed.Revision, Row = claimed with { Revision = claimed.Revision + 1 },
            FoundationHeader = new("uncommitted", tooMany) };
        var refused = false;
        try { await store.CommitAsync(batch with { Position = fence.Position, Turns = [invalid] }); }
        catch (StoreException exception) when (exception.Failure == Failure.Definite) { refused = true; }
        check(refused && (await reader.GetAsync(scope, new(authoredFault)))?.Header.TypeData == "opaque data" &&
            store.Instances[claimed.Id].Revision == claimed.Revision, "an oversized label set cannot change the checkpoint or its read model");

        var first = await reader.ListAsync(scope, query);
        check(first.Items.Count == 2 && first.Next is not null && first.AsOf == now &&
            first.Items.All(item => item.AsOf == now && item.Header.EndedAt == stamp), "the first bound list page includes only committed matches and an AsOf reading");

        await firstOwner.DisposeAsync();
        await using var successor = (await new DurableHost([Owner("list-replacement")]).ActivateAsync(0))!;
        var nextReader = new ActorList(store, 2);
        var reordered = query with { Labels = new Dictionary<string, string> { ["definition"] = "invoice", ["version"] = "1" } };
        var second = await nextReader.ListAsync(scope, reordered, first.Next);
        var third = await nextReader.ListAsync(scope, query, second.Next);
        var combined = first.Items.Concat(second.Items).Concat(third.Items).Select(item => item.Header.Ref.Id).ToArray();
        check(second is { Items.Count: 2, Next: not null } && third is { Items.Count: 1, Next: null } &&
              combined.SequenceEqual(started.Order(StringComparer.Ordinal)),
            "three pages visit tied results exactly once after owner replacement and label reordering");

        var wrongTenant = new Contract.BoundScope("other");
        check(await Refused(wrongTenant, query, first.Next) && await Refused(scope, query with { Parked = true }, first.Next) &&
            await Refused(scope, query, new Contract.PageToken("journal-opaque")),
            "list continuations reject another tenant, altered filters and a different read kind");
        var forgedContext = System.Text.Encoding.UTF8.GetBytes("journal");
        var wrongKind = new Contract.PageToken(store.SealActorListCursor(forgedContext, started[0]));
        check(await Refused(scope, query, wrongKind), "even a valid token from another read context cannot continue the list");

        var parkView = await reader.GetAsync(scope, new(parked));
        check(parkView is { Header.Status: Contract.ActorStatus.Faulted, Park.At: stamp } &&
              await reader.GetAsync(wrongTenant, new(parked)) is null,
            "the bound point read shows Park and masks a foreign exact ref");
        var parkedOnly = await reader.ListAsync(scope, new(Parked: true));
        check(parkedOnly.Items is [{ Header.Ref.Id: var parkedRef }] && parkedRef == parked,
            "the Park filter returns parked work through the live read route");

        async Task<bool> Refused(Contract.BoundScope fromScope, Contract.ActorQuery selected, Contract.PageToken? token)
        {
            try { await nextReader.ListAsync(fromScope, selected, token); return false; }
            catch (ArgumentException exception) when (exception.ParamName == "from") { return true; }
        }
    }

    private static async Task Pump(IScopeActivation activation)
    {
        for (var step = 0; step < 20; step++)
        {
            await activation.WokenAsync(null);
            await activation.RunAsync();
            await Task.Yield();
        }
    }

    private sealed class Probe(string key) : Model<List<string>>
    {
        public override string Key => key;
        protected override void Turn(ITurn turn, List<string> state)
        {
            var kind = turn.Inbox[0].Kind;
            if (kind == "finish") turn.SetStatus(InstanceStatus.Finished);
            else if (kind == "park") throw new InvalidOperationException("park marker");
        }
    }
}
