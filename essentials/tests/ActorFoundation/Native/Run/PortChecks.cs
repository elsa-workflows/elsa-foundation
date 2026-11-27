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
        yield return Workflow("port-switch-null", Switch("sw", null, (Case("missing", Var("nowhere")), Set("s", "a", 1))), ("a", null));
        yield return Workflow("port-switch-number", Switch("sw", Literal(2), (Case("2"), Set("s", "a", 1))), ("a", null));
        // A Value is read only on reaching a case without a condition: a number beside matching conditions does not fault.
        yield return Workflow("port-switch-unread", Sequence("root", Switch("sw", Literal(2), (Case("yes", Literal(true)), Set("s", "output", "matched")), (Case("2"), null)), Hold()), ("output", null));
        // An empty label is a label: the old core allows it, and an empty Value matches it.
        yield return Workflow("port-switch-blank", Sequence("root", Switch("sw", Literal(""), (Case(""), Set("s", "output", "blank"))), Hold()), ("output", null));

        // For: half-open and closed ranges, a negative step, a step away from the end, and Break through an If.
        yield return Workflow("port-for", Sequence("root",
                For("up", 0, 5, Set("s1", "up", Expr("up + index"))),
                For("closed", 1, 3, Set("s2", "closed", Expr("closed + index")), endInclusive: true),
                For("down", 6, 0, Set("s3", "down", Expr("down + index")), step: -2),
                For("away", 0, 5, Set("s4", "away", 99), step: -1),
                For("pinned", 0, Var("n"), Set("s6", "n", Expr("n + 1"))),
                For("downto", 3, 1, Set("s7", "downto", Expr("downto + index")), step: -1, endInclusive: true),
                For("broken", 0, 10, Sequence("body", If("stop", Expr("index == 2"), Break("brk")), Set("s5", "broken", Expr("broken + index")))),
                Set("out", "output", Expr("up + ' ' + closed + ' ' + down + ' ' + away + ' ' + n + ' ' + downto + ' ' + broken")), Hold()),
            ("up", 0), ("closed", 0), ("down", 0), ("away", 0), ("n", 3), ("downto", 0), ("broken", 0), ("output", null));
        yield return Workflow("port-for-zero", For("zero", 0, 5, Set("s", "a", 1), step: 0), ("a", null));
        // Each pass of a For is its own activation: an outbound effect in the body gets a key per index.
        yield return Workflow("port-for-effects", For("each", 0, 3, Email("mail", "ops@example.com", Expr("'pass ' + index"), "body")));
        yield return Workflow("port-do-effects", Do("each", Expr("count < 3"), Sequence("body", Set("inc", "count", Expr("count + 1")), Email("mail", "ops@example.com", "pass", "body"))), ("count", 0));

        // Bodies that wait, so that each loop and the Switch resume from committed state in later turns.
        yield return Workflow("port-suspend", Sequence("root",
                For("for", 0, 2, Sequence("fb", Delay("fd", 5), Set("fs", "f", Expr("f + index")))),
                Do("do", Expr("d < 2"), Sequence("db", Delay("dd", 5), Set("ds", "d", Expr("d + 1")))),
                Switch("sw", Var("tier"), (Case("gold"), Sequence("sb", Delay("sd", 5), Set("ss", "s", "gold")))),
                Set("out", "output", Expr("f + ' ' + d + ' ' + s")), Hold()),
            ("f", 0), ("d", 0), ("s", null), ("tier", "gold"), ("output", null));

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
                    Switch("sw", Expr("'' + currentItem"), (Case("2"), Break("brk1"))),
                    Set("s1", "seen", Expr("seen + currentItem")))),
                While("loop", true, Flowchart("flow",
                    [Set("inc", "n", Expr("n + 1")), If("decide", Expr("n == 3")), Break("brk2")],
                    [new("inc", "decide"), new("decide", "brk2", "True")])),
                // An If in a Flowchart whose branch breaks ends the Flowchart, and the loop, on Break.
                While("loop2", true, Flowchart("flow2",
                    [Set("inc2", "m", Expr("m + 1")), If("decide2", Expr("m == 2"), Break("brk3"))],
                    [new("inc2", "decide2")])),
                Set("out", "output", Expr("seen + ' ' + n + ' ' + m")), Hold()),
            ("seen", 0), ("n", 0), ("m", 0), ("output", null));

        foreach (var child in new[] { "port-switch-invalid", "port-switch-null", "port-switch-number", "port-for-zero", "port-lines-invalid", "port-fault", "port-fault-default" })
            yield return Workflow("port-parent-" + child, Sequence("root", Dispatch("child", child, wait: true).Capture("result"), Hold()), ("result", null));

        yield return Workflow("port-lines", Sequence("root", WriteLines("lines", Literal(new JsonArray("one", 2, true, null))), WriteLines("none", Literal(null))));
        yield return Workflow("port-lines-invalid", WriteLines("lines", Expr("[['nested']]")));
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
              Refused(() => Workflow("cases", Node<Sequence>("seq").Also(n => n.Cases.Add(Case("a"))))) &&
              Refused(() => Workflow("stray", Node<Switch>("sw").With(Set("s", "a", 1).In("Case:missing")))) &&
              Refused(() => Workflow("twice", Node<Switch>("sw").With(Set("s1", "a", 1).In("Default"), Set("s2", "a", 2).In("Default")))),
            "a Switch with a repeated label or a branch outside a case's slot or twice in one, and cases on another node, are refused when it is registered");

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
                check(loops == "10 6 12 0 6 6 1",
                    $"{mode}: For walks [0,5), [1,3], 6 down by 2, nothing for a step away from the end, a range pinned at the start, [3,1] down by 1, and stops on Break ('{loops}')");
                var passes = Output(await Run("port-do"));
                check(passes == "1 3 2", $"{mode}: Do runs once on a false condition, repeats while it holds, and stops on Break ('{passes}')");
                var broken = Output(await Run("port-break"));
                check(broken == "1 3 2", $"{mode}: Break ends ForEach through a Switch, and While through a Flowchart, from a node and from an If's branch ('{broken}')");
                var unread = Output(await Run("port-switch-unread"));
                check(unread == "matched", $"{mode}: a Value that is not a string is not read when a condition matches first ('{unread}')");
                var blankLabel = Output(await Run("port-switch-blank"));
                check(blankLabel == "blank", $"{mode}: an empty label is a label, and an empty Value matches it ('{blankLabel}')");

                await Run("port-for-effects");
                var mails = outbound.Where(o => o.Effect is EmailSendEffect && o.Instance.Ref.Id == $"0/port-for-effects-{mode}").ToList();
                var keys = mails.Select(m => EffectKey.For(m.Instance, m.Node, m.IterationPath, m.Attempt)).Distinct().Count();
                check(mails.Count == 3 && keys == 3, $"{mode}: each For pass is its own activation, with its own effect key ({mails.Count} mails, {keys} keys)");
                await Run("port-do-effects");
                var doMails = outbound.Where(o => o.Effect is EmailSendEffect && o.Instance.Ref.Id == $"0/port-do-effects-{mode}").ToList();
                var doKeys = doMails.Select(m => EffectKey.For(m.Instance, m.Node, m.IterationPath, m.Attempt)).Distinct().Count();
                check(doMails.Count == 3 && doKeys == 3, $"{mode}: each Do pass is its own activation, with its own effect key ({doMails.Count} mails, {doKeys} keys)");

                var suspendId = $"port-suspend-{mode}";
                owner.Worker.Post(new Message($"{suspendId}#start", InstanceRef.In(0, suspendId), Kinds.Start, Start: NativeModel.Spec("port-suspend", mode)), fresh: true);
                await owner.PumpAsync(poll: true, keepPolling: true, until: () => Output(owner.State($"0/{suspendId}")) is not null);
                var resumed = Output(owner.State($"0/{suspendId}"));
                check(resumed == "1 2 gold", $"{mode}: For, Do and Switch go on from committed state when their bodies wait across turns ('{resumed}')");

                lines.GetStringBuilder().Clear();
                await Run("port-lines");
                check(lines.ToString() == string.Join("", new[] { "one", "2", "true", "" }.Select(l => l + Environment.NewLine)),
                    $"{mode}: WriteLines writes each line in order, a number or Boolean as its JSON text, null as an empty line, and nothing for none");

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
                var nullCondition = await Child("port-switch-null");
                check(nullCondition?.Status == "Faulted" && FaultOf(nullCondition)?.Code == "switch.condition.invalid",
                    $"{mode}: a condition that evaluates to null faults, as on the old core ({FaultOf(nullCondition)?.Code})");
                var number = await Child("port-switch-number");
                check(number?.Status == "Faulted" && FaultOf(number)?.Code == "switch.value.invalid", $"{mode}: a Value that is not a string faults the Switch ({FaultOf(number)?.Code})");
                var badLines = await Child("port-lines-invalid");
                check(badLines?.Status == "Faulted" && FaultOf(badLines)?.Code == "writelines.lines.invalid", $"{mode}: a line that is not a scalar faults WriteLines ({FaultOf(badLines)?.Code})");
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

    private static Node Also(this Node node, Action<Node> change)
    {
        change(node);
        return node;
    }

    private static bool Refused(Action register)
    {
        try { register(); return false; }
        catch (InvalidOperationException) { return true; }
    }
}
