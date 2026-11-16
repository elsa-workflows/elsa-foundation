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
            [new("secret", new("private-literal", "Literal"), null, null, null, null),
                new("value", new(new VariableReference("missing"), "Variable"), null, null, null, null)], []);
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

    private sealed class Draft(WorkflowDefinitionState state) : IWorkflowDefinitionDraft
    {
        public string Id => "draft";
        public string WorkflowDefinitionId => "workflow";
        public WorkflowDefinitionState State => state;
        public DateTimeOffset CreatedAt => DateTimeOffset.UnixEpoch;
        public DateTimeOffset LastModifiedAt => DateTimeOffset.UnixEpoch;
    }
}
