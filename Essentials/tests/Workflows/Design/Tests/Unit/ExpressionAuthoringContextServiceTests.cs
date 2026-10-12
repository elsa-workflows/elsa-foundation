using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Services;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

public sealed class ExpressionAuthoringContextServiceTests
{
    [Theory]
    [InlineData(-1, 100, 0)]
    [InlineData(0, 0, 0)]
    [InlineData(0, 501, 0)]
    [InlineData(0, 100, 257)]
    public async Task Rejects_catalog_requests_outside_the_wire_contract(int skip, int take, int searchLength)
    {
        var service = new ExpressionAuthoringContextService([new Source()]);
        var request = new ResolveExpressionAuthoringContextRequest(
            ExpressionToolingContractVersion.V1,
            "draft",
            "node",
            "text",
            "JavaScript",
            "revision",
            Search: searchLength == 0 ? null : new string('x', searchLength),
            Skip: skip,
            Take: take);

        var result = await service.ResolveAsync(request, new(true), CancellationToken.None);

        Assert.Equal(ExpressionToolingOutcomeState.Incompatible, result.State);
        Assert.Equal("invalid-page", result.Code);
    }

    [Fact]
    public async Task Bounds_and_filters_the_authoritative_symbol_catalog()
    {
        var service = new ExpressionAuthoringContextService([new Source()]);
        var request = new ResolveExpressionAuthoringContextRequest(ExpressionToolingContractVersion.V1, "draft", "node", "text", "JavaScript", "revision", Search: "name", Take: 1);

        var result = await service.ResolveAsync(request, new(true), CancellationToken.None);

        Assert.Equal(ExpressionToolingOutcomeState.Success, result.State);
        Assert.Equal("name", Assert.Single(result.Payload!.RootSymbols).Name);
    }

    [Fact]
    public async Task Does_not_invoke_sources_for_an_unauthorized_caller()
    {
        var source = new Source();
        var service = new ExpressionAuthoringContextService([source]);
        var request = new ResolveExpressionAuthoringContextRequest(ExpressionToolingContractVersion.V1, "draft", "node", "text", "JavaScript", "revision");

        var result = await service.ResolveAsync(request, new(false), CancellationToken.None);

        Assert.Equal(ExpressionToolingOutcomeState.Unauthorized, result.State);
        Assert.False(source.WasCalled);
    }

    [Fact]
    public async Task Preserves_the_bounded_post_policy_catalog_for_language_providers()
    {
        var symbols = Enumerable.Range(0, 501)
            .Select(index => new ExpressionSymbol($"symbol:{index}", $"symbol{index}", ExpressionSymbolKind.WorkflowInput))
            .ToArray();
        var service = new ExpressionAuthoringContextService([new Source(symbols)]);
        var request = new ResolveExpressionAuthoringContextRequest(
            ExpressionToolingContractVersion.V1,
            "draft",
            "node",
            "text",
            "JavaScript",
            "revision",
            Take: 1);

        var result = await service.ResolveForProviderAsync(request, new(true), CancellationToken.None);

        Assert.Equal(ExpressionToolingOutcomeState.Success, result.State);
        Assert.Equal(501, result.Payload!.RootSymbols.Count);
    }

    private sealed class Source(IReadOnlyList<ExpressionSymbol>? symbols = null) : IExpressionAuthoringContextSource
    {
        public bool WasCalled { get; private set; }

        public ValueTask<ExpressionAuthoringContext?> TryResolveAsync(ResolveExpressionAuthoringContextRequest request, ExpressionAuthoringAuthorization authorization, CancellationToken cancellationToken)
        {
            WasCalled = true;
            var document = new ExpressionAuthoringDocument("document", request.WorkflowDraftId, request.NodeId, request.PropertyKey, request.ExpressionType, request.DocumentRevision);
            var contextSymbols = symbols ?? [new("name", "name", ExpressionSymbolKind.WorkflowInput), new("age", "age", ExpressionSymbolKind.WorkflowInput)];
            return ValueTask.FromResult<ExpressionAuthoringContext?>(new(ExpressionToolingContractVersion.V1, document, "context", "catalog", contextSymbols, new()));
        }
    }
}
