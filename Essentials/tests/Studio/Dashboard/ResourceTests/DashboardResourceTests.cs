using Elsa.Studio.Dashboard.Core.Models;
using Elsa.Studio.Dashboard.Core.Resources;
using Elsa.Studio.Dashboard.Management.Resources;
using Elsa.Studio.Dashboard.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Studio.Dashboard.ResourceTests;

public sealed class DashboardResourceTests
{
    [Fact]
    public async Task Private_definitions_are_isolated_by_actor_and_tenant_even_with_wildcard_permission()
    {
        await using var host = await ResourceHost.CreateAsync();
        var question = await host.With(service => service.CreateAsync(Actor("owner"), "question", Question("one")));
        await AssertCode("notFound", () => host.With(service => service.GetAsync(Actor("viewer"), "question", question.Id)));
        await AssertCode("notFound", () => host.With(service => service.GetAsync(Actor("owner", "other-tenant"), "question", question.Id)));
        Assert.Empty((await host.With(service => service.ListAsync(Actor("viewer"), "question"))).Items);
        Assert.Single((await host.With(service => service.ListAsync(Actor("owner"), "question", "mine"))).Items);
    }

    [Fact]
    public async Task Owners_editors_viewers_and_role_audiences_have_distinct_rights()
    {
        await using var host = await ResourceHost.CreateAsync();
        var question = await host.With(service => service.CreateAsync(Actor("owner"), "question", Question("one")));
        question = await host.With(service => service.SetAudienceAsync(Actor("owner"), "question", question.Id, question.Revision,
            [new("user", "viewer", "viewer"), new("role", "operators", "editor")]));
        var viewer = await host.With(service => service.GetAsync(Actor("viewer"), "question", question.Id));
        Assert.False(viewer.Capabilities.CanEdit); Assert.True(viewer.Capabilities.CanCopy);
        await AssertCode("denied", () => host.With(service => service.UpdateAsync(Actor("viewer"), "question", question.Id, question.Revision, Question("update"))));
        var editorActor = Actor("editor", roles: ["operators"]);
        var editor = await host.With(service => service.GetAsync(editorActor, "question", question.Id));
        Assert.True(editor.Capabilities.CanEdit); Assert.False(editor.Capabilities.CanShare); Assert.False(editor.Capabilities.CanDelete);
        await AssertCode("denied", () => host.With(service => service.SetAudienceAsync(editorActor, "question", question.Id, question.Revision, [])));
        await AssertCode("denied", () => host.With(async service => { await service.DeleteAsync(editorActor, "question", question.Id, question.Revision); return true; }));
        await host.With(service => service.UpdateAsync(editorActor, "question", question.Id, question.Revision, Question("update") with { OperationKey = null }));
    }

    [Fact]
    public async Task Revocation_blocks_current_reads_history_and_copy()
    {
        await using var host = await ResourceHost.CreateAsync();
        var question = await host.With(service => service.CreateAsync(Actor("owner"), "question", Question("one")));
        question = await host.With(service => service.SetAudienceAsync(Actor("owner"), "question", question.Id, question.Revision, [new("user", "viewer", "viewer")]));
        Assert.True((await host.With(service => service.GetAsync(Actor("viewer"), "question", question.Id))).Capabilities.CanRead);
        question = await host.With(service => service.SetAudienceAsync(Actor("owner"), "question", question.Id, question.Revision, []));
        await AssertCode("notFound", () => host.With(service => service.GetAsync(Actor("viewer"), "question", question.Id)));
        await AssertCode("notFound", () => host.With(service => service.HistoryAsync(Actor("viewer"), "question", question.Id)));
        await AssertCode("notFound", () => host.With(service => service.CopyAsync(Actor("viewer"), "question", question.Id, question.Revision, new("copy", "Copy"))));
    }

