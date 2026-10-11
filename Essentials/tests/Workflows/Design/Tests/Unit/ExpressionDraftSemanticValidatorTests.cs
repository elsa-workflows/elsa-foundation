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
    /// <summary>Mirrors the product composition: JavaScript and Liquid are text-authored, everything else is not.</summary>
    private static readonly IExpressionDescriptor[] DefaultDescriptors =
    [
        new StubDescriptor("JavaScript", ExpressionEditingMode.Text),
        new StubDescriptor("Liquid", ExpressionEditingMode.Text),
        new StubDescriptor("Variable", ExpressionEditingMode.Reference),
        new StubDescriptor("Secret", ExpressionEditingMode.Reference)
    ];

    [Fact]
    public async Task Literal_string_values_do_not_require_a_text_tooling_provider()
    {
        var provider = new ErrorProvider();

        var result = await Validate(provider, StateWith("hello", "Literal"));

        Assert.Equal(ExpressionDraftValidationState.Valid, result.State);
        Assert.Null(provider.Source);
    }

    [Theory]
    [InlineData("Object")]
    [InlineData("Variable")]
    [InlineData("Input")]
    [InlineData("Secret")]
    public async Task Structured_and_reference_expression_types_do_not_require_a_text_tooling_provider(string expressionType)
    {
        var provider = new ErrorProvider();

        var result = await Validate(provider, StateWith("Msg", expressionType));

        Assert.Equal(ExpressionDraftValidationState.Valid, result.State);
        Assert.Null(provider.Source);
    }

    [Fact]
    public async Task Structured_object_arguments_are_skipped_whether_raw_or_stringified()
    {
        using var json = JsonDocument.Parse("""["GET"]""");

        var raw = await Validate(new ErrorProvider(), StateWith(json.RootElement.Clone(), "Object"));
        var stringified = await Validate(new ErrorProvider(), StateWith("""["GET"]""", "Object"));

        Assert.Equal(ExpressionDraftValidationState.Valid, raw.State);
        Assert.Equal(ExpressionDraftValidationState.Valid, stringified.State);
    }

    [Fact]
    public async Task Unknown_expression_types_are_skipped_rather_than_blocking_the_draft()
    {
        var result = await Validate(provider: null, StateWith("whatever", "Mystery"));

        Assert.Equal(ExpressionDraftValidationState.Valid, result.State);
    }

    [Fact]
    public async Task A_text_expression_type_without_a_registered_provider_is_genuinely_unavailable()
    {
        var validator = Validator(provider: null, [new StubDescriptor("Fake", ExpressionEditingMode.Text)]);

        var result = await validator.ValidateAsync(StateWith("source", "Fake"), "draft", CancellationToken.None);

        Assert.Equal(ExpressionDraftValidationState.Unavailable, result.State);
        Assert.Equal("provider-unavailable", result.Code);
    }

    [Fact]
    public async Task Known_errors_outrank_an_unavailable_text_provider()
    {
        var validator = Validator(new ErrorProvider(), [.. DefaultDescriptors, new StubDescriptor("Fake", ExpressionEditingMode.Text)]);
        var state = StateOf(
            Argument("text", "bad source", "JavaScript"),
            Argument("other", "source", "Fake"));

        var result = await validator.ValidateAsync(state, "draft", CancellationToken.None);

        Assert.Equal(ExpressionDraftValidationState.Errors, result.State);
        Assert.Equal("JavaScript/Semantic", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task Validates_json_string_sources_and_aggregates_provider_errors()
    {
        using var json = JsonDocument.Parse("\"bad source\"");
        var provider = new ErrorProvider();

        var result = await Validate(provider, StateWith(json.RootElement.Clone()));

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

        var result = await Validate(provider, StateWith(json.RootElement.Clone()));

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
        var adapter = new ExpressionDraftValidator(Validator(new ErrorProvider()));
        var draft = new WorkflowDefinitionDraftModel("draft", "definition", StateWith("bad source"));

        var errors = (await adapter.Validate(draft, CancellationToken.None)).ToArray();

        var error = Assert.Single(errors);
        Assert.Equal("node/inputs/text", error.Path);
        Assert.Equal("Expressions/JavaScript/Semantic", error.Type);
    }

    [Fact]
    public async Task Real_JavaScript_provider_rejects_balanced_invalid_grammar_for_full_draft_gates()
    {
        var result = await Validate(new JavaScriptExpressionToolingProvider(), StateWith("const = 1;"));

        Assert.Equal(ExpressionDraftValidationState.Errors, result.State);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("JavaScript/Syntax", diagnostic.Code);
        Assert.Equal("node/inputs/text", diagnostic.AuthoredPath);
    }

    [Fact]
    public async Task Real_JavaScript_provider_rejects_empty_source_for_full_draft_gates()
    {
        var result = await Validate(new JavaScriptExpressionToolingProvider(), StateWith(string.Empty));

        Assert.Equal(ExpressionDraftValidationState.Errors, result.State);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("JavaScript/Syntax", diagnostic.Code);
        Assert.Equal("node/inputs/text", diagnostic.AuthoredPath);
    }

    [Fact]
    public async Task A_case_variant_expression_type_still_reaches_the_real_provider()
    {
        var result = await Validate(new JavaScriptExpressionToolingProvider(), StateWith("const = 1;", "javascript"));

        Assert.Equal(ExpressionDraftValidationState.Errors, result.State);
        Assert.Equal("JavaScript/Syntax", Assert.Single(result.Diagnostics).Code);
    }

    private static ExpressionDraftSemanticValidator Validator(IExpressionToolingProvider? provider) =>
        Validator(provider, DefaultDescriptors);

    private static ExpressionDraftSemanticValidator Validator(IExpressionToolingProvider? provider, IReadOnlyCollection<IExpressionDescriptor> descriptors) =>
        new(new Resolver(provider), new DescriptorRegistry(descriptors), new LeafStructureService());

    private static ValueTask<ExpressionDraftValidationResult> Validate(IExpressionToolingProvider? provider, WorkflowDefinitionState state) =>
        Validator(provider).ValidateAsync(state, "draft", CancellationToken.None);

    private static ArgumentState Argument(string referenceKey, object? value, string expressionType) =>
        new(referenceKey, new(value, expressionType), null, null, null, null);

    private static WorkflowDefinitionState StateWith(object? value, string expressionType = "JavaScript") =>
        StateOf(Argument("text", value, expressionType));

    private static WorkflowDefinitionState StateOf(params ArgumentState[] inputs) =>
        new([], new ActivityNode("node", "activity", inputs, []), [], [], null);

    private static async Task<ExpressionDraftValidationResult> ValidateOutcome(ExpressionToolingOutcomeState state) =>
        await Validate(new OutcomeProvider(state), StateWith("source"));

    private sealed record StubDescriptor(string TypeName, ExpressionEditingMode EditingMode) : IExpressionDescriptor
    {
        public string DisplayName => TypeName;
        public IDictionary<string, object> Properties { get; } = new Dictionary<string, object>();
    }

    private sealed class DescriptorRegistry(IReadOnlyCollection<IExpressionDescriptor> descriptors) : IExpressionDescriptorRegistry
    {
        public void Add(IExpressionDescriptor descriptor) => throw new NotSupportedException();
        public void AddRange(IEnumerable<IExpressionDescriptor> items) => throw new NotSupportedException();
        public IEnumerable<IExpressionDescriptor> ListAll() => descriptors;
        public IExpressionDescriptor? Find(Func<IExpressionDescriptor, bool> predicate) => descriptors.FirstOrDefault(predicate);
        public IExpressionDescriptor? Find(string type) => descriptors.FirstOrDefault(descriptor => string.Equals(descriptor.TypeName, type, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class Resolver(IExpressionToolingProvider? provider) : IExpressionToolingProviderResolver
    {
        public IExpressionToolingProvider? Find(string expressionType) =>
            provider is not null && string.Equals(expressionType, provider.ExpressionType, StringComparison.OrdinalIgnoreCase) ? provider : null;
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
