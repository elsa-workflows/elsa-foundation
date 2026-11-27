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
            .Add(Workflow("too-long", SetCorrelationId("c", new string('x', 1_025))))
            .Add(Workflow("blank", Sequence("root", SetCorrelationId("c", "set"), SetCorrelationId("clear", "  "), Event("wait", "never"))))
            // A migration plan answered (here, refused at its source) beside both labels, in either order.
            .Add(Workflow("labelled-target", Sequence("root", SetCorrelationId("c", "order-42"), SetInstanceName("n", "Order"), Event("wait", "never"))))
            .Add(Workflow("late-labels", Sequence("root", Event("go", "go-now"), SetCorrelationId("c", "late"), SetInstanceName("n", "Late"), Event("wait", "never"))))
            .Add(Workflow("plan", Event("wait", "never")))
            // A host expression reads the correlation id the model set (K10's instance parameter).
            .Add(Workflow("host-read", Sequence("root", SetCorrelationId("c", "order-9"),
                SetInstanceName("n", Binding.Host("JavaScript", "echo", ("c", Binding.Instance(InstanceField.CorrelationId)), ("i", Binding.Instance(InstanceField.Id)))),
                Event("wait", "never"))))
            // K10: a published version carries its definition as the definition label, also when a parent starts it.
            .Add(VersionOf("invoices", Workflow("invoices@3", Event("wait", "never"))))
            .Add(Workflow("invoicing", Sequence("root", Dispatch("child", "invoices@3", wait: false), Event("wait", "never"))));
        var model = new NativeExecutionModel(registry, new NativeExecutionOptions { Expressions = new EchoExpressions() });
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
              header.Labels.GetValueOrDefault("definition") == "labelled" && !header.Labels.ContainsKey("version"),
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
        var blank = await Start("blank");
        await driver.PassAll(4);
        check(!(await Header(blank)).Labels.ContainsKey(NativeLabels.CorrelationId), "a blank value clears a label, as on the old core");

        // Both labels beside a migration plan's answer, in either order: the actor's own labels are the definition and the
        // answered plan, so a model's two still fit the header's four.
        var plan = await Start("plan");
        await driver.PassAll(4);
        async Task<Contract.Admission> Migrate(Contract.ActorRef target, string from, string to, string id)
        {
            var admission = await driver.Ingress.SendAsync(scope, new Contract.Target.Ref(target), id, Contract.Kinds.Migrate,
                System.Text.Json.JsonSerializer.Serialize(new { From = from, To = to, Plan = plan }));
            await driver.PassAll(6);
            return admission;
        }
        var answered = await Migrate(labelled, "labelled", "labelled-target", "migrate-labelled");
        var afterMigration = await Header(labelled);
        var late = await Start("late-labels");
        await driver.PassAll(4);
        await Migrate(late, "other", "plan", "migrate-late");
        await driver.Ingress.SendAsync(scope, new Contract.Target.Event("event:go-now"), "go", "go", "now");
        await driver.PassAll(6);
        var lateHeader = await Header(late);
        check(answered.Outcome == Contract.AdmissionOutcome.Accepted && afterMigration.Status == Contract.ActorStatus.Running &&
              afterMigration.Labels.GetValueOrDefault(NativeLabels.CorrelationId) == "order-42" &&
              lateHeader.Status == Contract.ActorStatus.Running && lateHeader.Labels.GetValueOrDefault(NativeLabels.InstanceName) == "Late" && lateHeader.Labels.Count == 4 &&
              lateHeader.Labels.ContainsKey("migration-plan") && afterMigration.Labels.ContainsKey("migration-plan"),
            $"an instance with both labels takes a migration plan, and one that answered a plan sets both labels ({afterMigration.Status}, {lateHeader.Status}, {string.Join(", ", lateHeader.Labels.Keys)})");

        var hostRead = await Start("host-read");
        await driver.PassAll(4);
        var hostHeader = await Header(hostRead);
        check(hostHeader.Labels.GetValueOrDefault(NativeLabels.InstanceName) == "order-9 True",
            $"a host expression's instance parameters read the correlation id the model set and the instance's id ({hostHeader.Labels.GetValueOrDefault(NativeLabels.InstanceName)})");

        var invoicing = await Start("invoicing");
        await driver.PassAll(6);
        var invoices = await driver.Reads.ListAsync(scope, new Contract.ActorQuery(Type: PublicWorkflow.ActorType,
            Labels: new Dictionary<string, string> { ["definition"] = "invoices" }));
        check(invoices.Items is [var invoice] && invoice.Header.TypeData.Contains("\"Workflow\":\"invoices@3\"", StringComparison.Ordinal) &&
              (await Header(invoicing)).Labels.GetValueOrDefault("definition") == "invoicing",
            $"a child started on a published version carries its definition as the definition label and the version in its type data ({invoices.Items.Count})");

        var refused = await Header(tooLong);
        check(refused.Status == Contract.ActorStatus.Faulted && refused.TypeData.Contains("workflow.label.refused", StringComparison.Ordinal),
            $"a label value over 1,024 bytes faults the run ({refused.Status})");
    }

    private sealed class EchoExpressions : INativeExpressions, INativeExpressionSession
    {
        public INativeExpressionSession Open(CancellationToken cancellationToken) => this;

        public JsonNode? Evaluate(HostExpression expression, IReadOnlyDictionary<string, JsonNode?> parameters, IReadOnlyDictionary<string, JsonNode?> variables) =>
            JsonValue.Create($"{parameters["c"]?.GetValue<string>()} {parameters["i"]?.GetValue<string>()?.Length > 0}");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
