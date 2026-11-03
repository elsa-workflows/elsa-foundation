using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Tasks.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Host = Elsa.Workflows.Design.Api.Tests.WorkflowsDesignApiContractTests.AuthorizationHost;

namespace Elsa.Workflows.Design.Api.Tests;

// Spec 1193 slice E: the optional client operation key on tag and view create (FR-021, FR-033) and the library's
// organization commands behind IWorkflowLibraryCommands (D4), which the endpoints call.
public sealed partial class WorkflowLibraryApiContractTests
{
    [Theory]
    [InlineData("/design/workflows/tags", "library-catalog", "{\"name\":\"New tag\",\"operationKey\":\"create-1\"}")]
    [InlineData("/design/workflows/views", "library-read", "{\"name\":\"New view\",\"query\":{},\"operationKey\":\"create-1\"}")]
    public async Task A_keyed_create_against_a_store_without_key_support_is_unavailable_never_a_second_create(string path, string identity, string body)
    {
        // LibraryProbe implements only the unkeyed members, so the store's default operation-key members answer.
        var probe = new LibraryProbe();
        await using var host = await StartAsync(probe);
        using var response = await SendAsync(host, "POST", path, identity, body);
        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Contains(WorkflowLibraryOperationKeyUnsupportedException.FixedMessage, await response.Content.ReadAsStringAsync());
        Assert.Null(probe.Actor ?? probe.ViewId);
        Assert.False(probe.CanShare);
    }

    [Theory]
    [InlineData("/design/workflows/tags", "library-catalog", "{\"name\":\"New tag\"}")]
    [InlineData("/design/workflows/views", "library-read", "{\"name\":\"New view\",\"query\":{}}")]
    public async Task An_unkeyed_create_is_unchanged_on_a_store_without_key_support(string path, string identity, string body)
    {
        await using var host = await StartAsync(new LibraryProbe());
        using var response = await SendAsync(host, "POST", path, identity, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Keyed_creates_pass_the_client_key_and_a_repeat_or_a_different_body_resolve_by_the_key()
    {
        var probe = new KeyedLibraryProbe();
        await using var host = await StartAsync(probe);
        using var first = await SendAsync(host, "POST", "/design/workflows/tags", "library-catalog", "{\"name\":\"Critical\",\"operationKey\":\"tag-key\"}");
        using var repeat = await SendAsync(host, "POST", "/design/workflows/tags", "library-catalog", "{\"name\":\"Critical\",\"operationKey\":\"tag-key\"}");
        using var other = await SendAsync(host, "POST", "/design/workflows/tags", "library-catalog", "{\"name\":\"Urgent\",\"operationKey\":\"tag-key\"}");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode); Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await repeat.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
        Assert.Equal(new[] { "tag-key", "tag-key", "tag-key" }, probe.TagKeys);
        Assert.Equal(1, probe.TagsCreated);

        using var view = await SendAsync(host, "POST", "/design/workflows/views", "library-read", "{\"name\":\"Mine\",\"query\":{},\"operationKey\":\"view-key\"}");
        using var viewRepeat = await SendAsync(host, "POST", "/design/workflows/views", "library-read", "{\"name\":\"Mine\",\"query\":{},\"operationKey\":\"view-key\"}");
        Assert.Equal(HttpStatusCode.OK, view.StatusCode);
        Assert.Equal(await view.Content.ReadAsStringAsync(), await viewRepeat.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "view-key", "view-key" }, probe.ViewKeys);
        // The update route is bound by its revision and never sends a key.
        using var update = await SendAsync(host, "PUT", "/design/workflows/views/view-1", "library-read", "{\"name\":\"Renamed\",\"query\":{},\"revision\":\"v1\"}");
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(2, probe.ViewKeys.Count);
    }

