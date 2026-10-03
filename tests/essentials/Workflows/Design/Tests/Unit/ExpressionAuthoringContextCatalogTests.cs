using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Services;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

public sealed class ExpressionAuthoringContextCatalogTests
{
    private const string ProfileUnavailable = "profile-unavailable";

    [Fact]
    public async Task Unauthorized_and_unowned_locations_do_not_resolve_a_provider_or_filter_profile_symbols()
    {
        var source = new Source(ownsLocation: false);
        var resolver = new Resolver(_ => throw new InvalidOperationException("must not resolve"));
        var filter = new SymbolFilter((symbol, _, _, _) => ValueTask.FromResult<ExpressionSymbol?>(symbol));
        var service = new ExpressionAuthoringContextService([source], resolver, [filter]);

        var unauthorized = await service.ResolveAsync(Request(), new(false), CancellationToken.None);
        var unowned = await service.ResolveAsync(Request(), new(true), CancellationToken.None);

        Assert.Equal(ExpressionToolingOutcomeState.Unauthorized, unauthorized.State);
        Assert.Equal(ExpressionToolingOutcomeState.Unavailable, unowned.State);
        Assert.Equal(1, source.Calls);
        Assert.Equal(0, resolver.Calls);
        Assert.Equal(0, filter.Calls);
    }

