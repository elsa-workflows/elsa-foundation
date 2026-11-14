using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;

internal static class ReadWitness
{
    internal static IEnumerable<string> Names => from m in new[] { Modes.Balanced, Modes.Durable }
        from n in new[] { false, true } from r in new[] { false, true } select $"lexical:read,{m},nested={n},reload={r}";
    internal static void Run()
    {
        foreach (var m in new[] { Modes.Balanced, Modes.Durable }) foreach (var n in new[] { false, true }) foreach (var r in new[] { false, true }) Run(m, n, r);
    }
    private static void Run(string mode, bool nested, bool reload)
    {
        var world = new World(new Config(2, 1, Flaw.None, Native: true)) { Auto = true };
        var target = InstanceRef.In(0, "read-proof"); var owner = 0; var serial = 0;
        Actor Actor() => world.Of(owner, 0);
        Node Branch(string prefix, int item)
        {
            var probe = Wf.Node<ReadProbe>("probe" + prefix, ("Prefix", prefix), ("Ancestor", Binding.Var("ancestor"))).With(
                Wf.Sequence("entry" + prefix, Wf.Set("increment" + prefix, "counter", Binding.Expr("counter + 1")),
                    Wf.Set("local" + prefix, "activityLocal" + prefix, Binding.Var("counter")),
                    Wf.Set("outer" + prefix, "activityOuter" + prefix, Binding.Var("outerOnly")),
                    Wf.Set("borrow" + prefix, "activityBorrow" + prefix, Binding.Var("borrowedOnly")),
                    Wf.Set("item" + prefix, "activityItem" + prefix, Binding.Var("CurrentItem")),
                    Wf.Set("global" + prefix, "activityGlobal" + prefix, Binding.Var("workflowOnly")),
                    Wf.Node<ReadValue>("value" + prefix, ("Value", Binding.Literal(new JsonObject { ["n"] = 23 }))),
                    Wf.Event("wait" + prefix, "read-" + prefix)));
            probe.Captures.Add("composite" + prefix, "");
            return Wf.ForEach("loop" + prefix, Binding.Literal(new[] { item }), Wf.Node<ReadWrapper>("wrapper" + prefix).With(probe));
        }
        Node root = Wf.Parallel("pair", null, Branch("A", 5), Branch("B", 6));
        if (nested) root = Wf.Flowchart("flow", [root, Wf.Set("return", "returned", true)], [new("pair", "return")]);
        NativeWorkflows.Registry.Add(Wf.Workflow("read-proof", root, ("counter", 3), ("workflowOnly", 13)));
        NativeState State() => Json.Read<NativeState>(world.Store.Instances[target.Id].State);
        void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
        void Apply(Act a) { world.Apply(a); if (world.Check() is { } error) throw new InvalidOperationException("Kernel: " + error); }
        void Drain()
        {
            for (var i = 0; i < 100; i++)
            {
                var actor = Actor(); Apply(new("poll", actor.Id)); if (actor.Worker.NeedsLoad) Apply(new("load", actor.Id));
                if (actor.Worker.Ready.Contains(target.Id)) Apply(new("turn", actor.Id, S: target.Id));
                else if (!actor.Worker.NeedsFlush && !actor.Worker.NeedsMeet) return;
                if (actor.Worker.NeedsFlush) Apply(new("flush", actor.Id)); if (actor.Worker.NeedsMeet) Apply(new("meet", actor.Id));
            }
            throw new InvalidOperationException("RS0: read proof exceeded bounded drain");
        }
        Frame Frame(string prefix) => State().Frames.Values.Single(f => f.Node == "probe" + prefix);
        void Signal(string name, string? value = null)
        {
            var before = Json.Write(State());
            Apply(new("add", M: new("signal" + ++serial, target, CompositeSignal.Kind, Json.Write(new CompositeSignal(Frame("A").Id, name, value)), TargetCreation: 1))); Drain();
            Check(Json.Write(State()) == before, "RS3: refused or invalid read changed native values, work or allocation");
        }
        void Release(string prefix)
        {
            var wait = world.Store.Subscriptions.Single(s => s.Instance == target.Id && s.Key == "event:read-" + prefix);
            var delivery = Events.Route(new(wait.Key, "release" + ++serial, null, world.Time + 120_000), wait);
            var accepted = world.Store.AddEventAsync(delivery).Result;
            Check(accepted.Added && accepted.Target == target, "RS0: actual scope wait did not accept its registered delivery");
            world.Oracle.Note(delivery.Message); Drain();
        }
        Apply(new("take", Actor().Id)); Apply(new("add", M: new("start", target, Kinds.Start, Start: NativeModel.Spec("read-proof", mode) with { Creation = new(1, 0, "read-proof") }))); Drain();
        var foreign = Values.To<ReadScopeState>(Frame("B").State)!.Inner;
        if (reload)
        {
            var scopes = Json.Write(State().LexicalScopes); Apply(new("crash", owner)); Apply(new("tick", 20_000)); owner = 1 - owner; Apply(new("take", Actor().Id)); Drain();
            Check(Json.Write(State().LexicalScopes) == scopes, "RS3: read scope identity/value changed on cold-owner restart");
        }
        Signal("foreign-live", foreign); Signal("syntax"); Release("B"); Signal("foreign-retired", foreign); Release("A");
        var state = State();
        Check(world.Store.Instances[target.Id].Status == InstanceStatus.Finished && state.LexicalScopes is null && state.Variables["counter"]?.ToString() == "3", "RS6: scope reads leaked resources or changed workflow fallback");
        foreach (var (prefix, item) in new[] { ("A", 5), ("B", 6) })
        {
            var values = state.Variables; var read = values.GetValueOrDefault("composite" + prefix);
            foreach (var (key, expected) in new[] { ("Local", 21), ("Outer", 11), ("Borrow", 17), ("Item", item), ("Global", 13) })
                Check(values.GetValueOrDefault("activity" + key + prefix)?.ToString() == expected.ToString() && read?[key]?.ToString() == expected.ToString(),
                    "RS1: composite read differs from ordinary binding or literal expected scope: " + key);
            Check(read?["Result"]?.ToString() == "23" && read?["Expression"]?.ToString() == (62 + item).ToString(), "RS1: normal result/expression lookup changed");
        }
        if (nested) Check(state.Variables["returned"]?.ToString() == "true", "RS6: nested read composite did not return");
        var records = world.Store.Journal.Values.Where(j => j.Instance == target.Id).SelectMany(j => Json.Read<string[]>(j.Entries)).ToArray();
        Check(records.Contains("read-refused:foreign-live") && records.Contains("read-refused:foreign-retired") && records.Contains("read-error:syntax"), "RS3: refusal/error proof did not execute");
        Console.WriteLine($"PASS lexical:read,{mode},nested={nested},reload={reload}");
    }
}