    [Fact]
    public async Task Commands_serve_only_the_current_request_actor_and_tenant()
    {
        await using var host = await StartAsync(new KeyedLibraryProbe());
        using var scope = Request(host, "library-all");
        var commands = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryCommands>();
        Assert.Equal(WorkflowLibraryCommandStatus.Applied, (await commands.GetPermissionsAsync(new("user-a", "tenant-design"))).Status);
        Assert.Equal(WorkflowLibraryCommandStatus.Denied, (await commands.GetPermissionsAsync(new("user-b", "tenant-design"))).Status);
        Assert.Equal(WorkflowLibraryCommandStatus.Denied, (await commands.GetPermissionsAsync(new("user-a", "tenant-other"))).Status);
        var forged = await commands.CreateTagAsync(new("user-b", "tenant-design"), new("Critical"), "forged");
        Assert.Equal(WorkflowLibraryCommandStatus.Denied, forged.Status);
        Assert.Empty(((KeyedLibraryProbe)scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>()).TagKeys);
    }

    [Fact]
    public async Task Commands_report_the_native_flags_and_apply_each_route_gate()
    {
        await using var host = await StartAsync(new KeyedLibraryProbe());
        var actor = new WorkflowLibraryActor("user-a", "tenant-design");
        using (var reader = Request(host, "library-read"))
        {
            var commands = reader.ServiceProvider.GetRequiredService<IWorkflowLibraryCommands>();
            Assert.Equal(new WorkflowLibraryPermissionFlags(false, false, false, false), (await commands.GetPermissionsAsync(actor)).Value);
            // Tag creation needs tag-management authority, tag edits tag assignment, folder moves management.
            Assert.Equal(WorkflowLibraryCommandStatus.Denied, (await commands.CreateTagAsync(actor, new("Critical"), "key")).Status);
            Assert.Equal(WorkflowLibraryCommandStatus.Denied, (await commands.EditTagsAsync(actor, new([new("orders", "r2")], [new("critical")], [], "edit"))).Status);
            Assert.Equal(WorkflowLibraryCommandStatus.Denied, (await commands.MoveDefinitionsAsync(actor, ["orders"], "invoices", "move")).Status);
            // Sharing a view needs view-sharing authority; a private view does not.
            Assert.Equal(WorkflowLibraryCommandStatus.Denied, (await commands.SaveViewAsync(actor, null, new("Team", new(), Shared: true), "share")).Status);
            var saved = await commands.SaveViewAsync(actor, null, new("Mine", new(SearchTerm: "pay")), "mine");
            Assert.Equal(WorkflowLibraryCommandStatus.Applied, saved.Status);
            Assert.Equal("Mine", saved.Value!.Name);
            Assert.Equal("view-1", (await commands.FindViewByOperationKeyAsync(actor, "mine")).Value!.Id);
            Assert.Null((await commands.FindViewByOperationKeyAsync(actor, "other")).Value);
        }
        using (var catalog = Request(host, "library-catalog"))
        {
            var commands = catalog.ServiceProvider.GetRequiredService<IWorkflowLibraryCommands>();
            Assert.Equal(new WorkflowLibraryPermissionFlags(false, false, true, false), (await commands.GetPermissionsAsync(actor)).Value);
            var tag = await commands.CreateTagAsync(actor, new("Critical"), "create");
            Assert.Equal(WorkflowLibraryCommandStatus.Applied, tag.Status);
            Assert.Equal("critical", tag.Value!.Id);
            Assert.Equal(WorkflowLibraryCommandStatus.Conflict, (await commands.CreateTagAsync(actor, new("Urgent"), "create")).Status);
            Assert.Equal("critical", (await commands.FindTagByOperationKeyAsync(actor, "create")).Value!.Id);
        }
    }

