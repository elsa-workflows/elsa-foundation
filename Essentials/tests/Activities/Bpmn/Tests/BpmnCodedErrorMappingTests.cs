using System.Text.Json;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using F = Elsa.Activities.Bpmn.Tests.BpmnRuntimeFixture;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnCodedErrorMappingTests
{
    [Theory]
    [InlineData("bpmn.error", "PAYMENT-DECLINED", "incident.payment", true)]
    [InlineData("bpmn.error", "OTHER", "PAYMENT-DECLINED", false)]
    [InlineData("ordinary.category", "PAYMENT-DECLINED", "PAYMENT-DECLINED", false)]
    [InlineData(null, null, "PAYMENT-DECLINED", false)]
    public async Task Coded_error_matching_uses_bpmn_category_and_authored_fault_type_separately_from_incident_code(
        string? category, string? authoredCode, string incidentCode, bool caught)
    {
        await using var host = await F.CreateAsync(Enumerable.Range(0, 20).Select(index => $"coded-error-{index}"));
        var type = typeof(BpmnClassificationFaultActivity);
        var contract = ClrActivityContractTestBuilder.BuildContract(type);
        var bindings = new Dictionary<string, RuntimeInputBinding>
        {
            [nameof(BpmnClassificationFaultActivity.IncidentCode)] = Literal(nameof(BpmnClassificationFaultActivity.IncidentCode), incidentCode)
        };
        if (category is not null) bindings[nameof(BpmnClassificationFaultActivity.Category)] = Literal(nameof(BpmnClassificationFaultActivity.Category), category);
        if (authoredCode is not null) bindings[nameof(BpmnClassificationFaultActivity.AuthoredCode)] = Literal(nameof(BpmnClassificationFaultActivity.AuthoredCode), authoredCode);
        var child = new ExecutableNode("node-fault", "authored-fault", type.FullName!, "1.0.0", contract.DescriptorKind, contract.DescriptorPayload,
            ClrActivityContractTestBuilder.CompleteInputBindings(contract, bindings), new Dictionary<string, string>(), activityContract: contract);
        var boundary = new BpmnElement("error", BpmnElementTypes.BoundaryEvent, attachedToRef: "host", cancelActivity: true,
            eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Error,
                new Dictionary<string, string> { [BpmnEventDefinitionProperties.Code] = "PAYMENT-DECLINED" })]);
        var graph = host.NewExecutable([child, host.NewProbeNode("node-handler")],
            [F.StartEvent(), F.Task("host", child.ExecutableNodeId), boundary, F.Task("handler", "node-handler"),
                F.EndEvent("host-end"), F.EndEvent("handler-end")],
            [F.Flow("start-host", "start", "host"), F.Flow("host-end", "host", "host-end"),
                F.Flow("error-handler", "error", "handler"), F.Flow("handler-end", "handler", "handler-end")]);

        var run = await host.RunAsync(graph);

        var faulted = run.State(child.ExecutableNodeId);
        Assert.Equal(ActivityExecutionStatus.Faulted, faulted.Status);
        Assert.Equal(incidentCode, faulted.Fault!.Code);
        Assert.Equal(category, faulted.Fault.Metadata.GetValueOrDefault(ActivityFault.CategoryMetadataKey));
        Assert.Equal(authoredCode, faulted.Fault.Metadata.GetValueOrDefault(ActivityFault.FaultTypeMetadataKey));
        var incidents = await host.Provider.GetRequiredService<IIncidentStateStore>().ListAsync("wfexec-1");
        var original = Assert.Single(incidents, incident => incident.ActivityExecutionId == faulted.InvocationId);
        Assert.Equal("ActivityReturnedFault", original.FailureType);
        if (caught)
        {
            run.AssertWorkflowCompleted();
            run.AssertCompleted(F.ProcessNodeId);
            Assert.Null(run.State(F.ProcessNodeId).Fault);
            Assert.Single(run.States("node-handler"));
            Assert.False(original.IsBlocking);
            Assert.Equal(IncidentResolutionActionKinds.AbsorbFault, original.ResolutionOutcome!.ActionKind);
            Assert.Equal(IncidentResolutionSystemSources.StructuralFaultAbsorption, original.ResolutionOutcome.SystemSource);
            Assert.Equal("bpmn.boundary.error-absorbed", original.Metadata.GetValueOrDefault(RuntimeMetadataKeys.FaultAbsorptionReason));
        }
        else
        {
            Assert.Equal(WorkflowExecutionStatus.Faulted, run.WorkflowState!.Status);
            var parent = run.State(F.ProcessNodeId);
            Assert.Equal("bpmn.work.faulted", parent.Fault!.Code);
            Assert.Equal(category == "bpmn.error" ? "bpmn.error" : null, parent.Fault.Metadata.GetValueOrDefault(ActivityFault.CategoryMetadataKey));
            Assert.Equal(category == "bpmn.error" ? authoredCode : null, parent.Fault.Metadata.GetValueOrDefault(ActivityFault.FaultTypeMetadataKey));
            run.AssertDidNotRun("node-handler");
            Assert.True(original.IsBlocking);
            Assert.NotEqual(IncidentResolutionActionKinds.AbsorbFault, original.ResolutionOutcome?.ActionKind);
        }
        Assert.Empty(await host.BookmarksAsync());
    }

    private static RuntimeInputBinding Literal(string key, string value) => new(key, new ValueTypeDescriptor("String"),
        ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
        literal: ValueEnvelope.Inline(new ValueTypeDescriptor("String"), JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));
}

public sealed class BpmnClassificationFaultActivity : Activity<ActivityUnit>
{
    [ActivityInput] public string IncidentCode { get; set; } = null!;
    [ActivityInput] public string? Category { get; set; }
    [ActivityInput] public string? AuthoredCode { get; set; }

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Fault<ActivityUnit>(new ActivityFault(IncidentCode, "actual classified child fault",
            category: Category, faultType: AuthoredCode)));
}