    [Fact]
    public async Task Keyed_creation_and_private_copy_survive_restart_without_duplicates_or_inherited_grants()
    {
        await using var host = await ResourceHost.CreateAsync();
        var question = await host.With(service => service.CreateAsync(Actor("owner"), "question", Question("same-key")));
        var replay = await host.With(service => service.CreateAsync(Actor("owner"), "question", Question("same-key")));
        Assert.Equal(question.Id, replay.Id);
        await AssertCode("conflict", () => host.With(service => service.CreateAsync(Actor("owner"), "question", Question("same-key") with { Name = "Different" })));
        question = await host.With(service => service.SetAudienceAsync(Actor("owner"), "question", question.Id, question.Revision, [new("user", "viewer", "viewer")]));
        var copy = await host.With(service => service.CopyAsync(Actor("viewer"), "question", question.Id, question.Revision, new("copy-key", "Personal copy")));
        Assert.Equal("viewer", copy.OwnerId);
        Assert.Empty((await host.With(service => service.GetAudienceAsync(Actor("viewer"), "question", copy.Id))).Items);
        Assert.Single((await host.With(service => service.HistoryAsync(Actor("viewer"), "question", copy.Id))).Items);
        await using var restarted = await ResourceHost.CreateAsync(host.Path, initialize: false);
        Assert.Equal(copy.Id, (await restarted.With(service => service.GetAsync(Actor("viewer"), "question", copy.Id))).Id);
        Assert.Equal(question.Id, (await restarted.With(service => service.CreateAsync(Actor("owner"), "question", Question("same-key")))).Id);
    }

    [Fact]
    public async Task Stale_content_and_audience_updates_cannot_restore_removed_grants()
    {
        await using var host = await ResourceHost.CreateAsync();
        var question = await host.With(service => service.CreateAsync(Actor("owner"), "question", Question("one")));
        question = await host.With(service => service.SetAudienceAsync(Actor("owner"), "question", question.Id, question.Revision, [new("user", "editor", "editor")]));
        var obsolete = question.Revision;
        question = await host.With(service => service.SetAudienceAsync(Actor("owner"), "question", question.Id, obsolete, []));
        await AssertCode("conflict", () => host.With(service => service.UpdateAsync(Actor("owner"), "question", question.Id, obsolete, Question("edit") with { OperationKey = null })));
        await AssertCode("notFound", () => host.With(service => service.UpdateAsync(Actor("editor"), "question", question.Id, obsolete, Question("edit") with { OperationKey = null })));
        await AssertCode("conflict", () => host.With(service => service.SetAudienceAsync(Actor("owner"), "question", question.Id, obsolete, [new("user", "editor", "editor")])));
        Assert.Empty((await host.With(service => service.GetAudienceAsync(Actor("owner"), "question", question.Id))).Items);
    }

    [Fact]
    public async Task Hidden_references_survive_layout_edit_and_dashboard_copy_without_disclosing_question()
    {
        await using var host = await ResourceHost.CreateAsync();
        var question = await host.With(service => service.CreateAsync(Actor("owner"), "question", Question("q")));
        var board = await host.With(service => service.CreateAsync(Actor("owner"), "dashboard", new() {
            Name = "Operations", OperationKey = "board", Tiles = [new() { Id = "tile-one", QuestionId = question.Id }] }));
        board = await host.With(service => service.SetAudienceAsync(Actor("owner"), "dashboard", board.Id, board.Revision, [new("user", "editor", "editor")]));
        var projected = await host.With(service => service.GetAsync(Actor("editor"), "dashboard", board.Id));
        Assert.Equal("unavailable", projected.Tiles[0].ReferenceState); Assert.Null(projected.Tiles[0].QuestionId);
        projected = await host.With(service => service.UpdateAsync(Actor("editor"), "dashboard", board.Id, projected.Revision, new() {
            Name = "Rearranged", Tiles = [new() { Id = "tile-one", PreserveReference = true, Size = "full" }] }));
        Assert.Null(projected.Tiles[0].QuestionId);
        var original = await host.With(service => service.GetAsync(Actor("owner"), "dashboard", board.Id));
        Assert.Equal(question.Id, original.Tiles[0].QuestionId); Assert.Equal("full", original.Tiles[0].Size);
        var copy = await host.With(service => service.CopyAsync(Actor("editor"), "dashboard", board.Id, original.Revision, new("copy-board", "My operations")));
        Assert.Null(copy.Tiles[0].QuestionId); Assert.NotEqual("tile-one", copy.Tiles[0].Id);
        await AssertCode("validation", () => host.With(service => service.CreateAsync(Actor("editor"), "dashboard", new() {
            Name = "Forged", OperationKey = "forged", Tiles = [new() { Id = "tile-one", PreserveReference = true }] })));
        await host.With(service => service.SetAudienceAsync(Actor("owner"), "question", question.Id, question.Revision, [new("user", "editor", "viewer")]));
        Assert.Equal(question.Id, (await host.With(service => service.GetAsync(Actor("editor"), "dashboard", copy.Id))).Tiles[0].QuestionId);
    }

