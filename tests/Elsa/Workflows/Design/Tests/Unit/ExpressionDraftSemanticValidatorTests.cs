using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Services;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Validators;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

public sealed class ExpressionDraftSemanticValidatorTests
{
    [Fact]
    public async Task Validates_json_string_sources_and_aggregates_provider_errors()
    {
        using var json = JsonDocument.Parse("\"bad source\"");
        var provider = new ErrorProvider();
        var validator = new ExpressionDraftSemanticValidator(new Resolver(provider), new LeafStructureService());
        var state = new WorkflowDefinitionState(
            [],
            new ActivityNode("node", "activity", [new("text", new(json.RootElement.Clone(), "JavaScript"), null, null, null, null)], []),
            [], [], null);

        var result = await validator.ValidateAsync(state, "draft", CancellationToken.None);

        Assert.Equal(ExpressionDraftValidationState.Errors, result.State);
        Assert.Equal("bad source", provider.Source);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("JavaScript/Semantic", diagnostic.Code);
        Assert.Equal("node/inputs/text", diagnostic.AuthoredPath);
    }

    [Fact]
    public async Task Extracts_source_from_a_portable_expression_definition_json_object()
    {
        using var json = JsonDocument.Parse("""{"type":"JavaScript","source":"bad embedded source"}""");
        var provider = new ErrorProvider();
        var validator = new ExpressionDraftSemanticValidator(new Resolver(provider), new LeafStructureService());
        var state = StateWith(json.RootElement.Clone());

        var result = await validator.ValidateAsync(state, "draft", CancellationToken.None);

        Assert.Equal(ExpressionDraftValidationState.Errors, result.State);
        Assert.Equal("bad embedded source", provider.Source);
    }

    [Fact]
    public async Task Treats_stale_and_unauthorized_provider_outcomes_as_non_valid()
    {
        var stale = await ValidateOutcome(ExpressionToolingOutcomeState.Stale);
        var unauthorized = await ValidateOutcome(ExpressionToolingOutcomeState.Unauthorized);

        Assert.Equal(ExpressionDraftValidationState.Stale, stale.State);
        Assert.Equal(ExpressionDraftValidationState.Unauthorized, unauthorized.State);
    }

    [Fact]
    public async Task Draft_adapter_contributes_known_expression_errors_to_promotion_validation()
    {
        var semantic = new ExpressionDraftSemanticValidator(new Resolver(new ErrorProvider()), new LeafStructureService());
        var adapter = new ExpressionDraftValidator(semantic);
        var draft = new WorkflowDefinitionDraftModel("draft", "definition", StateWith("bad source"));

        var errors = (await adapter.Validate(draft, CancellationToken.None)).ToArray();

        var error = Assert.Single(errors);
        Assert.Equal("node/inputs/text", error.Path);
        Assert.Equal("Expressions/JavaScript/Semantic", error.Type);
    }

    [Fact]
    public async Task Real_JavaScript_provider_rejects_balanced_invalid_grammar_for_full_draft_gates()
    {
        var provider = new JavaScriptExpressionToolingProvider();
        var validator = new ExpressionDraftSemanticValidator(new Resolver(provider), new LeafStructureService());

        var result = await validator.ValidateAsync(StateWith("const = 1;"), "draft", CancellationToken.None);

        Assert.Equal(ExpressionDraftValidationState.Errors, result.State);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("JavaScript/Syntax", diagnostic.Code);
        Assert.Equal("node/inputs/text", diagnostic.AuthoredPath);
    }

    private static WorkflowDefinitionState StateWith(object value) => new(
        [],
        new ActivityNode("node", "activity", [new("text", new(value, "JavaScript"), null, null, null, null)], []),
        [], [], null);

    private static async Task<ExpressionDraftValidationResult> ValidateOutcome(ExpressionToolingOutcomeState state)
    {
        var validator = new ExpressionDraftSemanticValidator(
            new Resolver(new OutcomeProvider(state)),
            new LeafStructureService());
        return await validator.ValidateAsync(StateWith("source"), "draft", CancellationToken.None);
    }

    private sealed class Resolver(IExpressionToolingProvider provider) : IExpressionToolingProviderResolver
    {
        public IExpressionToolingProvider? Find(string expressionType) => expressionType == provider.ExpressionType ? provider : null;
    }

    private sealed class ErrorProvider : IExpressionToolingProvider
    {
        public string? Source { get; private set; }
        public string ExpressionType => "JavaScript";
        public ExpressionToolingContractVersion SupportedVersion => ExpressionToolingContractVersion.V1;
        public ValueTask<ExpressionToolingOutcome<ExpressionToolingCapabilities>> GetCapabilitiesAsync(ExpressionToolingRequestScope scope, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ExpressionToolingOutcome<ExpressionToolingItems>> GetCompletionsAsync(ExpressionCompletionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ExpressionToolingOutcome<ExpressionHover>> GetHoverAsync(ExpressionHoverRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ExpressionToolingOutcome<ExpressionDiagnosticSet>> ValidateAsync(ExpressionValidationRequest request, CancellationToken cancellationToken)
        {
            Source = request.Source;
            var diagnostic = new ExpressionDiagnostic("JavaScript/Semantic", ExpressionDiagnosticSeverity.Error, "Invalid expression.", request.Scope.Document.DocumentRevision);
            return ValueTask.FromResult(ExpressionToolingOutcome<ExpressionDiagnosticSet>.Success(new([diagnostic]), ExpressionToolingContractVersion.V1, request.Scope.Document.DocumentRevision, request.Scope.Context.ContextRevision));
        }
    }

    private sealed class OutcomeProvider(ExpressionToolingOutcomeState state) : IExpressionToolingProvider
    {
        public string ExpressionType => "JavaScript";
        public ExpressionToolingContractVersion SupportedVersion => ExpressionToolingContractVersion.V1;
        public ValueTask<ExpressionToolingOutcome<ExpressionToolingCapabilities>> GetCapabilitiesAsync(ExpressionToolingRequestScope scope, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ExpressionToolingOutcome<ExpressionToolingItems>> GetCompletionsAsync(ExpressionCompletionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ExpressionToolingOutcome<ExpressionHover>> GetHoverAsync(ExpressionHoverRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ExpressionToolingOutcome<ExpressionDiagnosticSet>> ValidateAsync(ExpressionValidationRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ExpressionToolingOutcome<ExpressionDiagnosticSet>.Failure(
                state,
                ExpressionToolingContractVersion.V1,
                state.ToString(),
                documentRevision: request.Scope.Document.DocumentRevision,
                contextRevision: request.Scope.Context.ContextRevision));
    }

    private sealed class LeafStructureService : IActivityStructureService
    {
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) => [];
        public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];
        public bool SupportsScopedVariables(ActivityNode activity) => false;
        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => throw new NotSupportedException();
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => throw new NotSupportedException();
    }
}
