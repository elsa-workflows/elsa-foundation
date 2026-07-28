using System.Reflection;
using System.Text.Json;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Core.Contracts;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

public sealed class ExpressionToolingEndpointTests
{
    [Theory]
    [InlineData("ResolveExpressionToolingContext")]
    [InlineData("SearchExpressionToolingSymbols")]
    [InlineData("CompleteExpressionTooling")]
    [InlineData("HoverExpressionTooling")]
    [InlineData("ValidateExpressionTooling")]
    [InlineData("DescribeExpressionTooling")]
    public void Tooling_endpoints_require_design_read_permission_before_the_handler_runs(string endpointName)
    {
        var endpoint = Create(endpointName, new RecordingContextService(), new ProviderResolver());

        endpoint.Configure();

        Assert.Contains(PermissionNames.WorkflowDesignRead, endpoint.Definition.AllowedPermissions!);
        Assert.Contains(PermissionNames.All, endpoint.Definition.AllowedPermissions!);
        Assert.Null(endpoint.Definition.AnonymousVerbs);
    }

    [Fact]
    public async Task Completion_passes_the_full_bounded_catalog_to_the_provider_and_marks_the_response_no_store()
    {
        var provider = new RecordingProvider();
        var contextService = new RecordingContextService(ContextWithSymbols(501));
        var endpoint = Create("CompleteExpressionTooling", contextService, new ProviderResolver(provider));

        await HandleAsync(endpoint, new ExpressionToolingCompletionRequest(
            ExpressionToolingContractVersion.V1,
            "draft",
            "node",
            "text",
            "JavaScript",
            "document",
            "args.symbol500",
            new(0, 14)));

        Assert.Equal(501, provider.SymbolCount);
        Assert.Equal(1, contextService.ProviderCalls);
        Assert.Equal(0, contextService.PageCalls);
        Assert.Equal("no-store", endpoint.HttpContext.Response.Headers.CacheControl.ToString());
        Assert.Equal(StatusCodes.Status200OK, endpoint.HttpContext.Response.StatusCode);
    }

