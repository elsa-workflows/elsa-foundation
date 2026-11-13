using Kernel.Durable;
using Kernel.Native;
using Kernel.Sim;
using Kernel.Sim.Native;

sealed partial class CreationWitness
{
    internal void RecreationPark(string failure)
    {
        NativeWorkflows.Registry.Add(Wf.Workflow("creation-probe", Wf.Node<IdentityReceiver>("receiver").With(Wf.Event("finish", "finish"))));
        Apply(new("take", Actor.Id)); Send(Kinds.Start, 1, start: Spec(1)); Drain(); Retire(1);
        var predecessor = Row; var refusals = 0;
        var attempted = new List<InstanceRow>();
        if (failure == "refused")
        {
            // Oracle expectation only: the real store predicate must reject a normal generation-two row below.
            _world.Oracle.Fragiles[_target.Id] = true;
        }
        _world.Store.Refuses = row =>
        {
            if (row.Id == _target.Id) attempted.Add(row);
            if (failure != "refused" || row.Id != _target.Id || row.Creation != 2 || row.Status != InstanceStatus.Running) return false;
            refusals++; return true;
        };
        Send(Kinds.Start, 2, start: Spec(2));
        var start = _world.Store.Messages.Values.Single(m => m.Message.Start?.Creation?.Generation == 2).Message.Id;
        if (failure == "poison")
        {
            Apply(new("poll", Actor.Id)); Apply(new("flush", Actor.Id)); Takeover();
            for (var attempt = 0; attempt < 3; attempt++)
            {
                Apply(new("poll", Actor.Id)); if (Actor.Worker.NeedsLoad) Apply(new("load", Actor.Id));
                Apply(new("flush", Actor.Id));
                Require(_world.Store.Messages[start].Trial > 0 && _world.Store.Messages[start].Attempts == attempt,
                    "C11", "poison witness did not mark each actual new-creation trial before losing its owner");
                Takeover();
            }
            Require(_world.Store.Messages[start].Attempts == 3, "C11", "actual owner losses did not exhaust attempts");
        }
        try { Drain(); }
        catch (InvalidOperationException error) when (error.Message.StartsWith("C0:", StringComparison.Ordinal))
        { throw new InvalidOperationException("C11: recreation could not settle its failed start", error); }
        Require(attempted.Count > 0 && attempted.All(row => row.Creation == 2 && !row.Retired),
            "C11", "recreation tried to overwrite its lifecycle predecessor");
        Require(Row is { Creation: 2, Retired: false, Status: InstanceStatus.Faulted, State: "null", Revision: 1 } &&
            Row.StartIntent == CreationProtocol.Fingerprint(Spec(2)) && !_world.Store.Messages.ContainsKey(start) &&
            predecessor is { Creation: 1, Retired: true } && (failure != "refused" || refusals > 0),
            "C11", "recreation failed to park as a new creation over the retained predecessor");
        Send(Kinds.Delete, 2); Drain();
        Require(Row is { Creation: 2, Retired: true }, "C11", "faulted replacement could not retire normally");
        Console.WriteLine($"PASS creation:recreation-park,{mode},failure={failure}");
    }
}
