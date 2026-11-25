using System.Text.Json.Nodes;
using static Kernel.Native.Binding;
using static Kernel.Native.Wf;

namespace Kernel.Native.Run;

/// <summary>
/// The release 1 activity ports of the old core's control flow and primitives (K6; GA test drive card 3): Switch, For,
/// Do, Break, WriteLines and Fault, each in every mode on the in-memory store.
/// </summary>
internal static class PortChecks
{
    private static IEnumerable<Workflow> Workflows()
    {
        // Switch: a condition that holds selects its case, a later condition is not evaluated, and the outcome routes a Flowchart.
        yield return Workflow("port-switch", Sequence("root",
                Flowchart("flow",
                    [
                        Switch("sw", null,
                            (Case("first", Expr("x == 2")), Set("first", "picked", "first")),
                            (Case("second", Expr("x == 1")), Set("second", "picked", "second")),
                            (Case("never", Expr("'not a Boolean'")), Set("never", "picked", "never"))),
                        Set("routed", "route", "second"),
                    ],
                    [new("sw", "routed", "second")]),
                Set("out", "output", Expr("picked + ' ' + route")), Hold()),
            ("x", 1), ("picked", null), ("route", null), ("output", null));
        // A case without a condition compares Value with its label; no match runs the default; a case with no branch completes.
        yield return Workflow("port-switch-value", Sequence("root",
                Switch("legacy", Var("tier"), (Case("silver"), Set("s", "a", "silver")), (Case("gold"), Set("g", "a", "gold"))),
                Switch("fallback", Var("tier"), Set("d", "b", "default"), (Case("bronze"), Set("bz", "b", "bronze"))),
                Flowchart("flow",
                    [Switch("empty", Var("tier"), (Case("gold"), null)), Set("routed", "c", "gold")],
                    [new("empty", "routed", "gold")]),
                Flowchart("flow2",
                    [Switch("none", Var("tier"), (Case("iron"), Set("i", "d", "iron"))), Set("routed2", "d", "default")],
                    [new("none", "routed2", Switch.DefaultOutcome)]),
                Set("out", "output", Expr("a + ' ' + b + ' ' + c + ' ' + d")), Hold()),
            ("tier", "gold"), ("a", null), ("b", null), ("c", null), ("d", null), ("output", null));
        yield return Workflow("port-switch-invalid", Switch("sw", null, (Case("bad", Expr("'yes'")), Set("s", "a", 1))), ("a", null));

        // For: half-open and closed ranges, a negative step, a step away from the end, and Break through an If.
        yield return Workflow("port-for", Sequence("root",
                For("up", 0, 5, Set("s1", "up", Expr("up + CurrentIndex"))),
                For("closed", 1, 3, Set("s2", "closed", Expr("closed + CurrentIndex")), endInclusive: true),
                For("down", 6, 0, Set("s3", "down", Expr("down + CurrentIndex")), step: -2),
                For("away", 0, 5, Set("s4", "away", 99), step: -1),
                For("broken", 0, 10, Sequence("body", If("stop", Expr("CurrentIndex == 2"), Break("brk")), Set("s5", "broken", Expr("broken + CurrentIndex")))),
                Set("out", "output", Expr("up + ' ' + closed + ' ' + down + ' ' + away + ' ' + broken")), Hold()),
            ("up", 0), ("closed", 0), ("down", 0), ("away", 0), ("broken", 0), ("output", null));
        yield return Workflow("port-for-zero", For("zero", 0, 5, Set("s", "a", 1), step: 0), ("a", null));
        // Each pass of a For is its own activation: an outbound effect in the body gets a key per index.
        yield return Workflow("port-for-effects", For("each", 0, 3, Email("mail", "ops@example.com", Expr("'pass ' + CurrentIndex"), "body")));

        // Do: the body runs once before the condition is read, then while it holds; Break ends it without reading it.
        yield return Workflow("port-do", Sequence("root",
                Do("once", false, Set("o1", "once", Expr("once + 1"))),
                Do("count", Expr("count < 3"), Set("o2", "count", Expr("count + 1"))),
                Do("broken", true, Sequence("body", Set("o3", "broken", Expr("broken + 1")), If("stop", Expr("broken == 2"), Break("brk")))),
                Set("out", "output", Expr("once + ' ' + count + ' ' + broken")), Hold()),
            ("once", 0), ("count", 0), ("broken", 0), ("output", null));

        // Break through a Switch and through a Flowchart, inside ForEach and While.
        yield return Workflow("port-break", Sequence("root",
                ForEach("each", Expr("[1, 2, 3]"), Sequence("body",
                    Switch("sw", Expr("'' + CurrentItem"), (Case("2"), Break("brk1"))),
                    Set("s1", "seen", Expr("seen + CurrentItem")))),
                While("loop", true, Flowchart("flow",
                    [Set("inc", "n", Expr("n + 1")), If("decide", Expr("n == 3")), Break("brk2")],
                    [new("inc", "decide"), new("decide", "brk2", "True")])),
                Set("out", "output", Expr("seen + ' ' + n")), Hold()),
            ("seen", 0), ("n", 0), ("output", null));

        foreach (var child in new[] { "port-switch-invalid", "port-for-zero", "port-fault", "port-fault-default" })
            yield return Workflow("port-parent-" + child, Sequence("root", Dispatch("child", child, wait: true).Capture("result"), Hold()), ("result", null));

        yield return Workflow("port-lines", Sequence("root", WriteLines("lines", Expr("['one', 'two']")), WriteLines("none", Literal(null))));
        yield return Workflow("port-fault", Fault("f", "order.rejected", "The order was rejected.", "business", "Validation"));
        yield return Workflow("port-fault-default", Fault("f", " ", null));
    }