    [Fact]
    public async Task Withheld_question_can_be_renamed_without_roundtripping_or_copying_hidden_definition()
    {
        await using var host = await ResourceHost.CreateAsync();
        var question = await host.With(service => service.CreateAsync(Actor("owner"), "question", Question("q")));
        host.Validator.Withheld = true;
        var withheld = await host.With(service => service.GetAsync(Actor("owner"), "question", question.Id));
        Assert.Null(withheld.Definition); Assert.False(withheld.Capabilities.CanCopy);
        withheld = await host.With(service => service.UpdateAsync(Actor("owner"), "question", question.Id, question.Revision,
            new() { Name = "Safe rename", PreserveDefinition = true }));
        Assert.Equal("withheld", withheld.DefinitionState);
        await AssertCode("denied", () => host.With(service => service.CopyAsync(Actor("owner"), "question", question.Id, withheld.Revision, new("copy", "Copy"))));
        host.Validator.Withheld = false;
        var visible = await host.With(service => service.GetAsync(Actor("owner"), "question", question.Id));
        Assert.Equal("test.records", visible.Definition!.Query.Dataset.Id); Assert.Equal("Safe rename", visible.Name);
    }

    [Fact]
    public async Task Restore_appends_content_revision_without_rolling_back_acl_and_deleted_key_replay_stays_deleted()
    {
        await using var host = await ResourceHost.CreateAsync();
        var question = await host.With(service => service.CreateAsync(Actor("owner"), "question", Question("q")));
        var original = question.Revision;
        question = await host.With(service => service.UpdateAsync(Actor("owner"), "question", question.Id, original, Question("edit") with { Name = "Changed", OperationKey = null }));
        question = await host.With(service => service.SetAudienceAsync(Actor("owner"), "question", question.Id, question.Revision, [new("user", "viewer", "viewer")]));
        question = await host.With(service => service.RestoreAsync(Actor("owner"), "question", question.Id, question.Revision, original));
        Assert.Equal("Question", question.Name); Assert.NotEqual(original, question.Revision);
        Assert.Single((await host.With(service => service.GetAudienceAsync(Actor("owner"), "question", question.Id))).Items);
        await host.With(async service => { await service.DeleteAsync(Actor("owner"), "question", question.Id, question.Revision); return true; });
        await AssertCode("notFound", () => host.With(service => service.CreateAsync(Actor("owner"), "question", Question("q"))));
        await AssertCode("notFound", () => host.With(service => service.HistoryAsync(Actor("owner"), "question", question.Id)));
    }

