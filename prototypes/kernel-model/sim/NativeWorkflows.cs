using Kernel.Native;
using static Kernel.Native.Binding;
using static Kernel.Native.Wf;

namespace Kernel.Sim.Native;

/// <summary>
/// The native workflows of the simulation's mix. Their final variables do not depend on the order in which events,
/// timers, children and job results arrive, so a run with faults can be compared with a run without.
/// </summary>
public static class NativeWorkflows
{
    public static readonly WorkflowRegistry Registry = Build();

    /// <summary>The workflows the generator starts, with the events each waits for (by the key in its input).</summary>
    public static readonly (string Workflow, int Events)[] Mix =
    [
        ("n-seq", 0), ("n-join", 3), ("n-join2", 3), ("n-flow", 2), ("n-dispatch", 0), ("n-loop", 0), ("n-pub", 0), ("n-graph", 1),
    ];

    /// <summary>The workflows with a Flowchart, whose committed states the graph checks read.</summary>
    public static readonly HashSet<string> Graphs = new(StringComparer.Ordinal) { "n-flow", "n-graph" };

    private static WorkflowRegistry Build()
    {
        Node Branch(int i) => Sequence($"b{i}", Event($"e{i}", Expr($"input.key + '-{i}'")), Set($"h{i}", $"hit{i}", true), Set($"n{i}", "hits", Expr("hits + 1")));
        var registry = new WorkflowRegistry();
        registry.Add(Workflow("n-seq", Sequence("root",
                WriteLine("w1", "one"), Set("s1", "count", Expr("count + 1")), WriteLine("w2", Expr("'count ' + count")),
                Notify("notify", Expr("'hello ' + input.key")), Set("s2", "count", Expr("count + 1")),
                If("if", Expr("count == 2"), Set("then", "branch", "then"), Set("else", "branch", "else")),
                Set("out", "output", Expr("branch + ' ' + count"))),
            ("count", 0), ("branch", null), ("output", null)));
        registry.Add(Workflow("n-join", Sequence("root", Parallel("par", null, Branch(1), Branch(2), Branch(3)), Set("out", "output", Expr("'joined ' + hits"))),
            ("hits", 0), ("hit1", false), ("hit2", false), ("hit3", false), ("output", null)));
        // Two of three: the third branch is cancelled. Which one depends on the order of events, so only the count is compared.
        registry.Add(Workflow("n-join2", Sequence("root",
                Parallel("par", 2,
                    Sequence("b1", Event("e1", Expr("input.key + '-1'")), Set("n1", "hits", Expr("hits + 1"))),
                    Sequence("b2", Event("e2", Expr("input.key + '-2'")), Set("n2", "hits", Expr("hits + 1"))),
                    Sequence("b3", Event("e3", Expr("input.key + '-3'")), Set("n3", "hits", Expr("hits + 1")))),
                Set("out", "output", Expr("'joined ' + hits"))),
            ("hits", 0), ("output", null)));
        registry.Add(Workflow("n-flow", Sequence("root",
                Flowchart("flow",
                    [WriteLine("f1", "start"), Event("f2", Expr("input.key + '-1'")), Event("f3", Expr("input.key + '-2'")), Set("f4", "joined", Expr("joined + 1"))],
                    [new("f1", "f2"), new("f1", "f3"), new("f2", "f4"), new("f3", "f4")], ("f4", Gateway.ParallelJoin)),
                Set("out", "output", Expr("'flow ' + joined"))),
            ("joined", 0), ("output", null)));
        // The graph engine's shapes (NATIVE.md, section 6): a loop from the start node with a fork and an implicit join
        // in its body (a join per round), the decision that leaves it (its untaken side is a dead path every round), a
        // first-wins race between an event and a timer (the loser is cancelled; the join behind it is released), and an
        // All join after a decision (it fires on one live and one dead arrival). Which racer wins depends on the run, so
        // only the variables are compared.
        registry.Add(Workflow("n-graph", Sequence("root",
                Flowchart("graph",
                [
                    Set("head", "round", Expr("round + 1")), Delay("a", 100), Set("b", "hits", Expr("hits + 1")), WriteLine("join", "joined"),
                    If("decide", Expr("round < 3")), WriteLine("race", "race"), Event("r1", Expr("input.key + '-1'")), Delay("r2", 300),
                    WriteLine("raced", "raced"), If("check", Expr("hits == 3")), Set("yes", "verdict", "ok"), Set("no", "verdict", "bad"), Set("end", "done", "yes"),
                ],
                [
                    new("head", "a"), new("head", "b"), new("a", "join"), new("b", "join"), new("join", "decide"),
                    new("decide", "head", "True"), new("decide", "race", "False"),
                    new("race", "r1"), new("race", "r2"), new("r1", "raced"), new("r2", "raced"),
                    new("raced", "check"), new("check", "yes", "True"), new("check", "no", "False"), new("yes", "end"), new("no", "end"),
                ],
                ("race", Gateway.FirstWins), ("end", Gateway.ParallelJoin)),
                Set("out", "output", Expr("verdict + ' ' + done + ' ' + round + ' ' + hits"))),
            ("round", 0), ("hits", 0), ("verdict", null), ("done", "no"), ("output", null)));
        registry.Add(Workflow("n-dispatch", Sequence("root",
                Parallel("par", null,
                    Dispatch("c1", "n-child", wait: true).Capture("first", "Output"),
                    Dispatch("c2", "n-child", wait: true).Capture("second", "Output")),
                Dispatch("c3", "n-child", wait: false),
                Set("out", "output", Expr("first + ' and ' + second"))),
            ("first", null), ("second", null), ("output", null)));
        registry.Add(Workflow("n-child", Sequence("root", Delay("d", 400), Set("o", "output", "child")), ("output", null)));
        registry.Add(Workflow("n-loop", Sequence("root",
                ForEach("each", Expr("[1, 2, 3]"), Sequence("body", Delay("d", 300), Set("add", "sum", Expr("sum + CurrentItem * 10 + CurrentIndex")))),
                While("loop", Expr("i < 2"), Set("inc", "i", Expr("i + 1"))),
                Call("svc", "pricing").Capture("quote"),
                Set("out", "output", Expr("sum + ' ' + i + ' ' + quote"))),
            ("sum", 0), ("i", 0), ("quote", null), ("output", null)));
        registry.Add(Workflow("n-pub", Sequence("root",
                Publish("p1", Expr("input.target + '-1'")), Publish("p2", Expr("input.target + '-2'")),
                Set("out", "output", "published")),
            ("output", null)));
        return registry;
    }
}