    [Fact]
    public async Task Invalid_source_identity_and_source_faults_do_not_expose_profile_metadata()
    {
        var wrongContext = Context(Request() with { NodeId = "other-node" }, []);
        var invalidSource = new Source(fixedContext: wrongContext);
        var resolver = new Resolver(_ => throw new InvalidOperationException("must not resolve"));
        var filter = new SymbolFilter((symbol, _, _, _) => ValueTask.FromResult<ExpressionSymbol?>(symbol));
        var invalidService = new ExpressionAuthoringContextService([invalidSource], resolver, [filter]);
        var faultingSource = new Source(failure: new InvalidOperationException("private source details"));
        var faultingService = new ExpressionAuthoringContextService([faultingSource], resolver, [filter]);

        var invalid = await invalidService.ResolveAsync(Request(), new(true), CancellationToken.None);
        var faulted = await faultingService.ResolveAsync(Request(), new(true), CancellationToken.None);

        Assert.Equal("invalid-context-source", invalid.Code);
        Assert.Null(invalid.Payload);
        Assert.Equal(ExpressionToolingOutcomeState.Unavailable, faulted.State);
        Assert.Equal("context-source-failed", faulted.Code);
        Assert.DoesNotContain("private source details", faulted.Message ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(0, resolver.Calls);
        Assert.Equal(0, filter.Calls);
    }

    [Fact]
    public async Task Legacy_constructor_and_missing_or_null_catalog_preserve_source_revisions()
    {
        var source = new Source(symbols: [Symbol("source", "source")]);
        var service = new ExpressionAuthoringContextService([source]);

        var legacy = await service.ResolveAsync(Request(), new(true), CancellationToken.None);
        var noCatalog = await Service(source, new Provider(() => null))
            .ResolveAsync(Request(), new(true), CancellationToken.None);

        Assert.Equal("context-source", legacy.Payload!.ContextRevision);
        Assert.Equal("catalog-source", legacy.Payload.SymbolCatalogRevision);
        Assert.Equal("context-source", noCatalog.Payload!.ContextRevision);
        Assert.Equal("catalog-source", noCatalog.Payload.SymbolCatalogRevision);
    }

    [Fact]
    public async Task Composes_only_the_provider_selected_for_the_validated_expression_type()
    {
        var selected = new Provider(() => new("javascript-profile", [Symbol("javascript", "javascript")]), "JavaScript");
        var unrelated = new Provider(() => new("liquid-profile", [Symbol("liquid", "liquid")]), "Liquid");
        var resolver = new Resolver(type => type == "JavaScript" ? selected : unrelated);
        var service = new ExpressionAuthoringContextService([new Source()], resolver, []);

        var result = await service.ResolveAsync(Request(), new(true), CancellationToken.None);

        Assert.Equal("JavaScript", resolver.RequestedExpressionType);
        Assert.Equal("javascript", Assert.Single(result.Payload!.RootSymbols).SymbolId);
    }

    [Fact]
    public async Task Composes_selected_provider_catalog_and_preserves_rich_metadata_and_policy_revisions()
    {
        var profile = new ExpressionSymbol(
            "profile:json-parse",
            "JSON.parse",
            ExpressionSymbolKind.Function,
            new("parse(text): Any", ExpressionValueKind.Function, false),
            "Parses JSON.",
            [new("parse(text)", ["text"], new("Any"))]);
        var sourceSymbol = Symbol("source", "name");
        var source = new Source(
            symbols: [sourceSymbol],
            contextRevision: "context-source",
            catalogRevision: "catalog-source",
            policyFingerprint: "policy-9",
            permissionRevision: "permission-3");
        var service = Service(source, new Provider(() => new("profile-r1", [profile])));

        var result = await service.ResolveAsync(Request(), new(true), CancellationToken.None);

        Assert.Equal(ExpressionToolingOutcomeState.Success, result.State);
        Assert.Collection(result.Payload!.RootSymbols,
            actual => Assert.Equal(profile, actual),
            actual => Assert.Equal(sourceSymbol, actual));
        Assert.NotEqual("context-source", result.Payload.ContextRevision);
        Assert.NotEqual("catalog-source", result.Payload.SymbolCatalogRevision);
        Assert.Equal("policy-9", result.Payload.PolicyFingerprint);
        Assert.Equal("permission-3", result.Payload.PermissionRevision);
        var signature = Assert.Single(result.Payload.RootSymbols[0].Signatures!);
        Assert.Equal("Any", signature.ReturnShape!.DisplayName);
    }

    [Fact]
    public async Task Composes_context_and_symbol_catalog_revisions_independently_and_checks_staleness_afterward()
    {
        var provider = new Provider(() => new("profile-r1", [Symbol("profile", "profile")]));
        var first = await Service(new Source(contextRevision: "ctx-a", catalogRevision: "symbols-a"), provider)
            .ResolveAsync(Request(), new(true), CancellationToken.None);
        var contextChanged = await Service(new Source(contextRevision: "ctx-b", catalogRevision: "symbols-a"), provider)
            .ResolveAsync(Request(), new(true), CancellationToken.None);
        var symbolsChanged = await Service(new Source(contextRevision: "ctx-a", catalogRevision: "symbols-b"), provider)
            .ResolveAsync(Request(), new(true), CancellationToken.None);
        var stale = await Service(new Source(contextRevision: "ctx-a", catalogRevision: "symbols-a"), provider)
            .ResolveAsync(Request(contextRevision: "ctx-a"), new(true), CancellationToken.None);

        Assert.NotEqual(first.Payload!.ContextRevision, contextChanged.Payload!.ContextRevision);
        Assert.Equal(first.Payload.SymbolCatalogRevision, contextChanged.Payload.SymbolCatalogRevision);
        Assert.Equal(first.Payload.ContextRevision, symbolsChanged.Payload!.ContextRevision);
        Assert.NotEqual(first.Payload.SymbolCatalogRevision, symbolsChanged.Payload.SymbolCatalogRevision);
        Assert.Equal(ExpressionToolingOutcomeState.Stale, stale.State);
        Assert.Null(stale.Payload);
        Assert.Equal(first.Payload.ContextRevision, stale.ContextRevision);
    }

    [Fact]
    public async Task Selected_profile_candidates_are_filtered_once_without_refiltering_source_symbols()
    {
        var profileSymbols = new[] { Symbol("profile:b", "b"), Symbol("profile:a", "a") };
        var sourceSymbol = Symbol("source", "source");
        var source = new Source(symbols: [sourceSymbol]);
        var filter = new SymbolFilter((symbol, _, _, _) => ValueTask.FromResult<ExpressionSymbol?>(
            symbol.SymbolId == "profile:b" ? null : symbol with { Documentation = "filtered" }));
        var service = new ExpressionAuthoringContextService([source],
            new Resolver(_ => new Provider(() => new("profile-r1", profileSymbols))), [filter]);

        var result = await service.ResolveAsync(Request(), new(true), CancellationToken.None);

        Assert.Equal(new[] { "profile:a", "profile:b" }, filter.SeenIds);
        Assert.Single(result.Payload!.RootSymbols, symbol => symbol.SymbolId == "profile:a");
        Assert.Equal("filtered", result.Payload.RootSymbols[0].Documentation);
        Assert.Contains(sourceSymbol, result.Payload.RootSymbols);
        Assert.DoesNotContain("source", filter.SeenIds);
        Assert.Equal(2, filter.Calls);
    }

    [Fact]
    public async Task Profile_id_collision_replaces_every_matching_source_id_but_same_names_with_distinct_ids_survive()
    {
        var source = new Source(symbols:
        [
            Symbol("shared", "source-name"),
            Symbol("shared", "duplicate-source-name"),
            Symbol("workflow-value", "same-name"),
            Symbol("source-filter", "same-name", ExpressionSymbolKind.Filter)
        ]);
        var profile = Symbol("shared", "profile-name");
        var service = Service(source, new Provider(() => new("profile-r1", [profile])));

        var result = await service.ResolveAsync(Request(), new(true), CancellationToken.None);

        Assert.Equal(3, result.Payload!.RootSymbols.Count);
        Assert.Equal(1, result.Payload.RootSymbols.Count(symbol => symbol.SymbolId == "shared"));
        Assert.Contains(profile, result.Payload.RootSymbols);
        Assert.Contains(result.Payload.RootSymbols, symbol => symbol.SymbolId == "workflow-value" && symbol.Name == "same-name");
        Assert.Contains(result.Payload.RootSymbols, symbol => symbol.SymbolId == "source-filter" && symbol.Name == "same-name");
    }

    [Fact]
    public async Task Denied_profile_candidate_removes_matching_source_authority_without_removing_distinct_ids()
    {
        var source = new Source(symbols:
        [
            Symbol("shared", "source-name"),
            Symbol("visible-source", "shared-name")
        ]);
        var filter = new SymbolFilter((symbol, _, _, _) => ValueTask.FromResult<ExpressionSymbol?>(
            symbol.SymbolId == "shared" ? null : symbol));
        var service = new ExpressionAuthoringContextService([source],
            new Resolver(_ => new Provider(() => new("profile-r1", [Symbol("shared", "profile-name")]))), [filter]);

        var result = await service.ResolveAsync(Request(), new(true), CancellationToken.None);

        Assert.DoesNotContain(result.Payload!.RootSymbols, symbol => symbol.SymbolId == "shared");
        Assert.Contains(result.Payload.RootSymbols, symbol => symbol.SymbolId == "visible-source");
    }

    [Fact]
    public async Task Profile_ids_are_sorted_first_and_reserve_the_500_symbol_share_of_the_5500_symbol_bound()
    {
        var profile = Enumerable.Range(0, 500)
            .Reverse()
            .Select(index => Symbol($"profile:{index:D3}", $"profile{index:D3}"))
            .ToArray();
        var sourceSymbols = Enumerable.Range(0, 6_000)
            .Select(index => Symbol($"source:{index:D4}", $"source{index:D4}"))
            .ToArray();
        var service = Service(new Source(symbols: sourceSymbols), new Provider(() => new("profile-r1", profile)));

        var result = await service.ResolveForProviderAsync(Request(take: 1, search: "absent"), new(true), CancellationToken.None);

        Assert.Equal(5_500, result.Payload!.RootSymbols.Count);
        Assert.Equal(profile.Select(symbol => symbol.SymbolId).OrderBy(id => id, StringComparer.Ordinal),
            result.Payload.RootSymbols.Take(500).Select(symbol => symbol.SymbolId));
        Assert.DoesNotContain(result.Payload.RootSymbols, symbol => symbol.SymbolId == "source:5000");
        Assert.DoesNotContain(result.Payload.RootSymbols, symbol => symbol.SymbolId == "source:5999");
        Assert.Equal("profile:000", result.Payload.RootSymbols[0].SymbolId);
        Assert.Equal("source:0000", result.Payload.RootSymbols[500].SymbolId);
    }

    [Fact]
    public async Task Search_and_page_apply_to_the_composed_policy_filtered_catalog()
    {
        var source = new Source(symbols:
        [
            Symbol("source:alpha", "alpha-source"),
            Symbol("source:beta", "beta-source"),
            Symbol("source:gamma", "gamma-source")
        ]);
        var profile = new[]
        {
            Symbol("profile:alpha", "alpha-profile"),
            Symbol("profile:blocked", "alpha-blocked")
        };
        var filter = new SymbolFilter((symbol, _, _, _) => ValueTask.FromResult<ExpressionSymbol?>(
            symbol.SymbolId == "profile:blocked" ? null : symbol));
        var service = new ExpressionAuthoringContextService([source],
            new Resolver(_ => new Provider(() => new("profile-r1", profile))), [filter]);

        var result = await service.ResolveAsync(Request(search: "alpha", skip: 1, take: 1), new(true), CancellationToken.None);

        Assert.Equal("alpha-source", Assert.Single(result.Payload!.RootSymbols).Name);
    }

    [Fact]
    public async Task Provider_context_resolution_composes_catalog_without_client_search_or_paging()
    {
        var service = Service(
            new Source(symbols: [Symbol("source", "unmatched")]),
            new Provider(() => new("profile-r1", [Symbol("profile", "match-me")])));

        var result = await service.ResolveForProviderAsync(Request(search: "nothing", skip: 4, take: 1), new(true), CancellationToken.None);

        Assert.Equal(2, result.Payload!.RootSymbols.Count);
        Assert.Contains(result.Payload.RootSymbols, symbol => symbol.Name == "match-me");
        Assert.Contains(result.Payload.RootSymbols, symbol => symbol.Name == "unmatched");
    }

    [Theory]
    [InlineData("empty-revision")]
    [InlineData("duplicate-id")]
    [InlineData("empty-id")]
    [InlineData("empty-name")]
    [InlineData("null-name")]
    [InlineData("too-many")]
    [InlineData("null-symbols")]
    public async Task Invalid_declared_profiles_fail_unavailable_without_partial_context(string profileCase)
    {
        ExpressionToolingCatalog? catalog = profileCase switch
        {
            "empty-revision" => new(" ", [Symbol("profile", "profile")]),
            "duplicate-id" => new("profile-r1", [Symbol("duplicate", "one"), Symbol("duplicate", "two")]),
            "empty-id" => new("profile-r1", [Symbol(" ", "empty-id")]),
            "empty-name" => new("profile-r1", [Symbol("profile", " ")]),
            "null-name" => new("profile-r1", [Symbol("profile", null!)]),
            "too-many" => new("profile-r1", Enumerable.Range(0, 501).Select(index => Symbol($"profile:{index}", $"profile{index}")).ToArray()),
            "null-symbols" => new("profile-r1", null!),
            _ => throw new ArgumentOutOfRangeException(nameof(profileCase))
        };
        var service = Service(new Source(symbols: [Symbol("source", "source")]), new Provider(() => catalog));

        var result = await service.ResolveAsync(Request(), new(true), CancellationToken.None);

        Assert.Equal(ExpressionToolingOutcomeState.Unavailable, result.State);
        Assert.Equal(ProfileUnavailable, result.Code);
        Assert.Null(result.Payload);
        Assert.Null(result.DocumentRevision);
        Assert.Null(result.ContextRevision);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task Invalid_names_returned_by_a_profile_filter_fail_closed_before_search(string? name)
    {
        var filter = new SymbolFilter((symbol, _, _, _) => ValueTask.FromResult<ExpressionSymbol?>(symbol with { Name = name! }));
        var service = new ExpressionAuthoringContextService([new Source()],
            new Resolver(_ => new Provider(() => new("profile-r1", [Symbol("profile", "valid")]))), [filter]);

        var result = await service.ResolveAsync(Request(search: "valid"), new(true), CancellationToken.None);

        Assert.Equal(ExpressionToolingOutcomeState.Unavailable, result.State);
        Assert.Equal(ProfileUnavailable, result.Code);
        Assert.Null(result.Payload);
    }

    [Theory]
    [InlineData("resolver")]
    [InlineData("catalog")]
    [InlineData("filter")]
    public async Task Resolver_catalog_and_filter_faults_are_safe_unavailable_without_partial_context(string fault)
    {
        var source = new Source(symbols: [Symbol("source", "source")]);
        var resolver = fault == "resolver"
            ? new Resolver(_ => throw new InvalidOperationException("resolver private detail"))
            : new Resolver(_ => new Provider(() => fault == "catalog"
                ? throw new InvalidOperationException("catalog private detail")
                : new("profile-r1", [Symbol("profile", "profile")])));
        IExpressionAuthoringSymbolFilter[] filters = fault == "filter"
            ? new IExpressionAuthoringSymbolFilter[]
            {
                new SymbolFilter((_, _, _, _) => throw new InvalidOperationException("filter private detail"))
            }
            : [];
        var service = new ExpressionAuthoringContextService([source], resolver, filters);

        var result = await service.ResolveAsync(Request(), new(true), CancellationToken.None);

        Assert.Equal(ExpressionToolingOutcomeState.Unavailable, result.State);
        Assert.Equal(ProfileUnavailable, result.Code);
        Assert.Null(result.Payload);
        Assert.DoesNotContain("private detail", result.Message ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Caller_cancellation_during_profile_filtering_propagates_without_a_partial_result()
    {
        using var cancellation = new CancellationTokenSource();
        var filter = new SymbolFilter((symbol, _, _, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ExpressionSymbol?>(symbol);
        });
        var service = new ExpressionAuthoringContextService(
            [new Source()],
            new Resolver(_ => new Provider(() => new("profile-r1", [Symbol("profile", "profile")]))),
            [filter]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await service.ResolveAsync(Request(), new(true), cancellation.Token));
    }

    private static ResolveExpressionAuthoringContextRequest Request(
        string? contextRevision = null,
        string? search = null,
        int skip = 0,
        int take = 100) => new(
        ExpressionToolingContractVersion.V1,
        "draft",
        "node",
        "text",
        "JavaScript",
        "document-r1",
        contextRevision,
        search,
        skip,
        take);

    private static ExpressionAuthoringContext Context(
        ResolveExpressionAuthoringContextRequest request,
        IReadOnlyList<ExpressionSymbol> symbols,
        string contextRevision = "context-source",
        string catalogRevision = "catalog-source",
        string? policyFingerprint = null,
        string? permissionRevision = null) => new(
        ExpressionToolingContractVersion.V1,
        new("document", request.WorkflowDraftId, request.NodeId, request.PropertyKey, request.ExpressionType, request.DocumentRevision),
        contextRevision,
        catalogRevision,
        symbols,
        new(),
        PolicyFingerprint: policyFingerprint,
        PermissionRevision: permissionRevision);

    private static ExpressionSymbol Symbol(string id, string name, ExpressionSymbolKind kind = ExpressionSymbolKind.Function) =>
        new(id, name, kind);

    private static ExpressionAuthoringContextService Service(Source source, IExpressionToolingProvider provider) =>
        new([source], new Resolver(_ => provider), []);

    private sealed class Source(
        IReadOnlyList<ExpressionSymbol>? symbols = null,
        string contextRevision = "context-source",
        string catalogRevision = "catalog-source",
        string? policyFingerprint = null,
        string? permissionRevision = null,
        ExpressionAuthoringContext? fixedContext = null,
        Exception? failure = null,
        bool ownsLocation = true) : IExpressionAuthoringContextSource
    {
        public int Calls { get; private set; }

        public ValueTask<ExpressionAuthoringContext?> TryResolveAsync(
            ResolveExpressionAuthoringContextRequest request,
            ExpressionAuthoringAuthorization authorization,
            CancellationToken cancellationToken)
        {
            Calls++;
            if (failure is not null)
                throw failure;
            if (!ownsLocation)
                return ValueTask.FromResult<ExpressionAuthoringContext?>(null);
            return ValueTask.FromResult<ExpressionAuthoringContext?>(fixedContext ?? Context(
                request,
                symbols ?? [],
                contextRevision,
                catalogRevision,
                policyFingerprint ?? authorization.PolicyFingerprint,
                permissionRevision ?? authorization.PermissionRevision));
        }
    }

    private sealed class Resolver(Func<string, IExpressionToolingProvider?> find) : IExpressionToolingProviderResolver
    {
        public int Calls { get; private set; }
        public string? RequestedExpressionType { get; private set; }

        public IExpressionToolingProvider? Find(string expressionType)
        {
            Calls++;
            RequestedExpressionType = expressionType;
            return find(expressionType);
        }
    }

    private sealed class Provider(Func<ExpressionToolingCatalog?> catalog, string expressionType = "JavaScript") : IExpressionToolingProvider
    {
        public string ExpressionType => expressionType;
        public ExpressionToolingContractVersion SupportedVersion => ExpressionToolingContractVersion.V1;
        public ExpressionToolingCatalog? DeclaredCatalog => catalog();
        public ValueTask<ExpressionToolingOutcome<ExpressionToolingCapabilities>> GetCapabilitiesAsync(ExpressionToolingRequestScope scope, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ExpressionToolingOutcome<ExpressionToolingItems>> GetCompletionsAsync(ExpressionCompletionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ExpressionToolingOutcome<ExpressionHover>> GetHoverAsync(ExpressionHoverRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ExpressionToolingOutcome<ExpressionDiagnosticSet>> ValidateAsync(ExpressionValidationRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class SymbolFilter(
        Func<ExpressionSymbol, ExpressionAuthoringContext, ExpressionAuthoringAuthorization, CancellationToken, ValueTask<ExpressionSymbol?>> filter)
        : IExpressionAuthoringSymbolFilter
    {
        private readonly List<string> _seenIds = [];
        public int Calls => _seenIds.Count;
        public IReadOnlyList<string> SeenIds => _seenIds;

        public ValueTask<ExpressionSymbol?> FilterAsync(
            ExpressionSymbol symbol,
            ExpressionAuthoringContext context,
            ExpressionAuthoringAuthorization authorization,
            CancellationToken cancellationToken)
        {
            _seenIds.Add(symbol.SymbolId);
            return filter(symbol, context, authorization, cancellationToken);
        }
    }
}