    [Fact]
    public async Task Completion_maps_absent_and_faulted_providers_to_non_success_outcomes()
    {
        var context = ContextWithSymbols();
        var absent = Create("CompleteExpressionTooling", new RecordingContextService(context), new ProviderResolver());
        var faulted = Create("CompleteExpressionTooling", new RecordingContextService(context), new ProviderResolver(new RecordingProvider(failCapabilities: true)));
        var request = CompletionRequest();

        await HandleAsync(absent, request);
        await HandleAsync(faulted, request);

        Assert.Equal("Unavailable", ReadState(absent));
        Assert.Equal("Unavailable", ReadState(faulted));
        Assert.Equal(("document", "context"), ReadRevisions(absent));
        Assert.Equal("no-store", absent.HttpContext.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-store", faulted.HttpContext.Response.Headers.CacheControl.ToString());
    }

    [Theory]
    [InlineData("CompleteExpressionTooling")]
    [InlineData("HoverExpressionTooling")]
    [InlineData("ValidateExpressionTooling")]
    public async Task Provider_absent_semantic_outcomes_preserve_resolved_revisions(string endpointName)
    {
        var endpoint = Create(endpointName, new RecordingContextService(ContextWithSymbols()), new ProviderResolver());
        object request = endpointName switch
        {
            "CompleteExpressionTooling" => CompletionRequest(),
            "HoverExpressionTooling" => new ExpressionToolingHoverRequest(
                ExpressionToolingContractVersion.V1, "draft", "node", "text", "JavaScript", "document", "args.symbol", new(0, 11)),
            _ => new ExpressionToolingSourceRequest(
                ExpressionToolingContractVersion.V1, "draft", "node", "text", "JavaScript", "document", "args.symbol")
        };

        await HandleAsync(endpoint, request);

        Assert.Equal("Unavailable", ReadState(endpoint));
        Assert.Equal(("document", "context"), ReadRevisions(endpoint));
    }

    [Fact]
    public async Task Host_authorization_policy_can_deny_context_without_source_disclosure()
    {
        var contextService = new RecordingContextService(ContextWithSymbols());
        var policy = new StubAuthorizationPolicy(new(false, "author-1", "permissions-2", "policy-2"));
        var endpoint = Create(
            "ResolveExpressionToolingContext",
            contextService,
            new ProviderResolver(new RecordingProvider()),
            policy);

        await HandleAsync(endpoint, new ExpressionToolingContextRequest(
            ExpressionToolingContractVersion.V1,
            "draft",
            "node",
            "text",
            "JavaScript",
            "document"));

        Assert.Equal("Unauthorized", ReadState(endpoint));
        Assert.False(contextService.LastAuthorization!.IsAuthorized);
        Assert.Equal("policy-2", contextService.LastAuthorization.PolicyFingerprint);
    }

    [Theory]
    [InlineData(ExpressionToolingOutcomeState.Stale)]
    [InlineData(ExpressionToolingOutcomeState.Canceled)]
    public async Task Completion_preserves_non_success_context_states_without_invoking_the_provider(ExpressionToolingOutcomeState state)
    {
        var contextService = new RecordingContextService(
            ExpressionToolingOutcome<ExpressionAuthoringContext>.Failure(state, ExpressionToolingContractVersion.V1, contextRevision: "current"));
        var provider = new RecordingProvider();
        var endpoint = Create("CompleteExpressionTooling", contextService, new ProviderResolver(provider));

        await HandleAsync(endpoint, CompletionRequest());

        Assert.Equal(state.ToString(), ReadState(endpoint));
        Assert.Equal(0, provider.CompletionCalls);
        Assert.Equal("no-store", endpoint.HttpContext.Response.Headers.CacheControl.ToString());
    }

    private static ExpressionToolingCompletionRequest CompletionRequest() => new(
        ExpressionToolingContractVersion.V1,
        "draft",
        "node",
        "text",
        "JavaScript",
        "document",
        "args.symbol",
        new(0, 11));

    private static ExpressionAuthoringContext ContextWithSymbols(int count = 1)
    {
        var document = new ExpressionAuthoringDocument("document", "draft", "node", "text", "JavaScript", "document");
        var symbols = Enumerable.Range(0, count)
            .Select(index => new ExpressionSymbol($"symbol:{index}", $"symbol{index}", ExpressionSymbolKind.WorkflowInput, new("String")))
            .ToArray();
        return new(ExpressionToolingContractVersion.V1, document, "context", "catalog", symbols, new());
    }

    private static BaseEndpoint Create(
        string endpointName,
        IExpressionAuthoringContextService contextService,
        IExpressionToolingProviderResolver resolver,
        IExpressionAuthoringAuthorizationPolicy? authorizationPolicy = null)
    {
        var endpointType = typeof(WorkflowsDesignApiFeature).Assembly.GetType(
            $"Elsa.Workflows.Design.Api.Endpoints.Authoring.{endpointName}",
            throwOnError: true)!;
        var dependencies = endpointType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single()
            .GetParameters()
            .Select(parameter => ResolveDependency(
                parameter.ParameterType,
                contextService,
                resolver,
                authorizationPolicy ?? new DefaultExpressionAuthoringAuthorizationPolicy()))
            .ToArray();
        var create = typeof(Factory).GetMethods()
            .Single(method => method.Name == nameof(Factory.Create) && method.IsGenericMethodDefinition &&
                              method.GetParameters() is [var first, var rest] &&
                              first.ParameterType == typeof(Action<DefaultHttpContext>) &&
                              rest.ParameterType == typeof(object[]))
            .MakeGenericMethod(endpointType);
        return (BaseEndpoint)create.Invoke(null,
        [
            (Action<DefaultHttpContext>)(context => context.Response.Body = new MemoryStream()),
            dependencies
        ])!;
    }

    private static object ResolveDependency(
        Type type,
        IExpressionAuthoringContextService contextService,
        IExpressionToolingProviderResolver resolver,
        IExpressionAuthoringAuthorizationPolicy authorizationPolicy) =>
        type == typeof(IExpressionAuthoringContextService)
            ? contextService
            : type == typeof(IExpressionToolingProviderResolver)
                ? resolver
                : type == typeof(IExpressionAuthoringAuthorizationPolicy)
                    ? authorizationPolicy
                : type == typeof(IEnumerable<IExpressionToolingProvider>)
                    ? ((ProviderResolver)resolver).Providers
                    : throw new InvalidOperationException($"Unexpected tooling endpoint dependency '{type}'.");

    private static Task HandleAsync(BaseEndpoint endpoint, ExpressionToolingCompletionRequest request) =>
        HandleAsync(endpoint, (object)request);

    private static Task HandleAsync(BaseEndpoint endpoint, ExpressionToolingContextRequest request) =>
        HandleAsync(endpoint, (object)request);

    private static Task HandleAsync(BaseEndpoint endpoint, object request) =>
        (Task)endpoint.GetType()
            .GetMethod("HandleAsync", [request.GetType(), typeof(CancellationToken)])!
            .Invoke(endpoint, [request, CancellationToken.None])!;

    private static string ReadState(BaseEndpoint endpoint)
    {
        var stream = Assert.IsType<MemoryStream>(endpoint.HttpContext.Response.Body);
        stream.Position = 0;
        using var json = JsonDocument.Parse(stream);
        return ((ExpressionToolingOutcomeState)Property(Property(json.RootElement, "result"), "state").GetInt32()).ToString();

        static JsonElement Property(JsonElement element, string name) =>
            element.EnumerateObject()
                .Single(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                .Value;
    }

    private static (string? DocumentRevision, string? ContextRevision) ReadRevisions(BaseEndpoint endpoint)
    {
        var stream = Assert.IsType<MemoryStream>(endpoint.HttpContext.Response.Body);
        stream.Position = 0;
        using var json = JsonDocument.Parse(stream);
        var result = Property(json.RootElement, "result");
        return (
            Property(result, "documentRevision").GetString(),
            Property(result, "contextRevision").GetString());

        static JsonElement Property(JsonElement element, string name) =>
            element.EnumerateObject()
                .Single(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                .Value;
    }

    private sealed class RecordingContextService : IExpressionAuthoringContextService
    {
        private readonly ExpressionToolingOutcome<ExpressionAuthoringContext> _result;

        public RecordingContextService(ExpressionAuthoringContext? context = null) : this(
            ExpressionToolingOutcome<ExpressionAuthoringContext>.Success(
                context ?? ContextWithSymbols(),
                ExpressionToolingContractVersion.V1,
                "document",
                "context"))
        {
        }

        public RecordingContextService(ExpressionToolingOutcome<ExpressionAuthoringContext> result) => _result = result;
        public int PageCalls { get; private set; }
        public int ProviderCalls { get; private set; }
        public ExpressionAuthoringAuthorization? LastAuthorization { get; private set; }

        public ValueTask<ExpressionToolingOutcome<ExpressionAuthoringContext>> ResolveAsync(
            ResolveExpressionAuthoringContextRequest request,
            ExpressionAuthoringAuthorization authorization,
            CancellationToken cancellationToken)
        {
            PageCalls++;
            LastAuthorization = authorization;
            return ValueTask.FromResult(AuthorizedResult(authorization));
        }

        public ValueTask<ExpressionToolingOutcome<ExpressionAuthoringContext>> ResolveForProviderAsync(
            ResolveExpressionAuthoringContextRequest request,
            ExpressionAuthoringAuthorization authorization,
            CancellationToken cancellationToken)
        {
            ProviderCalls++;
            LastAuthorization = authorization;
            return ValueTask.FromResult(AuthorizedResult(authorization));
        }

        private ExpressionToolingOutcome<ExpressionAuthoringContext> AuthorizedResult(ExpressionAuthoringAuthorization authorization) =>
            authorization.IsAuthorized
                ? _result
                : ExpressionToolingOutcome<ExpressionAuthoringContext>.Failure(
                    ExpressionToolingOutcomeState.Unauthorized,
                    ExpressionToolingContractVersion.V1);
    }

    private sealed class ProviderResolver(params IExpressionToolingProvider[] providers) : IExpressionToolingProviderResolver
    {
        public IReadOnlyList<IExpressionToolingProvider> Providers { get; } = providers;
        public IExpressionToolingProvider? Find(string expressionType) =>
            Providers.FirstOrDefault(provider => string.Equals(provider.ExpressionType, expressionType, StringComparison.Ordinal));
    }

    private sealed class RecordingProvider(bool failCapabilities = false) : IExpressionToolingProvider
    {
        public string ExpressionType => "JavaScript";
        public ExpressionToolingContractVersion SupportedVersion => ExpressionToolingContractVersion.V1;
        public ExpressionToolingCapabilities DeclaredCapabilities { get; } = new();
        public int SymbolCount { get; private set; }
        public int CompletionCalls { get; private set; }

        public ValueTask<ExpressionToolingOutcome<ExpressionToolingCapabilities>> GetCapabilitiesAsync(
            ExpressionToolingRequestScope scope,
            CancellationToken cancellationToken)
        {
            if (failCapabilities)
                throw new InvalidOperationException("provider failure");
            return ValueTask.FromResult(ExpressionToolingOutcome<ExpressionToolingCapabilities>.Success(
                DeclaredCapabilities,
                SupportedVersion,
                scope.Document.DocumentRevision,
                scope.Context.ContextRevision));
        }

        public ValueTask<ExpressionToolingOutcome<ExpressionToolingItems>> GetCompletionsAsync(
            ExpressionCompletionRequest request,
            CancellationToken cancellationToken)
        {
            CompletionCalls++;
            SymbolCount = request.Scope.Context.RootSymbols.Count;
            return ValueTask.FromResult(ExpressionToolingOutcome<ExpressionToolingItems>.Success(
                new([new ExpressionToolingItem("symbol500")]),
                SupportedVersion,
                request.Scope.Document.DocumentRevision,
                request.Scope.Context.ContextRevision));
        }

        public ValueTask<ExpressionToolingOutcome<ExpressionHover>> GetHoverAsync(ExpressionHoverRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<ExpressionToolingOutcome<ExpressionDiagnosticSet>> ValidateAsync(ExpressionValidationRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubAuthorizationPolicy(ExpressionAuthoringAuthorization result)
        : IExpressionAuthoringAuthorizationPolicy
    {
        public ValueTask<ExpressionAuthoringAuthorization> AuthorizeAsync(
            ExpressionAuthoringAuthorization caller,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(result);
    }
}
