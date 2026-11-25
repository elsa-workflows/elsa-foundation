using System.Text.Json.Nodes;
using Kernel.Native;
using static Kernel.Native.Wf;
using Contract = Kernel.Foundation.Contracts;
using PublicRuntime = Kernel.WorkflowActor.WorkflowRuntime;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Sim;

/// <summary>
/// Set Correlation Id and Set Instance Name through the public contract (owner decisions of 25 November: the four
/// intrinsics run, and a model sets its own labels): the labels land on the instance's header beside the workflow actor's
/// own, reads filter on them, a null clears one, the actor's later header writes keep them, and one too long is refused.
/// </summary>
internal static class LabelChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var registry = new WorkflowRegistry()
            .Add(Workflow("labelled", Sequence("root",
                SetCorrelationId("c", Binding.Expr("input.order")), SetInstanceName("n", Binding.Expr("'Order ' + input.order")), Event("wait", "never"))))
            .Add(Workflow("cleared", Sequence("root", SetCorrelationId("c", "kept"), SetInstanceName("n", "gone"), SetInstanceName("clear", Binding.Literal(null)))))
            .Add(Workflow("too-long", SetCorrelationId("c", new string('x', 1_025))));
        var model = new NativeExecutionModel(registry);
        await using var driver = new ContractWorkload.InMemoryDriver(4, Flaw.None, new PublicWorkflow([model]), authorizeOperator: (_, _) => true);
        var scope = ContractScopes.Bind("labels");
        var runtime = new PublicRuntime(driver.Ingress, driver.Reads);
        await driver.TakeAll();

        async Task<Contract.ActorRef> Start(string workflow, JsonObject? input = null) =>
            (await runtime.StartAsync(scope, workflow, NativeExecutionModel.Start(workflow, Modes.Balanced, input))).Actor!.Value;
        async Task<Contract.ActorHeader> Header(Contract.ActorRef actor) => (await driver.Reads.GetAsync(scope, actor))!.Header;

        var labelled = await Start("labelled", new JsonObject { ["order"] = "order-42" });
        var cleared = await Start("cleared");
        var tooLong = await Start("too-long");
        await driver.PassAll(6);

        var header = await Header(labelled);
        check(header.Labels.GetValueOrDefault(NativeLabels.CorrelationId) == "order-42" && header.Labels.GetValueOrDefault(NativeLabels.InstanceName) == "Order order-42" &&
              header.Labels.GetValueOrDefault("definition") == "labelled" && header.Labels.ContainsKey("version"),
            $"the correlation id and instance name are labels on the header, beside the actor's own ({string.Join(", ", header.Labels.Select(p => p.Key + "=" + p.Value))})");
        var found = await driver.Reads.ListAsync(scope, new Contract.ActorQuery(Type: PublicWorkflow.ActorType,
            Labels: new Dictionary<string, string> { [NativeLabels.CorrelationId] = "order-42" }));
        check(found.Items is [var only] && only.Header.Ref == labelled, $"a read filters instances by correlation id ({found.Items.Count})");

        var suspend = await driver.Ingress.SendAsync(scope, new Contract.Target.Ref(labelled), "suspend", Contract.Kinds.Suspend);
        await driver.PassAll(4);
        var suspended = await Header(labelled);
        check(suspend.Outcome == Contract.AdmissionOutcome.Accepted && suspended.Labels.GetValueOrDefault(NativeLabels.CorrelationId) == "order-42" &&
              suspended.TypeData.Contains("\"Suspended\":true", StringComparison.Ordinal),
            $"the actor's own header write (a suspension) keeps the model's labels ({string.Join(", ", suspended.Labels.Keys)})");

        var clearedHeader = await Header(cleared);
        check(clearedHeader.Status == Contract.ActorStatus.Finished && clearedHeader.Labels.GetValueOrDefault(NativeLabels.CorrelationId) == "kept" &&
              !clearedHeader.Labels.ContainsKey(NativeLabels.InstanceName),
            $"a null clears a label, and the labels outlive the run ({clearedHeader.Status}; {string.Join(", ", clearedHeader.Labels.Keys)})");
        var refused = await Header(tooLong);
        check(refused.Status == Contract.ActorStatus.Faulted && refused.TypeData.Contains("workflow.label.refused", StringComparison.Ordinal),
            $"a label value over 1,024 bytes faults the run ({refused.Status})");
    }
}
