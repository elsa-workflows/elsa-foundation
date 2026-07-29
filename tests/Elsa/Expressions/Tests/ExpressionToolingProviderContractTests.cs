using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Services;
using Elsa.Expressions.Liquid.Services;
using Elsa.Expressions.Services;
using Xunit;

namespace Elsa.Expressions.Tests;

public sealed class ExpressionToolingProviderContractTests
{
    [Fact]
    public void Resolver_routes_exact_expression_type_and_rejects_duplicates()
    {
        var javascript = new JavaScriptExpressionToolingProvider();
        var resolver = new ExpressionToolingProviderResolver([javascript]);

        Assert.Same(javascript, resolver.Find("JavaScript"));
        Assert.Null(resolver.Find("javascript"));
        Assert.Throws<ArgumentException>(() => resolver.Find(" "));
        Assert.Throws<ArgumentException>(() => new ExpressionToolingProviderResolver([javascript, new JavaScriptExpressionToolingProvider()]));
    }

    [Fact]
    public async Task Providers_return_only_context_symbols_and_never_need_an_evaluator()
    {
        var provider = new JavaScriptExpressionToolingProvider();
        var scope = CreateScope("JavaScript", [new("input-name", "input", ExpressionSymbolKind.WorkflowInput, Documentation: "Visible input")]);

        var completion = await provider.GetCompletionsAsync(new(scope, "args.inp", new(0, 8)), CancellationToken.None);

        Assert.Equal(ExpressionToolingOutcomeState.Success, completion.State);
        Assert.Equal("input", Assert.Single(completion.Payload!.Items).Label);
    }

