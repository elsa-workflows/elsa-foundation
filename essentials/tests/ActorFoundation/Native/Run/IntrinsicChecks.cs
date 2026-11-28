using System.Text.Json.Nodes;
using static Kernel.Native.Binding;
using static Kernel.Native.Wf;

namespace Kernel.Native.Run;

/// <summary>
/// The engine intrinsics the palette offers besides Set Variable (owner decision of 25 November: all four run): Set Output,
/// Finish, and here, on a host that projects no labels, Set Correlation Id's refusal. The labels themselves are checked
/// through the public contract (Kernel.Sim --label-check).
/// </summary>
internal static class IntrinsicChecks
{
    private static IEnumerable<Workflow> Workflows()
    {
        yield return Workflow("intrinsic-outputs", Sequence("root", SetOutput("o1", "total", Expr("1 + 2")), SetOutput("o2", "status", "ok")));
        yield return Workflow("intrinsic-conflict", Sequence("root", Set("s", "output", "scalar"), SetOutput("o", "total", 1)));
        // Finish ends the workflow at once: the other branch's wait goes and nothing after it runs.
        yield return Workflow("intrinsic-finish", Sequence("root",
            Parallel("par", null, Sequence("b1", SetOutput("o", "done", true), Finish("f", "Approved"), SetOutput("late", "late", true)), Event("w", "never")),
            SetOutput("after", "after", true)));
        // Inside a GraphActivity's body, Set Output assigns the boundary's output; a required one left unassigned faults it.
        yield return Workflow("intrinsic-boundary", Sequence("root",
            GraphActivity("g", new GraphBoundary { Outputs = [new("answer"), new("mapped", Literal("m"))] }, SetOutput("o", "answer", 42)).Capture("result"),
            SetOutput("out", "result", Var("result"))), ("result", null));
        yield return Workflow("intrinsic-required", GraphActivity("g", new GraphBoundary { Outputs = [new("answer", Required: true)] }, Set("s", "x", 1)));
        yield return Workflow("intrinsic-label", SetCorrelationId("c", "order-42"));
        foreach (var child in new[] { "intrinsic-outputs", "intrinsic-conflict", "intrinsic-finish", "intrinsic-boundary", "intrinsic-required", "intrinsic-label" })
            yield return Workflow("intrinsic-parent-" + child, Sequence("root", Dispatch("child", child, wait: true).Capture("result"), Event("hold", "never")), ("result", null));
    }

    internal static async Task RunAsync(Action<bool, string> check)
    {
        Console.WriteLine("intrinsics: Set Output, Finish, Set Correlation Id");
        var registry = new WorkflowRegistry();
        foreach (var workflow in Workflows()) registry.Add(workflow);

        check(Refused(() => Workflow("computed", SetOutput("o", "x", 1).Also(n => n.Inputs["Name"] = Expr("'x'")))) &&
              Refused(() => Workflow("blank", Finish("f", " "))) &&
              Refused(() => Workflow("undeclared", GraphActivity("g", new GraphBoundary(), SetOutput("o", "x", 1)))) &&
              Refused(() => Workflow("mapped", GraphActivity("g", new GraphBoundary { Outputs = [new("x", Literal(1))] }, SetOutput("o", "x", 2)))),
            "Set Output needs a literal name, and inside a boundary an output it declares and maps nothing to; Finish needs a literal outcome");
        check(NativeActivityCatalog.Intrinsics.Count == 5 && NativeActivityCatalog.Resolve("Elsa.Finish", "f") == typeof(Finish) &&
              NativeActivityCatalog.Resolve("Elsa.SetCorrelationId", "c") == typeof(SetCorrelationId),
            "the five intrinsics resolve to their Native types");

        foreach (var mode in new[] { Modes.Ephemeral, Modes.Balanced, Modes.Durable })
        {
            var owner = new Owner("intrinsics", 0, new InMemoryStore(1, () => Environment.TickCount64), registry, new Network());
            await owner.StartAsync();
            async Task<DispatchResult?> Child(string workflow)
            {
                var id = $"{workflow}-{mode}";
                owner.Worker.Post(new Message($"{id}#start", InstanceRef.In(0, id), Kinds.Start, Start: NativeModel.Spec("intrinsic-parent-" + workflow, mode)), fresh: true);
                await owner.PumpAsync();
                return Values.To<DispatchResult>(owner.State($"0/{id}")?.Variables.GetValueOrDefault("result"));
            }
            string? Code(DispatchResult? result) => Values.To<ActivityFault>(result?.Output)?.Code;

            var outputs = await Child("intrinsic-outputs");
            check(outputs is { Status: "Finished", Output: JsonObject o } && o["total"]?.GetValue<double>() == 3 && o["status"]?.GetValue<string>() == "ok",
                $"{mode}: Set Output names the fields of the workflow's output ({outputs?.Output?.ToJsonString()})");
            var conflict = await Child("intrinsic-conflict");
            check(Code(conflict) == "workflow.output.conflict", $"{mode}: Set Output on a workflow whose output is no object faults ({Code(conflict)})");
            var finish = await Child("intrinsic-finish");
            check(finish is { Status: "Finished", Output: JsonObject f } && f.Count == 1 && f["done"]?.GetValue<bool>() == true,
                $"{mode}: Finish completes the workflow at once; nothing after it runs and the waiting branch ends ({finish?.Status} {finish?.Output?.ToJsonString()})");
            var boundary = await Child("intrinsic-boundary");
            check(boundary?.Output?["result"] is JsonObject r && r["answer"]?.GetValue<int>() == 42 && r["mapped"]?.GetValue<string>() == "m",
                $"{mode}: inside a boundary Set Output assigns its output, beside its mapped one ({boundary?.Output?.ToJsonString()})");
            var required = await Child("intrinsic-required");
            check(Code(required) == GraphActivity.OutputRequiredFaultCode, $"{mode}: a required output neither assigned nor mapped faults the boundary ({Code(required)})");
            var label = await Child("intrinsic-label");
            check(Code(label) == "workflow.label.refused", $"{mode}: Set Correlation Id faults on a host that projects no labels ({Code(label)})");
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
