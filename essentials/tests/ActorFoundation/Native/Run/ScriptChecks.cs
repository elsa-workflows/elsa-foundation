using System.Text.Json.Nodes;
using static Kernel.Native.Binding;
using static Kernel.Native.Wf;

namespace Kernel.Native.Run;

/// <summary>
/// The release 1 ports of the old core's Inline and RunJavaScript (K6; GA test drive card 3). RunJavaScript runs on the
/// host's engine; here a stand-in engine runs named programs, since what is checked is the activity, not JavaScript.
/// </summary>
internal static class ScriptChecks
{
    /// <summary>Programs by their text: each sees only its arguments, as the old core's sandbox does.</summary>
    private sealed class Engine : INativeScripts
    {
        public int Calls;

        public ValueTask<JsonNode?> EvaluateAsync(string script, JsonObject arguments, CancellationToken cancellationToken)
        {
            Calls++;
            return script switch
            {
                "sum" => new(JsonValue.Create(arguments["a"]!.GetValue<int>() + arguments["b"]!.GetValue<int>())),
                "pick" => new(arguments["choice"]?.DeepClone()),
                "touch" => Touch(arguments),
                "throw" => throw new InvalidOperationException("the script threw"),
                _ => throw new ArgumentException($"no program '{script}'"),
            };
        }

        // A program that changes its args changes only its own copy.
        private static ValueTask<JsonNode?> Touch(JsonObject arguments)
        {
            arguments["a"] = 99;
            return new(JsonValue.Create("touched"));
        }
    }

    private static IEnumerable<Workflow> Workflows()
    {
        yield return Workflow("script-values", Sequence("root",
                Inline("inline", Expr("x * 2")).Capture("doubled"),
                RunJavaScript("sum", "sum", Literal(new JsonObject { ["a"] = 3, ["b"] = 4 })).Capture("sum", "Value"),
                RunJavaScript("blank", " ").Capture("blank", "Value"),
                Set("out", "output", Expr("doubled + ' ' + sum + ' ' + blank")), Hold()),
            ("x", 3), ("doubled", null), ("sum", null), ("blank", "unset"), ("output", null));

        // With possible outcomes the program routes by returning: a string or a number naming one, else Unmatched.
        yield return Workflow("script-routes", Sequence("root",
                Flowchart("flow",
                    [
                        RunJavaScript("approve", "pick", Literal(new JsonObject { ["choice"] = "approve" }), Literal(new JsonArray("approve", "reject"))),
                        Set("a", "first", "approve"),
                        RunJavaScript("maybe", "pick", Literal(new JsonObject { ["choice"] = "maybe" }), Literal(new JsonArray("approve", "reject"))),
                        Set("u", "second", "unmatched"),
                        RunJavaScript("number", "pick", Literal(new JsonObject { ["choice"] = 7 }), Literal(new JsonArray(7, "eight"))),
                        Set("n", "third", "7"),
                    ],
                    [
                        new("approve", "a", "approve"), new("a", "maybe"), new("maybe", "u", RunJavaScript.UnmatchedOutcome),
                        new("u", "number"), new("number", "n", "7"),
                    ]),
                Set("out", "output", Expr("first + ' ' + second + ' ' + third")), Hold()),
            ("first", null), ("second", null), ("third", null), ("output", null));

        // The program's copy of its arguments is its own.
        yield return Workflow("script-frozen", Sequence("root",
                Set("args", "args", Literal(new JsonObject { ["a"] = 1 })),
                RunJavaScript("touch", "touch", Var("args")),
                Set("out", "output", Expr("'' + args.a")), Hold()),
            ("args", null), ("output", null));

        yield return Workflow("script-throws", RunJavaScript("js", "throw"));
        yield return Workflow("script-arguments", RunJavaScript("js", "sum", Literal(new JsonArray(1, 2))));
        yield return Workflow("script-outcomes", RunJavaScript("js", "sum", Literal(new JsonObject { ["a"] = 1, ["b"] = 2 }), Literal(new JsonArray(new JsonObject()))));
        foreach (var child in new[] { "script-throws", "script-arguments", "script-outcomes" })
            yield return Workflow("script-parent-" + child, Sequence("root", Dispatch("child", child, wait: true).Capture("result"), Hold()), ("result", null));
    }

    // A wait that never ends keeps an Ephemeral instance, and its state, in memory to be read.
    private static Node Hold() => Event("hold", "never");

    internal static async Task RunAsync(Action<bool, string> check)
    {
        Console.WriteLine("activity ports: Inline and RunJavaScript");
        var registry = new WorkflowRegistry();
        foreach (var workflow in Workflows()) registry.Add(workflow);

        foreach (var mode in new[] { Modes.Ephemeral, Modes.Balanced, Modes.Durable })
        {
            var engine = new Engine();
            var store = new InMemoryStore(1, () => Environment.TickCount64);
            var owner = new Owner("scripts", 0, store, registry, new Network(), native: new NativeOptions { Scripts = engine });
            var bare = new Owner("bare", 0, new InMemoryStore(1, () => Environment.TickCount64), registry, new Network());
            await owner.StartAsync();
            await bare.StartAsync();

            async Task<NativeState?> Run(Owner on, string workflow)
            {
                var id = $"{workflow}-{mode}";
                on.Worker.Post(new Message($"{id}#start", InstanceRef.In(0, id), Kinds.Start, Start: NativeModel.Spec(workflow, mode)), fresh: true);
                await on.PumpAsync();
                return on.State($"0/{id}");
            }
            string? Output(NativeState? state) => state?.Variables.GetValueOrDefault("output")?.GetValue<string>();

            var values = await Run(owner, "script-values");
            check(Output(values) == "6 7 " && values!.Variables["blank"] is null && engine.Calls == 1,
                $"{mode}: Inline's result is its expression; RunJavaScript's Value is the program's; a blank script returns null and runs nothing ('{Output(values)}', {engine.Calls} call)");
            var routes = Output(await Run(owner, "script-routes"));
            check(routes == "approve unmatched 7", $"{mode}: with possible outcomes the program routes by its value, a number by its text, else Unmatched ('{routes}')");
            var frozen = Output(await Run(owner, "script-frozen"));
            check(frozen == "1", $"{mode}: the program changes only its own copy of its arguments ('{frozen}')");

            async Task<string?> Code(Owner on, string workflow) =>
                Values.To<ActivityFault>(Values.To<DispatchResult>((await Run(on, "script-parent-" + workflow))?.Variables.GetValueOrDefault("result"))?.Output)?.Code;
            var unavailable = await Code(bare, "script-throws");
            check(unavailable == "javascript.unavailable", $"{mode}: a script on a host without an engine faults ({unavailable})");
            var thrown = await Code(owner, "script-throws");
            check(thrown == "javascript.failed", $"{mode}: a program that throws faults the activity ({thrown})");
            var arguments = await Code(owner, "script-arguments");
            var outcomes = await Code(owner, "script-outcomes");
            check(arguments == "javascript.arguments.invalid" && outcomes == "javascript.outcomes.invalid",
                $"{mode}: arguments that are no object, and possible outcomes that are no strings or numbers, fault ({arguments}, {outcomes})");
        }
    }
}
