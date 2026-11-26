using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.UserTasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

[Collection(FastEndpointsHostCollection.Name)]
public sealed class UserTaskApiTests
{
    [Fact]
    public async Task Trusted_canonical_identity_and_current_membership_control_claims_not_body_or_token_roles()
    {
        await using var host = await Host.Start();
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/runtime/user-tasks")).StatusCode);
        host.SignIn("alice");
        var identity = await host.Client.GetFromJsonAsync<JsonElement>("/runtime/user-task-identity");
        Assert.Equal("alice", identity.GetProperty("userId").GetString());
        Assert.Equal("finance", Assert.Single(identity.GetProperty("groupIds").EnumerateArray()).GetString());
        var claimed = await host.Client.PostAsJsonAsync("/runtime/user-tasks/task/claim", new { revision = 1, requestId = "claim", id = "foreign", actorId = "bob", groupId = "forged" });
        Assert.Equal(HttpStatusCode.OK, claimed.StatusCode);
        Assert.Equal("alice", (await claimed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("claimantId").GetString());
        host.Identity.User = host.Identity.User with { RoleIds = new HashSet<string>() };
        // Management still permits inspection; it never substitutes for current group eligibility to complete.
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.PostAsJsonAsync("/runtime/user-tasks/task/complete", new { revision = 2, requestId = "complete", action = "Approve", data = new { } })).StatusCode);
        Assert.Equal(UserTaskStatus.Claimed, (await host.Read())!.Status);
    }

    [Fact]
    public async Task Current_resource_policy_revocation_and_other_tenants_refuse_disclosure_and_mutations()
    {
        await using var host = await Host.Start(); host.SignIn("alice");
        host.Identity.DenyResource = true;
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/runtime/user-tasks/task")).StatusCode);
        var page = await host.Client.GetFromJsonAsync<JsonElement>("/runtime/user-tasks?view=queue&limit=1");
        Assert.Empty(page.GetProperty("items").EnumerateArray());
        host.Identity.DenyResource = false; host.Identity.Revoked = true;
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.PostAsJsonAsync("/runtime/user-tasks/task/claim", new { revision = 1, requestId = "claim" })).StatusCode);
        host.Identity.Revoked = false; host.SignIn("alice", "other");
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/runtime/user-tasks/task")).StatusCode);
        Assert.Equal(1, (await host.Read())!.Revision);
    }

    [Fact]
    public async Task Validation_preserves_opaque_field_keys_and_custom_client_receipts_are_idempotent()
    {
        await using var host = await Host.Start(); host.SignIn("alice");
        await host.Rewrite(task => task with { AssigneeId = "alice", GroupId = null, Form = new("f", "1", "F", UserTaskProtocol.BasicForms, [new("AmountUSD", "Amount", "number", true)]) });
        var invalid = await host.Client.PostAsJsonAsync("/runtime/user-tasks/task/complete", new { revision = 2, requestId = "response", action = "Approve", data = new { } });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var problem = await invalid.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("AmountUSD", out _));
        var response = new { revision = 2, requestId = "response", action = "Approve", data = new Dictionary<string, object> { ["AmountUSD"] = 42 } };
        var accepted = await host.Client.PostAsJsonAsync("/runtime/user-tasks/task/complete", response);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var replay = await host.Client.PostAsJsonAsync("/runtime/user-tasks/task/complete", response);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal(await accepted.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PostAsJsonAsync("/runtime/user-tasks/task/complete", response with { action = "Reject" })).StatusCode);
        Assert.Single((await host.Read())!.History.Where(x => x.Operation == "accepted"));
    }

    [Fact]
    public async Task Immutable_form_versions_are_paged_and_malformed_definitions_fail_safely()
    {
        await using var host = await Host.Start(); host.SignIn("alice");
        for (var index = 0; index < 103; index++)
        {
            var result = await host.Client.PostAsJsonAsync("/runtime/user-task-forms", new UserTaskForm("purchase", index.ToString(), "Purchase", UserTaskProtocol.BasicForms, []));
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        }
        var seen = new HashSet<string>(); string? next = null;
        do
        {
            var page = await host.Client.GetFromJsonAsync<UserTaskFormPage>("/runtime/user-task-forms?limit=20" + (next is null ? "" : "&after=" + Uri.EscapeDataString(next)));
            foreach (var version in page!.Items) Assert.True(seen.Add(version.Form.Version));
            next = page.Next;
        } while (next is not null);
        Assert.Equal(103, seen.Count);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/runtime/user-task-forms/purchase/versions/102")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PostAsJsonAsync("/runtime/user-task-forms", new UserTaskForm("purchase", "102", "Changed", UserTaskProtocol.BasicForms, []))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.PostAsJsonAsync("/runtime/user-task-forms", new { id = "bad", version = "1", name = "Bad", provider = UserTaskProtocol.BasicForms, fields = new object?[] { null } })).StatusCode);
    }

    [Fact]
    public async Task Form_definition_endpoints_save_publish_discard_and_keep_drafts_out_of_published_versions()
    {
        await using var host = await Host.Start(); host.SignIn("alice");
        var draft = new UserTaskForm("purchase", "1", "", UserTaskProtocol.BasicForms,
        [
            new UserTaskField("accepted", "Accept terms", "boolean", Required: true) { Presentation = new("radio", TrueLabel: "I agree", FalseLabel: "I decline", UnansweredLabel: "No response"), MustBeTrue = true },
            new UserTaskField("channel", "Channel", "choice", Choices: ["Email", "Phone"]) { Presentation = new("radio") }
        ]);
        var saved = await host.Client.PostAsJsonAsync("/runtime/user-task-forms/purchase/draft", new { revision = 0, form = draft });
        var savedBody = await saved.Content.ReadAsStringAsync();
        Assert.True(saved.StatusCode == HttpStatusCode.OK, $"{saved.StatusCode}: {savedBody}");
        var definition = (await saved.Content.ReadFromJsonAsync<UserTaskFormDefinition>())!;
        Assert.Equal(1, definition.Revision); Assert.NotNull(definition.Draft); Assert.Null(definition.LatestPublished);
        Assert.Equal(new UserTaskFieldPresentation("radio", "I agree", "I decline", "No response"), definition.Draft.Form!.Fields[0].Presentation);
        Assert.True(definition.Draft.Form.Fields[0].MustBeTrue);
        Assert.Empty((await host.Client.GetFromJsonAsync<UserTaskFormPage>("/runtime/user-task-forms?limit=10"))!.Items);

        var publishedForm = draft with { Name = "Purchase request" };
        var publish = await host.Client.PostAsJsonAsync("/runtime/user-task-forms/purchase/publish", new { revision = 1, form = publishedForm });
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        var publishedVersion = (await publish.Content.ReadFromJsonAsync<UserTaskFormVersion>())!;
        Assert.Equal("1", publishedVersion.Form.Version);
        Assert.Equal(new UserTaskFieldPresentation("radio", "I agree", "I decline", "No response"), publishedVersion.Form.Fields[0].Presentation);
        Assert.True(publishedVersion.Form.Fields[0].MustBeTrue);
        var afterPublish = await host.Client.GetFromJsonAsync<UserTaskFormDefinition>("/runtime/user-task-forms/purchase/definition");
        Assert.Equal(2, afterPublish!.Revision); Assert.Null(afterPublish.Draft);

        var secondDraft = await host.Client.PostAsJsonAsync("/runtime/user-task-forms/purchase/draft", new { revision = 2, form = publishedForm with { Name = "Unpublished edit" } });
        Assert.Equal(HttpStatusCode.OK, secondDraft.StatusCode);
        var history = await host.Client.GetFromJsonAsync<UserTaskFormPage>("/runtime/user-task-forms/purchase/versions?limit=1");
        Assert.Equal("Purchase request", Assert.Single(history!.Items).Form.Name);
        Assert.Empty((await host.Client.GetFromJsonAsync<UserTaskFormPage>("/runtime/user-task-forms?limit=10"))!.Items.Where(x => x.Form.Name == "Unpublished edit"));

        var discarded = await host.Client.PostAsJsonAsync("/runtime/user-task-forms/purchase/discard", new { revision = 3 });
        Assert.Equal(HttpStatusCode.OK, discarded.StatusCode);
        var result = (await discarded.Content.ReadFromJsonAsync<UserTaskFormDefinition>())!;
        Assert.Equal(4, result.Revision); Assert.Null(result.Draft); Assert.Equal("Purchase request", result.LatestPublished!.Form.Name);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PostAsJsonAsync("/runtime/user-task-forms/purchase/draft", new { revision = 3, form = draft })).StatusCode);
    }

    [Fact]
    public async Task Draft_API_preserves_custom_control_metadata_but_publication_requires_a_server_validator()
    {
        await using var host = await Host.Start(); host.SignIn("alice");
        var form = new UserTaskForm("custom", "1", "Custom", UserTaskProtocol.BasicForms,
            [new UserTaskField("rating", "Rating", "number")
            {
                ControlId = "example.rating/1",
                Configuration = JsonSerializer.SerializeToElement(new { maximum = 5 })
            }]);

        var saved = await host.Client.PostAsJsonAsync("/runtime/user-task-forms/custom/draft", new { revision = 0, form });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var definition = await host.Client.GetFromJsonAsync<UserTaskFormDefinition>("/runtime/user-task-forms/custom/definition");
        var field = Assert.Single(definition!.Draft!.Form!.Fields);
        Assert.Equal("example.rating/1", field.ControlId);
        Assert.Equal(5, field.Configuration?.GetProperty("maximum").GetInt32());

        var publish = await host.Client.PostAsJsonAsync("/runtime/user-task-forms/custom/publish", new { revision = 1, form });
        Assert.Equal(HttpStatusCode.BadRequest, publish.StatusCode);
    }

    [Fact]
    public async Task Form_delete_endpoint_requires_current_manage_permission_and_keeps_published_versions_resolvable()
    {
        await using var host = await Host.Start(); host.SignIn("alice");
        var form = new UserTaskForm("purchase", "1", "Purchase", UserTaskProtocol.BasicForms, []);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.PostAsJsonAsync("/runtime/user-task-forms", form)).StatusCode);

        host.Identity.Granted = new HashSet<string> { "user-task-forms:read" };
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.PostAsJsonAsync("/runtime/user-task-forms/purchase/delete", new { revision = 0 })).StatusCode);

        host.Identity.Granted = null;
        var deleted = await host.Client.PostAsJsonAsync("/runtime/user-task-forms/purchase/delete", new { revision = 0 });
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.True((await deleted.Content.ReadFromJsonAsync<UserTaskFormDefinition>())!.Deleted);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PostAsJsonAsync("/runtime/user-task-forms/purchase/draft", new { revision = 1, form })).StatusCode);
        Assert.Empty((await host.Client.GetFromJsonAsync<UserTaskFormDefinitionPage>("/runtime/user-task-forms/definitions"))!.Items);
        Assert.Empty((await host.Client.GetFromJsonAsync<UserTaskFormPage>("/runtime/user-task-forms?limit=10"))!.Items);
        Assert.Empty((await host.Client.GetFromJsonAsync<UserTaskFormPage>("/runtime/user-task-forms/purchase/versions?limit=10"))!.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/runtime/user-task-forms/purchase/definition")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/runtime/user-task-forms/purchase/versions/1")).StatusCode);
    }

    [Fact]
    public async Task Task_responses_name_people_and_groups_for_a_reader_who_cannot_read_the_target_directory()
    {
        await using var host = await Host.Start(); host.SignIn("alice");
        host.Identity.Granted = new HashSet<string> { "user-tasks:read", "user-tasks:act" };
        host.Identity.Others["bob"] = host.Identity.User with { Id = "bob", UserName = "bob", DisplayName = "Bob Builder", Status = UserStatus.Disabled };
        host.Identity.Others["mallory"] = host.Identity.User with { Id = "mallory", TenantId = "other", DisplayName = "Mallory" };
        host.Identity.Others["nameless"] = host.Identity.User with { Id = "nameless", UserName = "nameless", DisplayName = " " };
        host.Identity.Groups["finance"] = "Finance team";
        var at = DateTimeOffset.UtcNow;
        await host.Rewrite(task => task with { History = [new("created", "workflow", at), new("claimed", "bob", at), new("released", "bob", at), new("claimed", "mallory", at), new("released", "zed", at), new("claimed", "nameless", at)] });
        // The reader holds no manage permission, so the target directory is closed to them, yet every task response names the people it stores.
        Assert.False((await host.Client.GetFromJsonAsync<JsonElement>("/runtime/user-task-identity")).GetProperty("canManage").GetBoolean());
        host.Identity.UserLookups.Clear();
        var row = Assert.Single((await host.Client.GetFromJsonAsync<JsonElement>("/runtime/user-tasks?view=queue")).GetProperty("items").EnumerateArray());
        Assert.Equal("finance", row.GetProperty("groupId").GetString()); Assert.Equal("Finance team", row.GetProperty("groupName").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("assigneeName").ValueKind); Assert.Equal(JsonValueKind.Null, row.GetProperty("claimantName").ValueKind);
        // Disabled people keep their name; unknown and other-tenant IDs get none, and a blank display name falls back to the user name.
        Assert.Equal(new[] { "workflow:", "bob:Bob Builder", "bob:Bob Builder", "mallory:", "zed:", "nameless:nameless" }, row.GetProperty("history").EnumerateArray()
            .Select(entry => entry.GetProperty("actorId").GetString() + ":" + entry.GetProperty("actorName").GetString()));
        // Each distinct ID is read from the directory once per response.
        Assert.Single(host.Identity.UserLookups, id => id == "bob");
        var detail = await host.Client.GetFromJsonAsync<JsonElement>("/runtime/user-tasks/task");
        Assert.Equal("Finance team", detail.GetProperty("groupName").GetString());
        Assert.Equal("Bob Builder", detail.GetProperty("history")[1].GetProperty("actorName").GetString());
        var claimed = await (await host.Client.PostAsJsonAsync("/runtime/user-tasks/task/claim", new { revision = 2, requestId = "claim" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Alice", claimed.GetProperty("claimantName").GetString());
        Assert.Equal("alice:Alice", claimed.GetProperty("history").EnumerateArray().Select(entry => entry.GetProperty("actorId").GetString() + ":" + entry.GetProperty("actorName").GetString()).Last());
        // A directory failure only leaves a name out; it never turns a saved change into a failed response.
        host.Identity.Failing.Add("bob");
        var released = await host.Client.PostAsJsonAsync("/runtime/user-tasks/task/release", new { revision = 3, requestId = "release" });
        Assert.Equal(HttpStatusCode.OK, released.StatusCode);
        var history = (await released.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("history");
        Assert.Equal(JsonValueKind.Null, history[1].GetProperty("actorName").ValueKind); Assert.Equal("Alice", history[history.GetArrayLength() - 1].GetProperty("actorName").GetString());
    }

    private sealed class Host(WebApplication app, HttpClient client, Identity identity) : IAsyncDisposable
    {
        public HttpClient Client => client;
        public Identity Identity => identity;
        public static async Task<Host> Start()
        {
            var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Services.AddElsaEndpoints();
            builder.Services.AddAuthentication(Auth.SchemeId).AddScheme<AuthenticationSchemeOptions, Auth>(Auth.SchemeId, _ => { }); builder.Services.AddAuthorization();
            builder.Services.AddFoundationIdentityAbstractions(options => options.NormalizedAuthenticationTypes = new HashSet<string> { Auth.SchemeId });
            new WorkflowsRuntimeApiFeature().ConfigureServices(builder.Services);
            var identity = new Identity();
            builder.Services.Replace(ServiceDescriptor.Singleton<IUserStore>(identity)); builder.Services.Replace(ServiceDescriptor.Singleton<IRoleStore>(identity));
            builder.Services.Replace(ServiceDescriptor.Singleton<ITenantMembershipStore>(identity)); builder.Services.Replace(ServiceDescriptor.Singleton<ICurrentSubjectPermissionAuthorizer>(identity));
            builder.Services.EnsureReplacementContract<IUserStore, Identity>(); builder.Services.EnsureReplacementContract<IRoleStore, Identity>();
            builder.Services.EnsureReplacementContract<ITenantMembershipStore, Identity>(); builder.Services.EnsureReplacementContract<ICurrentSubjectPermissionAuthorizer, Identity>();
            builder.Services.Replace(ServiceDescriptor.Singleton<IUserTaskRuntime, NoRuntime>());
            var app = builder.Build();
            app.Use(async (context, next) => { context.RequestServices.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new("default"))); await next(); });
            app.UseAuthentication(); app.UseAuthorization(); WorkflowsRuntimeApi.MapWorkflowsRuntimeApi(app); await app.StartAsync();
            var host = new Host(app, app.GetTestClient(), identity);
            using var scope = app.Services.CreateScope(); scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new("default")));
            var now = DateTimeOffset.UtcNow;
            await scope.ServiceProvider.GetRequiredService<IUserTaskStore>().TryWriteAsync(new("task", "default", "execution", "invocation", "node", "operation", "Purchase", null, null, "finance", null,
                ["Approve", "Reject"], [], null, JsonSerializer.SerializeToElement(new { }), UserTaskStatus.Open, 1, now, now, [new("created", "workflow", now)]), null);
            return host;
        }
        public void SignIn(string user, string tenant = "default") { client.DefaultRequestHeaders.Remove("X-User"); client.DefaultRequestHeaders.Add("X-User", user); client.DefaultRequestHeaders.Remove("X-Tenant"); client.DefaultRequestHeaders.Add("X-Tenant", tenant); }
        public async Task<UserTaskRecord?> Read() { using var scope = app.Services.CreateScope(); scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new("default"))); return await scope.ServiceProvider.GetRequiredService<IUserTaskStore>().FindAsync("default", "task"); }
        public async Task Rewrite(Func<UserTaskRecord, UserTaskRecord> change) { using var scope = app.Services.CreateScope(); scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new("default"))); var store = scope.ServiceProvider.GetRequiredService<IUserTaskStore>(); var task = (await store.FindAsync("default", "task"))!; Assert.True(await store.TryWriteAsync(change(task) with { Revision = task.Revision + 1 }, task.Revision)); }
        public async ValueTask DisposeAsync() { client.Dispose(); await app.DisposeAsync(); }
    }
    private sealed class Auth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeId = "UserTasksTest";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-User", out var user)) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim> { new(IdentityClaimTypes.UserId, user.ToString()), new(IdentityClaimTypes.TenantId, Request.Headers["X-Tenant"].ToString()), new(ClaimTypes.NameIdentifier, "forged-subject"), new(IdentityClaimTypes.Normalized, "v1"), new(ClaimTypes.Role, "forged-group") };
            foreach (var permission in new[] { "user-tasks:read", "user-tasks:act", "user-tasks:manage", "user-task-forms:read", "user-task-forms:manage" }) claims.Add(new(IdentityClaimTypes.Permission, permission));
            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeId)), SchemeId)));
        }
    }
    private sealed class Identity : IUserStore, IRoleStore, ITenantMembershipStore, ICurrentSubjectPermissionAuthorizer
    {
        public UserRecord User = new("alice", "default", "Alice", null, "Alice", UserStatus.Active, ResourceOwnership.Foundation, new HashSet<string> { "finance" }, new HashSet<string>());
        public bool Revoked; public bool DenyResource; public HashSet<string>? Granted;
        public readonly Dictionary<string, UserRecord> Others = new(); public readonly Dictionary<string, string> Groups = new(); public readonly List<string> UserLookups = []; public readonly HashSet<string> Failing = [];
        public ValueTask<UserRecord?> FindAsync(string tenantId, string userId, CancellationToken cancellationToken = default) { UserLookups.Add(userId); if (Failing.Contains(userId)) throw new InvalidOperationException("Directory unavailable."); return ValueTask.FromResult<UserRecord?>(Others.TryGetValue(userId, out var other) ? other : userId == User.Id ? User with { TenantId = tenantId } : null); }
        public ValueTask<UserRecord?> FindByEmailAsync(string tenantId, string email, CancellationToken cancellationToken = default) => ValueTask.FromResult<UserRecord?>(null);
        public ValueTask SaveAsync(UserRecord user, CancellationToken cancellationToken = default) { User = user; return ValueTask.CompletedTask; }
        public ValueTask<bool> SetStatusAsync(string tenantId, string userId, UserStatus status, CancellationToken cancellationToken = default) { User = User with { Status = status }; return ValueTask.FromResult(true); }
        public ValueTask<bool> DeleteAsync(string tenantId, string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        ValueTask<RoleRecord?> IRoleStore.FindAsync(string tenantId, string roleId, CancellationToken cancellationToken) => ValueTask.FromResult<RoleRecord?>(new(roleId, tenantId, Groups.GetValueOrDefault(roleId, roleId), null, new HashSet<string>(), false));
        ValueTask<IReadOnlyList<RoleRecord>> IRoleStore.ListAsync(string tenantId, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<RoleRecord>>([]);
        public ValueTask SaveAsync(RoleRecord role, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        ValueTask<TenantMembershipRecord?> ITenantMembershipStore.FindAsync(string tenantId, string userId, CancellationToken cancellationToken) => ValueTask.FromResult<TenantMembershipRecord?>(null);
        public ValueTask SaveAsync(TenantMembershipRecord membership, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<CurrentSubjectPermissionResult> AuthorizeAsync(CurrentSubjectPermissionRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(Revoked || DenyResource && request.Resource is UserTaskRecord || Granted?.Contains(request.Permission) == false ? CurrentSubjectPermissionResult.Denied("Revoked") : CurrentSubjectPermissionResult.Granted);
    }
    private sealed class NoRuntime : IUserTaskRuntime
    {
        public ValueTask<UserTaskRecord> StageAsync(UserTaskCreation request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<UserTaskResponse> ReadAcceptedAsync(string tenantId, string taskId, string completionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask ReconcileAsync(UserTaskRecord task, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
