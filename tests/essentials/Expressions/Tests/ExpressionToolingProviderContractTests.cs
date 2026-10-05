using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Expressions.JavaScript.Services;
using Elsa.Expressions.Liquid.Services;
using Elsa.Expressions.Services;
using Xunit;

namespace Elsa.Expressions.Tests;

public sealed class ExpressionToolingProviderContractTests
{
    [Fact]
    public void Resolver_routes_expression_types_case_insensitively_and_rejects_duplicates()
    {
        var javascript = new JavaScriptExpressionToolingProvider();
        var resolver = new ExpressionToolingProviderResolver([javascript]);

        Assert.Same(javascript, resolver.Find("JavaScript"));
        Assert.Same(javascript, resolver.Find("javascript"));
        Assert.Null(resolver.Find("Liquid"));
        Assert.Throws<ArgumentException>(() => resolver.Find(" "));
        Assert.Throws<ArgumentException>(() => new ExpressionToolingProviderResolver([javascript, new JavaScriptExpressionToolingProvider()]));
    }

    [Fact]
    public async Task Providers_accept_a_case_variant_of_their_own_expression_type()
    {
        var javascript = await new JavaScriptExpressionToolingProvider().ValidateAsync(new(CreateScope("javascript", []), "1 + 1"), CancellationToken.None);
        var liquidProvider = new LiquidExpressionToolingProvider();
        var liquid = await liquidProvider.ValidateAsync(new(CreateLiquidScope(liquidProvider, "liquid", []), "{{ 1 }}"), CancellationToken.None);

        Assert.NotEqual(ExpressionToolingOutcomeState.Incompatible, javascript.State);
        Assert.NotEqual(ExpressionToolingOutcomeState.Incompatible, liquid.State);
    }

