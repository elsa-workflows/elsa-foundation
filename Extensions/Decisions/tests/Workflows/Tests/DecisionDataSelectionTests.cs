using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Decisions.Workflows.Design;
using Elsa.Decisions.Workflows.Runtime.Activities;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Decisions.Workflows.Tests;

public sealed class DecisionDataSelectionTests
{
    [Fact]
    public async Task Headless_admission_rejects_a_dynamic_selector_and_discovery_exposes_its_owning_constraint()
    {
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();
        var analyzer = scope.ServiceProvider.GetRequiredService<IActivityDataSelectionAnalyzer>();
        var document = Document(nameof(EvaluateDecision.Decision));
        var admission = await analyzer.AnalyzeAsync(document);
        var error = Assert.Single(admission.Findings);
        Assert.Equal("decisions.selector-fixed-at-publication", error.Type);
        Assert.Equal("evaluate/inputs/Decision", error.Path);

        var discovery = await analyzer.AnalyzeAsync(document, new("evaluate", "Decision"));
        Assert.Empty(discovery.Findings);
        Assert.Equal("Elsa.Decisions", discovery.Target!.Constraint!.Owner);
        Assert.Equal(new[] { "Literal" }, discovery.Target.Constraint.AllowedExpressionTypes);
        Assert.Empty(discovery.Sources);
    }

    [Theory]
    [InlineData(nameof(EvaluateDecision.Inputs))]
    [InlineData(nameof(EvaluateDecision.OperationBudget))]
    public async Task Ordinary_decision_inputs_retain_shared_variable_discovery_and_admission(string input)
    {
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();
        var analyzer = scope.ServiceProvider.GetRequiredService<IActivityDataSelectionAnalyzer>();
        var document = Document(input);
        Assert.Empty((await analyzer.AnalyzeAsync(document)).Findings);
        var discovery = await analyzer.AnalyzeAsync(document, new("evaluate", input));
        Assert.Null(discovery.Target!.Constraint);
        Assert.Contains(discovery.Sources, source => source.Binding.ExpressionType == "Variable");
    }

    private static ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        new WorkflowDesignValidationsFeature().ConfigureServices(services);
        new DecisionsWorkflowsDesignFeature().ConfigureServices(services);
        services.AddSingleton<IActivityDefinitionLookup>(new Catalog());
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static ActivityDataSelectionDocument Document(string input) => new(
        "WorkflowDefinition", "decision-workflow", "candidate-1",
        new ActivityNode("evaluate", "decision-version", [new(input,
            new ArgumentValue(new VariableReference("value"), "Variable"), null, null, null, null)], []),
        [], [new VariableDefinition("value", "Value", new("String"), null, null)], []);

    private sealed class Catalog : IActivityDefinitionLookup
    {
        private readonly ActivityDefinitionVersion _version = new("1.0.0", "decision-definition")
        {
            Id = "decision-version",
            Definition = new() { Id = "decision-definition", ActivityTypeKey = EvaluateDecision.ActivityType, DisplayName = "Evaluate decision" },
            Inputs = new[] { "Decision", "Inputs", "OperationBudget" }.Select(key =>
                new InputDefinition(key, key, new("String"), null, key, null, true))
        };
        public Task<IActivityDefinitionVersion?> FindVersion(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IActivityDefinitionVersion?>(versionId == _version.Id ? _version : null);
        public Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IActivityDefinitionVersion>(versionId == _version.Id ? _version : throw new ArgumentException(nameof(versionId)));
        public Task<IActivityDefinition> GetDefinition(string idOrActivityTypeKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IEnumerable<IActivityDefinition>> ListDefinitions(string? id = null, string? category = null,
            string? searchTerm = null, string? displayName = null, string? description = null, bool? tenantAgnostic = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IEnumerable<ActivityDefinitionVersionSummary>> ListVersions(string definitionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
