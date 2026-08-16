using Elsa.Agent.Api.Constants;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using System.Net;
using Xunit;

namespace Elsa.Architecture.Tests;

[Collection(Wave4AgentFastEndpointsCollection.Name)]
public sealed class Wave4AgentAuthorizationTests
{
    [Fact]
    public async Task Minimal_and_fastendpoints_routes_share_the_policy_provider_and_distinguish_401_and_403()
    {
        await using var host = await Wave4AgentMinimalApiHost.StartAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/_elsa/agent/bootstrap")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync(Wave4FastEndpointsCanary.Route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(host, "/_elsa/agent/bootstrap", "denied")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(host, Wave4FastEndpointsCanary.Route, "denied")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(host, "/_elsa/agent/bootstrap", "use")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(host, Wave4FastEndpointsCanary.Route, "use")).StatusCode);
    }

    [Fact]
    public async Task Exact_implied_and_wildcard_grants_follow_the_agent_catalog()
    {
        await using var host = await Wave4AgentMinimalApiHost.StartAsync();

        Assert.Equal(HttpStatusCode.OK, (await GetAsync(host, "/_elsa/agent/bootstrap", "use")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, "/_elsa/agent/proposals/proposal-1/approve", "proposals")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(host, "/_elsa/agent/bootstrap", "proposals")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(host, "/_elsa/agent/proposals/proposal-1/approve", "use")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(host, "/_elsa/agent/audit", "audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(host, "/_elsa/agent/audit", "use")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(host, "/_elsa/agent/bootstrap", "wildcard")).StatusCode);
    }

    [Fact]
    public async Task Session_and_proposal_routes_fail_closed_for_foreign_actor_or_tenant()
    {
        await using var host = await Wave4AgentMinimalApiHost.StartAsync();

        Assert.Equal(HttpStatusCode.OK, (await GetAsync(host, "/_elsa/agent/sessions/session-1", "use|actor-1|tenant-1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(host, "/_elsa/agent/sessions/session-1", "use|actor-2|tenant-1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(host, "/_elsa/agent/sessions/session-1", "use|actor-1|tenant-2")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(host, "/_elsa/agent/proposals/proposal-1/approve", "proposals|actor-2|tenant-1")).StatusCode);
        using var missing = await GetAsync(host, "/_elsa/agent/sessions/session-missing", "use|actor-1|tenant-1");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Contains("\"code\":\"agent.session.not_found\"", await missing.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public void Contributor_owns_exact_actions_and_only_proposals_implies_use()
    {
        var contributor = new Elsa.Agent.Api.Authorization.AgentPermissionContributor();
        var permissions = contributor.Contribute().ToDictionary(permission => permission.Key, StringComparer.Ordinal);

        Assert.Equal("Elsa.Agent.Api", contributor.OwnerId);
        Assert.Equal("Elsa.Agent.Api", permissions[AgentPermissionKeys.Use].OwnerId);
        Assert.Contains(AgentPermissionKeys.Use, permissions[AgentPermissionKeys.Proposals].Implies!);
        Assert.True(permissions[AgentPermissionKeys.Audit].Implies is null or { Count: 0 });
        Assert.DoesNotContain(PermissionKey.Wildcard, permissions.Keys);
    }

    private static Task<HttpResponseMessage> GetAsync(Wave4AgentMinimalApiHost host, string path, string identity)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(Wave4AgentHost.IdentityHeader, identity);
        return host.Client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> PostAsync(Wave4AgentMinimalApiHost host, string path, string identity)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add(Wave4AgentHost.IdentityHeader, identity);
        request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        return host.Client.SendAsync(request);
    }
}