    [Fact]
    public async Task Accepted_preview_parameter_values_become_persisted_defaults()
    {
        await using var host = await ResourceHost.CreateAsync();
        var request = Question("values");
        request = request with { Definition = request.Definition! with { Query = request.Definition.Query with {
            Parameters = [new("category", "string", Default: new("string", "retail"))] },
            ParameterValues = new Dictionary<string, DashboardValue> { ["category"] = new("string", "enterprise") } } };
        var question = await host.With(service => service.CreateAsync(Actor("owner"), "question", request));
        var reopened = await host.With(service => service.GetAsync(Actor("owner"), "question", question.Id));
        Assert.Equal("enterprise", reopened.Definition!.Query.Parameters[0].Default!.Literal);
        Assert.Equal("enterprise", reopened.Definition.ParameterValues["category"].Literal);
    }

    internal static DashboardResourceInput Question(string key) => new() { Name = "Question", OperationKey = key,
        Definition = new() { Query = new() { Dataset = new("test.records", "1"), Form = "records", Fields = ["id"] } } };
    internal static DashboardActor Actor(string id, string tenant = "tenant-a", string[]? roles = null) =>
        new(tenant, id, new HashSet<string>(roles ?? [], StringComparer.Ordinal), new HashSet<string>(["*"], StringComparer.Ordinal));
    private static async Task AssertCode<T>(string code, Func<Task<T>> action) =>
        Assert.Equal(code, (await Assert.ThrowsAsync<DashboardResourceException>(action)).Code);
}

internal sealed class ResourceHost : IAsyncDisposable
{
    private readonly ServiceProvider provider;
    private readonly bool ownsPath;
    private ResourceHost(ServiceProvider provider, string path, bool ownsPath, ResourceValidator validator)
    { this.provider = provider; Path = path; this.ownsPath = ownsPath; Validator = validator; }
    public string Path { get; }
    public ResourceValidator Validator { get; }
    public static async Task<ResourceHost> CreateAsync(string? path = null, bool initialize = true)
    {
        var owned = path is null;
        path ??= System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"elsa-dashboard-resources-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection(); var validator = new ResourceValidator();
        services.AddSingleton<IDashboardDefinitionValidator>(validator);
        services.AddSingleton<IDashboardAudienceDirectory, ResourceDirectory>();
        services.AddDbContext<DashboardResourcesSqliteDbContext>(options => options.UseSqlite($"Data Source={path}"));
        services.AddDashboardResourcesEntityFrameworkCore<DashboardResourcesSqliteDbContext>();
        services.AddDashboardResources();
        var provider = services.BuildServiceProvider();
        if (initialize)
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DashboardResourcesSqliteDbContext>().Database.EnsureCreatedAsync();
        }
        return new(provider, path, owned, validator);
    }
    public async Task<T> With<T>(Func<IDashboardResourceService, ValueTask<T>> action)
    { await using var scope = provider.CreateAsyncScope(); return await action(scope.ServiceProvider.GetRequiredService<IDashboardResourceService>()); }
    public async ValueTask DisposeAsync()
    {
        await provider.DisposeAsync();
        if (ownsPath) { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(Path); }
    }
}

internal sealed class ResourceValidator : IDashboardDefinitionValidator
{
    public bool Withheld { get; set; }
    public ValueTask ValidateAsync(DashboardActor actor, DashboardQueryRequest definition, CancellationToken cancellationToken = default)
    { if (Withheld) return ValueTask.FromException(new DashboardResourceException("denied", "Test field denied.")); return ValueTask.CompletedTask; }
    public ValueTask<bool> CanDiscloseAsync(DashboardActor actor, DashboardQueryRequest definition, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(!Withheld);
}
internal sealed class ResourceDirectory : IDashboardAudienceDirectory
{
    public ValueTask<bool> IsValidAsync(DashboardActor actor, DashboardAudienceGrant grant, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(grant.Kind == "user" && grant.Id is "owner" or "editor" or "viewer" || grant.Kind == "role" && grant.Id == "operators");
    public ValueTask<DashboardAudienceCandidatePage> SearchAsync(DashboardActor actor, string kind, string? search, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new DashboardAudienceCandidatePage([]));
}
