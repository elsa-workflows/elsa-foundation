using System.Collections.Immutable;
using System.Text.Encodings.Web;
using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.Liquid;
using Elsa.Expressions.Liquid.Services;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Primitives.Models;
using Fluid;
using Fluid.Ast;
using Fluid.Values;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.Tests;

public sealed class LiquidExpressionProfileTests
{
    private static readonly IReadOnlyDictionary<string, JsonElement> NoParameters =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    [Fact]
    public void Default_catalog_matches_the_effective_Fluid_2_31_registries()
    {
        var profile = LiquidExpressionProfile.Default;
        var parser = profile.CreateParser();
        var options = profile.CreateTemplateOptions();
        var catalog = profile.ToolingCatalog;
        var symbols = catalog.Symbols;

        Assert.False(string.IsNullOrWhiteSpace(catalog.Revision));
        Assert.Equal(
            parser.RegisteredTags.Keys.Where(name => name != "#").OrderBy(name => name, StringComparer.Ordinal),
            symbols.Where(symbol => symbol.Kind == ExpressionSymbolKind.Tag).Select(symbol => symbol.Name));
        Assert.Equal(
            options.Filters.Select(filter => filter.Key)
                .Where(name => name is not ("date" or "format_date" or "time_zone"))
                .OrderBy(name => name, StringComparer.Ordinal),
            symbols.Where(symbol => symbol.Kind == ExpressionSymbolKind.Filter).Select(symbol => symbol.Name));
        Assert.DoesNotContain(symbols, symbol => symbol.Name is "date" or "format_date" or "time_zone" or "include" or "render" or "macro" or "from");
        Assert.Equal(symbols.Count, symbols.Select(symbol => symbol.SymbolId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("append(value): String", Assert.Single(symbols.Single(symbol => symbol.Name == "append").Signatures!).Display);
        Assert.Equal("if condition", Assert.Single(symbols.Single(symbol => symbol.Name == "if").Signatures!).Display);
        Assert.IsType<ImmutableArray<ExpressionSymbol>>(symbols);
    }

    [Fact]
    public void Trusted_profile_snapshots_mutable_input_catalog_metadata()
    {
        var parameters = new List<string> { "value" };
        var symbols = LiquidExpressionProfile.Default.ToolingCatalog.Symbols
            .Select(symbol => symbol.Name == "append"
                ? symbol with
                {
                    Signatures = [Assert.Single(symbol.Signatures!) with { Parameters = parameters }]
                }
                : symbol)
            .ToList();
        var profile = LiquidExpressionProfile.Create("host-snapshot-9f22", symbols);

        parameters[0] = "changed";
        symbols.Clear();
        var append = Assert.Single(profile.ToolingCatalog.Symbols, symbol => symbol.Name == "append");
        var signature = Assert.Single(append.Signatures!);

        Assert.Equal("value", Assert.Single(signature.Parameters!));
        Assert.IsType<ImmutableArray<string>>(signature.Parameters);
        Assert.IsType<ImmutableArray<ExpressionSymbol>>(profile.ToolingCatalog.Symbols);
    }

    [Fact]
    public void Profile_factories_return_fresh_fluid_objects_with_binding_pure_defaults()
    {
        var profile = LiquidExpressionProfile.Default;
        var firstParser = profile.CreateParser();
        var secondParser = profile.CreateParser();
        var firstOptions = profile.CreateTemplateOptions();
        var secondOptions = profile.CreateTemplateOptions();

        Assert.NotSame(firstParser, secondParser);
        Assert.NotSame(firstOptions, secondOptions);
        Assert.NotSame(firstOptions.Filters, secondOptions.Filters);
        Assert.Equal(DateTimeOffset.UnixEpoch, firstOptions.Now());
        Assert.Equal(TimeZoneInfo.Utc, firstOptions.TimeZone);
        Assert.Equal(System.Globalization.CultureInfo.InvariantCulture, firstOptions.CultureInfo);
        Assert.Null(firstOptions.FileProvider);
        Assert.False(firstParser.RegisteredTags.ContainsKey("include"));
        Assert.False(firstParser.RegisteredTags.ContainsKey("render"));
        Assert.False(firstParser.RegisteredTags.ContainsKey("macro"));
        Assert.False(firstParser.RegisteredTags.ContainsKey("from"));
        Assert.False(firstParser.TryParse("{{ value() }}", out _, out _));
        Assert.False(firstParser.TryParse("{% include 'private' %}", out _, out _));
        Assert.False(firstParser.TryParse("{% render 'private' %}", out _, out _));
        Assert.False(firstParser.TryParse("{% macro example() %}x{% endmacro %}", out _, out _));
        Assert.False(firstParser.TryParse("{% from 'private' import value %}", out _, out _));

        firstOptions.Filters.AddFilter("profile_isolation_probe", static (_, _, _) =>
            new ValueTask<FluidValue>(new StringValue("first")));
        firstParser.RegisterEmptyTag("profile_isolation_probe", static (_, _, _) =>
            new ValueTask<Completion>(Completion.Normal));

        Assert.DoesNotContain(secondOptions.Filters, filter => filter.Key == "profile_isolation_probe");
        Assert.False(secondParser.RegisteredTags.ContainsKey("profile_isolation_probe"));
    }

    [Fact]
    public async Task Default_profile_signatures_match_runtime_filter_and_tag_behavior()
    {
        var handler = new PortableLiquidExpressionHandler(new FluidParser());
        var upper = await handler.EvaluateAsync(Request("{{ 'hello' | upcase }}"));
        var appended = await handler.EvaluateAsync(Request("{{ 'hello' | append: ' world' }}"));
        var conditional = await handler.EvaluateAsync(Request("{% if true %}yes{% endif %}"));
        var loop = await handler.EvaluateAsync(Request("{% for item in items %}{{ item }}{% endfor %}", new Dictionary<string, JsonElement>
        {
            ["items"] = JsonSerializer.SerializeToElement(new[] { "a", "b" })
        }));

        Assert.Equal("HELLO", upper.GetString());
        Assert.Equal("hello world", appended.GetString());
        Assert.Equal("yes", conditional.GetString());
        Assert.Equal("ab", loop.GetString());
    }

    [Theory]
    [InlineData("date")]
    [InlineData("format_date")]
    [InlineData("time_zone")]
    public async Task Time_dependent_filters_remain_registered_only_as_explicit_denials(string filter)
    {
        var handler = new PortableLiquidExpressionHandler(new FluidParser());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await handler.EvaluateAsync(Request($"{{{{ 'input' | {filter}: '%Y' }}}}")));

        Assert.Contains("binding-pure-v1", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(LiquidExpressionProfile.Default.ToolingCatalog.Symbols,
            symbol => symbol.Kind == ExpressionSymbolKind.Filter && symbol.Name == filter);
    }

    [Fact]
    public async Task Tooling_completes_only_context_symbols_for_each_liquid_cursor_mode()
    {
        var provider = new LiquidExpressionToolingProvider();
        var customer = new ExpressionSymbol(
            "input:customer", "customer", ExpressionSymbolKind.WorkflowInput,
            new("Customer", ExpressionValueKind.Object, IsNullable: false, Members:
            [
                new("Name", new("String", ExpressionValueKind.Scalar, false), "Customer name.")
            ]));
        var scope = CreateScope(provider, [customer,
            new("input:caption", "caption", ExpressionSymbolKind.WorkflowInput, new("String"))]);

        var value = await provider.GetCompletionsAsync(new(scope, "{{ customer.Na", new(0, 14)), CancellationToken.None);
        var filter = await provider.GetCompletionsAsync(new(scope, "{{ caption | upc", new(0, 16)), CancellationToken.None);
        var tag = await provider.GetCompletionsAsync(new(scope, "{% cap", new(0, 6)), CancellationToken.None);
        var multilineFilterSource = "{{ caption |\n  upc";
        var multilineFilter = await provider.GetCompletionsAsync(
            new(scope, multilineFilterSource, new(1, 5)), CancellationToken.None);
        var multilineTagSource = "{%\n  cap";
        var multilineTag = await provider.GetCompletionsAsync(
            new(scope, multilineTagSource, new(1, 5)), CancellationToken.None);
        var tagValue = await provider.GetCompletionsAsync(new(scope, "{% if cap", new(0, 9)), CancellationToken.None);
        var plainText = await provider.GetCompletionsAsync(new(scope, "cap", new(0, 3)), CancellationToken.None);
        var stringContext = await provider.GetCompletionsAsync(new(scope, "{{ 'cap", new(0, 7)), CancellationToken.None);

        Assert.Equal("Name", Assert.Single(value.Payload!.Items).Label);
        Assert.Equal("upcase", Assert.Single(filter.Payload!.Items).Label);
        Assert.All(filter.Payload.Items, item => Assert.Equal(ExpressionSymbolKind.Filter, item.Kind));
        Assert.Contains(tag.Payload!.Items, item => item.Label == "capture" && item.Kind == ExpressionSymbolKind.Tag);
        Assert.All(tag.Payload.Items, item => Assert.Equal(ExpressionSymbolKind.Tag, item.Kind));
        Assert.Equal("upcase", Assert.Single(multilineFilter.Payload!.Items).Label);
        Assert.Contains(multilineTag.Payload!.Items, item => item.Label == "capture" && item.Kind == ExpressionSymbolKind.Tag);
        Assert.Contains(tagValue.Payload!.Items, item => item.Label == "caption");
        Assert.Equal(ExpressionToolingOutcomeState.SupportedEmpty, plainText.State);
        Assert.Empty(plainText.Payload!.Items);
        Assert.Equal(ExpressionToolingOutcomeState.SupportedEmpty, stringContext.State);
        Assert.Empty(stringContext.Payload!.Items);
    }

    [Fact]
    public async Task Cursor_help_uses_exact_ranges_and_skips_string_contents()
    {
        var provider = new LiquidExpressionToolingProvider();
        var scope = CreateScope(provider,
        [
            new("input:customer", "customer", ExpressionSymbolKind.WorkflowInput,
                new("Customer", ExpressionValueKind.Object, IsNullable: false, Members:
                [new("Name", new("String", ExpressionValueKind.Scalar, false), "Customer name.")]))
        ]);
        var filterSource = "{{ value | upcase }}";
        var filterStart = filterSource.IndexOf("upcase", StringComparison.Ordinal);
        var filterHover = await provider.GetHoverAsync(
            new(scope, filterSource, new(0, filterStart + 3)), CancellationToken.None);
        var tagSource = "{% if condition %}";
        var tagHover = await provider.GetHoverAsync(new(scope, tagSource, new(0, 4)), CancellationToken.None);
        var memberSource = "{{ customer.Name }}";
        var memberStart = memberSource.IndexOf("Name", StringComparison.Ordinal);
        var memberHover = await provider.GetHoverAsync(
            new(scope, memberSource, new(0, memberStart + 2)), CancellationToken.None);
        var filterString = "{{ 'value | upcase' }}";
        var stringHover = await provider.GetHoverAsync(
            new(scope, filterString, new(0, filterString.IndexOf("upcase", StringComparison.Ordinal) + 3)),
            CancellationToken.None);
        var tagString = "{% if 'render' %}";
        var hiddenTagHover = await provider.GetHoverAsync(
            new(scope, tagString, new(0, tagString.IndexOf("render", StringComparison.Ordinal) + 3)),
            CancellationToken.None);
        var rawSource = "{% raw %}{{ upcase";
        var rawCompletion = await provider.GetCompletionsAsync(
            new(scope, rawSource, new(0, rawSource.Length)), CancellationToken.None);
        var rawHover = await provider.GetHoverAsync(
            new(scope, rawSource, new(0, rawSource.IndexOf("upcase", StringComparison.Ordinal) + 3)),
            CancellationToken.None);
        var commentSource = "{% comment %}{{ upcase";
        var commentCompletion = await provider.GetCompletionsAsync(
            new(scope, commentSource, new(0, commentSource.Length)), CancellationToken.None);
        var commentHover = await provider.GetHoverAsync(
            new(scope, commentSource, new(0, commentSource.IndexOf("upcase", StringComparison.Ordinal) + 3)),
            CancellationToken.None);

        Assert.Equal(new ExpressionToolingRange(new(0, filterStart), new(0, filterStart + "upcase".Length)), filterHover.Payload!.Range);
        Assert.Contains("upcase(): String", filterHover.Payload.Contents, StringComparison.Ordinal);
        Assert.Contains("Converts the input text to uppercase.", filterHover.Payload.Contents, StringComparison.Ordinal);
        Assert.Equal(new ExpressionToolingRange(new(0, 3), new(0, 5)), tagHover.Payload!.Range);
        Assert.Contains("if condition", tagHover.Payload.Contents, StringComparison.Ordinal);
        Assert.Equal(new ExpressionToolingRange(new(0, memberStart), new(0, memberStart + "Name".Length)), memberHover.Payload!.Range);
        Assert.Contains("Customer name.", memberHover.Payload.Contents, StringComparison.Ordinal);
        Assert.Equal(ExpressionToolingOutcomeState.SupportedEmpty, stringHover.State);
        Assert.Equal(ExpressionToolingOutcomeState.SupportedEmpty, hiddenTagHover.State);
        Assert.Equal(ExpressionToolingOutcomeState.SupportedEmpty, rawCompletion.State);
        Assert.Empty(rawCompletion.Payload!.Items);
        Assert.Equal(ExpressionToolingOutcomeState.SupportedEmpty, rawHover.State);
        Assert.Equal(ExpressionToolingOutcomeState.SupportedEmpty, commentCompletion.State);
        Assert.Empty(commentCompletion.Payload!.Items);
        Assert.Equal(ExpressionToolingOutcomeState.SupportedEmpty, commentHover.State);
    }

    [Fact]
    public async Task Direct_tooling_does_not_restore_filtered_catalog_entries_or_guess_unknown_shapes()
    {
        var provider = new LiquidExpressionToolingProvider();
        var unfilteredScope = CreateScope(provider, []);
        var filteredSymbols = provider.DeclaredCatalog.Symbols.Where(symbol => symbol.Name != "upcase").ToArray();
        var filteredScope = CreateScope(provider, [], filteredSymbols);
        var dynamicScope = CreateScope(provider,
        [new("input:customer", "customer", ExpressionSymbolKind.WorkflowInput, new("Customer", ExpressionValueKind.Unknown))]);

        var emptyContext = await provider.GetCompletionsAsync(
            new(CreateEmptyScope(), "{{ 'x' | upc", new(0, 12)), CancellationToken.None);
        var removedFilter = await provider.GetCompletionsAsync(
            new(filteredScope, "{{ 'x' | upc", new(0, 12)), CancellationToken.None);
        var unknownMember = await provider.ValidateAsync(
            new(dynamicScope, "{{ customer.unknown }}"), CancellationToken.None);
        var incomplete = await provider.ValidateAsync(
            new(dynamicScope, "{{ customer."), CancellationToken.None);
        var looseFilterArguments = await provider.ValidateAsync(
            new(unfilteredScope, "{{ 'x' | upcase: one, two }}"), CancellationToken.None);

        Assert.Equal(ExpressionToolingOutcomeState.SupportedEmpty, emptyContext.State);
        Assert.Empty(emptyContext.Payload!.Items);
        Assert.Equal(ExpressionToolingOutcomeState.SupportedEmpty, removedFilter.State);
        Assert.Empty(removedFilter.Payload!.Items);
        Assert.Empty(unknownMember.Payload!.Diagnostics);
        Assert.Equal("Liquid/Syntax", Assert.Single(incomplete.Payload!.Diagnostics).Code);
        Assert.DoesNotContain(looseFilterArguments.Payload!.Diagnostics, diagnostic => diagnostic.Code.Contains("Arity", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Trusted_profile_pairs_custom_runtime_registration_and_tooling_catalog()
    {
        var symbols = LiquidExpressionProfile.Default.ToolingCatalog.Symbols.Concat(
        [
            new ExpressionSymbol("liquid:tag:tenant_label", "tenant_label", ExpressionSymbolKind.Tag, Documentation: "Writes the tenant label."),
            new ExpressionSymbol("liquid:filter:tenant_label", "tenant_label", ExpressionSymbolKind.Filter, Documentation: "Returns the tenant label.")
        ]).ToArray();
        var profile = LiquidExpressionProfile.Create(
            "host-liq-8d30b31f",
            symbols,
            configureParser: parser => parser.RegisterEmptyTag("tenant_label", static (writer, _, _) =>
            {
                writer.Write("tenant");
                return new ValueTask<Completion>(Completion.Normal);
            }),
            configureTemplateOptions: options => options.Filters.AddFilter("tenant_label", static (_, _, _) =>
                new ValueTask<FluidValue>(new StringValue("label"))));
        var services = new ServiceCollection();
        services.AddSingleton(profile);
        new LiquidExpressionsFeature().ConfigureServices(services);
        using var root = services.BuildServiceProvider();
        using var scope = root.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IPortableExpressionHandler>();
        var provider = scope.ServiceProvider.GetRequiredService<IExpressionToolingProvider>();
        var source = "{% tenant_label %}{{ 'x' | tenant_label }}";
        var runtimeResult = await handler.EvaluateAsync(Request(source));
        var validation = await provider.ValidateAsync(new(CreateScope(provider, []), source), CancellationToken.None);
        var tagCompletion = await provider.GetCompletionsAsync(
            new(CreateScope(provider, []), "{% tenant", new(0, 9)), CancellationToken.None);
        var filterCompletion = await provider.GetCompletionsAsync(
            new(CreateScope(provider, []), "{{ 'x' | tenant", new(0, 14)), CancellationToken.None);
        var declaredCatalog = Assert.IsType<ExpressionToolingCatalog>(provider.DeclaredCatalog);

        Assert.Same(profile.ToolingCatalog, declaredCatalog);
        Assert.Equal("host-liq-8d30b31f", declaredCatalog.Revision);
        Assert.Equal("tenantlabel", runtimeResult.GetString());
        Assert.Equal(ExpressionToolingOutcomeState.SupportedEmpty, validation.State);
        Assert.Contains(tagCompletion.Payload!.Items, item => item.Label == "tenant_label" && item.Kind == ExpressionSymbolKind.Tag);
        Assert.Contains(filterCompletion.Payload!.Items, item => item.Label == "tenant_label" && item.Kind == ExpressionSymbolKind.Filter);
    }

    [Fact]
    public async Task Scoped_parser_override_and_public_parser_constructor_remain_compatible_without_invented_metadata()
    {
        var services = new ServiceCollection();
        services.AddScoped<FluidParser>(_ => LegacyParser());
        new LiquidExpressionsFeature().ConfigureServices(services);
        using var root = services.BuildServiceProvider();
        using var firstScope = root.CreateScope();
        using var secondScope = root.CreateScope();
        var firstParser = firstScope.ServiceProvider.GetRequiredService<FluidParser>();
        var secondParser = secondScope.ServiceProvider.GetRequiredService<FluidParser>();
        var handler = firstScope.ServiceProvider.GetRequiredService<IPortableExpressionHandler>();
        var provider = firstScope.ServiceProvider.GetRequiredService<IExpressionToolingProvider>();
        var throughDi = await handler.EvaluateAsync(Request("{% legacy_label %}"));
        var throughConstructor = await new PortableLiquidExpressionHandler(LegacyParser()).EvaluateAsync(Request("{% legacy_label %}"));

        Assert.NotSame(firstParser, secondParser);
        Assert.Equal("legacy", throughDi.GetString());
        Assert.Equal("legacy", throughConstructor.GetString());
        Assert.DoesNotContain(Assert.IsType<ExpressionToolingCatalog>(provider.DeclaredCatalog).Symbols,
            symbol => symbol.Name == "legacy_label");
    }

    [Fact]
    public void Feature_shares_the_singleton_profile_but_creates_scoped_parsers()
    {
        var services = new ServiceCollection();
        new LiquidExpressionsFeature().ConfigureServices(services);
        using var root = services.BuildServiceProvider();
        using var firstScope = root.CreateScope();
        using var secondScope = root.CreateScope();

        var firstParser = firstScope.ServiceProvider.GetRequiredService<FluidParser>();
        var secondParser = secondScope.ServiceProvider.GetRequiredService<FluidParser>();
        var firstTooling = firstScope.ServiceProvider.GetRequiredService<IExpressionToolingProvider>();
        var secondTooling = secondScope.ServiceProvider.GetRequiredService<IExpressionToolingProvider>();

        Assert.NotSame(firstParser, secondParser);
        Assert.Same(firstTooling, secondTooling);
        firstParser.RegisterEmptyTag("scope_only", static (_, _, _) => new ValueTask<Completion>(Completion.Normal));
        Assert.False(secondParser.RegisteredTags.ContainsKey("scope_only"));
    }

    [Fact]
    public async Task Liquid_feature_adds_to_existing_language_handlers_and_tooling()
    {
        var customProfile = LiquidExpressionProfile.Create(
            "host-paired-profile-3a9541",
            LiquidExpressionProfile.Default.ToolingCatalog.Symbols.Concat(
            [new ExpressionSymbol("liquid:filter:tenant_label", "tenant_label", ExpressionSymbolKind.Filter)]).ToArray(),
            configureTemplateOptions: options => options.Filters.AddFilter("tenant_label", static (_, _, _) =>
                new ValueTask<FluidValue>(new StringValue("paired"))));
        var services = new ServiceCollection();
        services.AddSingleton(customProfile);
        services.AddScoped<IPortableJavaScriptEvaluator, FixedJavaScriptEvaluator>();
        new JavaScriptFeature().ConfigureServices(services);
        new LiquidExpressionsFeature().ConfigureServices(services);
        using var root = services.BuildServiceProvider();
        using var scope = root.CreateScope();

        var providers = root.GetServices<IExpressionToolingProvider>().ToArray();
        var handlers = scope.ServiceProvider.GetServices<IPortableExpressionHandler>().ToArray();
        var liquidProvider = Assert.Single(providers, provider => provider.ExpressionType == "Liquid");
        var liquidHandler = Assert.Single(handlers, handler => handler.Language == "Liquid");
        var javaScriptHandler = Assert.Single(handlers, handler => handler.Language == "JavaScript");
        var liquidResult = await liquidHandler.EvaluateAsync(Request("{{ 'x' | tenant_label }}"));
        var javaScriptResult = await javaScriptHandler.EvaluateAsync(Request("1 + 1", language: "JavaScript"));

        Assert.Contains(providers, provider => provider.ExpressionType == "JavaScript");
        Assert.Equal("host-paired-profile-3a9541", liquidProvider.DeclaredCatalog!.Revision);
        Assert.Equal("paired", liquidResult.GetString());
        Assert.Equal("javascript", javaScriptResult.GetString());
    }

    private static ExpressionToolingRequestScope CreateScope(
        IExpressionToolingProvider provider,
        IReadOnlyList<ExpressionSymbol> values,
        IReadOnlyList<ExpressionSymbol>? profileSymbols = null)
    {
        var symbols = (profileSymbols ?? provider.DeclaredCatalog!.Symbols).Concat(values).ToArray();
        var document = new ExpressionAuthoringDocument("doc", "draft", "node", "text", provider.ExpressionType, "revision");
        var context = new ExpressionAuthoringContext(ExpressionToolingContractVersion.V1, document, "context", "catalog", symbols, new());
        return new(ExpressionToolingContractVersion.V1, document, context);
    }

    private static ExpressionToolingRequestScope CreateEmptyScope()
    {
        var document = new ExpressionAuthoringDocument("doc", "draft", "node", "text", "Liquid", "revision");
        var context = new ExpressionAuthoringContext(ExpressionToolingContractVersion.V1, document, "context", "catalog", [], new());
        return new(ExpressionToolingContractVersion.V1, document, context);
    }

    private static FluidParser LegacyParser()
    {
        var parser = new FluidParser();
        parser.RegisterEmptyTag("legacy_label", static (writer, _, _) =>
        {
            writer.Write("legacy");
            return new ValueTask<Completion>(Completion.Normal);
        });
        return parser;
    }

    private static ExpressionEvaluationRequest Request(
        string source,
        IReadOnlyDictionary<string, JsonElement>? values = null,
        string language = "Liquid")
    {
        values ??= NoParameters;
        var bindings = values.ToDictionary(
            pair => pair.Key,
            pair => (ExpressionParameterBinding)new LiteralExpressionParameterBinding(pair.Value),
            StringComparer.Ordinal);
        var definition = new ExpressionDefinition(
            language,
            source,
            new TypeReference("String"),
            bindings,
            JsonSerializer.SerializeToElement(new { }),
            ExpressionCapabilityProfiles.BindingPureV1);
        return new(definition, values, CancellationToken.None);
    }

    private sealed class FixedJavaScriptEvaluator : IPortableJavaScriptEvaluator
    {
        public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request) =>
            ValueTask.FromResult(JsonSerializer.SerializeToElement("javascript"));
    }
}
