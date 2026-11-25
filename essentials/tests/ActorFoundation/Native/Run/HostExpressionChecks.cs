using System.Text.Json.Nodes;
using static Kernel.Native.Binding;
using static Kernel.Native.Wf;

namespace Kernel.Native.Run;

/// <summary>
/// Host-evaluated expressions (K10; owner decision of 25 November: JavaScript and Liquid through the host's portable
/// evaluator), here on a stand-in evaluator that runs named programs: parameters, ambient variables, the instance's
/// facts, one session per burst, and failures where an activity starts and where a composite reads its inputs again.
/// </summary>
internal static class HostExpressionChecks
{
    private sealed class Evaluator : INativeExpressions
    {
        public int Opened, Closed, Named;

        public INativeExpressionSession Open(CancellationToken cancellationToken)
        {
            Opened++;
            return new Session(this);
        }

        private sealed class Session(Evaluator owner) : INativeExpressionSession
        {
            private int _counts;

            public JsonNode? Evaluate(HostExpression expression, IReadOnlyDictionary<string, JsonNode?> parameters, IReadOnlyDictionary<string, JsonNode?> variables) =>
                expression.Text switch
                {
                    "sum" => JsonValue.Create(Number(parameters["a"]) + Number(parameters["b"])),
                    "variable-x" => variables.TryGetValue("x", out var x) ? x?.DeepClone() : JsonValue.Create("absent"),
                    "first" => parameters["p"]?.DeepClone(),
                    "pair" => JsonValue.Create($"{parameters["id"]}|{parameters["corr"]}"),
                    "typed" => JsonValue.Create($"{expression.Language}:{expression.ResultType}"),
                    "below-two" => JsonValue.Create(Number(variables["i"]) < 2),
                    "fail-second" => ++_counts < 2 ? JsonValue.Create(true) : throw new InvalidOperationException("second read"),
                    "name-then-fail" => ++owner.Named < 2 ? JsonValue.Create("hx-go") : throw new InvalidOperationException("read at resume"),
                    "fail" => throw new InvalidOperationException("the expression threw"),
                    var other => throw new ArgumentException($"no program '{other}'"),
                };

            public ValueTask DisposeAsync()
            {
                owner.Closed++;
                return ValueTask.CompletedTask;
            }

