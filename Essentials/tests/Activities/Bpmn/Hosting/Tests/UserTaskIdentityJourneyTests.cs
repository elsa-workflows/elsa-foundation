using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

public sealed class UserTaskIdentityJourneyTests
{
    [Fact]
    public async Task Real_signed_in_users_race_claims_and_observe_current_permission_and_group_revocation()
    {
        await using var host = await OwnedWorkbench.StartAsync(recoverySweepSeconds: 0.2);
        await host.StopAsync(); await host.SeedAdministratorAsync("finance-member"); await host.RestartAsync(); await host.LoginAsync();
        using var alice = await host.SignInSessionAsync("admin"); using var bob = await host.SignInSessionAsync("finance-member");
        async Task<JsonNode> Get(HttpClient client, string path) => JsonNode.Parse(await client.GetStringAsync(path))!;
        var a = await Get(alice, "/runtime/user-task-identity"); var b = await Get(bob, "/runtime/user-task-identity");
        var aliceId = a["userId"]!.GetValue<string>(); var bobId = b["userId"]!.GetValue<string>(); Assert.NotEqual(aliceId, bobId);
        var tenant = a["tenantId"]!.GetValue<string>(); var group = Assert.Single(a["groupIds"]!.AsArray())!.GetValue<string>();
        var catalog = await host.GetAsync("/design/activities/definitions?search=Elsa.Activities.Primitives.Activities.CreateUserTask");
        var version = Assert.Single(catalog["items"]!.AsArray())!["definition"]!["headVersionId"]!.GetValue<string>();
        object Input(string key, object value) => new { referenceKey = key, value = new { value, expressionType = "Literal" } };
        var definition = await host.PostAsync("/design/workflows/definitions/submit", new { name = "Current identity User Task", state = new {
            variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>(), rootActivity = new {
                nodeId = "group-task", activityVersionId = version, outputs = Array.Empty<object>(), inputs = new[] {
                    Input("Title", "Finance group decision"), Input("GroupId", group), Input("Actions", new[] { "Approve" }), Input("OptionalFormActions", Array.Empty<string>()), Input("Prefill", new { }) }
            } } });
        var published = await host.PostAsync($"/publishing/workflows/{definition["version"]!["id"]!.GetValue<string>()}/publish", new { });
        await host.PostAsync($"/runtime/workflows/executables/{published["artifactId"]!.GetValue<string>()}/execute", new { sourceReferenceId = published["sourceReferenceId"]!.GetValue<string>() });
        JsonNode? task = null; var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (task is null)
        {
            task = (await Get(alice, "/runtime/user-tasks?view=queue"))["items"]!.AsArray().SingleOrDefault();
            Assert.True(DateTimeOffset.UtcNow < deadline); if (task is null) await Task.Delay(100);
        }
        var taskId = task["id"]!.GetValue<string>(); var revision = task["revision"]!.GetValue<long>();
        // Modify only the owned fixture's directory through its public authority store. Both sessions retain old * claims.
        await using (var directory = Directory(host))
        {
            var roles = new EfRoleStore(directory, new Access(tenant)); var role = (await roles.FindAsync(tenant, group))!;
            await roles.SaveAsync(role with { Permissions = new HashSet<string> { "user-tasks:read", "user-tasks:act" } });
        }
        Assert.False((await Get(alice, "/runtime/user-task-identity"))["canManage"]!.GetValue<bool>());
        using (var denied = await alice.PostAsJsonAsync($"/runtime/user-tasks/{taskId}/reassign", new { revision, requestId = "stale-token-permission", assigneeId = aliceId }))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var claims = await Task.WhenAll(alice.PostAsJsonAsync($"/runtime/user-tasks/{taskId}/claim", new { revision, requestId = "alice-claim" }), bob.PostAsJsonAsync($"/runtime/user-tasks/{taskId}/claim", new { revision, requestId = "bob-claim" }));
        Assert.Single(claims, response => response.StatusCode == HttpStatusCode.OK); Assert.Single(claims, response => response.StatusCode == HttpStatusCode.Conflict);
        var claimed = JsonNode.Parse(await claims.Single(response => response.IsSuccessStatusCode).Content.ReadAsStringAsync())!;
        foreach (var response in claims) response.Dispose();
        var first = claimed["claimantId"]!.GetValue<string>() == aliceId ? alice : bob;
        var next = ReferenceEquals(first, alice) ? bob : alice; var nextId = ReferenceEquals(next, alice) ? aliceId : bobId;
        using (var release = await first.PostAsJsonAsync($"/runtime/user-tasks/{taskId}/release", new { revision = claimed["revision"]!.GetValue<long>(), requestId = "release" })) Assert.Equal(HttpStatusCode.OK, release.StatusCode);
        var open = await Get(next, "/runtime/user-tasks/" + taskId);
        using (var claim = await next.PostAsJsonAsync($"/runtime/user-tasks/{taskId}/claim", new { revision = open["revision"]!.GetValue<long>(), requestId = "claim-released" })) Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
        var current = await Get(next, "/runtime/user-tasks/" + taskId);
        // #4198: a reader without manage permission, who may not list assignment targets, sees the other person and the group by name.
        var firstId = ReferenceEquals(first, alice) ? aliceId : bobId; string firstName, nextName, groupName;
        await using (var directory = Directory(host))
        {
            var users = new EfUserStore(directory, new Access(tenant));
            firstName = Named((await users.FindAsync(tenant, firstId))!); nextName = Named((await users.FindAsync(tenant, nextId))!);
            groupName = (await new EfRoleStore(directory, new Access(tenant)).FindAsync(tenant, group))!.Name;
        }
        Assert.False((await Get(next, "/runtime/user-task-identity"))["canManage"]!.GetValue<bool>());
        using (var targets = await next.GetAsync("/runtime/user-task-targets")) Assert.Equal(HttpStatusCode.Forbidden, targets.StatusCode);
        Assert.Equal(nextName, current["claimantName"]!.GetValue<string>()); Assert.Equal(groupName, current["groupName"]!.GetValue<string>());
        Assert.Contains(current["history"]!.AsArray(), entry => entry!["actorId"]!.GetValue<string>() == firstId && entry!["actorName"]?.GetValue<string>() == firstName);
        var row = Assert.Single((await Get(next, "/runtime/user-tasks?view=mine"))["items"]!.AsArray(), item => item!["id"]!.GetValue<string>() == taskId)!;
        Assert.Equal(nextName, row["claimantName"]!.GetValue<string>());
        await using (var directory = Directory(host))
        {
            var users = new EfUserStore(directory, new Access(tenant)); var user = (await users.FindAsync(tenant, nextId))!;
            await users.SaveAsync(user with { RoleIds = new HashSet<string>(), DirectPermissions = new HashSet<string> { "user-tasks:read", "user-tasks:act" } });
            var memberships = new EfTenantMembershipStore(directory, new Access(tenant));
            if (await memberships.FindAsync(tenant, nextId) is { } membership)
                await memberships.SaveAsync(membership with { RoleIds = new HashSet<string>() });
        }
        Assert.Empty((await Get(next, "/runtime/user-task-identity"))["groupIds"]!.AsArray());
        using (var revoked = await next.PostAsJsonAsync($"/runtime/user-tasks/{taskId}/complete", new { revision = current["revision"]!.GetValue<long>(), requestId = "revoked-member", action = "Approve", data = new { } })) Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
        await using (var directory = Directory(host))
        {
            var roles = new EfRoleStore(directory, new Access(tenant)); var role = (await roles.FindAsync(tenant, group))!;
            await roles.SaveAsync(role with { Permissions = new HashSet<string> { "*" } });
        }
        var supervisor = ReferenceEquals(next, alice) ? bob : alice; var supervisorId = ReferenceEquals(supervisor, alice) ? aliceId : bobId;
        using (var reassigned = await supervisor.PostAsJsonAsync($"/runtime/user-tasks/{taskId}/reassign", new { revision = current["revision"]!.GetValue<long>(), requestId = "supervisor-reassign", assigneeId = supervisorId })) Assert.Equal(HttpStatusCode.OK, reassigned.StatusCode);
        var assigned = await Get(supervisor, "/runtime/user-tasks/" + taskId); Assert.Null(assigned["claimantId"]); Assert.Equal(supervisorId, assigned["assigneeId"]!.GetValue<string>());
        using (var old = await next.GetAsync("/runtime/user-tasks/" + taskId)) Assert.Equal(HttpStatusCode.NotFound, old.StatusCode);
        using (var complete = await supervisor.PostAsJsonAsync($"/runtime/user-tasks/{taskId}/complete", new { revision = assigned["revision"]!.GetValue<long>(), requestId = "final-response", action = "Approve", actorId = "forged-body-user", data = new { } })) Assert.Equal(HttpStatusCode.Accepted, complete.StatusCode);
        deadline = DateTimeOffset.UtcNow.AddSeconds(30); var terminal = await Get(supervisor, "/runtime/user-tasks/" + taskId);
        while (terminal["status"]!.GetValue<string>() != "Completed") { Assert.True(DateTimeOffset.UtcNow < deadline); await Task.Delay(100); terminal = await Get(supervisor, "/runtime/user-tasks/" + taskId); }
        Assert.Equal(supervisorId, terminal["decision"]!["actorId"]!.GetValue<string>());
        Assert.Single(terminal["history"]!.AsArray(), entry => entry!["operation"]!.GetValue<string>() == "accepted");
    }
    private static string Named(Elsa.Foundation.Identity.Core.Iam.UserRecord user) => string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName;
    private static IdentityIamSqliteDbContext Directory(OwnedWorkbench host) => new(new DbContextOptionsBuilder<IdentityIamSqliteDbContext>().UseSqlite($"Data Source={Path.Combine(host.ContentRoot, "elsa.db")};Pooling=False").Options);
    private sealed class Access(string scope) : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new(scope)); }
}
