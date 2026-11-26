using System.Text.Json.Nodes;
using static Kernel.Native.Binding;
using static Kernel.Native.Wf;

namespace Kernel.Native.Run;

/// <summary>
/// The release 1 port of the old core's GraphActivity and Complete (K6; GA test drive card 3): a reusable activity's
/// boundary, its inputs, variables and outputs, its outcome mappings, and the Complete exit from any depth of its body.
/// </summary>
internal static class GraphChecks
{
    private static readonly GraphBoundary Pricing = new()
    {
        Inputs = [new("amount", Required: true), new("rate", Default: Literal(2))],
        Variables = [new("total", Expr("amount * rate"))],
        Outputs = [new("total", Var("total"))],
        OutcomeMappings = new Dictionary<string, string> { ["Done"] = "Priced" },
    };

    private static readonly GraphBoundary Approval = new()
    {
        CompleteOutcomes = new Dictionary<string, string> { ["approve"] = "Approved", ["reject"] = "Rejected" },
        Outputs = [new("steps", Var("steps"))],
        Variables = [new("steps", Literal(0))],
    };

    private static IEnumerable<Workflow> Workflows()
    {
        // The body ends by itself: its outcome is mapped, its variables start from its inputs and its outputs are its result.
        // A body write to a name the boundary declares stays in the boundary.
        yield return Workflow("graph-natural", Sequence("root",
                Flowchart("flow",
                    [
                        GraphActivity("price", Pricing, Sequence("body", Set("inc", "total", Expr("total + 1")))
                            , ("amount", Var("amount"))).Capture("priced", "total"),
                        Set("routed", "route", "Priced"),
                    ],
                    [new("price", "routed", "Priced")]),
                Set("out", "output", Expr("priced + ' ' + route + ' ' + total")), Hold()),
            ("amount", 5), ("priced", null), ("route", null), ("total", 100), ("output", null));

        // A Complete deep in the body, inside a Parallel branch, ends the boundary: the other branch's wait goes and nothing
        // after the Complete runs.
        yield return Workflow("graph-exit", Sequence("root",
                Flowchart("flow",
                    [
                        GraphActivity("approve", Approval, Sequence("body",
                            Set("s1", "steps", Expr("steps + 1")),
                            If("check", true, Parallel("par", null,
                                Sequence("b1", Set("s2", "steps", Expr("steps + 1")), Complete("done", "approve"), Set("after", "late", "ran")),
                                Event("other", "graph-body-wait"))),
                            Set("tail", "late", "tail ran"))).Capture("steps", "steps"),
                        Set("routed", "route", "Approved"),
                    ],
                    [new("approve", "routed", "Approved")]),
                Set("out", "output", Expr("steps + ' ' + route + ' ' + late")), Hold()),
            ("steps", null), ("route", null), ("late", "no"), ("output", null));

        // A Complete inside a loop ends the boundary, not only the loop; a nested boundary is its own.
        yield return Workflow("graph-nested", Sequence("root",
                GraphActivity("outer", new GraphBoundary
                {
                    CompleteOutcomes = new Dictionary<string, string> { ["stop"] = "Stopped" },
                    Variables = [new("between", Literal("no")), new("count", Literal(0))],
                    Outputs = [new("between", Var("between")), new("count", Var("count"))],
                }, Sequence("outer-body",
                    GraphActivity("inner", new GraphBoundary { CompleteOutcomes = new Dictionary<string, string> { ["leave"] = "Left" } },
                        While("loop", true, Complete("leave", "leave"))).Capture("inner"),
                    Set("between", "between", "ran"),
                    ForEach("each", Expr("[1, 2, 3]"), Sequence("pass", Set("count", "count", Expr("count + 1")), Complete("stop", "stop"))))).Capture("between", "between").Capture("count", "count"),
                Set("out", "output", Expr("between + ' ' + count")), Hold()),
            ("between", "no"), ("count", 0), ("output", null), ("inner", null));

        // The body is closed off: it reads nothing it does not declare, its writes of undeclared names stay inside, and a
        // nested boundary sees nothing of the one around it.
        yield return Workflow("graph-closed", Sequence("root",
                GraphActivity("g", new GraphBoundary
                {
                    Variables = [new("seen"), new("local", Literal("mine")), new("deep")],
                    Outputs = [new("seen", Var("seen")), new("deep", Var("deep")), new("kept", Var("secret"))],
                }, Sequence("body",
                    Set("read", "seen", Expr("'' + secret")),
                    Set("write", "secret", "inner"),
                    GraphActivity("inner", new GraphBoundary { Outputs = [new("local", Var("local"))] }, Set("noop", "x", 1)).Capture("deep", "local"))).Capture("result"),
                Set("out", "output", Expr("secret + ' ' + result.seen + ' ' + result.kept + ' ' + result.deep")), Hold()),
            ("secret", "outer"), ("result", null), ("output", null));

        // A Complete that is the whole body; two in racing branches, the first to run winning; and a nested boundary whose
        // body still waits, ended with the outer one.
        yield return Workflow("graph-edges", Sequence("root",
                GraphActivity("whole", Approval, Complete("only", "reject")),
                GraphActivity("race", Approval, Parallel("par", null, Complete("first", "approve"), Complete("second", "reject"))),
                GraphActivity("outer", new GraphBoundary { CompleteOutcomes = new Dictionary<string, string> { ["done"] = "Done" } },
                    Parallel("par2", null,
                        GraphActivity("inner", new GraphBoundary { Variables = [new("v", Literal(1))] }, Delay("wait", 60_000)),
                        Complete("done", "done"))),
                Flowchart("routes",
                    [Node<SetVariable>("mark", ("Name", "r"), ("Value", "x")), GraphActivity("again-whole", Approval, Complete("only2", "reject")),
                        GraphActivity("again-race", Approval, Parallel("par3", null, Complete("first2", "approve"), Complete("second2", "reject"))),
                        Set("rej", "whole", "Rejected"), Set("app", "race", "Approved")],
                    [new("mark", "again-whole"), new("again-whole", "rej", "Rejected"), new("rej", "again-race"), new("again-race", "app", "Approved")]),
                Set("out", "output", Expr("whole + ' ' + race")), Hold()),
            ("whole", null), ("race", null), ("r", null), ("output", null));

        // Faults, each read through a waiting parent.
        yield return Workflow("graph-no-exit", GraphActivity("g", Approval, Set("s", "steps", 1)));
        yield return Workflow("graph-unmapped-outcome", GraphActivity("g", new GraphBoundary { OutcomeMappings = new Dictionary<string, string> { ["Done"] = "Ok" } }, Break("brk")));
        yield return Workflow("graph-required", GraphActivity("g", Pricing, Set("s", "x", 1)));
        yield return Workflow("graph-unknown", GraphActivity("g", Approval, Complete("c", Expr("'maybe'"))));
        foreach (var child in new[] { "graph-no-exit", "graph-unmapped-outcome", "graph-required", "graph-unknown" })
            yield return Workflow("graph-parent-" + child, Sequence("root", Dispatch("child", child, wait: true).Capture("result"), Hold()), ("result", null));
    }

