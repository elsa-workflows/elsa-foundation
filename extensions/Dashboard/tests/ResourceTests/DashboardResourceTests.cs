using Elsa.Studio.Dashboard.Core.Models;
using Elsa.Studio.Dashboard.Core.Resources;
using Elsa.Studio.Dashboard.Management.Resources;
using Elsa.Studio.Dashboard.Persistence.EntityFrameworkCore;
using System.Text.Json.Nodes;
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

    [Fact]
    public async Task Equivalent_parameter_dictionary_orders_replay_the_same_creation_key()
    {
        await using var host = await ResourceHost.CreateAsync();
        var first = Question("canonical");
        var definition = first.Definition! with { Query = first.Definition.Query with {
            Parameters = [new("category", "string"), new("region", "string")] },
            ParameterValues = new Dictionary<string, DashboardValue> {
                ["category"] = new("string", "retail"), ["region"] = new("string", "north") } };
        first = first with { Definition = definition };
        var saved = await host.With(service => service.CreateAsync(Actor("owner"), "question", first));
        var reordered = first with { Definition = definition with {
            ParameterValues = new Dictionary<string, DashboardValue> {
                ["region"] = new("string", "north"), ["category"] = new("string", "retail") } } };
        Assert.Equal(saved.Id, (await host.With(service => service.CreateAsync(Actor("owner"), "question", reordered))).Id);
    }

    [Fact]
    public async Task Effective_tile_overrides_require_disclosure_and_history_does_not_leak_them()
    {
        await using var host = await ResourceHost.CreateAsync();
        var input = Question("template");
        input = input with { Definition = input.Definition! with { Query = input.Definition.Query with {
            Parameters = [new("category", "string", Default: new("string", "public"))] } } };
        var question = await host.With(service => service.CreateAsync(Actor("owner"), "question", input));
        question = await host.With(service => service.SetAudienceAsync(Actor("owner"), "question", question.Id, question.Revision,
            [new("user", "viewer", "viewer")]));
        var dashboard = await host.With(service => service.CreateAsync(Actor("owner"), "dashboard", new() {
            Name = "Protected overrides", OperationKey = "overrides",
            Tiles = [new() { Id = "private", QuestionId = question.Id,
                Parameters = new Dictionary<string, DashboardValue> { ["category"] = new("string", "private-customer") } }] }));
        var historical = dashboard.Revision;
        dashboard = await host.With(service => service.SetAudienceAsync(Actor("owner"), "dashboard", dashboard.Id, dashboard.Revision,
            [new("user", "viewer", "viewer")]));
        host.Validator.Disclosure = (actor, query) => actor.UserId != "viewer" ||
            !query.ParameterValues.Values.Any(value => value.Literal == "private-customer");
        Assert.NotNull((await host.With(service => service.GetAsync(Actor("viewer"), "question", question.Id))).Definition);
        var deniedTile = (await host.With(service => service.GetAsync(Actor("viewer"), "dashboard", dashboard.Id))).Tiles[0];
        Assert.Equal("unavailable", deniedTile.ReferenceState); Assert.Null(deniedTile.QuestionId); Assert.Null(deniedTile.Parameters);
        var historicalTile = (await host.With(service => service.GetRevisionAsync(Actor("viewer"), "dashboard", dashboard.Id, historical))).Tiles[0];
        Assert.Null(historicalTile.Parameters); Assert.Null(historicalTile.QuestionId);
        Assert.Equal("private-customer", (await host.With(service => service.GetAsync(Actor("owner"), "dashboard", dashboard.Id))).Tiles[0].Parameters!["category"].Literal);
    }

    [Fact]
    public async Task History_attributes_each_revision_to_its_actual_modifier_across_restart()
    {
        await using var host = await ResourceHost.CreateAsync();
        var question = await host.With(service => service.CreateAsync(Actor("owner"), "question", Question("history")));
        question = await host.With(service => service.SetAudienceAsync(Actor("owner"), "question", question.Id, question.Revision,
            [new("user", "editor", "editor")]));
        question = await host.With(service => service.UpdateAsync(Actor("editor"), "question", question.Id, question.Revision,
            new() { Name = "Editor revision", PreserveDefinition = true }));
        var editorRevision = question.Revision;
        Assert.Equal("editor", (await host.With(service => service.HistoryAsync(Actor("owner"), "question", question.Id))).Items[0].ActorId);
        question = await host.With(service => service.UpdateAsync(Actor("owner"), "question", question.Id, question.Revision,
            new() { Name = "Owner revision", PreserveDefinition = true }));
        await using var restarted = await ResourceHost.CreateAsync(host.Path, initialize: false);
        var history = await restarted.With(service => service.HistoryAsync(Actor("owner"), "question", question.Id));
        Assert.Equal("owner", history.Items[0].ActorId);
        Assert.Equal("editor", history.Items.Single(item => item.Revision == editorRevision).ActorId);
    }

    [Fact]
    public async Task Missing_legacy_modifier_is_unknown_after_an_edit_and_never_falsely_attributed_to_owner()
    {
        await using var host = await ResourceHost.CreateAsync();
        var question = await host.With(service => service.CreateAsync(Actor("owner"), "question", Question("legacy")));
        question = await host.With(service => service.UpdateAsync(Actor("owner"), "question", question.Id, question.Revision,
            new() { Name = "Edited legacy", PreserveDefinition = true }));
        await host.WithContext(async context => {
            var row = await context.Resources.SingleAsync();
            var payload = JsonNode.Parse(row.DocumentJson)!.AsObject();
            payload.Remove("updatedBy"); row.DocumentJson = payload.ToJsonString();
            await context.SaveChangesAsync();
        });
        Assert.Equal("unknown", (await host.With(service => service.HistoryAsync(Actor("owner"), "question", question.Id))).Items[0].ActorId);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("revision")]
    [InlineData("schema")]
    public async Task Corrupt_or_incompatible_persistence_fails_meaningfully_without_an_empty_or_unchecked_definition(string change)
    {
        await using var host = await ResourceHost.CreateAsync();
        var question = await host.With(service => service.CreateAsync(Actor("owner"), "question", Question("corrupt")));
        await host.WithContext(async context => {
            var row = await context.Resources.SingleAsync();
            if (change == "json") row.DocumentJson = "{ malformed";
            if (change == "revision") row.Revision = "rev-corrupt";
            if (change == "schema") row.SchemaVersion = "9.0.0";
            await context.SaveChangesAsync();
        });
        await AssertCode(change == "schema" ? "schemaIncompatible" : "unavailable",
            () => host.With(service => service.GetAsync(Actor("owner"), "question", question.Id)));
    }

    [Fact]
    public async Task Concurrent_creates_cannot_exceed_the_owned_resource_quota_and_existing_key_replay_still_works()
    {
        await using var host = await ResourceHost.CreateAsync();
        for (var index = 0; index < 99; index++)
            await host.With(service => service.CreateAsync(Actor("owner"), "question", Question($"seed-{index}")));
        async Task<string> Create(string key)
        {
            try { await host.With(service => service.CreateAsync(Actor("owner"), "question", Question(key))); return "created"; }
            catch (DashboardResourceException error) { return error.Code; }
        }
        var outcomes = await Task.WhenAll(Create("race-a"), Create("race-b"));
        Assert.Single(outcomes, outcome => outcome == "created");
        Assert.Single(outcomes, outcome => outcome == "validation");
        Assert.Equal(100, (await host.With(service => service.ListAsync(Actor("owner"), "question"))).Items.Count);
        Assert.NotNull(await host.With(service => service.CreateAsync(Actor("owner"), "question", Question("seed-0"))));
    }

    [Fact]
    public async Task Concurrent_delete_and_update_with_one_revision_cannot_resurrect_the_deleted_resource()
    {
        await using var host = await ResourceHost.CreateAsync();
        var question = await host.With(service => service.CreateAsync(Actor("owner"), "question", Question("delete-race")));
        async Task<string> Delete()
        {
            try { await host.With(async service => { await service.DeleteAsync(Actor("owner"), "question", question.Id, question.Revision); return true; }); return "deleted"; }
            catch (DashboardResourceException error) { return error.Code; }
        }
        async Task<string> Update()
        {
            try { await host.With(service => service.UpdateAsync(Actor("owner"), "question", question.Id, question.Revision,
                new() { Name = "Updated", PreserveDefinition = true })); return "updated"; }
            catch (DashboardResourceException error) { return error.Code; }
        }
        var results = await Task.WhenAll(Delete(), Update());
        Assert.Single(results, result => result is "deleted" or "updated");
        if (results.Contains("deleted"))
            await AssertCode("notFound", () => host.With(service => service.GetAsync(Actor("owner"), "question", question.Id)));
        else Assert.Equal("Updated", (await host.With(service => service.GetAsync(Actor("owner"), "question", question.Id))).Name);
    }

    [Fact]
    public async Task Copy_rechecks_encoded_content_limit_before_creating_a_destination()
    {
        await using var host = await ResourceHost.CreateAsync();
        var actor = Actor("owner");
        var input = Question("near-limit");
        input = input with { Definition = input.Definition! with { ParameterValues = new Dictionary<string, DashboardValue>
            { ["padding"] = new("string", new string('a', 64_000)) } } };
        var source = await host.With(service => service.CreateAsync(actor, "question", input));
        var before = (await host.With(service => service.ListAsync(actor, "question"))).Items.Count;
        await AssertCode("validation", () => host.With(service => service.CopyAsync(actor, "question", source.Id, source.Revision,
            new("oversize-copy", "Copy", new string('b', 2_000)))));
        Assert.Equal(before, (await host.With(service => service.ListAsync(actor, "question"))).Items.Count);
        var valid = await host.With(service => service.CopyAsync(actor, "question", source.Id, source.Revision,
            new("valid-copy", "Copy")));
        Assert.NotEqual(source.Id, valid.Id);
        Assert.Equal(before + 1, (await host.With(service => service.ListAsync(actor, "question"))).Items.Count);
    }

    [Fact]
    public async Task Copy_rechecks_size_when_regenerating_short_dashboard_tile_ids()
    {
        await using var host = await ResourceHost.CreateAsync();
        var actor = Actor("owner");
        var question = await host.With(service => service.CreateAsync(actor, "question", Question("tile-source")));
        var parameters = new Dictionary<string, DashboardValue> { ["padding"] = new("string", "") };
        var content = new DashboardResourceContent("Board", null, null, [new("a", question.Id, "medium", Parameters: parameters)]);
        var overhead = System.Text.Encoding.UTF8.GetByteCount(System.Text.Json.JsonSerializer.Serialize(content));
        parameters["padding"] = new("string", new string('a', 131_060 - overhead));
        var source = await host.With(service => service.CreateAsync(actor, "dashboard", new() {
            Name = "Board", OperationKey = "near-dashboard-limit", Tiles = [new() { Id = "a", QuestionId = question.Id, Size = "medium", Parameters = parameters }] }));
        await AssertCode("validation", () => host.With(service => service.CopyAsync(actor, "dashboard", source.Id, source.Revision,
            new("oversize-dashboard-copy", "Board"))));
        Assert.Single((await host.With(service => service.ListAsync(actor, "dashboard"))).Items);
    }

    [Fact]
    public async Task Private_resource_composition_without_a_directory_refuses_audience_grants()
    {
        await using var host = await ResourceHost.CreateAsync(withAudienceDirectory: false);
        var actor = Actor("owner");
        var question = await host.With(service => service.CreateAsync(actor, "question", Question("without-directory")));
        await AssertCode("unavailable", () => host.With(service => service.SetAudienceAsync(actor, "question", question.Id,
            question.Revision, [new("user", "viewer", "viewer")])));
        Assert.Empty((await host.With(service => service.GetAudienceAsync(actor, "question", question.Id))).Items);
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
    public static async Task<ResourceHost> CreateAsync(string? path = null, bool initialize = true, bool withAudienceDirectory = true, Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor? interceptor = null)
    {
        var owned = path is null;
        path ??= System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"elsa-dashboard-resources-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection(); var validator = new ResourceValidator();
        services.AddSingleton<IDashboardDefinitionValidator>(validator);
        if (withAudienceDirectory) services.AddSingleton<IDashboardAudienceDirectory, ResourceDirectory>();
        services.AddDbContext<DashboardResourcesSqliteDbContext>(options => {
            options.UseSqlite($"Data Source={path};Pooling=False");
            if (interceptor is not null) options.AddInterceptors(interceptor);
        });
        services.AddDashboardResourcesEntityFrameworkCore<DashboardResourcesSqliteDbContext>();
        services.AddDashboardResources();
        var provider = services.BuildServiceProvider();
        if (initialize)
        {
            await using var scope = provider.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<DashboardResourcesSqliteDbContext>().Database;
            await database.MigrateAsync();
            Assert.Single(await database.GetAppliedMigrationsAsync());
        }
        return new(provider, path, owned, validator);
    }
    public async Task<T> With<T>(Func<IDashboardResourceService, ValueTask<T>> action)
    { await using var scope = provider.CreateAsyncScope(); return await action(scope.ServiceProvider.GetRequiredService<IDashboardResourceService>()); }
    public async Task<T> WithStore<T>(Func<IDashboardResourceStore, ValueTask<T>> action)
    { await using var scope = provider.CreateAsyncScope(); return await action(scope.ServiceProvider.GetRequiredService<IDashboardResourceStore>()); }
    public async Task WithContext(Func<DashboardResourcesSqliteDbContext, Task> action)
    { await using var scope = provider.CreateAsyncScope(); await action(scope.ServiceProvider.GetRequiredService<DashboardResourcesSqliteDbContext>()); }
    public async ValueTask DisposeAsync()
    {
        await provider.DisposeAsync();
        if (ownsPath) File.Delete(Path);
    }
}