    [Fact]
    public async Task Commands_report_tag_edits_and_favorites_per_target_and_a_folder_move_as_one_outcome()
    {
        await using var host = await StartAsync(new KeyedLibraryProbe(), services => services.AddSingleton<IPermissionResourceHandler, DeniedDefinitionPolicy>());
        var actor = new WorkflowLibraryActor("user-a", "tenant-design");
        using var scope = Request(host, "library-all");
        var commands = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryCommands>();
        var edit = await commands.EditTagsAsync(actor, new([new("orders", "r2"), new("deniedBeta", "r2")], [new("critical")], [], "edit-1"));
        Assert.Equal(WorkflowLibraryCommandStatus.Applied, edit.Status);
        Assert.Equal(new[] { ("orders", "applied"), ("deniedBeta", "denied") }, edit.Value!.Select(outcome => (outcome.DefinitionId, outcome.Status)));
        var favorites = await commands.SetFavoritesAsync(actor, ["orders", "deniedBeta"], true);
        Assert.Equal(new[] { ("orders", "applied"), ("deniedBeta", "denied") }, favorites.Value!.Select(outcome => (outcome.DefinitionId, outcome.Status)));
        // A move is atomic in the store: one denied target refuses the whole set, and every target says so.
        var move = await commands.MoveDefinitionsAsync(actor, ["orders", "deniedBeta"], "invoices", "move-1");
        Assert.Equal(WorkflowLibraryCommandStatus.Denied, move.Status);
        Assert.All(move.Value!, outcome => Assert.Equal(WorkflowLibraryCommandStatus.Denied, outcome.Status));
        var store = (KeyedLibraryProbe)scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
        Assert.Null(store.MovedIds);
        var organization = await commands.GetOrganizationAsync(actor, "orders");
        Assert.Equal(new WorkflowLibraryTagReference("critical"), Assert.Single(organization.Value!.Tags));
        Assert.Equal("r2", organization.Value.Revision);
        Assert.Equal(WorkflowLibraryCommandStatus.Denied, (await commands.GetOrganizationAsync(actor, "hidden")).Status);
    }

    [Fact]
    public async Task Commands_are_unavailable_with_the_backend_reason_when_organization_is_disabled()
    {
        await using var host = await StartAsync(new KeyedLibraryProbe(), organizationEnabled: false);
        using var scope = Request(host, "library-all");
        var commands = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryCommands>();
        var actor = new WorkflowLibraryActor("user-a", "tenant-design");
        foreach (var result in new[]
                 {
                     (await commands.GetPermissionsAsync(actor)).Status, (await commands.CreateTagAsync(actor, new("Critical"), "key")).Status,
                     (await commands.SetFavoritesAsync(actor, ["orders"], true)).Status, (await commands.SaveViewAsync(actor, null, new("Mine", new()), "key")).Status
                 })
            Assert.Equal(WorkflowLibraryCommandStatus.Unavailable, result);
        Assert.Equal("Workflow organization is disabled by this backend's administrator.", (await commands.ListTagsAsync(actor, null)).Message);
    }

    [Fact]
    public async Task A_store_without_key_support_makes_keyed_command_creates_unsupported()
    {
        await using var host = await StartAsync(new LibraryProbe());
        using var scope = Request(host, "library-all");
        var commands = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryCommands>();
        var actor = new WorkflowLibraryActor("user-a", "tenant-design");
        Assert.Equal(WorkflowLibraryCommandStatus.Unsupported, (await commands.CreateTagAsync(actor, new("Critical"), "key")).Status);
        Assert.Equal(WorkflowLibraryCommandStatus.Unsupported, (await commands.FindTagByOperationKeyAsync(actor, "key")).Status);
        Assert.Equal(WorkflowLibraryCommandStatus.Unsupported, (await commands.SaveViewAsync(actor, null, new("Mine", new()), "key")).Status);
    }