            private static double Number(JsonNode? node) => double.Parse(node!.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private static Node Hold() => Event("hold", "never");

    private static IEnumerable<Workflow> Workflows()
    {
        yield return Workflow("hx-values", Sequence("root",
                Set("x", "x", 5),
                Set("sum", "sum", Host("JavaScript", "sum", ("a", Var("x")), ("b", 3))),
                Set("ambient", "ambient", Host("JavaScript", "variable-x")),
                Set("member", "member", Host("JavaScript", "first", ("p", Var("input", "order.id")))),
                Set("typed", "typed", new HostExpressionBinding("Liquid", "typed", [], "String")),
                Set("out", "output", Expr("'' + sum + ' ' + ambient + ' ' + member + ' ' + typed")), Hold()),
            ("x", null), ("sum", null), ("ambient", null), ("member", null), ("typed", null), ("output", null));
        // The correlation id once a model sets it is checked where labels are projected (Kernel.Sim --label-check).
        yield return Workflow("hx-instance", Sequence("root",
                Set("pair", "output", Host("JavaScript", "pair", ("id", Instance(InstanceField.Id)), ("corr", Instance(InstanceField.CorrelationId)))), Hold()),
            ("output", null));
        // A GraphActivity's body sees nothing outside it, its host expressions included.
        yield return Workflow("hx-closed", Sequence("root",
                Set("x", "x", 5),
                GraphActivity("g", new GraphBoundary { Variables = [new("seen")], Outputs = [new("seen", Var("seen"))] },
                    Set("inner", "seen", Host("JavaScript", "variable-x"))).Capture("seen", "seen"),
                Set("out", "output", Expr("'' + seen")), Hold()),
            ("x", null), ("seen", "unset"), ("output", null));
        yield return Workflow("hx-loop", Sequence("root",
                While("loop", Host("JavaScript", "below-two"), Set("inc", "i", Expr("i + 1"))),
                Set("out", "output", Expr("'' + i")), Hold()),
            ("i", 0), ("output", null));
        yield return Workflow("hx-fail", Set("s", "a", Host("JavaScript", "fail")), ("a", null));
        // A composite reads its inputs again at every callback: a condition that fails the second time faults the loop.
        yield return Workflow("hx-fail-callback", While("loop", Host("JavaScript", "fail-second"), Set("inc", "i", Expr("i + 1"))), ("i", 0));
        // A stateful activity reads its inputs again when it resumes: an event name that fails then faults the activity.
        yield return Workflow("hx-fail-resume", Event("wait", Host("JavaScript", "name-then-fail")));
        foreach (var child in new[] { "hx-fail", "hx-fail-callback", "hx-fail-resume" })
            yield return Workflow("hx-parent-" + child, Sequence("root", Dispatch("child", child, wait: true).Capture("result"), Hold()), ("result", null));
    }

    internal static async Task RunAsync(Action<bool, string> check)
    {
        Console.WriteLine("host expressions");
        var registry = new WorkflowRegistry();
        foreach (var workflow in Workflows()) registry.Add(workflow);

        foreach (var mode in new[] { Modes.Ephemeral, Modes.Balanced, Modes.Durable })
        {
            var evaluator = new Evaluator();
            var store = new InMemoryStore(1, () => Environment.TickCount64);
            var owner = new Owner("hx", 0, store, registry, new Network(), native: new NativeOptions { Expressions = evaluator });
            var bare = new Owner("hx-bare", 0, new InMemoryStore(1, () => Environment.TickCount64), registry, new Network());
            await owner.StartAsync();
            await bare.StartAsync();

            async Task<NativeState?> Run(Owner on, string workflow, JsonObject? input = null)
            {
                var id = $"{workflow}-{mode}";
                on.Worker.Post(new Message($"{id}#start", InstanceRef.In(0, id), Kinds.Start, Start: NativeModel.Spec(workflow, mode, input)), fresh: true);
                await on.PumpAsync();
                return on.State($"0/{id}");
            }
            string? Output(NativeState? state) => state?.Variables.GetValueOrDefault("output")?.GetValue<string>();
            ActivityFault? Fault(NativeState? parent) => Values.To<ActivityFault>(Values.To<DispatchResult>(parent?.Variables.GetValueOrDefault("result"))?.Output);

            var values = Output(await Run(owner, "hx-values", new JsonObject { ["order"] = new JsonObject { ["id"] = "o-7" } }));
            check(values == "8 5 o-7 Liquid:String",
                $"{mode}: a host expression gets its resolved parameters (a variable, a member of the input), the visible variables, its language and result type ('{values}')");
            if (mode == Modes.Balanced)
                check(evaluator.Opened == 1 && evaluator.Closed == 1, $"{mode}: a burst opens one evaluation session, and closes it ({evaluator.Opened} opened, {evaluator.Closed} closed)");
            var instance = Output(await Run(owner, "hx-instance"));
            check(instance == $"0/hx-instance-{mode}|", $"{mode}: an instance parameter reads the instance's id, and a correlation id it never set as null ('{instance}')");
            var closed = Output(await Run(owner, "hx-closed"));
            check(closed == "absent", $"{mode}: inside a GraphActivity's body a host expression sees nothing of the workflow's variables ('{closed}')");
            var loop = Output(await Run(owner, "hx-loop"));
            check(loop == "2", $"{mode}: a composite's host-expression condition is read again at each pass ('{loop}')");

            var failed = Fault(await Run(owner, "hx-parent-hx-fail"));
            check(failed is { Code: "expression.failed" } && failed.Message.Contains("Node 's', input 'Value'", StringComparison.Ordinal) && failed.Message.Contains("the expression threw", StringComparison.Ordinal),
                $"{mode}: an expression that throws faults its activity with expression.failed, naming the node and input ({failed?.Message})");
            var callback = Fault(await Run(owner, "hx-parent-hx-fail-callback"));
            check(callback is { Code: "expression.failed" } && callback.Message.Contains("Node 'loop', input 'Condition'", StringComparison.Ordinal),
                $"{mode}: one that throws when a composite reads it again faults the composite ({callback?.Message})");
            await Run(owner, "hx-parent-hx-fail-resume");
            await Events.PublishAsync(store, new EventRow("event:hx-go", $"ev-hx-go-{mode}", "null", Environment.TickCount64 + 60_000));
            await owner.PumpAsync(poll: true, until: () => Fault(owner.State($"0/hx-parent-hx-fail-resume-{mode}")) is not null, keepPolling: true, timeoutMs: 5000);
            var resumed = Fault(owner.State($"0/hx-parent-hx-fail-resume-{mode}"));
            check(resumed is { Code: "expression.failed" } && resumed.Message.Contains("Node 'wait', input 'EventName'", StringComparison.Ordinal) && resumed.Message.Contains("read at resume", StringComparison.Ordinal),
                $"{mode}: one that throws when a waiting activity resumes faults that activity ({resumed?.Message})");
            var none = Fault(await Run(bare, "hx-parent-hx-fail"));
            check(none is { Code: "expression.failed" } && none.Message.Contains("composes no evaluator", StringComparison.Ordinal),
                $"{mode}: on a host with no evaluator a host expression faults its activity ({none?.Message})");
        }
    }
}
