using Elsa.Api.AspNetCore;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Tests.Support;
using Elsa.Workflows.Design.Core.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

/// <summary>Guards the expression tooling operation names/routes and shared read permission.</summary>
public sealed class ExpressionToolingEndpointTests
{
    public static TheoryData<string, string> ToolingEndpoints => new()
    {
        { "AuthoringResolveExpressionToolingContext", "design/workflows/expression-tooling/context" },
        { "AuthoringSearchExpressionToolingSymbols", "design/workflows/expression-tooling/symbols" },
        { "AuthoringCompleteExpressionTooling", "design/workflows/expression-tooling/completions" },
        { "AuthoringHoverExpressionTooling", "design/workflows/expression-tooling/hover" },
        { "AuthoringValidateExpressionTooling", "design/workflows/expression-tooling/validate" },
        { "AuthoringDescribeExpressionTooling", "design/workflows/expression-tooling/descriptors" }
    };

    [Theory]
    [MemberData(nameof(ToolingEndpoints))]
    public void Tooling_endpoints_require_design_read_permission_before_the_handler_runs(string operation, string route)
    {
        var endpoint = WorkflowDesignEndpointTestSupport.MapEndpoints().Single(candidate =>
            candidate.RoutePattern.RawText == route && candidate.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == $"ElsaWorkflowsDesignApiEndpoints{operation}");
        var security = Assert.IsType<EndpointSecurityDispositionMetadata>(endpoint.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>());
        var policy = Assert.IsType<PermissionPolicyParseResult>(new PermissionPolicyCodec().Parse(security.Value!));
        Assert.Contains(PermissionKey.Normalize(WorkflowDesignPermissions.Read), policy.Descriptor!.Permissions);
        Assert.NotNull(endpoint.Metadata.GetMetadata<AuthorizeAttribute>());
    }

    [Fact]
    public async Task Mapped_descriptor_endpoint_preserves_empty_catalog_and_no_store_cache_contract()
    {
        await using var host = ExpressionToolingHost.Create();

        var response = await host.InvokeAsync(
            "GET",
            "/design/workflows/expression-tooling/descriptors",
            body: null,
            contentType: null);
        using var document = JsonDocument.Parse(response.Body);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("no-store", response.CacheControl);
        Assert.Equal(1, document.RootElement.GetProperty("result").GetProperty("state").GetInt32());
        Assert.Empty(document.RootElement.GetProperty("result").GetProperty("payload").EnumerateArray());
    }

    [Fact]
    public async Task Mapped_completion_endpoint_preserves_full_catalog_and_provider_cache_contract()
    {
        var provider = new RecordingProvider();
        await using var host = ExpressionToolingHost.Create(provider);

        var response = await host.InvokeAsync(
            "POST",
            "/design/workflows/expression-tooling/completions",
            "{\"contractVersion\":{\"major\":1,\"minor\":0},\"workflowDraftId\":\"draft\",\"nodeId\":\"node\",\"propertyKey\":\"text\",\"expressionType\":\"JavaScript\",\"documentRevision\":\"document\",\"source\":\"args.symbol500\",\"cursor\":{\"line\":0,\"character\":14}}",
            "application/json");
        using var document = JsonDocument.Parse(response.Body);
        var result = document.RootElement.GetProperty("result");

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("no-store", response.CacheControl);
        Assert.Equal(0, result.GetProperty("state").GetInt32());
        Assert.Equal(501, provider.SymbolCount);
        Assert.Equal(1, provider.CompletionCalls);
    }