    [Fact]
    public async Task A_second_command_registration_fails_configuration()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(new LibraryProbe(), services =>
        {
            services.AddScoped<IWorkflowLibraryCommands, DuplicateCommands>();
            new WorkflowsDesignApiFeature().ConfigureServices(services);
        }));
        Assert.Contains("IWorkflowLibraryCommands has multiple registrations", error.Message);
    }

    [Fact]
    public async Task A_second_command_registration_fails_shell_startup()
    {
        var services = new ServiceCollection();
        new WorkflowsDesignApiFeature().ConfigureServices(services);
        services.AddScoped<IWorkflowLibraryCommands, DuplicateCommands>();
        var task = services.Where(item => item.ServiceType == typeof(IStartupTask)).Select(item => item.ImplementationInstance)
            .OfType<IStartupTask>().Single(item => item.GetType().Name == "WorkflowLibraryCommandsStartupTask");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => task.ExecuteAsync(CancellationToken.None));
        Assert.Contains("IWorkflowLibraryCommands has multiple registrations", error.Message);
    }

    [Fact]
    public async Task A_replaced_command_service_decides_the_endpoints_that_have_a_core_counterpart_too()
    {
        var replacement = new RecordingCommands();
        await using var host = await StartAsync(new LibraryProbe(), services =>
        {
            services.RemoveAll<IWorkflowLibraryCommands>();
            services.AddSingleton<IWorkflowLibraryCommands>(replacement);
        });
        using var created = await SendAsync(host, "POST", "/design/workflows/tags", "library-catalog", "{\"name\":\"Critical\"}");
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Contains("replacement-tag", await created.Content.ReadAsStringAsync());
        // An unkeyed HTTP create still reaches the Core contract under a fresh key, for the request's own actor and tenant.
        Assert.Equal(("user-a", "tenant-design"), (replacement.Actors.Single().ActorId, replacement.Actors.Single().TenantId));
        Assert.False(string.IsNullOrWhiteSpace(replacement.TagKeys.Single()));
        using var favorite = await SendAsync(host, "PUT", "/design/workflows/definitions/orders/favorite", "library-read", "{}");
        Assert.Equal(HttpStatusCode.Forbidden, favorite.StatusCode);
        Assert.Contains("Replacement refused", await favorite.Content.ReadAsStringAsync());
        using var view = await SendAsync(host, "POST", "/design/workflows/views", "library-read", "{\"name\":\"Mine\",\"query\":{}}");
        Assert.Equal(HttpStatusCode.Conflict, view.StatusCode);
        Assert.NotNull(replacement.ViewKeys.Single());
    }

    [Fact]
    public async Task A_core_create_without_an_operation_key_is_invalid_and_an_update_takes_none()
    {
        await using var host = await StartAsync(new KeyedLibraryProbe());
        using var scope = Request(host, "library-all");
        var commands = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryCommands>();
        var actor = new WorkflowLibraryActor("user-a", "tenant-design");
        Assert.Equal(WorkflowLibraryCommandStatus.Invalid, (await commands.CreateTagAsync(actor, new("Critical"), " ")).Status);
        Assert.Equal(WorkflowLibraryCommandStatus.Invalid, (await commands.SaveViewAsync(actor, null, new("Mine", new()), null)).Status);
        Assert.Equal(WorkflowLibraryCommandStatus.Invalid, (await commands.SaveViewAsync(actor, "view-1", new("Mine", new(), Revision: "v1"), "key")).Status);
        Assert.Equal(WorkflowLibraryCommandStatus.Invalid, (await commands.MoveDefinitionsAsync(actor, ["orders"], null, "")).Status);
    }

    private sealed class RecordingCommands : IWorkflowLibraryCommands
    {
        public List<WorkflowLibraryActor> Actors { get; } = [];
        public List<string> TagKeys { get; } = [];
        public List<string?> ViewKeys { get; } = [];
        public ValueTask<WorkflowLibraryCommandResult<WorkflowLibraryPermissionFlags>> GetPermissionsAsync(WorkflowLibraryActor actor, CancellationToken cancellationToken = default) => ValueTask.FromResult(new WorkflowLibraryCommandResult<WorkflowLibraryPermissionFlags>(WorkflowLibraryCommandStatus.Applied, null, new(true, true, true, true)));
        public ValueTask<WorkflowLibraryCommandResult<WorkflowLibraryOrganizationSnapshot>> GetOrganizationAsync(WorkflowLibraryActor actor, string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowLibraryCommandResult<IReadOnlyList<WorkflowLibraryTagSnapshot>>> ListTagsAsync(WorkflowLibraryActor actor, string? search, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowLibraryCommandResult<WorkflowLibraryTagSnapshot>> CreateTagAsync(WorkflowLibraryActor actor, WorkflowLibraryTagCreate input, string operationKey, CancellationToken cancellationToken = default)
        {
            Actors.Add(actor); TagKeys.Add(operationKey);
            return ValueTask.FromResult(new WorkflowLibraryCommandResult<WorkflowLibraryTagSnapshot>(WorkflowLibraryCommandStatus.Applied, null,
                new("replacement-tag", input.Name, null, "marker", "single", false, "r1", [])));
        }
        public ValueTask<WorkflowLibraryCommandResult<WorkflowLibraryTagSnapshot>> FindTagByOperationKeyAsync(WorkflowLibraryActor actor, string operationKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowLibraryCommandResult<IReadOnlyList<WorkflowLibraryTargetOutcome>>> EditTagsAsync(WorkflowLibraryActor actor, WorkflowLibraryTagEdit edit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowLibraryCommandResult<IReadOnlyList<WorkflowLibraryTargetOutcome>>> SetFavoritesAsync(WorkflowLibraryActor actor, IReadOnlyList<string> definitionIds, bool favorite, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkflowLibraryCommandResult<IReadOnlyList<WorkflowLibraryTargetOutcome>>(WorkflowLibraryCommandStatus.Applied, null,
                definitionIds.Select(id => new WorkflowLibraryTargetOutcome(id, WorkflowLibraryCommandStatus.Denied, null, "Replacement refused")).ToArray()));
        public ValueTask<WorkflowLibraryCommandResult<IReadOnlyList<WorkflowLibraryTargetOutcome>>> MoveDefinitionsAsync(WorkflowLibraryActor actor, IReadOnlyList<string> definitionIds, string? folderId, string operationKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowLibraryCommandResult<IReadOnlyList<WorkflowLibraryViewSnapshot>>> ListViewsAsync(WorkflowLibraryActor actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowLibraryCommandResult<WorkflowLibraryViewSnapshot>> SaveViewAsync(WorkflowLibraryActor actor, string? viewId, WorkflowLibraryViewSave input, string? operationKey, CancellationToken cancellationToken = default)
        {
            ViewKeys.Add(operationKey);
            return ValueTask.FromResult(new WorkflowLibraryCommandResult<WorkflowLibraryViewSnapshot>(WorkflowLibraryCommandStatus.Conflict, "Replacement conflict"));
        }
        public ValueTask<WorkflowLibraryCommandResult<WorkflowLibraryViewSnapshot>> FindViewByOperationKeyAsync(WorkflowLibraryActor actor, string operationKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    // A command runs within the current request: the scope's accessor carries a principal like the route's own.
    private static IServiceScope Request(Host host, string identity)
    {
        var scope = host.Services.CreateScope();
        var claims = new List<Claim>
        {
            new(IdentityClaimTypes.Normalized, "v1"), new(IdentityClaimTypes.TenantId, "tenant-design"),
            new(IdentityClaimTypes.Permission, WorkflowDesignPermissions.Read), new(ClaimTypes.NameIdentifier, "user-a")
        };
        if (identity == "library-all") claims.Add(new(IdentityClaimTypes.Permission, PermissionKey.Wildcard));
        if (identity == "library-catalog") claims.Add(new(IdentityClaimTypes.Permission, WorkflowDesignPermissions.ManageTags));
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider, User = new ClaimsPrincipal(new ClaimsIdentity(claims, "WorkflowDesignContract"))
        };
        return scope;
    }

    private sealed class KeyedLibraryProbe : LibraryProbe, IWorkflowLibraryStore
    {
        private readonly Dictionary<string, (string Fingerprint, WorkflowTag Tag)> tags = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (string Fingerprint, WorkflowLibraryView View)> views = new(StringComparer.Ordinal);
        public List<string> TagKeys { get; } = [];
        public List<string> ViewKeys { get; } = [];
        public int TagsCreated { get; private set; }

        Task<WorkflowTag> IWorkflowLibraryStore.CreateTagAsync(WorkflowTagInput input, string actorId, string operationKey, CancellationToken cancellationToken)
        {
            TagKeys.Add(operationKey);
            var fingerprint = JsonSerializer.Serialize(input);
            if (tags.TryGetValue(operationKey, out var existing))
                return existing.Fingerprint == fingerprint ? Task.FromResult(existing.Tag)
                    : Task.FromException<WorkflowTag>(new WorkflowLibraryConflictException("This operation key already created a different tag."));
            TagsCreated++;
            var tag = new WorkflowTag("critical", input.Name, null, "marker", "single", false, "t1", []);
            tags[operationKey] = (fingerprint, tag);
            return Task.FromResult(tag);
        }

        Task<WorkflowTag?> IWorkflowLibraryStore.FindTagByOperationKeyAsync(string operationKey, CancellationToken cancellationToken) =>
            Task.FromResult(tags.TryGetValue(operationKey, out var existing) ? existing.Tag : null);

        Task<WorkflowLibraryView> IWorkflowLibraryStore.SaveViewAsync(string? id, WorkflowLibraryViewInput input, string subjectId, bool canShare,
            string operationKey, CancellationToken cancellationToken)
        {
            ViewKeys.Add(operationKey);
            var fingerprint = JsonSerializer.Serialize(new { input.Name, input.Shared, input.Query.SearchTerm });
            if (views.TryGetValue(subjectId + "\0" + operationKey, out var existing))
                return existing.Fingerprint == fingerprint ? Task.FromResult(existing.View)
                    : Task.FromException<WorkflowLibraryView>(new WorkflowLibraryConflictException("This operation key already created a different workflow view."));
            var view = new WorkflowLibraryView("view-1", input.Name, input.Shared, subjectId, input.Query, input.Columns ?? [], "v1");
            views[subjectId + "\0" + operationKey] = (fingerprint, view);
            return Task.FromResult(view);
        }

        Task<WorkflowLibraryView?> IWorkflowLibraryStore.FindViewByOperationKeyAsync(string operationKey, string subjectId, CancellationToken cancellationToken) =>
            Task.FromResult(views.TryGetValue(subjectId + "\0" + operationKey, out var existing) ? existing.View : null);
    }

    private sealed class DuplicateCommands : IWorkflowLibraryCommands
    {
        public ValueTask<WorkflowLibraryCommandResult<WorkflowLibraryPermissionFlags>> GetPermissionsAsync(WorkflowLibraryActor actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowLibraryCommandResult<WorkflowLibraryOrganizationSnapshot>> GetOrganizationAsync(WorkflowLibraryActor actor, string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowLibraryCommandResult<IReadOnlyList<WorkflowLibraryTagSnapshot>>> ListTagsAsync(WorkflowLibraryActor actor, string? search, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowLibraryCommandResult<WorkflowLibraryTagSnapshot>> CreateTagAsync(WorkflowLibraryActor actor, WorkflowLibraryTagCreate input, string operationKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowLibraryCommandResult<WorkflowLibraryTagSnapshot>> FindTagByOperationKeyAsync(WorkflowLibraryActor actor, string operationKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowLibraryCommandResult<IReadOnlyList<WorkflowLibraryTargetOutcome>>> EditTagsAsync(WorkflowLibraryActor actor, WorkflowLibraryTagEdit edit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowLibraryCommandResult<IReadOnlyList<WorkflowLibraryTargetOutcome>>> SetFavoritesAsync(WorkflowLibraryActor actor, IReadOnlyList<string> definitionIds, bool favorite, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowLibraryCommandResult<IReadOnlyList<WorkflowLibraryTargetOutcome>>> MoveDefinitionsAsync(WorkflowLibraryActor actor, IReadOnlyList<string> definitionIds, string? folderId, string operationKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowLibraryCommandResult<IReadOnlyList<WorkflowLibraryViewSnapshot>>> ListViewsAsync(WorkflowLibraryActor actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowLibraryCommandResult<WorkflowLibraryViewSnapshot>> SaveViewAsync(WorkflowLibraryActor actor, string? viewId, WorkflowLibraryViewSave input, string? operationKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowLibraryCommandResult<WorkflowLibraryViewSnapshot>> FindViewByOperationKeyAsync(WorkflowLibraryActor actor, string operationKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