    // A wait that never ends keeps an Ephemeral instance, and its state, in memory to be read.
    private static Node Hold() => Event("hold", "never");

    internal static async Task RunAsync(Action<bool, string> check)
    {
        Console.WriteLine("activity ports: Switch, For, Do, Break, WriteLines, Fault");
        var registry = new WorkflowRegistry();
        foreach (var workflow in Workflows()) registry.Add(workflow);

        check(Refused(() => Workflow("dupe", Switch("sw", null, (Case("a"), null), (Case("a"), null)))) &&
              Refused(() => Workflow("blank", Switch("sw", null, (Case(""), null)))) &&
              Refused(() => Workflow("stray", Node<Switch>("sw").With(Set("s", "a", 1).In("Case:missing")))) &&
              Refused(() => Workflow("twice", Node<Switch>("sw").With(Set("s1", "a", 1).In("Default"), Set("s2", "a", 2).In("Default")))),
            "a Switch with a repeated or empty label, or a branch outside a case's slot or twice in one, is refused when it is registered");

        var original = WriteLine.Out;
        var lines = new StringWriter();
        WriteLine.Out = lines;
        try
        {
            foreach (var mode in new[] { Modes.Ephemeral, Modes.Balanced, Modes.Durable })
            {
                var outbound = new List<OutboundSending>();
                var store = new InMemoryStore(1, () => Environment.TickCount64);
                var owner = new Owner("ports", 0, store, registry, new Network(), native: new NativeOptions { OnOutbound = outbound.Add });
                await owner.StartAsync();

                async Task<NativeState?> Run(string workflow)
                {
                    var id = $"{workflow}-{mode}";
                    owner.Worker.Post(new Message($"{id}#start", InstanceRef.In(0, id), Kinds.Start, Start: NativeModel.Spec(workflow, mode)), fresh: true);
                    await owner.PumpAsync();
                    return owner.State($"0/{id}");
                }
                string? Output(NativeState? state) => state?.Variables.GetValueOrDefault("output")?.GetValue<string>();

                var picked = Output(await Run("port-switch"));
                check(picked == "second second", $"{mode}: Switch runs the first case whose condition holds, routes on its label, and reads no later condition ('{picked}')");
                var value = Output(await Run("port-switch-value"));
                check(value == "gold default gold default",
                    $"{mode}: a case without a condition matches Value; no match runs the default or completes with Default; an empty case completes with its label ('{value}')");
                var loops = Output(await Run("port-for"));
                check(loops == "10 6 12 0 1", $"{mode}: For walks [0,5), [1,3], 6 down by 2, nothing for a step away from the end, and stops on Break ('{loops}')");
                var passes = Output(await Run("port-do"));
                check(passes == "1 3 2", $"{mode}: Do runs once on a false condition, repeats while it holds, and stops on Break ('{passes}')");
                var broken = Output(await Run("port-break"));
                check(broken == "1 3", $"{mode}: Break ends ForEach through a Switch and While through a Flowchart ('{broken}')");

                await Run("port-for-effects");
                var mails = outbound.Where(o => o.Effect is EmailSendEffect && o.Instance.Ref.Id == $"0/port-for-effects-{mode}").ToList();
                var keys = mails.Select(m => EffectKey.For(m.Instance, m.Node, m.IterationPath, m.Attempt)).Distinct().Count();
                check(mails.Count == 3 && keys == 3, $"{mode}: each For pass is its own activation, with its own effect key ({mails.Count} mails, {keys} keys)");

                lines.GetStringBuilder().Clear();
                await Run("port-lines");
                check(lines.ToString() == $"one{Environment.NewLine}two{Environment.NewLine}", $"{mode}: WriteLines writes each line in order and nothing for none");

                // A parent that waits for the child reads its terminal status and the fault it ended with.
                async Task<DispatchResult?> Child(string workflow)
                {
                    var state = await Run("port-parent-" + workflow);
                    return Values.To<DispatchResult>(state?.Variables.GetValueOrDefault("result"));
                }
                ActivityFault? FaultOf(DispatchResult? result) => Values.To<ActivityFault>(result?.Output);

                var invalid = await Child("port-switch-invalid");
                check(invalid?.Status == "Faulted" && FaultOf(invalid)?.Code == "switch.condition.invalid",
                    $"{mode}: a reached condition that is not a Boolean faults the Switch ({invalid?.Status} {FaultOf(invalid)?.Code})");
                var zero = await Child("port-for-zero");
                check(zero?.Status == "Faulted" && FaultOf(zero)?.Code == "for.step.invalid", $"{mode}: a zero step faults the For ({zero?.Status} {FaultOf(zero)?.Code})");
                var authored = await Child("port-fault");
                check(authored?.Status == "Faulted" && FaultOf(authored) is { Code: "order.rejected", Message: "The order was rejected.", Category: "business", FaultType: "Validation" },
                    $"{mode}: Fault ends the instance with the authored code, message, category and fault type ({authored?.Output?.ToJsonString()})");
                var blank = await Child("port-fault-default");
                check(FaultOf(blank) is { Code: Fault.DefaultCode, Message: Fault.DefaultMessage, Category: null, FaultType: null } &&
                      blank!.Output is JsonObject o && !o.ContainsKey("Category") && !o.ContainsKey("FaultType"),
                    $"{mode}: a blank code or message takes the defaults, and a fault without a category or fault type writes neither ({blank?.Output?.ToJsonString()})");
            }
        }
        finally
        {
            WriteLine.Out = original;
        }
    }

    private static bool Refused(Action register)
    {
        try { register(); return false; }
        catch (InvalidOperationException) { return true; }
    }
}