    [Fact]
    public async Task JavaScript_completes_and_hovers_bounded_nested_metadata()
    {
        var provider = new JavaScriptExpressionToolingProvider();
        var customerShape = new ExpressionValueShape(
            "Customer",
            ExpressionValueKind.Object,
            Members:
            [
                new("Name", new("String", ExpressionValueKind.Scalar, false), "Customer name."),
                new("Address", new("Address", ExpressionValueKind.Object, Members:
                [
                    new("City", new("String", ExpressionValueKind.Scalar, false))
                ]))
            ]);
        var scope = CreateScope("JavaScript", [new("customer", "customer", ExpressionSymbolKind.WorkflowInput, customerShape)]);

        var completion = await provider.GetCompletionsAsync(
            new(scope, "args.customer.Addr", new(0, "args.customer.Addr".Length)),
            CancellationToken.None);
        var nested = await provider.GetCompletionsAsync(
            new(scope, "args.customer.Address.C", new(0, "args.customer.Address.C".Length)),
            CancellationToken.None);
        var hover = await provider.GetHoverAsync(
            new(scope, "args.customer.Name", new(0, "args.customer.Name".Length)),
            CancellationToken.None);

        Assert.Equal("Address", Assert.Single(completion.Payload!.Items).Label);
        Assert.Equal("City", Assert.Single(nested.Payload!.Items).Label);
        Assert.Contains("Customer name.", hover.Payload!.Contents, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JavaScript_projects_visible_variables_to_the_runtime_owned_namespace_and_getters()
    {
        var provider = new JavaScriptExpressionToolingProvider();
        var customerShape = new ExpressionValueShape(
            "Customer",
            ExpressionValueKind.Object,
            Members: [new("name", new("String", ExpressionValueKind.Scalar, false), "Customer name.")]);
        var scope = CreateScope("JavaScript", [
            new("current-value", "currentValue", ExpressionSymbolKind.Variable, new("Int32")),
            new("customer", "customer", ExpressionSymbolKind.Variable, customerShape)
        ]);

        var root = await provider.GetCompletionsAsync(new(scope, "getCur", new(0, 6)), CancellationToken.None);
        var member = await provider.GetCompletionsAsync(new(scope, "variables.current", new(0, 17)), CancellationToken.None);
        var getterMember = await provider.GetCompletionsAsync(
            new(scope, "getCustomer().na", new(0, "getCustomer().na".Length)),
            CancellationToken.None);
        var getterHover = await provider.GetHoverAsync(
            new(scope, "getCustomer().name", new(0, "getCustomer().name".Length)),
            CancellationToken.None);

        var rootGetter = Assert.Single(root.Payload!.Items);
        Assert.Equal("getCurrentValue", rootGetter.Label);
        Assert.Equal("getCurrentValue()", rootGetter.Detail);
        Assert.Equal("currentValue", Assert.Single(member.Payload!.Items).Label);
        Assert.Equal("name", Assert.Single(getterMember.Payload!.Items).Label);
        Assert.Contains("Customer name.", getterHover.Payload!.Contents, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Providers_resolve_dotted_activity_results_as_nested_namespaces()
    {
        var symbol = new ExpressionSymbol(
            "activity-result:producer:output",
            "producer.output",
            ExpressionSymbolKind.ActivityResult,
            new("String", ExpressionValueKind.Scalar, false),
            "Producer output.");

        var javascript = new JavaScriptExpressionToolingProvider();
        var javascriptScope = CreateScope("JavaScript", [symbol]);
        var javascriptCompletion = await javascript.GetCompletionsAsync(
            new(javascriptScope, "args.producer.", new(0, "args.producer.".Length)),
            CancellationToken.None);
        var javascriptHover = await javascript.GetHoverAsync(
            new(javascriptScope, "args.producer.output", new(0, "args.producer.output".Length)),
            CancellationToken.None);

        var liquid = new LiquidExpressionToolingProvider();
        var liquidScope = CreateScope("Liquid", [symbol]);
        var liquidCompletion = await liquid.GetCompletionsAsync(
            new(liquidScope, "producer.", new(0, "producer.".Length)),
            CancellationToken.None);

        Assert.Equal("output", Assert.Single(javascriptCompletion.Payload!.Items).Label);
        Assert.Contains("Producer output.", javascriptHover.Payload!.Contents, StringComparison.Ordinal);
        Assert.Contains(liquidCompletion.Payload!.Items, item => item.Label == "output");
    }

    [Fact]
    public async Task Liquid_reports_unclosed_output_without_rendering()
    {
        var provider = new LiquidExpressionToolingProvider();
        var scope = CreateScope("Liquid", []);

        var result = await provider.ValidateAsync(new(scope, "Hello {{ name"), CancellationToken.None);

        Assert.Equal(ExpressionToolingOutcomeState.Success, result.State);
        Assert.Equal("Liquid/Syntax", Assert.Single(result.Payload!.Diagnostics).Code);
    }

    [Fact]
    public async Task Liquid_exposes_the_composed_Fluid_tags_and_filters()
    {
        var provider = new LiquidExpressionToolingProvider();
        var scope = CreateScope("Liquid", []);

        var filter = await provider.GetCompletionsAsync(
            new(scope, "{{ value | upc", new(0, "{{ value | upc".Length)),
            CancellationToken.None);
        var tag = await provider.GetCompletionsAsync(
            new(scope, "{% cap", new(0, "{% cap".Length)),
            CancellationToken.None);

        Assert.Contains(filter.Payload!.Items, item =>
            item.Label == "upcase" && item.Kind == ExpressionSymbolKind.Filter);
        Assert.Contains(tag.Payload!.Items, item =>
            item.Label == "capture" && item.Kind == ExpressionSymbolKind.Tag);
    }

    [Fact]
    public async Task JavaScript_validation_ignores_delimiters_inside_strings_and_comments()
    {
        var provider = new JavaScriptExpressionToolingProvider();
        var scope = CreateScope("JavaScript", []);

        var result = await provider.ValidateAsync(
            new(scope, "({ text: '{', other: `[`, value: /* } */ '{' })"),
            CancellationToken.None);

        Assert.Equal(ExpressionToolingOutcomeState.SupportedEmpty, result.State);
        Assert.Empty(result.Payload!.Diagnostics);
    }

    [Fact]
    public async Task JavaScript_validation_reports_balanced_but_invalid_grammar_without_evaluating()
    {
        var provider = new JavaScriptExpressionToolingProvider();
        var scope = CreateScope("JavaScript", []);

        var result = await provider.ValidateAsync(new(scope, "const = 1;"), CancellationToken.None);

        Assert.Equal(ExpressionToolingOutcomeState.Success, result.State);
        var diagnostic = Assert.Single(result.Payload!.Diagnostics);
        Assert.Equal("JavaScript/Syntax", diagnostic.Code);
        Assert.Equal(ExpressionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.NotNull(diagnostic.Range);
    }

    [Theory]
    [InlineData("return 1;")]
    [InlineData("const value = 1; value")]
    public async Task JavaScript_validation_rejects_statement_bodies_that_runtime_expression_evaluation_rejects(string source)
    {
        var provider = new JavaScriptExpressionToolingProvider();
        var scope = CreateScope("JavaScript", []);

        var result = await provider.ValidateAsync(new(scope, source), CancellationToken.None);

        Assert.Equal(ExpressionToolingOutcomeState.Success, result.State);
        Assert.Equal("JavaScript/Syntax", Assert.Single(result.Payload!.Diagnostics).Code);
    }

    [Fact]
    public async Task JavaScript_validation_accepts_anonymous_function_expressions_like_runtime()
    {
        var provider = new JavaScriptExpressionToolingProvider();
        var scope = CreateScope("JavaScript", []);

        var result = await provider.ValidateAsync(
            new(scope, "function () { return 1; }"),
            CancellationToken.None);

        Assert.Equal(ExpressionToolingOutcomeState.SupportedEmpty, result.State);
        Assert.Empty(result.Payload!.Diagnostics);
    }

    [Fact]
    public async Task Liquid_uses_the_parser_for_tag_syntax_without_rendering()
    {
        var provider = new LiquidExpressionToolingProvider();
        var scope = CreateScope("Liquid", []);

        var result = await provider.ValidateAsync(new(scope, "{% if value %}Hello"), CancellationToken.None);

        Assert.Equal(ExpressionToolingOutcomeState.Success, result.State);
        Assert.Equal("Liquid/Syntax", Assert.Single(result.Payload!.Diagnostics).Code);
    }

    [Fact]
    public async Task Providers_rank_expected_result_types_and_Liquid_resolves_multiline_hover()
    {
        var symbols = new ExpressionSymbol[]
        {
            new("string", "alpha", ExpressionSymbolKind.WorkflowInput, new("String")),
            new("number", "amount", ExpressionSymbolKind.WorkflowInput, new("Int32"), "Current amount.")
        };
        var document = new ExpressionAuthoringDocument("doc", "draft", "node", "text", "Liquid", "revision", "Int32");
        var context = new ExpressionAuthoringContext(
            ExpressionToolingContractVersion.V1,
            document,
            "context",
            "catalog",
            symbols,
            new(),
            ExpectedResultType: "Int32");
        var scope = new ExpressionToolingRequestScope(ExpressionToolingContractVersion.V1, document, context);
        var provider = new LiquidExpressionToolingProvider();

        var completions = await provider.GetCompletionsAsync(new(scope, string.Empty, new(0, 0)), CancellationToken.None);
        var hover = await provider.GetHoverAsync(new(scope, "first\namount", new(1, 3)), CancellationToken.None);

        Assert.Equal("amount", completions.Payload!.Items[0].Label);
        Assert.Contains("Current amount.", hover.Payload!.Contents, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Provider_returns_incompatible_for_a_different_expression_type()
    {
        var provider = new JavaScriptExpressionToolingProvider();
        var result = await provider.ValidateAsync(new(CreateScope("Liquid", []), "value"), CancellationToken.None);

        Assert.Equal(ExpressionToolingOutcomeState.Incompatible, result.State);
        Assert.Null(result.Payload);
    }

    private static ExpressionToolingRequestScope CreateScope(string type, IReadOnlyList<ExpressionSymbol> symbols)
    {
        var document = new ExpressionAuthoringDocument("doc", "draft", "node", "text", type, "revision");
        var context = new ExpressionAuthoringContext(ExpressionToolingContractVersion.V1, document, "context", "catalog", symbols, new());
        return new(ExpressionToolingContractVersion.V1, document, context);
    }
}
