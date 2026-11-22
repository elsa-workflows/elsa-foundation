using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

/// <summary>
/// SC-005 HTTP-level non-admin half on the Owned Workbench. Inventory is the composed endpoint
/// registrations: <c>BpmnHostingApi</c>, <c>BpmnCatalog</c>, <c>BpmnInterchangeApi</c>, and
/// <c>BpmnDirectPublishingApi</c>. The Studio half is deferred. The tenant half is not hosted here.
/// </summary>
public sealed class BpmnHostingAuthorizationTests
{
    [Fact]
    public async Task Signed_in_user_without_bpmn_permissions_is_refused_on_every_bpmn_endpoint()
    {
        await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, authoredDirectPublication: true);
        await host.StopAsync(); await host.SeedAdministratorAsync("operator"); await host.RestartAsync();
        using var probe = await host.SignInSessionAsync("operator");
        var identity = JsonNode.Parse(await probe.GetStringAsync("/runtime/user-task-identity"))!;
        var tenant = identity["tenantId"]!.GetValue<string>();
        var operatorId = identity["userId"]!.GetValue<string>();
        await using (var directory = Directory(host))
        {
            var users = new EfUserStore(directory, new Access(tenant));
            var user = (await users.FindAsync(tenant, operatorId))!;
            await users.SaveAsync(user with
            {
                RoleIds = new HashSet<string>(),
                DirectPermissions = new HashSet<string> { "user-tasks:read" }
            });
            var memberships = new EfTenantMembershipStore(directory, new Access(tenant));
            if (await memberships.FindAsync(tenant, operatorId) is { } membership)
                await memberships.SaveAsync(membership with
                {
                    RoleIds = new HashSet<string>(),
                    DirectPermissions = new HashSet<string> { "user-tasks:read" }
                });
        }

        var stripped = JsonNode.Parse(await probe.GetStringAsync("/runtime/user-task-identity"))!;
        Assert.False(stripped["canManage"]!.GetValue<bool>());
        Assert.Empty(stripped["groupIds"]!.AsArray());

        using var denied = await SignInWithoutFollowAsync(probe.BaseAddress!, "operator");
        using var admin = await host.SignInSessionAsync("admin");
        using (var stillSignedIn = await denied.GetAsync("/runtime/user-task-identity"))
            Assert.Equal(HttpStatusCode.OK, stillSignedIn.StatusCode);
        using (var allowed = await admin.GetAsync("/bpmn/admission"))
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        var failures = new List<string>();
        foreach (var (method, path, body) in Endpoints)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await denied.SendAsync(request);
            var payload = await response.Content.ReadAsStringAsync();
            if (!IsAuthorizationRefusal(response))
                failures.Add($"{method.Method} {path} -> {(int)response.StatusCode} {response.Headers.Location}");
            else if (LooksLikePrivatePayload(payload))
                failures.Add($"{method.Method} {path} -> {(int)response.StatusCode} disclosed a private payload");
        }

        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    // Every BPMN HTTP registration on the composed Owned Workbench (hosting API, catalog, interchange, authored publish-direct).
    private static readonly (HttpMethod Method, string Path, object? Body)[] Endpoints =
    [
        (HttpMethod.Get, "/bpmn/admission", null),
        (HttpMethod.Get, "/bpmn/publications", null),
        (HttpMethod.Get, "/bpmn/executions", null),
        (HttpMethod.Post, "/bpmn/artifacts", new { requestId = "denied", definition = new { formatVersion = 1, process = new { }, humanBindings = Array.Empty<string>() } }),
        (HttpMethod.Post, "/bpmn/executions", new { sourceReferenceId = "denied", requestId = "denied" }),
        (HttpMethod.Get, "/bpmn/executions/denied", null),
        (HttpMethod.Get, "/bpmn/executions/denied/inspection", null),
        (HttpMethod.Get, "/bpmn/executions/denied/work", null),
        (HttpMethod.Get, "/bpmn/executions/denied/incidents", null),
        (HttpMethod.Get, "/bpmn/executions/denied/commands/denied", null),
        (HttpMethod.Post, "/bpmn/executions/denied/work/denied/complete", new { requestId = "denied", attemptId = "denied" }),
        (HttpMethod.Post, "/bpmn/executions/denied/work/denied/claim", new { requestId = "denied", attemptId = "denied" }),
        (HttpMethod.Post, "/bpmn/executions/denied/work/denied/complete-claimed", new { requestId = "denied", attemptId = "denied", claimId = "denied" }),
        (HttpMethod.Post, "/bpmn/executions/denied/cancel", new { requestId = "denied" }),
        (HttpMethod.Post, "/bpmn/executions/denied/retry-service", new { requestId = "denied", incidentId = "denied", invocationId = "denied", failedAttemptId = "denied" }),
        (HttpMethod.Post, "/interchange/bpmn/analyze", new { xml = "<definitions xmlns=\"http://www.omg.org/spec/BPMN/20100524/MODEL\"/>" }),
        (HttpMethod.Post, "/interchange/bpmn/import", new { xml = "<definitions xmlns=\"http://www.omg.org/spec/BPMN/20100524/MODEL\"/>" }),
        (HttpMethod.Post, "/interchange/bpmn/export", new { processNode = new { nodeId = "denied" } }),
        (HttpMethod.Post, "/interchange/bpmn/versions/denied/publish-direct", new { requestId = "denied" })
    ];

    /// <summary>
    /// Documented pair is 401/403. Authenticated Forbid on this host's cookie pipeline still
    /// redirects to AccessDenied unless the endpoint called <c>DisableCookieRedirect</c>.
    /// </summary>
    private static bool IsAuthorizationRefusal(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return true;
        if (response.StatusCode is not (HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect))
            return false;
        var location = response.Headers.Location;
        var path = location is null ? string.Empty : location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString;
        return path.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<HttpClient> SignInWithoutFollowAsync(Uri baseAddress, string username)
    {
        var session = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false })
        {
            BaseAddress = baseAddress
        };
        using var login = await session.PostAsJsonAsync("/_elsa/identity/login", new { username, password = "Password123!" });
        if (!login.IsSuccessStatusCode)
        {
            session.Dispose();
            throw new InvalidOperationException($"Test-account login returned HTTP {(int)login.StatusCode}");
        }

        return session;
    }

    private static bool LooksLikePrivatePayload(string payload) =>
        payload.Contains("\"payload\"", StringComparison.OrdinalIgnoreCase) ||
        payload.Contains("authoredInputs", StringComparison.Ordinal) ||
        payload.Contains("private-connector-input", StringComparison.Ordinal);

    private static IdentityIamSqliteDbContext Directory(OwnedWorkbench host) =>
        new(new DbContextOptionsBuilder<IdentityIamSqliteDbContext>().UseSqlite($"Data Source={Path.Combine(host.ContentRoot, "elsa.db")};Pooling=False").Options);

    private sealed class Access(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new(scope));
    }
}
