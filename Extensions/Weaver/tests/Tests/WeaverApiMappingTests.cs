using Elsa.Weaver.Api;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Workflows;
using Elsa.Weaver.Workflows.Services;
using Elsa.Weaver.Api.Constants;
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

namespace Elsa.Weaver.Tests;

public sealed class WeaverApiMappingTests
{
    [Fact]
    public async Task Posting_a_stored_context_identity_selects_only_its_exact_case()
    {
        using var root = WeaverTestServices.Create(services =>
        {
            services.AddRouting().AddElsaEndpoints();
            new WeaverWorkflowsFeature().ConfigureServices(services);
        });
        using var scope = root.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IWeaverSessionService>();
        var session = await sessions.CreateAsync(new("tenant-1", "actor-1", "conversation-1", "test", "explain", null, WeaverPolicy.Default, new Dictionary<string, string>()));
        WeaverContextAttachment Attachment(string id, string intent) => new(id, "workflow.request", "wf-1", "Request", "workflow.request",
            WeaverContextSensitivity.Internal, "request", "Request metadata", JsonSerializer.SerializeToElement(new { requestKind = "workflow-definition", referenceId = "wf-1", intent }), new Dictionary<string, string>())
        { ModuleId = WorkflowWeaverSnapshot.ModuleId, SchemaVersion = "1" };
        await sessions.AddContextAsync(session.Id, [Attachment("ContextA", "selected"), Attachment("Contexta", "unselected")]);
        Assert.Equal(2, (await sessions.ListContextAsync(session.Id)).Count);
        var app = new TestEndpointRouteBuilder(scope.ServiceProvider);
        WeaverApi.MapWeaverApi(app);
        var endpoint = Assert.Single(app.DataSources.SelectMany(source => source.Endpoints), endpoint => endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "ElsaWeaverApiEndpointsPostMessage");
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
        using var root = WeaverTestServices.Create(services =>
        {
            services.AddRouting().AddElsaEndpoints();
            new WeaverWorkflowsFeature().ConfigureServices(services);
        });
        using var scope = root.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IWeaverSessionService>();
        var session = await sessions.CreateAsync(new("tenant-1", "actor-1", "conversation-1", "test", "explain", null, WeaverPolicy.Default, new Dictionary<string, string>()));
        await sessions.AddContextAsync(session.Id, [new("ContextA", "workflow.request", "wf-1", "Request", "workflow.request",
            WeaverContextSensitivity.Internal, "request", "Request metadata", JsonSerializer.SerializeToElement(new { requestKind = "workflow-definition", referenceId = "wf-1", intent = "selected" }), new Dictionary<string, string>())
        { ModuleId = WorkflowWeaverSnapshot.ModuleId, SchemaVersion = "1" }]);
        var app = new TestEndpointRouteBuilder(scope.ServiceProvider);
        WeaverApi.MapWeaverApi(app);
        var endpoint = Assert.Single(app.DataSources.SelectMany(source => source.Endpoints), endpoint => endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "ElsaWeaverApiEndpointsPostMessage");
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
        Assert.Equal("weaver.context.required_unavailable", response.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain(missingId, response.RootElement.GetRawText());
        Assert.Empty(await sessions.ListMessagesAsync(session.Id));
        Assert.Single(await sessions.ListContextAsync(session.Id));
        Assert.DoesNotContain(await scope.ServiceProvider.GetRequiredService<IWeaverAuditReader>().ListAsync(session.Id),
            item => item.Kind is WeaverAuditEventKind.ContextCollected or WeaverAuditEventKind.ToolInvoked or WeaverAuditEventKind.ProposalCreated);
    }

    [Fact]
    public void Maps_exactly_eighteen_named_operations()
    {
        using var services = new ServiceCollection().AddRouting().AddElsaEndpoints().BuildServiceProvider();
        var app = new TestEndpointRouteBuilder(services);

        WeaverApi.MapWeaverApi(app);

        var names = app.DataSources
            .SelectMany(x => x.Endpoints)
            .Select(x => x.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName)
            .Where(x => x is not null)
            .OrderBy(x => x)
            .ToArray();

        Assert.Equal(18, names.Length);
        Assert.All(names, name => Assert.StartsWith("ElsaWeaverApiEndpoints", name, StringComparison.Ordinal));
    }

    [Fact]
    public void Every_route_declares_owner_minimal_authoring_and_exactly_one_permission_disposition()
    {
        using var services = new ServiceCollection().AddRouting().AddElsaEndpoints().BuildServiceProvider();
        var app = new TestEndpointRouteBuilder(services);
        WeaverApi.MapWeaverApi(app);

        var permissions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ElsaWeaverApiEndpointsBootstrap"] = WeaverPermissionKeys.Use,
            ["ElsaWeaverApiEndpointsSupport"] = WeaverPermissionKeys.Use,
            ["ElsaWeaverApiEndpointsCreateSession"] = WeaverPermissionKeys.Use,
            ["ElsaWeaverApiEndpointsGetSession"] = WeaverPermissionKeys.Use,
            ["ElsaWeaverApiEndpointsChangeAutonomy"] = WeaverPermissionKeys.Use,
            ["ElsaWeaverApiEndpointsPostMessage"] = WeaverPermissionKeys.Use,
            ["ElsaWeaverApiEndpointsCancelTurn"] = WeaverPermissionKeys.Use,
            ["ElsaWeaverApiEndpointsStreamSession"] = WeaverPermissionKeys.Use,
            ["ElsaWeaverApiEndpointsFeedback"] = WeaverPermissionKeys.Use,
            ["ElsaWeaverApiEndpointsApproveProposal"] = WeaverPermissionKeys.Proposals,
            ["ElsaWeaverApiEndpointsDenyProposal"] = WeaverPermissionKeys.Proposals,
            ["ElsaWeaverApiEndpointsExecuteProposal"] = WeaverPermissionKeys.Proposals,
            ["ElsaWeaverApiEndpointsAcknowledgeClientInvocation"] = WeaverPermissionKeys.Proposals,
            ["ElsaWeaverApiEndpointsAudit"] = WeaverPermissionKeys.Audit,
            ["ElsaWeaverApiEndpointsListSessions"] = WeaverPermissionKeys.Use,
            ["ElsaWeaverApiEndpointsListSessionProposals"] = WeaverPermissionKeys.Use,
            ["ElsaWeaverApiEndpointsWithdrawMessage"] = WeaverPermissionKeys.Use,
            ["ElsaWeaverApiEndpointsResolveClientInvocation"] = WeaverPermissionKeys.Proposals
        };
        var codec = new PermissionPolicyCodec();

        var endpoints = app.DataSources.SelectMany(x => x.Endpoints).Cast<RouteEndpoint>().ToArray();
        Assert.Equal(permissions.Count, endpoints.Length);
        foreach (var endpoint in endpoints)
        {
            var operationId = endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName;
            Assert.NotNull(operationId);
            Assert.Equal("Elsa.Weaver.Api", endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()?.OwnerId);
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