internal sealed record ReadScopeState(string Outer, string Inner);
internal sealed class ReadWrapper : CompositeActivity
{
    public ReadWrapper() { }
    protected internal override void Execute(CompositeContext context)
    {
        var scope = context.CreateLexicalScope(new Dictionary<string, JsonNode?> { ["borrowedOnly"] = Values.From(17), ["counter"] = Values.From(99) });
        context.SetState(scope);
        context.Schedule(context.Children.Single(), scope: new Dictionary<string, JsonNode?> { ["ancestor"] = Values.From(scope) });
    }
    protected internal override void OnChildCompleted(CompositeContext context, ChildCompleted child)
    { context.ReleaseLexicalScope(context.ReadState<string>()!); context.Complete(result: child.Result); }
}

internal sealed class ReadProbe : CompositeActivity, ISignalledComposite
{
    public ReadProbe() { }
    [Input] public string Prefix { get; set; } = "";
    [Input] public string Ancestor { get; set; } = "";
    protected internal override void Execute(CompositeContext context)
    {
        var outer = context.CreateLexicalScope(new Dictionary<string, JsonNode?> { ["counter"] = Values.From(10), ["outerOnly"] = Values.From(11) }, Ancestor);
        var inner = context.CreateLexicalScope(new Dictionary<string, JsonNode?> { ["counter"] = Values.From(20), ["obj"] = new JsonObject { ["n"] = 7 } }, outer);
        context.SetState(new ReadScopeState(outer, inner)); context.ScheduleInScope(context.Children.Single(), inner);
    }
    public bool CanAccept(CompositeContext context) => true;
    public void Accept(CompositeContext context, string messageId, CompositeSignal signal)
    {
        if (signal.Name.StartsWith("foreign-", StringComparison.Ordinal))
        {
            foreach (var (binding, kind) in new (Binding, string)[] { (Binding.Literal(1), "literal"), (Binding.Result("value" + Prefix), "result"), (Binding.Expr("true"), "expression") })
            {
                try { context.EvaluateInScope(binding, signal.Value!); }
                catch (InvalidOperationException e) when (e.Message == "Lexical scope is missing or inaccessible.") { continue; }
                throw new InvalidOperationException("RS2: invalid scope was read by " + kind);
            }
            context.Record("read-refused:" + signal.Name); return;
        }
        if (signal.Name == "syntax")
        {
            try { context.EvaluateInScope(Binding.Expr("("), context.ReadState<ReadScopeState>()!.Inner); }
            catch (FormatException e) when (e.Message == "Unexpected end of '('.") { context.Record("read-error:syntax"); return; }
            throw new InvalidOperationException("RS4: malformed expression did not preserve its normal error");
        }
        throw new InvalidOperationException("RS0: unexpected read signal");
    }
    protected internal override void OnChildCompleted(CompositeContext context, ChildCompleted child)
    {
        var state = context.ReadState<ReadScopeState>()!;
        JsonNode? Read(Binding binding) => context.EvaluateInScope(binding, state.Inner);
        if (Read(Binding.Var("counter"))?.ToString() != "21") throw new InvalidOperationException("RS1: targeted read lost nearest scope value");
        var copy = Read(Binding.Var("obj"))!; copy["n"] = 99;
        if (Read(Binding.Var("obj"))?["n"]?.ToString() != "7") throw new InvalidOperationException("RS5: variable read leaked a JSON alias");
        var literal = Binding.Literal(new JsonObject { ["n"] = 3 }); Read(literal)!["n"] = 99;
        if (((LiteralBinding)literal).Value?["n"]?.ToString() != "3") throw new InvalidOperationException("RS5: literal read leaked a JSON alias");
        Read(Binding.Result("value" + Prefix))!["n"] = 99;
        if (Read(Binding.Result("value" + Prefix, "n"))?.ToString() != "23") throw new InvalidOperationException("RS5: result read leaked a JSON alias");
        var result = new JsonObject { ["Local"] = Read(Binding.Var("counter")), ["Outer"] = Read(Binding.Var("outerOnly")),
            ["Borrow"] = Read(Binding.Var("borrowedOnly")), ["Item"] = Read(Binding.Var("CurrentItem")), ["Global"] = Read(Binding.Var("workflowOnly")),
            ["Result"] = Read(Binding.Result("value" + Prefix, "n")), ["Expression"] = Read(Binding.Expr("counter + outerOnly + borrowedOnly + CurrentItem + workflowOnly")) };
        context.ReleaseLexicalScope(state.Inner); context.ReleaseLexicalScope(state.Outer); context.Complete(result: result);
    }
}

internal sealed class ReadValue : Kernel.Native.Activity<JsonObject>
{
    public ReadValue() { }
    [Input] public JsonObject Value { get; set; } = new();
    protected override ValueTask<Transition> ExecuteAsync(ActivityContext context) => ValueTask.FromResult(Complete(Value));
}
