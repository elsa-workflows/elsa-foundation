namespace Kernel.Store.Conformance;

internal static class PhysicalRetirementChecks
{
    internal static readonly Case Case = new("physical retirement: nonreuse, atomic refusal, ended-only markers and typed twins", Level.Required, Run);
    private static async Task Run(Context c)
    {
        const string id = "g0/child/0.1.1", running = "g0/key/1";
        var row = Make.Row(id, 1, status: InstanceStatus.Finished);
        var live = Make.Row(running, 1);
        var lease = await c.Take(0);
        const string wire = "same-wire";
        var ordinary = Make.M(wire, 0);
        var creator = ordinary.WithInboxScope(long.MaxValue);
        await c.Lands(() => c.Commit(lease, 0, [Make.Turn(id, row, sent: [ordinary, creator]), Make.Turn(running, live), Make.Turn("g0/key/2",Make.Row("g0/key/2",1,status:InstanceStatus.Finished),journal:null), Make.Turn("0/not-physical", Make.Row("0/not-physical",1,status: InstanceStatus.Finished),journal:null)]));
        var retire = new TurnCommit(id, CreationProtocol.Retire(row), 1, [], [], [], [], [], "[\"deleted\"]", Unsubscribe: true, Delete: true);
        await c.Lands(() => c.Commit(0, lease.Epoch, 2, [retire]));
        var marker = (await c.S.LoadAsync([id])).Single();
        Context.Equal(CreationProtocol.Retire(row), marker, "empty retired identity");
        Context.That((await c.S.ReadJournalAsync(0, 0, 100)).All(j => j.Instance != id || j.Revision < 0), "positive history survived retirement");
        var twins = await c.All(0);
        Context.That(twins.Count(m => m.Message.Id == wire) == 2 && twins.Select(m => m.Message.InboxKey).ToHashSet().Count == 2, "retirement changed typed inbox twins");
        await c.Refused(() => c.Commit(0, lease.Epoch, 3, [Make.New("0/unrelated"), Make.New(id)]), "duplicate mixed with unrelated insert");
        Context.That((await c.S.LoadAsync(["0/unrelated"])).Count == 0, "failed duplicate partially committed unrelated insert");
        await c.Refused(() => c.Commit(0, lease.Epoch, 3, [Make.Turn(id, row with { Revision = 3 }, expected: 2)]), "rebirth update");
        await c.Refused(() => c.Commit(0, lease.Epoch, 3, [retire with { Instance = "g0/key/2", Row = marker with { Id = "g0/key/2", Status = InstanceStatus.Cancelled } }]), "changed terminal status");
        await c.Refused(() => c.Commit(0, lease.Epoch, 3, [retire with { Expected = 2, Row = marker with { Revision = 3 } }]), "wrong expected retirement revision");
        await c.Refused(() => c.Commit(0, lease.Epoch, 3, [retire with { Row = marker with { State = "payload" } }]), "nonempty retirement payload");
        await c.Refused(() => c.Commit(0, lease.Epoch, 3, [retire with { Row = marker with { CustomHandles = "[]" } }]), "extra retirement metadata");
        await c.Refused(() => c.Commit(0, lease.Epoch, 3, [retire with { Instance = running, Row = CreationProtocol.Retire(live with { Status = InstanceStatus.Finished }) }]), "retirement of running row");
        await c.Refused(() => c.Commit(0, lease.Epoch, 3, [retire with { Instance = "0/not-physical", Row = marker with { Id = "0/not-physical" } }]), "marker at arbitrary named address");
        Context.Equal(marker, (await c.S.LoadAsync([id])).Single(), "refusals changed marker");
        Context.Equal(live, (await c.S.LoadAsync([running])).Single(), "refusals changed running row");
        await c.Lands(() => c.Commit(0, lease.Epoch, 3, [Make.Turn("0/x", consumed: [ordinary.InboxKey], done: [creator.InboxKey])]));
        Context.That(await c.S.AddAsync(ordinary), "ordinary twin was not consumed");
        Context.That((await c.All(0)).Count(m => m.Message.Id == wire) == 1, "retained creator unexpectedly polled");
        // The explicit raw delete remains separate from owner retirement; ordinary legacy controls stay valid.
        await c.Lands(() => c.Commit(0, lease.Epoch, 4, [Make.New("0/named")]));
        await c.Lands(() => c.Commit(0, lease.Epoch, 5, [Make.Delete("0/named",1)]));
        await c.Lands(() => c.Commit(0, lease.Epoch, 6, [Make.New("0/named")]));
        await c.S.ReleaseAsync(0,lease.Epoch);
        await using var owner = new Worker("supplied-replay",0,c.S,new WorkflowInstance([new Kernel.Sim.Counter()]));
        await new DurableHost([owner]).ActivateAsync(0);
        await c.S.AddAsync(new Message("supplied-resurrection",new(id),Kinds.Start,Start:new("counter","c")));
        await owner.WokenAsync(null);
        for(var i=0;i<12;i++)await owner.RunAsync();
        Context.Equal(marker,(await c.S.LoadAsync([id])).Single(),"normal supplied Worker start resurrected marker");
        Context.That(!(await c.All(0)).Any(m=>m.Message.Id=="supplied-resurrection"),"supplied start not consumed");
        Context.That(!c.S.CreationIdentityAvailable || c.S is InMemoryStore, "physical marker accidentally enabled positive generation capability");
    }
}
