using System.Text.Json.Nodes;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;
using WorkflowRuntime = Kernel.WorkflowActor.WorkflowRuntime;

namespace Kernel.Native.Run;

internal static class NativeChildOutcomeChecks
{
    private sealed record Case(string Name, Contract.ActorStatus Status, string Route, Node Child, JsonNode? ExpectedOutput)
    {
        internal string Parent => $"child-outcome-parent-{Name}";
        internal string Definition => $"child-outcome-child-{Name}";
    }

    private static Case[] Scenarios() =>
    [
        Finished("success", new JsonObject { ["kind"] = "Success", ["reference"] = "order-42" }),
        Finished("business-error", new JsonObject { ["kind"] = "BusinessError", ["code"] = "OrderDeclined" }),
        new("technical-fault", Contract.ActorStatus.Faulted, "Faulted",
            Wf.Fault("fail", Binding.Literal("PartnerUnavailable"), Binding.Literal("Partner failed.")),
            new JsonObject { ["Code"] = "PartnerUnavailable", ["Message"] = "Partner failed." }),
        new("cancellation", Contract.ActorStatus.Cancelled, "Cancelled",
            Wf.Node<Event>("wait", ("EventName", "cancelled-child"), ("CanStartWorkflow", false)), null)
    ];

    private static Case Finished(string name, JsonObject output) =>
        new(name, Contract.ActorStatus.Finished, "Completed", Wf.SetOutput("return", "result", Binding.Literal(output)),
            new JsonObject { ["result"] = output.DeepClone() });

    internal static void AddTo(WorkflowRegistry registry)
    {
        foreach (var scenario in Scenarios())
        {
            registry.Add(Wf.Workflow(scenario.Definition, scenario.Child));
            registry.Add(Wf.Workflow(scenario.Parent, Wf.Sequence("parent",
                Wf.Flowchart("route-child",
                    [Wf.Dispatch("dispatch", scenario.Definition, wait: true),
                     Wf.SetOutput("complete", "route", "Completed"),
                     Wf.SetOutput("fault", "route", "Faulted"),
                     Wf.SetOutput("cancel", "route", "Cancelled")],
                    [new Connection("dispatch", "complete", "Completed"),
                     new Connection("dispatch", "fault", "Faulted"),
                     new Connection("dispatch", "cancel", "Cancelled")]),
                Wf.SetOutput("status", "status", Binding.Result("dispatch", "Status")),
                Wf.SetOutput("child-output", "output", Binding.Result("dispatch", "Output")))));
        }
    }

    internal static async Task VerifyAsync(Func<KernelDb> open, Contract.BoundScope scope, Contract.IIngress ingress,
        Contract.IActorReads reads, CancellationToken token)
    {
        var runtime = new WorkflowRuntime(ingress, reads);
        var outputs = (Contract.IActorTerminalOutputReads)reads;
        foreach (var scenario in Scenarios())
        {
            var admission = await runtime.StartAsync(scope, scenario.Parent, NativeExecutionModel.Start(scenario.Parent), cancellationToken: token);
            if (admission is not { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } parent })
                throw new InvalidOperationException($"Native child {scenario.Name} parent was not admitted: {admission}.");

            var child = new Contract.ActorRef(await EventuallyAsync(ReadChildReferenceAsync, value => value is not null, token));
            if (scenario.Status == Contract.ActorStatus.Cancelled)
            {
                var selected = await EventuallyAsync(() => ReadAsync(child),
                    view => view is { Header.Status: Contract.ActorStatus.Running, Selection: not null }, token);
                var cancelled = await runtime.TerminateAsync(scope, scenario.Definition, selected, cancellationToken: token);
                if (cancelled.Outcome != Contract.AdmissionOutcome.Accepted)
                    throw new InvalidOperationException($"Native child cancellation was refused: {cancelled}.");
            }

            var childRun = await EventuallyAsync(() => ReadAsync(child),
                view => view is { Park: null } && view.Header.Status == scenario.Status, token);
            var parentRun = await EventuallyAsync(() => ReadAsync(parent),
                view => view is { Header.Status: Contract.ActorStatus.Finished, Header.EndedAt: not null }, token);
            var terminal = await outputs.ReadTerminalOutputAsync(scope, parent, parentRun.Header.EndedAt!.Value,
                includeValues: true, cancellationToken: token);
            var result = terminal?.Values is { } bytes ? JsonNode.Parse(bytes) : null;
            if (childRun.Header.Parent != parent || result?["route"]?.GetValue<string>() != scenario.Route ||
                result["status"]?.GetValue<string>() != scenario.Status.ToString() || !result.AsObject().ContainsKey("output") ||
                !JsonNode.DeepEquals(result["output"], scenario.ExpectedOutput))
                throw new InvalidOperationException($"Native child {scenario.Name} returned the wrong Dispatch result: {result}.");

            Console.WriteLine($"ok Native child {scenario.Name} reached its exact parent as {scenario.Status} on the {scenario.Route} route with the expected output");

            async Task<string?> ReadChildReferenceAsync()
            {
                await using var db = open();
                return await db.Database.SqlQuery<string>($"""
                    SELECT child."FoundationReference" AS "Value" FROM "Instances" child
                    JOIN "Instances" parent ON child."Parent" = parent."Id" AND child."FoundationTenant" = parent."FoundationTenant"
                    WHERE parent."FoundationReference" = {parent.Id} AND parent."FoundationTenant" = {scope.Tenant}
                    """).SingleOrDefaultAsync(token);
            }
        }

        Task<Contract.ActorView?> ReadAsync(Contract.ActorRef actor) => reads.GetAsync(scope, actor, token).AsTask();
    }

    private static async Task<T> EventuallyAsync<T>(Func<Task<T?>> read, Func<T?, bool> ready, CancellationToken token) where T : class
    {
        while (true)
        {
            if (await read() is { } result && ready(result)) return result;
            await Task.Delay(25, token);
        }
    }
}
