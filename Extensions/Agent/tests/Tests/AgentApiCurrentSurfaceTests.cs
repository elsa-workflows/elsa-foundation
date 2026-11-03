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

    [Fact]
    public async Task Overlapping_deliveries_each_pass_their_own_surface_and_only_the_accepted_one_keeps_its_admitted_context()
    {
        // #3440 T016: the surface a delivery was collected for travels with that delivery through insertion.
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
        async Task<int> PostAsync(string requestId, string resourceId, string label)
        {
            var body = JsonSerializer.SerializeToUtf8Bytes(new
            {
                content = $"Explain {resourceId}", requestId,
                source = new { moduleId = "surface.module", actionId = "explain", label },
                activeSurface = new { route = $"/resource-{resourceId}", resourceType = "surface.resource", resourceId }
            });
            using var request = root.CreateScope();
            var context = new DefaultHttpContext
            {
                RequestServices = request.ServiceProvider,
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, "actor"), new Claim("elsa.identity.tenant_id", "tenant")], "test"))
            };
            context.Request.RouteValues["sessionId"] = session.Id;
            context.Request.ContentType = "application/json";
            context.Request.Body = new MemoryStream(body);
            context.Request.ContentLength = body.Length;
            context.Response.Body = new MemoryStream();
            await endpoint.RequestDelegate!(context);
            return context.Response.StatusCode;
        }
        var held = source.HoldNext();

        var late = Task.Run(() => PostAsync("r-b", "b", "Explain B"));
        await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var accepted = await PostAsync("r-c", "c", "Explain C");
        held.Release.SetResult();
        var refused = await late.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(200, accepted);
        Assert.Equal(409, refused);
        Assert.Equal(["b", "c"], source.Requests.Select(x => x.Inputs["resourceId"]).Order().ToArray());
        var message = Assert.Single(await sessions.ListMessagesAsync(session.Id));
        Assert.Equal("r-c", message.RequestId);
        Assert.Equal("Explain C", message.Source?.Label);
        Assert.Equal("admitted-c", Assert.Single(message.Context).Id);
        Assert.Equal("admitted-c", Assert.Single(await sessions.ListContextAsync(session.Id)).Id);
        // Neither surface hint becomes the session's own target.
        Assert.Equal("/resource-a", (await sessions.FindAsync(session.Id))!.Metadata["route"]);
    }

    private sealed class SurfaceSource : IAgentContextProvider
    {
        private (TaskCompletionSource Entered, TaskCompletionSource Release)? _next;
        public AgentContextProviderDescriptor Descriptor { get; } = new("surface.source", "surface.module", "*", ["surface.context"], ["1"], []);
        public List<AgentContextRequest> Requests { get; } = [];

        public (TaskCompletionSource Entered, TaskCompletionSource Release) HoldNext()
        {
            var hold = (new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            lock (Requests) _next = hold;
            return hold;
        }

        public async ValueTask<AgentContextContribution> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default)
        {
            (TaskCompletionSource Entered, TaskCompletionSource Release)? next;
            lock (Requests)
            {
                Requests.Add(request);
                next = _next;
                _next = null;
            }
            if (next is { } hold)
            {
                hold.Entered.TrySetResult();
                await hold.Release.Task.WaitAsync(cancellationToken);
            }
            var resourceId = request.Inputs["resourceId"];
            return new AgentContextContribution([
                new($"admitted-{resourceId}", "surface.module", resourceId, "Admitted resource", "surface.context", AgentContextSensitivity.Internal,
                    "surface", "A bounded admitted summary", null, new Dictionary<string, string>()) { ModuleId = "surface.module", SchemaVersion = "1" }
            ], []);
        }
    }
}
