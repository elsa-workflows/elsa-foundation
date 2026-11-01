using System.Security.Claims;
using System.Text.Json;
using Elsa.Agent.Api;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Api.AspNetCore;
using Elsa.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Tests;

public sealed class AgentApiCurrentSurfaceTests
{
    [Fact]
    public async Task A_new_message_passes_its_current_surface_to_installed_admission_without_changing_the_session_or_old_target()
    {
        var source = new SurfaceSource();
        using var root = AgentTestServices.Create(services =>
        {
            services.AddRouting().AddElsaEndpoints();
            services.AddScoped<IAgentContextProvider>(_ => source);
        });
        using var scope = root.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IAgentSessionService>();
        var session = await sessions.CreateAsync(new("tenant", "actor", "conversation", "test", "explain", null,
            AgentPolicy.Default, new Dictionary<string, string> { ["route"] = "/resource-a", ["resourceId"] = "a" }));
        var app = new TestEndpointRouteBuilder(scope.ServiceProvider);
        AgentApi.MapAgentApi(app);
        var endpoint = Assert.Single(app.DataSources.SelectMany(x => x.Endpoints),
            x => x.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "ElsaAgentApiEndpointsPostMessage");
        var requestId = Guid.NewGuid().ToString("N");
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            content = "Explain this selection", requestId,
            source = new { moduleId = "surface.module", actionId = "explain", label = "Explain" },
            activeSurface = new { route = "/resource-b", resourceType = "surface.resource", resourceId = "b", selection = new { item = "selected" } }
        });
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "actor"), new Claim("elsa.identity.tenant_id", "tenant")], "test"))
        };
        context.Request.RouteValues["sessionId"] = session.Id;
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentLength = body.Length;
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(context);

        Assert.Equal(200, context.Response.StatusCode);
        var request = Assert.Single(source.Requests);
        Assert.Equal("/resource-b", request.ScopeKind);
        Assert.Equal("/resource-b", request.Inputs["route"]);
        Assert.Equal("surface.resource", request.Inputs["resourceType"]);
        Assert.Equal("b", request.Inputs["resourceId"]);
        using var selection = JsonDocument.Parse(request.Inputs["selection"]);
        Assert.Equal("selected", selection.RootElement.GetProperty("item").GetString());
        Assert.Null(request.Target);
        Assert.Equal("/resource-a", (await sessions.FindAsync(session.Id))!.Metadata["route"]);
        var accepted = Assert.Single(await sessions.ListMessagesAsync(session.Id));
        Assert.Equal(requestId, accepted.RequestId);
        Assert.Equal("surface.module", accepted.Source?.ModuleId);
        Assert.Equal("admitted-b", Assert.Single(accepted.Context).Id);
    }

    private sealed class SurfaceSource : IAgentContextProvider
    {
        public AgentContextProviderDescriptor Descriptor { get; } = new("surface.source", "surface.module", "*", ["surface.context"], ["1"], []);
        public List<AgentContextRequest> Requests { get; } = [];
        public ValueTask<AgentContextContribution> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return ValueTask.FromResult(new AgentContextContribution([
                new("admitted-b", "surface.module", "b", "Admitted resource", "surface.context", AgentContextSensitivity.Internal,
                    "surface", "A bounded admitted summary", null, new Dictionary<string, string>()) { ModuleId = "surface.module", SchemaVersion = "1" }
            ], []));
        }
    }
}