    [Fact]
    public async Task Mapped_completion_endpoint_preserves_absent_and_faulted_provider_outcomes()
    {
        await using var absent = ExpressionToolingHost.Create();
        await using var faulted = ExpressionToolingHost.Create(new RecordingProvider(failCapabilities: true));
        const string body = "{\"contractVersion\":{\"major\":1,\"minor\":0},\"workflowDraftId\":\"draft\",\"nodeId\":\"node\",\"propertyKey\":\"text\",\"expressionType\":\"JavaScript\",\"documentRevision\":\"document\",\"source\":\"args.symbol\",\"cursor\":{\"line\":0,\"character\":11}}";

        var absentResponse = await absent.InvokeAsync("POST", "/design/workflows/expression-tooling/completions", body, "application/json");
        var faultedResponse = await faulted.InvokeAsync("POST", "/design/workflows/expression-tooling/completions", body, "application/json");

        Assert.Equal("Unavailable", ReadState(absentResponse.Body));
        Assert.Equal("Unavailable", ReadState(faultedResponse.Body));
        Assert.Equal("no-store", absentResponse.CacheControl);
        Assert.Equal("no-store", faultedResponse.CacheControl);
        Assert.Contains("document", absentResponse.Body, StringComparison.Ordinal);
        Assert.Contains("context", absentResponse.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mapped_completion_endpoint_preserves_provider_cancellation()
    {
        var provider = new RecordingProvider(cancelCompletions: true);
        await using var host = ExpressionToolingHost.Create(provider);

        var response = await host.InvokeAsync("POST", "/design/workflows/expression-tooling/completions", CompletionBody, "application/json");

        Assert.Equal("Canceled", ReadState(response.Body));
        Assert.Contains("provider-canceled", response.Body, StringComparison.Ordinal);
        Assert.Equal(1, provider.CompletionCalls);
    }

    [Theory]
    [InlineData(ExpressionToolingOutcomeState.Stale)]
    [InlineData(ExpressionToolingOutcomeState.Canceled)]
    public async Task Context_service_non_success_outcomes_preserve_revisions_without_invoking_provider(ExpressionToolingOutcomeState state)
    {
        var provider = new RecordingProvider();
        await using var host = ExpressionToolingHost.Create(provider, outcomeState: state);

        var response = await host.InvokeAsync("POST", "/design/workflows/expression-tooling/completions", CompletionBody, "application/json");

        Assert.Equal(state.ToString(), ReadState(response.Body));
        Assert.Contains("document", response.Body, StringComparison.Ordinal);
        Assert.Contains("context", response.Body, StringComparison.Ordinal);
        Assert.Equal("no-store", response.CacheControl);
        Assert.Equal(0, provider.CapabilityCalls);
        Assert.Equal(0, provider.CompletionCalls);
    }

    [Theory]
    [InlineData("POST", "/design/workflows/expression-tooling/completions", CompletionBody)]
    [InlineData("POST", "/design/workflows/expression-tooling/hover", HoverBody)]
    [InlineData("POST", "/design/workflows/expression-tooling/validate", ValidateBody)]
    public async Task Absent_provider_outcomes_preserve_request_revisions_for_all_provider_operations(string method, string route, string body)
    {
        await using var host = ExpressionToolingHost.Create();

        var response = await host.InvokeAsync(method, route, body, "application/json");

        Assert.Equal("Unavailable", ReadState(response.Body));
        Assert.Contains("document", response.Body, StringComparison.Ordinal);
        Assert.Contains("context", response.Body, StringComparison.Ordinal);
        Assert.Equal("no-store", response.CacheControl);
    }

    [Fact]
    public async Task Denied_authoring_context_does_not_invoke_the_provider()
    {
        var provider = new RecordingProvider();
        await using var host = ExpressionToolingHost.Create(provider, new DenyAuthorizationPolicy());

        var response = await host.InvokeAsync("POST", "/design/workflows/expression-tooling/completions", CompletionBody, "application/json");

        Assert.Equal("Unauthorized", ReadState(response.Body));
        Assert.Equal(0, provider.CapabilityCalls);
        Assert.Equal(0, provider.CompletionCalls);
        Assert.DoesNotContain("args.symbol500", response.Body, StringComparison.Ordinal);
        Assert.Equal("no-store", response.CacheControl);
    }

    private const string CompletionBody = "{\"contractVersion\":{\"major\":1,\"minor\":0},\"workflowDraftId\":\"draft\",\"nodeId\":\"node\",\"propertyKey\":\"text\",\"expressionType\":\"JavaScript\",\"documentRevision\":\"document\",\"source\":\"args.symbol500\",\"cursor\":{\"line\":0,\"character\":14}}";
    private const string HoverBody = "{\"contractVersion\":{\"major\":1,\"minor\":0},\"workflowDraftId\":\"draft\",\"nodeId\":\"node\",\"propertyKey\":\"text\",\"expressionType\":\"JavaScript\",\"documentRevision\":\"document\",\"source\":\"args.symbol500\",\"position\":{\"line\":0,\"character\":14}}";
    private const string ValidateBody = "{\"contractVersion\":{\"major\":1,\"minor\":0},\"workflowDraftId\":\"draft\",\"nodeId\":\"node\",\"propertyKey\":\"text\",\"expressionType\":\"JavaScript\",\"documentRevision\":\"document\",\"source\":\"args.symbol500\"}";

    private static string ReadState(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ((ExpressionToolingOutcomeState)document.RootElement.GetProperty("result").GetProperty("state").GetInt32()).ToString();
    }

    private sealed class ExpressionToolingHost(IServiceProvider services) : IAsyncDisposable
    {
        public static ExpressionToolingHost Create(RecordingProvider? provider = null, IExpressionAuthoringAuthorizationPolicy? policy = null, ExpressionToolingOutcomeState? outcomeState = null)
        {
            var serviceCollection = new ServiceCollection()
                .AddLogging()
                .AddSingleton<IExpressionAuthoringContextService>(new RecordingContextService(ContextWithSymbols(), outcomeState))
                .AddSingleton<IExpressionAuthoringAuthorizationPolicy>(policy ?? new AllowAuthorizationPolicy())
                .AddSingleton<IExpressionToolingProviderResolver>(new ProviderResolver(provider));
            if (provider is not null)
                serviceCollection.AddSingleton<IExpressionToolingProvider>(provider);
            var services = serviceCollection.BuildServiceProvider();
            return new(services);
        }

        public async Task<Response> InvokeAsync(string method, string path, string? body, string? contentType)
        {
            var endpoint = WorkflowDesignEndpointTestSupport.MapEndpoints().Single(candidate =>
                string.Equals(candidate.RoutePattern.RawText, path.TrimStart('/'), StringComparison.Ordinal));
            var context = new DefaultHttpContext { RequestServices = services };
            context.Request.Method = method;
            context.Request.Path = path;
            context.Request.ContentType = contentType;
            context.Request.Body = new MemoryStream(body is null ? [] : Encoding.UTF8.GetBytes(body));
            context.Response.Body = new MemoryStream();
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "author-1")], "test"));
            await endpoint.RequestDelegate!(context);
            context.Response.Body.Position = 0;
            using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
            var responseBody = await reader.ReadToEndAsync();
            return new(context.Response.StatusCode, context.Response.Headers.CacheControl.ToString(), responseBody);
        }

        public ValueTask DisposeAsync()
        {
            (services as IDisposable)?.Dispose();
            return ValueTask.CompletedTask;
        }

        public sealed record Response(int StatusCode, string CacheControl, string Body);
    }

    private static ExpressionAuthoringContext ContextWithSymbols(int count = 501)
    {
        var document = new ExpressionAuthoringDocument("document", "draft", "node", "text", "JavaScript", "document");
        var symbols = Enumerable.Range(0, count)
            .Select(index => new ExpressionSymbol($"symbol:{index}", $"symbol{index}", ExpressionSymbolKind.WorkflowInput, new("String")))
            .ToArray();
        return new(ExpressionToolingContractVersion.V1, document, "context", "catalog", symbols, new());
    }

    private sealed class RecordingContextService(ExpressionAuthoringContext context, ExpressionToolingOutcomeState? outcomeState = null) : IExpressionAuthoringContextService
    {
        public ValueTask<ExpressionToolingOutcome<ExpressionAuthoringContext>> ResolveAsync(
            ResolveExpressionAuthoringContextRequest request,
            ExpressionAuthoringAuthorization authorization,
            CancellationToken cancellationToken) => ValueTask.FromResult(Result(authorization));

        public ValueTask<ExpressionToolingOutcome<ExpressionAuthoringContext>> ResolveForProviderAsync(
            ResolveExpressionAuthoringContextRequest request,
            ExpressionAuthoringAuthorization authorization,
            CancellationToken cancellationToken) => ValueTask.FromResult(Result(authorization));

        private ExpressionToolingOutcome<ExpressionAuthoringContext> Result(ExpressionAuthoringAuthorization authorization) =>
            outcomeState is { } state
                ? ExpressionToolingOutcome<ExpressionAuthoringContext>.Failure(state, ExpressionToolingContractVersion.V1, "context-state", documentRevision: context.Document.DocumentRevision, contextRevision: context.ContextRevision)
                : authorization.IsAuthorized
                ? ExpressionToolingOutcome<ExpressionAuthoringContext>.Success(context, ExpressionToolingContractVersion.V1, "document", "context")
                : ExpressionToolingOutcome<ExpressionAuthoringContext>.Failure(ExpressionToolingOutcomeState.Unauthorized, ExpressionToolingContractVersion.V1);
    }

    private sealed class AllowAuthorizationPolicy : IExpressionAuthoringAuthorizationPolicy
    {
        public ValueTask<ExpressionAuthoringAuthorization> AuthorizeAsync(ExpressionAuthoringAuthorization caller, CancellationToken cancellationToken) =>
            ValueTask.FromResult(caller with { IsAuthorized = true, PolicyFingerprint = "policy" });
    }

    private sealed class ProviderResolver(RecordingProvider? provider) : IExpressionToolingProviderResolver
    {
        public IExpressionToolingProvider? Find(string expressionType) => provider is not null && provider.ExpressionType == expressionType ? provider : null;
    }

    private sealed class RecordingProvider(bool failCapabilities = false, bool cancelCompletions = false) : IExpressionToolingProvider
    {
        public string ExpressionType => "JavaScript";
        public ExpressionToolingContractVersion SupportedVersion => ExpressionToolingContractVersion.V1;
        public int SymbolCount { get; private set; }
        public int CapabilityCalls { get; private set; }
        public int CompletionCalls { get; private set; }

        public ValueTask<ExpressionToolingOutcome<ExpressionToolingCapabilities>> GetCapabilitiesAsync(ExpressionToolingRequestScope scope, CancellationToken cancellationToken)
        {
            CapabilityCalls++;
            if (failCapabilities)
                throw new InvalidOperationException("provider failure");
            return ValueTask.FromResult(ExpressionToolingOutcome<ExpressionToolingCapabilities>.Success(new(), SupportedVersion, scope.Document.DocumentRevision, scope.Context.ContextRevision));
        }

        public ValueTask<ExpressionToolingOutcome<ExpressionToolingItems>> GetCompletionsAsync(ExpressionCompletionRequest request, CancellationToken cancellationToken)
        {
            CompletionCalls++;
            if (cancelCompletions)
                throw new OperationCanceledException();
            SymbolCount = request.Scope.Context.RootSymbols.Count;
            return ValueTask.FromResult(ExpressionToolingOutcome<ExpressionToolingItems>.Success(new([new("symbol500")]), SupportedVersion, request.Scope.Document.DocumentRevision, request.Scope.Context.ContextRevision));
        }

        public ValueTask<ExpressionToolingOutcome<ExpressionHover>> GetHoverAsync(ExpressionHoverRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ExpressionToolingOutcome<ExpressionDiagnosticSet>> ValidateAsync(ExpressionValidationRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class DenyAuthorizationPolicy : IExpressionAuthoringAuthorizationPolicy
    {
        public ValueTask<ExpressionAuthoringAuthorization> AuthorizeAsync(ExpressionAuthoringAuthorization caller, CancellationToken cancellationToken) =>
            ValueTask.FromResult(caller with { IsAuthorized = false, PolicyFingerprint = "denied" });
    }
}