    [Fact]
    public async Task Providers_return_only_context_symbols_and_never_need_an_evaluator()
    {
        var provider = new JavaScriptExpressionToolingProvider();
        var scope = CreateJavaScriptScope([new("input-name", "input", ExpressionSymbolKind.WorkflowInput, Documentation: "Visible input")]);

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
        var scope = CreateJavaScriptScope([new("customer", "customer", ExpressionSymbolKind.WorkflowInput, customerShape)]);

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
        var scope = CreateJavaScriptScope([
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
    public async Task JavaScript_exposes_frozen_empty_args_and_selected_standard_global_metadata()
    {
        var provider = new JavaScriptExpressionToolingProvider();
        var scope = CreateJavaScriptScope([]);

        var root = await provider.GetCompletionsAsync(new(scope, string.Empty, new(0, 0)), CancellationToken.None);
        var pow = await provider.GetCompletionsAsync(new(scope, "Math.po", new(0, 7)), CancellationToken.None);
        var parse = await provider.GetCompletionsAsync(new(scope, "JSON.pa", new(0, 8)), CancellationToken.None);
        var stringify = await provider.GetCompletionsAsync(new(scope, "JSON.str", new(0, 8)), CancellationToken.None);

        Assert.Contains(root.Payload!.Items, item => item.Label == "args");
        Assert.Contains(root.Payload.Items, item => item.Label == "Math");
        Assert.Contains(root.Payload.Items, item => item.Label == "JSON");
        Assert.Equal("pow(base, exponent): Number", Assert.Single(pow.Payload!.Items).Detail);
        Assert.Equal("parse(text, reviver?): Any", Assert.Single(parse.Payload!.Items).Detail);
        Assert.Equal("stringify(value, replacer?, space?): String?", Assert.Single(stringify.Payload!.Items).Detail);

        var powSignature = Assert.Single(JavaScriptRuntimeProfile.SupportedStandardGlobals.Single(symbol => symbol.Name == "Math.pow").Signatures!);
        Assert.Equal(new[] { "base", "exponent" }, powSignature.Parameters);
        var parseReturn = Assert.Single(JavaScriptRuntimeProfile.SupportedStandardGlobals.Single(symbol => symbol.Name == "JSON.parse").Signatures!).ReturnShape;
        Assert.Equal(ExpressionValueKind.Unknown, parseReturn!.Kind);
        Assert.True(parseReturn.IsNullable);
        var stringifyReturn = Assert.Single(JavaScriptRuntimeProfile.SupportedStandardGlobals.Single(symbol => symbol.Name == "JSON.stringify").Signatures!).ReturnShape;
        Assert.Equal(ExpressionValueKind.Scalar, stringifyReturn!.Kind);
        Assert.True(stringifyReturn.IsNullable);
    }

    [Fact]
    public void JavaScript_declares_a_stable_immutable_runtime_tooling_catalog()
    {
        var first = new JavaScriptExpressionToolingProvider().DeclaredCatalog;
        var second = new JavaScriptExpressionToolingProvider().DeclaredCatalog;

        Assert.NotNull(first);
        Assert.Same(JavaScriptRuntimeProfile.DeclaredToolingCatalog, first);
        Assert.Equal(first.Revision, second!.Revision);
        Assert.False(string.IsNullOrWhiteSpace(first.Revision));
        Assert.Contains(first.Symbols, symbol => symbol.SymbolId == "javascript:profile:Math");
        Assert.Contains(first.Symbols, symbol => symbol.SymbolId == "javascript:profile:JSON.parse" && symbol.Signatures is not null);
        Assert.Contains(first.Symbols, symbol => symbol.SymbolId == "javascript:getVariable" && symbol.Signatures is not null);
        var symbols = Assert.IsAssignableFrom<IList<ExpressionSymbol>>(first.Symbols);
        Assert.Throws<NotSupportedException>(() => symbols[0] = new("replacement", "replacement", ExpressionSymbolKind.Function));
    }

    [Fact]
    public async Task JavaScript_does_not_restore_profile_symbols_omitted_from_authoritative_context()
    {
        var provider = new JavaScriptExpressionToolingProvider();
        var catalog = JavaScriptRuntimeProfile.DeclaredToolingCatalog.Symbols;
        var authorized = new[]
        {
            catalog.Single(symbol => symbol.SymbolId == "javascript:profile:Math"),
            catalog.Single(symbol => symbol.SymbolId == "javascript:profile:JSON")
        };
        var scope = CreateScope("JavaScript", authorized);

        var mathCompletion = await provider.GetCompletionsAsync(new(scope, "Math.po", new(0, 7)), CancellationToken.None);
        var mathHover = await provider.GetHoverAsync(new(scope, "Math.pow", new(0, 8)), CancellationToken.None);
        var jsonCompletion = await provider.GetCompletionsAsync(new(scope, "JSON.pa", new(0, 7)), CancellationToken.None);

        Assert.Equal(ExpressionToolingOutcomeState.SupportedEmpty, mathCompletion.State);
        Assert.Equal(ExpressionToolingOutcomeState.SupportedEmpty, mathHover.State);
        Assert.Equal(ExpressionToolingOutcomeState.SupportedEmpty, jsonCompletion.State);
    }

    [Fact]
    public async Task JavaScript_does_not_duplicate_runtime_getVariable_for_a_variable_with_the_same_generated_name()
    {
        var provider = new JavaScriptExpressionToolingProvider();
        var scope = CreateJavaScriptScope([new("variable", "variable", ExpressionSymbolKind.Variable)]);

        var root = await provider.GetCompletionsAsync(new(scope, string.Empty, new(0, 0)), CancellationToken.None);

        Assert.Single(root.Payload!.Items, item => item.Label == "getVariable");
    }

    [Fact]
    public async Task JavaScript_does_not_project_the_variable_accessor_without_visible_variable_bindings()
    {
        var provider = new JavaScriptExpressionToolingProvider();
        var scope = CreateJavaScriptScope([]);

        var completion = await provider.GetCompletionsAsync(new(scope, "getVariable", new(0, 11)), CancellationToken.None);

        Assert.Equal(ExpressionToolingOutcomeState.SupportedEmpty, completion.State);
    }

    [Fact]
    public async Task JavaScript_getter_metadata_preserves_runtime_support_for_digit_leading_variable_keys()
    {
        var provider = new JavaScriptExpressionToolingProvider();
        var scope = CreateJavaScriptScope([new("numeric-key", "123", ExpressionSymbolKind.Variable)]);

        var completion = await provider.GetCompletionsAsync(new(scope, "get12", new(0, 5)), CancellationToken.None);

        Assert.Equal("get123", Assert.Single(completion.Payload!.Items).Label);
    }

    [Theory]
    [InlineData("Date.now()", true)]
    [InlineData("Math.random()", true)]
    [InlineData("globalThis.Math.random()", true)]
    [InlineData("typeof Date", false)]
    [InlineData("typeof Math.random", false)]
    [InlineData("typeof globalThis.Math.random", false)]
    [InlineData("typeof Date.now", true)]
    [InlineData("window.location.href", true)]
    [InlineData("globalThis.document.title", true)]
    [InlineData("typeof Math?.random", false)]
    [InlineData("typeof globalThis?.Math.random", false)]
    [InlineData("typeof Date?.now", false)]
    [InlineData("typeof globalThis?.Date?.now", false)]
    [InlineData("typeof globalThis.Date?.now", false)]
    [InlineData("typeof Date?.()", false)]
    [InlineData("typeof Date?.now()", false)]
    [InlineData("typeof Date?.now(Math.random())", false)]
    [InlineData("typeof Date?.[Math.random()]", false)]
    [InlineData("typeof globalThis.Date?.[Math.random()]", false)]
    [InlineData("typeof Math.random?.(Date.now())", false)]
    [InlineData("typeof (Date?.now ?? 'fallback')", false)]
    [InlineData("typeof (Date?.now + 'fallback')", false)]
    [InlineData("typeof ('fallback' ?? Date.now)", false)]
    [InlineData("typeof (false ?? Math.random())", false)]
    [InlineData("typeof (0 ?? Date.now)", false)]
    [InlineData("typeof (null ?? Date?.now)", false)]
    [InlineData("typeof (Date?.now ?? null)?.[Math.random()]", false)]
    [InlineData("typeof (Date?.now ?? null)?.(Math.random())", false)]
    [InlineData("typeof (null ?? Date.now)", true)]
    [InlineData("typeof (globalThis ?? Date.now)", false)]
    [InlineData("typeof (('x').missing ?? Date.now)", true)]
    [InlineData("typeof (('x')?.missing ?? Date.now)", true)]
    [InlineData("(() => { Math = null; return typeof (globalThis.Math ?? Date.now); })()", true)]
    [InlineData("(() => { globalThis.Math = null; return typeof (globalThis.Math ?? Date.now); })()", true)]
    [InlineData("(() => { globalThis.globalThis = { Date: {} }; return typeof globalThis.Date?.[Math.random()]; })()", true)]
    [InlineData("(() => { globalThis['globalThis'] = { Date: {} }; return typeof globalThis.Date?.[Math.random()]; })()", true)]
    [InlineData("(() => { globalThis.globalThis = null; return typeof (globalThis ?? Date.now); })()", true)]
    [InlineData("(() => { globalThis[`globalThis`] = null; return typeof (globalThis ?? Date.now); })()", true)]
    [InlineData("(() => { globalThis[`Math`] = null; return typeof (globalThis.Math ?? Date.now); })()", true)]
    [InlineData("(() => { globalThis[('globalThis')] = null; return typeof (globalThis ?? Date.now); })()", true)]
    [InlineData("(() => { const key = 'Math'; globalThis[key] = null; return typeof (globalThis.Math ?? Date.now); })()", true)]
    [InlineData("((globalThis) => { globalThis[`Math`] = null; return typeof (Math ?? Date.now); })({})", false)]
    [InlineData("typeof ((Date?.now + 'x').missing ?? Math.random())", true)]
    [InlineData("typeof Date.now?.()", true)]
    [InlineData("typeof globalThis?.Date.now", true)]
    [InlineData("typeof (Date?.now).toString", true)]
    [InlineData("typeof ((Date?.now).toString ?? 'fallback')", true)]
    [InlineData("typeof Math[Math.random()]", true)]
    [InlineData("typeof Math.abs(Math.random())", true)]
    [InlineData("typeof (() => Math.random())?.()", true)]
    [InlineData("typeof (Date?.now ?? Math.random())", true)]
    [InlineData("typeof (Date?.now + Math.random())", true)]
    [InlineData("typeof (('fallback' ?? Date.now) + Math.random())", true)]
    [InlineData("typeof ('x' + (Date?.now + Math.random()))", true)]
    [InlineData("typeof window?.location", true)]
    [InlineData("Math[key]()", false)]
    [InlineData("globalThis[ambientName]()", false)]
    [InlineData("require('module')", true)]
    [InlineData("Buffer.from('text')", true)]
    [InlineData("/* Date.now() */ 'Math.random'", false)]
    [InlineData("({ Date: 'Date', text: 'Math.random' }).Date", false)]
    [InlineData("(() => { const { Date } = {}; return Date.now(); })()", false)]
    [InlineData("(() => { const { value = Math.random() } = {}; return value; })()", true)]
    [InlineData("({ Date } = {})", true)]
    [InlineData("({ value: Math.random } = {})", true)]
    [InlineData("((Date) => Date.now())({ now: () => 1 })", false)]
    [InlineData("((Math) => Math.random())({ random: () => 1 })", false)]
    [InlineData("((Date) => Date)(Date.now())", true)]
    [InlineData("((() => { const Date = {}; return Date; })(), Date.now())", true)]
    [InlineData("(() => { { let Date = {}; Date.now(); } Date.now(); })()", true)]
    [InlineData("(() => { switch (1) { case 1: let Date = {}; Date.now(); } Date.now(); })()", true)]
    [InlineData("(() => { switch (1) { case 1: Date.now(); let Date = {}; } })()", false)]
    [InlineData("(function () { return Math.random(); var Math = {}; })()", false)]
    [InlineData("(function (Date = Math.random()) { var Math = {}; return Date; })()", true)]
    [InlineData("(() => { try { throw {}; } catch (Date) { return Date.now(); } })()", false)]
    [InlineData("(() => { try { throw {}; } catch ({ [Date]: value }) { return value; } })()", true)]
    [InlineData("(() => { function Date() {} return Date.now(); })()", false)]
    [InlineData("(() => { class Date { static now() {} } return Date.now(); })()", false)]
    [InlineData("(() => { class Example { static { Math.random(); var Math = {}; } } })()", false)]
    [InlineData("(() => { class Example { static { var Math = {}; } } Math.random(); })()", true)]
    [InlineData("((function Date() { return Date.now(); }), Date.now())", true)]
    [InlineData("((class Date { value() { return Date.now(); } }), Date.now())", true)]
    [InlineData("(() => { Date: return 1; })()", false)]
    [InlineData("(() => { Date: { break Date; } return 1; })()", false)]
    [InlineData("(() => { Date: for (;;) { continue Date; } })()", false)]
    [InlineData("(() => { Date: Math.random(); })()", true)]
    [InlineData("(() => { for ([Date] of values) {} return 1; })()", true)]
    [InlineData("(() => { for ({ value: Date } of values) {} return 1; })()", true)]
    [InlineData("(() => { for ({ ...Date } of values) {} return 1; })()", true)]
    [InlineData("(() => { for ([Date] in values) {} return 1; })()", true)]
    [InlineData("(() => { let Date; for ([Date] of values) {} return 1; })()", false)]
    [InlineData("(() => { for (const [Date] of values) {} return 1; })()", false)]
    [InlineData("(Math = { random: () => 7 }, Math.random())", false)]
    [InlineData("(globalThis.Math = { random: () => 7 }, globalThis.Math.random())", false)]
    [InlineData("(globalThis['Math'] = { random: () => 7 }, globalThis.Math.random())", false)]
    [InlineData("(globalThis = { Math: { random: () => 7 } }, globalThis.Math.random())", false)]
    [InlineData("(globalThis = { Date: { now: () => 7 } }, globalThis.Date.now())", false)]
    [InlineData("(Math = {}, Date.now())", true)]
    [InlineData("(globalThis = {}, Date.now())", true)]
    [InlineData("(() => { globalThis = { Date: {} }; return typeof globalThis.Date?.[Math.random()]; })()", true)]
    [InlineData("((Date) => typeof Date?.[Math.random()])({})", true)]
    [InlineData("(() => { Math = { random: x => x }; return typeof Math.random?.(Date.now()); })()", true)]
    public async Task JavaScript_reports_only_proven_unavailable_ambient_references(string source, bool expectedDiagnostic)
    {
        var provider = new JavaScriptExpressionToolingProvider();
        var scope = CreateScope("JavaScript", []);

        var result = await provider.ValidateAsync(new(scope, source), CancellationToken.None);

        Assert.Equal(expectedDiagnostic, result.Payload!.Diagnostics.Any(diagnostic => diagnostic.Code == "JavaScript/AmbientCapability"));
        if (expectedDiagnostic)
        {
            var diagnostic = Assert.Single(result.Payload.Diagnostics);
            Assert.Equal("JavaScript/AmbientCapability", diagnostic.Code);
            Assert.Equal(ExpressionDiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal("revision", diagnostic.DocumentRevision);
            Assert.NotNull(diagnostic.Range);
        }
        else
            Assert.Empty(result.Payload.Diagnostics);
    }

    [Theory]
    [InlineData("typeof ((Date?.now + 'x').missing ?? Math.random())")]
    [InlineData("typeof (('fallback' ?? Date.now) + Math.random())")]
    [InlineData("typeof ('x' + (Date?.now + Math.random()))")]
    public async Task JavaScript_composed_probes_report_the_executed_capability_range(string source)
    {
        var provider = new JavaScriptExpressionToolingProvider();
        var result = await provider.ValidateAsync(new(CreateScope("JavaScript", []), source), CancellationToken.None);
        var diagnostic = Assert.Single(result.Payload!.Diagnostics);
        var start = source.IndexOf("Math.random", StringComparison.Ordinal);

        Assert.Equal("JavaScript/AmbientCapability", diagnostic.Code);
        Assert.Equal(new ExpressionToolingRange(new(0, start), new(0, start + "Math.random".Length)), diagnostic.Range);
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
        var javascriptScope = CreateJavaScriptScope([symbol]);
        var javascriptCompletion = await javascript.GetCompletionsAsync(
            new(javascriptScope, "args.producer.", new(0, "args.producer.".Length)),
            CancellationToken.None);
        var javascriptHover = await javascript.GetHoverAsync(
            new(javascriptScope, "args.producer.output", new(0, "args.producer.output".Length)),
            CancellationToken.None);

        var liquid = new LiquidExpressionToolingProvider();
        var liquidScope = CreateLiquidScope(liquid, "Liquid", [symbol]);
        var liquidCompletion = await liquid.GetCompletionsAsync(
            new(liquidScope, "{{ producer.", new(0, "{{ producer.".Length)),
            CancellationToken.None);
        var liquidHover = await liquid.GetHoverAsync(
            new(liquidScope, "{{ producer.output", new(0, "{{ producer.output".Length)),
            CancellationToken.None);

        Assert.Equal("output", Assert.Single(javascriptCompletion.Payload!.Items).Label);
        Assert.Contains("Producer output.", javascriptHover.Payload!.Contents, StringComparison.Ordinal);
        Assert.Contains(liquidCompletion.Payload!.Items, item => item.Label == "output");
        Assert.Contains("Producer output.", liquidHover.Payload!.Contents, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Liquid_reports_unclosed_output_without_rendering()
    {
        var provider = new LiquidExpressionToolingProvider();
        var scope = CreateLiquidScope(provider, "Liquid", []);

        var result = await provider.ValidateAsync(new(scope, "Hello {{ name"), CancellationToken.None);

        Assert.Equal(ExpressionToolingOutcomeState.Success, result.State);
        Assert.Equal("Liquid/Syntax", Assert.Single(result.Payload!.Diagnostics).Code);
    }

    [Fact]
    public async Task Liquid_exposes_the_composed_Fluid_tags_and_filters()
    {
        var provider = new LiquidExpressionToolingProvider();
        var scope = CreateLiquidScope(provider, "Liquid", []);

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
        var scope = CreateLiquidScope(provider, "Liquid", []);

        var result = await provider.ValidateAsync(new(scope, "{% if value %}Hello"), CancellationToken.None);

        Assert.Equal(ExpressionToolingOutcomeState.Success, result.State);
        Assert.Equal("Liquid/Syntax", Assert.Single(result.Payload!.Diagnostics).Code);
    }

    [Fact]
    public async Task Providers_rank_expected_result_types_and_Liquid_resolves_multiline_hover()
    {
        var provider = new LiquidExpressionToolingProvider();
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
            provider.DeclaredCatalog.Symbols.Concat(symbols).ToArray(),
            new(),
            ExpectedResultType: "Int32");
        var scope = new ExpressionToolingRequestScope(ExpressionToolingContractVersion.V1, document, context);

        var completions = await provider.GetCompletionsAsync(new(scope, "{{ ", new(0, 3)), CancellationToken.None);
        var hover = await provider.GetHoverAsync(new(scope, "{{\namount", new(1, 3)), CancellationToken.None);

        Assert.Equal("amount", completions.Payload!.Items[0].Label);
        Assert.Contains(completions.Payload.Items, item => item.Label == "alpha");
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

    private static ExpressionToolingRequestScope CreateJavaScriptScope(IReadOnlyList<ExpressionSymbol> symbols) =>
        CreateScope("JavaScript", JavaScriptRuntimeProfile.DeclaredToolingCatalog.Symbols.Concat(symbols).ToArray());

    private static ExpressionToolingRequestScope CreateLiquidScope(
        LiquidExpressionToolingProvider provider,
        string type,
        IReadOnlyList<ExpressionSymbol> symbols) =>
        CreateScope(type, provider.DeclaredCatalog.Symbols.Concat(symbols).ToArray());
}