internal sealed class ResourceValidator : IDashboardDefinitionValidator
{
    public bool Withheld { get; set; }
    public Func<DashboardActor, DashboardQueryRequest, bool>? Disclosure { get; set; }
    public Func<DashboardActor, Task>? BeforeValidation { get; set; }
    public async ValueTask ValidateAsync(DashboardActor actor, DashboardQueryRequest definition, CancellationToken cancellationToken = default)
    {
        if (Withheld) throw new DashboardResourceException("denied", "Test field denied.");
        if (BeforeValidation is not null) await BeforeValidation(actor).WaitAsync(cancellationToken);
    }
    public ValueTask<bool> CanDiscloseAsync(DashboardActor actor, DashboardQueryRequest definition, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(!Withheld && (Disclosure?.Invoke(actor, definition) ?? true));
}
internal sealed class ResourceDirectory : IDashboardAudienceDirectory
{
    public ValueTask<bool> IsValidAsync(DashboardActor actor, DashboardAudienceGrant grant, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(grant.Kind == "user" && grant.Id is "owner" or "editor" or "viewer" || grant.Kind == "role" && grant.Id == "operators");
    public ValueTask<DashboardAudienceCandidatePage> SearchAsync(DashboardActor actor, string kind, string? search, string? continuationToken = null, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new DashboardAudienceCandidatePage([]));
}
