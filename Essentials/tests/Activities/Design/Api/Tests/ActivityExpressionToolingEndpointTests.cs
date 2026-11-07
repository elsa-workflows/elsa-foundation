using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Api.Capabilities.Contracts;
using Elsa.Expressions.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Elsa.Api.AspNetCore;
using Elsa.Expressions.Core.Models;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Testing;
using Elsa.Workflows.Design.Core.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Elsa.Activities.Design.Api.Tests;

public sealed class ActivityExpressionToolingEndpointTests
{
    [Fact]
    public void Activity_tooling_routes_use_only_the_activity_read_gate()
    {
        using var services = new ServiceCollection().AddRouting().AddElsaEndpoints().BuildServiceProvider();
        var endpoints = Map(services).Where(endpoint => endpoint.RoutePattern.RawText!.Contains("/expression-tooling/", StringComparison.Ordinal)).ToArray();
        Assert.Equal(6, endpoints.Length);
        foreach (var endpoint in endpoints)
        {
            var policy = Assert.Single(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()).Policy;
            var parsed = new PermissionPolicyCodec().Parse(policy!);
            Assert.Equal([PermissionKey.Normalize(ActivityDesignPermissions.Read)], parsed.Descriptor!.Permissions);
            Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
        }
    }

    [Theory]
    [InlineData(true, true, true, true, 1, true)]
    [InlineData(false, true, true, true, 1, false)]
    [InlineData(true, false, true, true, 1, false)]
    [InlineData(true, true, false, true, 1, false)]
    [InlineData(true, true, true, false, 1, false)]
    [InlineData(true, true, true, true, 0, false)]
    [InlineData(true, true, true, true, 2, false)]
    public async Task Capability_requires_composed_operations_context_graph_language_and_one_authority(
        bool operations, bool context, bool graph, bool language, int authorities, bool advertised)
    {
        var services = new ServiceCollection();
        new ActivitiesDesignApiFeature().ConfigureServices(services);
        services.RemoveAll<IActivityDraftAuthoringReader>();
        for (var index = 0; index < authorities; index++) services.AddSingleton<IActivityDraftAuthoringReader>(new Reader());
        if (operations) services.AddSingleton<IExpressionToolingOperations>(new Operations());
        if (context) services.AddSingleton<IExpressionAuthoringContextService>(new Context());
        if (graph) services.AddSingleton<IActivityDraftExpressionGraphProjection>(new Projection());
        if (language) services.AddSingleton<IExpressionToolingProvider>(new Provider());
        services.AddSingleton<IExpressionAuthoringAuthorizationPolicy>(new Policy());
        await using var provider = services.BuildServiceProvider();
        var declarations = new List<Elsa.Api.Capabilities.Models.ApiCapabilityDeclaration>();
        foreach (var source in provider.GetServices<IApiCapabilitySource>())
            declarations.AddRange(await source.GetCapabilitiesAsync());
        Assert.Equal(advertised, declarations.Any(declaration => declaration.CapabilityId == "activity-expressions.tooling.v1"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Activity_context_derives_its_authority_from_the_activity_location_and_optional_operations(int operationCount)
    {
        var operations = new Operations();
        var services = new ServiceCollection().AddLogging().AddRouting()
            .AddSingleton<IActivityAuthoringContextAsync>(new Actor()).AddSingleton<IExpressionAuthoringAuthorizationPolicy>(new Policy());
        new ActivitiesDesignApiFeature().ConfigureServices(services);
        for (var index = 0; index < operationCount; index++) services.AddSingleton<IExpressionToolingOperations>(operations);
        await using var provider = services.BuildServiceProvider();
        var endpoint = Map(provider).Single(item => item.RoutePattern.RawText == "/design/activities/expression-tooling/context");
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Method = "POST";
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("""
            {"contractVersion":{"major":1,"minor":0},"activityDraftId":"activity-1","nodeId":"node","propertyKey":"text","expressionType":"JavaScript","documentRevision":"document",
            "workflowDraftId":"caller-draft","owner":{"kind":"workflow","id":"caller-draft"},"rootSymbols":[{"name":"injected"}]}
            """));
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(context);
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        var result = json.RootElement.GetProperty("result");
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        if (operationCount == 1)
        {
            Assert.Equal(1, operations.Calls);
            Assert.Equal("", operations.Location!.WorkflowDraftId);
            Assert.Equal(new ExpressionAuthoringDocumentOwner("activity-draft", "activity-1"), operations.Location.Owner);
            Assert.Equal("success", result.GetProperty("state").GetString());
            Assert.Equal("activity-draft", result.GetProperty("payload").GetProperty("document").GetProperty("owner").GetProperty("kind").GetString());
            var binding = result.GetProperty("payload").GetProperty("rootSymbols")[0].GetProperty("parameterBinding");
            Assert.Equal("ActivityResult", binding.GetProperty("kind").GetString());
            Assert.Equal("producer:one", binding.GetProperty("producerNodeId").GetString());
            Assert.Equal("result:key", binding.GetProperty("projectionKey").GetString());
        }
        else
        {
            Assert.Equal(0, operations.Calls);
            Assert.Equal("unavailable", result.GetProperty("state").GetString());
            Assert.False(result.TryGetProperty("payload", out var payload) && payload.ValueKind != JsonValueKind.Null);
        }
    }

    private static IEnumerable<RouteEndpoint> Map(IServiceProvider services)
    {
        var routes = new TestEndpointRouteBuilder(services);
        ActivitiesDesignApi.MapActivitiesDesignApi(routes);
        return routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>();
    }

    private sealed class Operations : IExpressionToolingOperations
    {
        public int Calls { get; private set; }
        public ResolveExpressionAuthoringContextRequest? Location { get; private set; }
        public ValueTask<ExpressionToolingOutcome<ExpressionAuthoringContext>> ResolveContextAsync(ResolveExpressionAuthoringContextRequest request, ExpressionAuthoringAuthorization authorization, CancellationToken cancellationToken)
        {
            Calls++;
            Location = request;
            var document = new ExpressionAuthoringDocument("activity-draft:activity-1:node:text:JavaScript", request.WorkflowDraftId, request.NodeId, request.PropertyKey,
                request.ExpressionType, request.DocumentRevision) { Owner = request.Owner };
            var symbol = new ExpressionSymbol("opaque-result", "Earlier result", ExpressionSymbolKind.ActivityResult)
                { ParameterBinding = new ActivityResultExpressionParameterBinding("producer:one", "result:key") };
            var context = new ExpressionAuthoringContext(ExpressionToolingContractVersion.V1, document, "context", "catalog", [symbol], new());
            return ValueTask.FromResult(ExpressionToolingOutcome<ExpressionAuthoringContext>.Success(context, ExpressionToolingContractVersion.V1, "document", "context"));
        }
        public ValueTask<ExpressionToolingOutcome<ExpressionToolingItems>> SearchSymbolsAsync(ResolveExpressionAuthoringContextRequest request, ExpressionAuthoringAuthorization authorization, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ExpressionToolingOutcome<ExpressionToolingItems>> CompleteAsync(ResolveExpressionAuthoringContextRequest request, string source, ExpressionToolingPosition cursor, ExpressionAuthoringAuthorization authorization, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ExpressionToolingOutcome<ExpressionHover>> HoverAsync(ResolveExpressionAuthoringContextRequest request, string source, ExpressionToolingPosition position, ExpressionAuthoringAuthorization authorization, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ExpressionToolingOutcome<ExpressionDiagnosticSet>> ValidateAsync(ResolveExpressionAuthoringContextRequest request, string source, ExpressionAuthoringAuthorization authorization, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Reader : IActivityDraftAuthoringReader
    {
        public ValueTask<ActivityDraftAuthoringSource?> FindAsync(string draftId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Context : IExpressionAuthoringContextService
    {
        public ValueTask<ExpressionToolingOutcome<ExpressionAuthoringContext>> ResolveAsync(ResolveExpressionAuthoringContextRequest request, ExpressionAuthoringAuthorization authorization, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Projection : IActivityDraftExpressionGraphProjection
    {
        public string ProviderKey => "elsa.activity-graph";
        public WorkflowDefinitionState? TryProject(ActivityDefinitionDraftState state) => throw new NotSupportedException();
    }

    private sealed class Provider : IExpressionToolingProvider
    {
        public string ExpressionType => "JavaScript";
        public ExpressionToolingContractVersion SupportedVersion => ExpressionToolingContractVersion.V1;
        public ValueTask<ExpressionToolingOutcome<ExpressionToolingCapabilities>> GetCapabilitiesAsync(ExpressionToolingRequestScope scope, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ExpressionToolingOutcome<ExpressionToolingItems>> GetCompletionsAsync(ExpressionCompletionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ExpressionToolingOutcome<ExpressionHover>> GetHoverAsync(ExpressionHoverRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ExpressionToolingOutcome<ExpressionDiagnosticSet>> ValidateAsync(ExpressionValidationRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Policy : IExpressionAuthoringAuthorizationPolicy
    {
        public ValueTask<ExpressionAuthoringAuthorization> AuthorizeAsync(ExpressionAuthoringAuthorization caller, CancellationToken cancellationToken) => ValueTask.FromResult(caller);
    }

    private sealed class Actor : IActivityAuthoringContextAsync
    {
        public string? TenantId => "tenant";
        public string ActorId => "author";
        public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult("profile");
        public ValueTask<bool> CanReadProviderPayloadAsync(string providerKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> CanAuthorProviderAsync(string providerKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> CanManageActivityDefinitionsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
