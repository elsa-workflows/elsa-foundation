using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;

internal static class SafetyWitness
{
    internal static IEnumerable<string> Names => from m in new[] { Modes.Balanced, Modes.Durable } from n in new[] { false, true } select $"lexical:ended-owner,{m},nested={n}";
    internal static void Run()
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable }) foreach (var nested in new[] { false, true })
        {
            var world = new World(new Config(2, 1, Flaw.None, Native: true)) { Auto = true };
            var target = InstanceRef.In(0, "ended-owner"); var actor = world.Of(0, 0);
            var owner = Wf.Node<EndedOwner>("owner");
            var root = nested ? Wf.Flowchart("outer", [owner, Wf.Set("returned", "returned", true)], [new("owner", "returned")]) : owner;
            NativeWorkflows.Registry.Add(Wf.Workflow("ended-owner", root));
            void Apply(Act action) { world.Apply(action); if (world.Check() is { } error) throw new InvalidOperationException("Kernel: " + error); }
            Apply(new("take", actor.Id)); Apply(new("add", M: new("start", target, Kinds.Start, Start: NativeModel.Spec("ended-owner", mode))));
            for (var i = 0; i < 100; i++)
            {
                Apply(new("poll", actor.Id)); if (actor.Worker.NeedsLoad) Apply(new("load", actor.Id));
                if (actor.Worker.Ready.Contains(target.Id)) Apply(new("turn", actor.Id, S: target.Id));
                else if (!actor.Worker.NeedsFlush && !actor.Worker.NeedsMeet) break;
                if (actor.Worker.NeedsFlush) Apply(new("flush", actor.Id)); if (actor.Worker.NeedsMeet) Apply(new("meet", actor.Id));
            }
            var state = Json.Read<NativeState>(world.Store.Instances[target.Id].State);
            if (state.LexicalScopes is not null || world.Store.Instances[target.Id].Status != InstanceStatus.Finished)
                throw new InvalidOperationException("L4: ended owner retained lexical resources");
            Console.WriteLine($"PASS lexical:ended-owner,{mode},nested={nested}");
        }
    }
}

internal sealed class EndedOwner : CompositeActivity
{
    public EndedOwner() { }
    protected internal override void Execute(CompositeContext context)
    {
        context.Complete();
        try { context.CreateLexicalScope(new Dictionary<string, System.Text.Json.Nodes.JsonNode?>()); }
        catch (InvalidOperationException e) when (e.Message == "Lexical scope owner is not live.") { context.Record("lexical-refused:ended-owner"); return; }
        throw new InvalidOperationException("L5: lexical guard did not refuse: ended-owner");
    }
    protected internal override void OnChildCompleted(CompositeContext context, ChildCompleted child) => throw new InvalidOperationException("Unexpected child.");
}