    // A wait that never ends keeps an Ephemeral instance, and its state, in memory to be read.
    private static Node Hold() => Event("hold", "never");

    internal static async Task RunAsync(Action<bool, string> check)
    {
        Console.WriteLine("activity ports: GraphActivity and Complete");
        var registry = new WorkflowRegistry();
        foreach (var workflow in Workflows()) registry.Add(workflow);

        check(Refused(() => Workflow("outside", Sequence("root", Complete("c", "approve")))),
            "a Complete outside every GraphActivity is refused when it is registered");
        check(Refused(() => Workflow("undeclared", GraphActivity("g", Approval, Complete("c", "maybe")))),
            "a Complete whose literal outcome its boundary does not declare is refused when it is registered");
        check(Refused(() => Workflow("two", Node<GraphActivity>("g").With(Set("a", "x", 1), Set("b", "x", 2)))) &&
              Refused(() => Workflow("bare", Node<GraphActivity>("g").With(Set("a", "x", 1)))) &&
              Refused(() => Workflow("stray", Node<Sequence>("seq").Declaring(Approval))) &&
              Refused(() => Workflow("clash", GraphActivity("g", new GraphBoundary { Inputs = [new("x")], Variables = [new("x")] }, Set("a", "x", 1)))),
            "a GraphActivity without a declaration, with other than one body, or with an input and a variable of one name, and a declaration on another node, are refused");
        check(Refused(() => Workflow("typo", GraphActivity("g", Pricing, Set("s", "x", 1), ("amout", 5)))) &&
              Refused(() => Workflow("blank-key", GraphActivity("g", new GraphBoundary { CompleteOutcomes = new Dictionary<string, string> { [" "] = "X" } }, Set("s", "x", 1)))) &&
              Refused(() => Workflow("cased", GraphActivity("g", new GraphBoundary { Inputs = [new("a"), new("A")] }, Set("s", "x", 1)))) &&
              Refused(() => Workflow("spaces", GraphActivity("g", new GraphBoundary { OutcomeMappings = new Dictionary<string, string> { ["Done"] = " " } }, Set("s", "x", 1)))) &&
              Refused(() => Workflow("captured", GraphActivity("g", Approval, Complete("c", "approve").Capture("x")))),
            "an input bound under a name the boundary does not declare, inputs that differ only by case, a blank outcome or key, and a capture on a Complete are refused");

        foreach (var mode in new[] { Modes.Ephemeral, Modes.Balanced, Modes.Durable })
        {
            var store = new InMemoryStore(1, () => Environment.TickCount64);
            var owner = new Owner("graph", 0, store, registry, new Network());
            await owner.StartAsync();

            async Task<NativeState?> Run(string workflow)
            {
                var id = $"{workflow}-{mode}";
                owner.Worker.Post(new Message($"{id}#start", InstanceRef.In(0, id), Kinds.Start, Start: NativeModel.Spec(workflow, mode)), fresh: true);
                await owner.PumpAsync();
                return owner.State($"0/{id}");
            }
            string? Output(NativeState? state) => state?.Variables.GetValueOrDefault("output")?.GetValue<string>();

            var natural = Output(await Run("graph-natural"));
            check(natural == "11 Priced 100",
                $"{mode}: the body's variables start from its inputs and a default, its outcome is mapped, its output is its result, and its writes stay inside ('{natural}')");
            var exit = await Run("graph-exit");
            check(Output(exit) == "2 Approved no",
                $"{mode}: a Complete inside a Parallel branch ends the GraphActivity on its outcome, with its outputs, and nothing after it runs ('{Output(exit)}')");
            check(exit?.Waits.Values.Select(w => w.Key).SequenceEqual(["event:never"]) == true && exit.Frames.Count == 2,
                $"{mode}: the other branch's wait, and the body's frames are gone; only the hold waits ({string.Join(", ", exit?.Waits.Values.Select(w => w.Key) ?? [])})");
            var nested = await Run("graph-nested");
            check(Output(nested) == "ran 1" && nested?.Variables["inner"] is null,
                $"{mode}: a Complete in a loop ends its own boundary, the nested one, and the outer one goes on ('{Output(nested)}')");
            var closed = Output(await Run("graph-closed"));
            check(closed == "outer  inner ",
                $"{mode}: the body reads nothing undeclared, its write of an undeclared name stays in its own variables, and a nested boundary sees nothing of the outer one ('{closed}')");
            var edges = await Run("graph-edges");
            check(Output(edges) == "Rejected Approved" && edges?.Waits.Values.Select(w => w.Key).SequenceEqual(["event:never"]) == true && edges.Frames.Count == 2,
                $"{mode}: a Complete as the whole body, the first of two racing ones, and a nested boundary that waits ended with the outer one ('{Output(edges)}')");

            async Task<DispatchResult?> Child(string workflow) => Values.To<DispatchResult>((await Run("graph-parent-" + workflow))?.Variables.GetValueOrDefault("result"));
            string? Code(DispatchResult? result) => Values.To<ActivityFault>(result?.Output)?.Code;
            var noExit = await Child("graph-no-exit");
            check(noExit?.Status == "Faulted" && Code(noExit) == GraphActivity.EntryCompletedWithoutMappingFaultCode,
                $"{mode}: a body that ends without reaching a Complete faults a boundary whose outcomes all come from Completes ({Code(noExit)})");
            var unmapped = await Child("graph-unmapped-outcome");
            check(unmapped?.Status == "Faulted" && Code(unmapped) == GraphActivity.OutcomeUnmappedFaultCode,
                $"{mode}: a body outcome no mapping covers faults the boundary ({Code(unmapped)})");
            var required = await Child("graph-required");
            check(required?.Status == "Faulted" && Code(required) == GraphActivity.InputRequiredFaultCode,
                $"{mode}: a required input left unbound faults the boundary before its body runs ({Code(required)})");
            var unknown = await Child("graph-unknown");
            check(unknown?.Status == "Faulted" && Code(unknown) == GraphActivity.CompleteOutcomeUnknownFaultCode,
                $"{mode}: a computed Complete outcome the boundary does not declare faults it ({Code(unknown)})");
        }
    }

    private static Node Declaring(this Node node, GraphBoundary graph)
    {
        node.Graph = graph;
        return node;
    }

    private static bool Refused(Action register)
    {
        try { register(); return false; }
        catch (InvalidOperationException) { return true; }
    }
}
