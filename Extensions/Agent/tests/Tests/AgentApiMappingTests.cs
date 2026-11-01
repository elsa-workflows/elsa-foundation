using Elsa.Agent.Api;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Workflows;
using Elsa.Agent.Workflows.Services;
using Elsa.Agent.Api.Constants;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Testing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using System.Text.Json;

namespace Elsa.Agent.Tests;

public sealed class AgentApiMappingTests
{
    [Fact]
    public async Task Posting_a_stored_context_identity_selects_only_its_exact_case()
    {
        using var root = AgentTestServices.Create(services =>
        {
            services.AddRouting().AddElsaEndpoints();
            new FoundationWorkflowsAgentFeature().ConfigureServices(services);
        });
        using var scope = root.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IAgentSessionService>();
        var session = await sessions.CreateAsync(new("tenant-1", "actor-1", "conversation-1", "test", "explain", null, AgentPolicy.Default, new Dictionary<string, string>()));
        AgentContextAttachment Attachment(string id, string intent) => new(id, "workflow.request", "wf-1", "Request", "workflow.request",
            AgentContextSensitivity.Internal, "request", "Request metadata", JsonSerializer.SerializeToElement(new { requestKind = "workflow-definition", referenceId = "wf-1", intent }), new Dictionary<string, string>())
        { ModuleId = WorkflowAgentSnapshot.ModuleId, SchemaVersion = "1" };
        await sessions.AddContextAsync(session.Id, [Attachment("ContextA", "selected"), Attachment("Contexta", "unselected")]);
        Assert.Equal(2, (await sessions.ListContextAsync(session.Id)).Count);
        var app = new TestEndpointRouteBuilder(scope.ServiceProvider);
        AgentApi.MapAgentApi(app);
        var endpoint = Assert.Single(app.DataSources.SelectMany(source => source.Endpoints), endpoint => endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "ElsaAgentApiEndpointsPostMessage");
        var body = JsonSerializer.SerializeToUtf8Bytes(new { content = "Explain selected request", contextAttachmentIds = new[] { "ContextA" }, requestId = "exact-context-selection" });
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "actor-1"), new Claim("elsa.identity.tenant_id", "tenant-1")], "test")) };
        context.Request.RouteValues["sessionId"] = session.Id;
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentLength = body.Length;
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        var message = Assert.Single(await sessions.ListMessagesAsync(session.Id));
        var selected = Assert.Single(message.Context, attachment => attachment.ContentType == "workflow.request");
        Assert.Equal("selected", Assert.IsType<JsonElement>(selected.Content).GetProperty("intent").GetString());
    }

    [Theory]
    [InlineData("MISSING-CONTEXT-SECRET-9137")]
    [InlineData("contexta")]
    public async Task Missing_requested_stored_context_refuses_the_whole_prompt_before_collection(string missingId)
    {
        using var root = AgentTestServices.Create(services =>
        {
            services.AddRouting().AddElsaEndpoints();
            new FoundationWorkflowsAgentFeature().ConfigureServices(services);
        });
        using var scope = root.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IAgentSessionService>();
        var session = await sessions.CreateAsync(new("tenant-1", "actor-1", "conversation-1", "test", "explain", null, AgentPolicy.Default, new Dictionary<string, string>()));
        await sessions.AddContextAsync(session.Id, [new("ContextA", "workflow.request", "wf-1", "Request", "workflow.request",
            AgentContextSensitivity.Internal, "request", "Request metadata", JsonSerializer.SerializeToElement(new { requestKind = "workflow-definition", referenceId = "wf-1", intent = "selected" }), new Dictionary<string, string>())
        { ModuleId = WorkflowAgentSnapshot.ModuleId, SchemaVersion = "1" }]);
        var app = new TestEndpointRouteBuilder(scope.ServiceProvider);
        AgentApi.MapAgentApi(app);
        var endpoint = Assert.Single(app.DataSources.SelectMany(source => source.Endpoints), endpoint => endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "ElsaAgentApiEndpointsPostMessage");
        var body = JsonSerializer.SerializeToUtf8Bytes(new { content = "Explain selected request", contextAttachmentIds = new[] { "ContextA", missingId }, requestId = "missing-context-selection" });
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "actor-1"), new Claim("elsa.identity.tenant_id", "tenant-1")], "test")) };
        context.Request.RouteValues["sessionId"] = session.Id;
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentLength = body.Length;
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var response = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("agent.context.required_unavailable", response.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain(missingId, response.RootElement.GetRawText());
        Assert.Empty(await sessions.ListMessagesAsync(session.Id));
        Assert.Single(await sessions.ListContextAsync(session.Id));
        Assert.DoesNotContain(await scope.ServiceProvider.GetRequiredService<IAgentAuditReader>().ListAsync(session.Id),
            item => item.Kind is AgentAuditEventKind.ContextCollected or AgentAuditEventKind.ToolInvoked or AgentAuditEventKind.ProposalCreated);
    }

    [Fact]
    public void Maps_exactly_thirteen_named_operations()
    {
        using var services = new ServiceCollection().AddRouting().AddElsaEndpoints().BuildServiceProvider();
        var app = new TestEndpointRouteBuilder(services);

        AgentApi.MapAgentApi(app);

        var names = app.DataSources
            .SelectMany(x => x.Endpoints)
            .Select(x => x.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName)
            .Where(x => x is not null)
            .OrderBy(x => x)
            .ToArray();

        Assert.Equal(13, names.Length);
        Assert.All(names, name => Assert.StartsWith("ElsaAgentApiEndpoints", name, StringComparison.Ordinal));
    }

    [Fact]
    public void Every_route_declares_owner_minimal_authoring_and_exactly_one_permission_disposition()
    {
        using var services = new ServiceCollection().AddRouting().AddElsaEndpoints().BuildServiceProvider();
        var app = new TestEndpointRouteBuilder(services);
        AgentApi.MapAgentApi(app);

        var permissions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ElsaAgentApiEndpointsBootstrap"] = AgentPermissionKeys.Use,
            ["ElsaAgentApiEndpointsCreateSession"] = AgentPermissionKeys.Use,
            ["ElsaAgentApiEndpointsGetSession"] = AgentPermissionKeys.Use,
            ["ElsaAgentApiEndpointsChangeAutonomy"] = AgentPermissionKeys.Use,
            ["ElsaAgentApiEndpointsPostMessage"] = AgentPermissionKeys.Use,
            ["ElsaAgentApiEndpointsCancelTurn"] = AgentPermissionKeys.Use,
            ["ElsaAgentApiEndpointsStreamSession"] = AgentPermissionKeys.Use,
            ["ElsaAgentApiEndpointsFeedback"] = AgentPermissionKeys.Use,
            ["ElsaAgentApiEndpointsApproveProposal"] = AgentPermissionKeys.Proposals,
            ["ElsaAgentApiEndpointsDenyProposal"] = AgentPermissionKeys.Proposals,
            ["ElsaAgentApiEndpointsExecuteProposal"] = AgentPermissionKeys.Proposals,
            ["ElsaAgentApiEndpointsAcknowledgeClientInvocation"] = AgentPermissionKeys.Proposals,
            ["ElsaAgentApiEndpointsAudit"] = AgentPermissionKeys.Audit
        };
        var codec = new PermissionPolicyCodec();

        var endpoints = app.DataSources.SelectMany(x => x.Endpoints).Cast<RouteEndpoint>().ToArray();
        Assert.Equal(permissions.Count, endpoints.Length);
        foreach (var endpoint in endpoints)
        {
            var operationId = endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName;
            Assert.NotNull(operationId);
            Assert.Equal("Elsa.Agent.Api", endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()?.OwnerId);
            Assert.Equal(EndpointAuthoringModels.MinimalApi, endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model);

            var dispositions = endpoint.Metadata.GetOrderedMetadata<EndpointSecurityDispositionMetadata>();
            Assert.Single(dispositions);
            Assert.Equal(EndpointSecurityDispositionKind.Permission, dispositions[0].Kind);
            Assert.Equal(codec.Format(PermissionPolicyDescriptor.Single(permissions[operationId!])), dispositions[0].Value);

            var authorization = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
            Assert.Single(authorization);
            Assert.Equal(dispositions[0].Value, authorization[0].Policy);
            Assert.DoesNotContain(endpoint.Metadata, item => item is IAllowAnonymous);
        }
    }
}
