using Elsa.Events.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Events;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests;

public sealed class DraftValidationFindingOwnershipTests
{
    [Fact]
    public async Task Composed_draft_validation_reports_each_owner_refusal_once_without_weakening_source_admission()
    {
        var secret = new Elsa.Activities.Design.Core.Models.InputDefinition("secret", "Secret", new("String"), null, "Secret", null, false)
            { IsCredential = true };
        var value = new Elsa.Activities.Design.Core.Models.InputDefinition("value", "Value", new("String"), null, "Value", null, true);
        var catalog = new StubActivityCatalog().Add("test", [secret, value]);
        using var root = ActivityDataSelectionTestSupport.Provider(catalog);
        using var scope = root.CreateScope();
        var node = new ActivityNode("consumer", "test",
            [new("secret", Elsa.Expressions.Core.Models.ArgumentValue.FromClr("private-literal", "Literal"), null, null, null, null),
                new("value", Elsa.Expressions.Core.Models.ArgumentValue.FromClr(new VariableReference("missing"), "Variable"), null, null, null, null)], []);
        var draft = new Draft(new([], node, [], [], null));
        var validating = new DraftValidating(draft);
        await scope.ServiceProvider.GetRequiredService<IInlineEventPublisher>().Publish(validating, CancellationToken.None);

        Assert.Single(validating.Errors, error => error.Path == "consumer/inputs/secret" && error.Type == "Inputs/CredentialLiteral");
        var unresolved = Assert.Single(validating.Errors, error => error.Path == "consumer/inputs/value" &&
            error.Type is "Expressions/UnresolvedVariable" or "Variable/UnresolvableReference");
        Assert.Equal("Expressions/UnresolvedVariable", unresolved.Type);

        var admission = await scope.ServiceProvider.GetRequiredService<IActivityDataSelectionAnalyzer>()
            .AnalyzeAsync(new("WorkflowDefinition", "draft", "revision", node, [], [], []));
        Assert.Contains(admission.Findings, error => error.Path == "consumer/inputs/secret" && error.Type == "Inputs/CredentialLiteral");
        Assert.Contains(admission.Findings, error => error.Path == "consumer/inputs/value" && error.Type == "Variable/UnresolvableReference");
    }

    /// <summary>
    /// A node with no arguments carries an empty list. A draft whose node omits its <c>inputs</c> list, or carries
    /// <c>"outputs": null</c>, is refused at that node, by the validation gate, credential admission, data selection and
    /// the rename planner, instead of throwing.
    /// </summary>
    [Fact]
    public async Task Draft_validation_refuses_a_missing_or_null_argument_list_instead_of_throwing()
    {
        var secret = new Elsa.Activities.Design.Core.Models.InputDefinition("secret", "Secret", new("String"), null, "Secret", null, false)
            { IsCredential = true };
        var catalog = new StubActivityCatalog().Add("test", [secret]).Add("sequence");
        using var root = ActivityDataSelectionTestSupport.Provider(catalog);
        using var scope = root.CreateScope();
        var state = System.Text.Json.JsonSerializer.Deserialize<WorkflowDefinitionState>("""
            {"variables":[],"inputs":[],"outputs":[],"rootActivity":{"nodeId":"root","activityVersionId":"sequence","outputs":[],
             "structure":{"kind":"elsa.sequence.structure","schemaVersion":"1.0.0","payload":{"variables":[],"activities":[
               {"nodeId":"child","activityVersionId":"test","inputs":[],"outputs":null},
               {"nodeId":"leaf","activityVersionId":"test","outputs":[]}]}}}}
            """, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        var validating = new DraftValidating(new Draft(state));

        await scope.ServiceProvider.GetRequiredService<IInlineEventPublisher>().Publish(validating, CancellationToken.None);

        Assert.Equal(["child/outputs", "leaf/inputs", "root/inputs"],
            validating.Errors.Where(error => error.Type == "Graph/MalformedArgumentList").Select(error => error.Path).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(validating.Errors, error => error.Type != "Graph/MalformedArgumentList");

        // Credential admission runs before the gate on every write; it cannot check such a node, so it refuses the state
        // with the same findings instead of throwing (the leaf's catalog version declares an input it would read).
        var admission = await scope.ServiceProvider.GetRequiredService<ICredentialLiteralValidator>().Validate(state, CancellationToken.None);
        Assert.All(admission, error => Assert.Equal("Graph/MalformedArgumentList", error.Type));
        Assert.Equal(["child/outputs", "leaf/inputs", "root/inputs"], admission.Select(error => error.Path).Order(StringComparer.Ordinal));

        // The rename planner reads posted state without the gate and refuses it as a bad request.
        var refused = await Assert.ThrowsAsync<ArgumentException>(() => scope.ServiceProvider.GetRequiredService<IScriptVariableRenamePlanner>()
            .PlanAsync(state, "root", "variable", "renamed").AsTask());
        Assert.StartsWith("root/inputs", refused.Message, StringComparison.Ordinal);
        // Scope resolution reads a ForEach's inputs, so the planner refuses before it resolves scopes.
        var forEach = new ActivityNode("each", "test", null!, [], new(Elsa.Activities.ForEach.Activities.ForEach.StructureKind, "1.0.0",
            System.Text.Json.JsonSerializer.SerializeToElement(new Elsa.Activities.ControlFlow.Loops.LoopAuthoredStructure(new ActivityNode("body", "test", [], [])),
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))));
        var loopRefused = await Assert.ThrowsAsync<ArgumentException>(() => scope.ServiceProvider.GetRequiredService<IScriptVariableRenamePlanner>()
            .PlanAsync(new([], forEach, [], [], null), "each", "variable", "renamed").AsTask());
        Assert.StartsWith("each/inputs", loopRefused.Message, StringComparison.Ordinal);

        // Data selection reads the same draft state directly and refuses it the same way.
        var analysis = await scope.ServiceProvider.GetRequiredService<IActivityDataSelectionAnalyzer>()
            .AnalyzeAsync(new("WorkflowDefinition", "draft", "revision", state.RootActivity, [], [], []));
        Assert.Equal(["child/outputs", "leaf/inputs", "root/inputs"],
            analysis.Findings.Where(error => error.Type == "Graph/MalformedArgumentList").Select(error => error.Path).Order(StringComparer.Ordinal));
    }

    private sealed class Draft(WorkflowDefinitionState state) : IWorkflowDefinitionDraft
    {
        public string Id => "draft";
        public string WorkflowDefinitionId => "workflow";
        public WorkflowDefinitionState State => state;
        public DateTimeOffset CreatedAt => DateTimeOffset.UnixEpoch;
        public DateTimeOffset LastModifiedAt => DateTimeOffset.UnixEpoch;
    }
}
