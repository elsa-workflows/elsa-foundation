using System.Text.Json.Nodes;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Elsa3.Activities.Design.Import.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>
/// One host that imported <c>reusable-calls-reusable</c>: <c>inner</c> v1 and v2, and <c>outer</c>, whose body calls
/// <c>inner</c> v1 by exact version.
/// </summary>
public sealed class ImportedDependenciesFixture : IAsyncLifetime
{
    public Elsa3ImportHost Host { get; private set; } = null!;
    public ImportAttempt Import { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Host = await Elsa3ImportHost.StartAsync();
        Import = await Host.ImportSelectionAsync("import-dependencies", ["reusable-calls-reusable"]);
    }

    public async Task DisposeAsync() => await Host.DisposeAsync();
}

/// <summary>
/// Spec 246 User Story 2: imported reusable activities that use each other publish dependencies first, the versions of
/// one definition ascending, under the plan's version ids.
/// </summary>
public sealed class ImportPublishOrderTests(ImportedDependenciesFixture fixture) : IClassFixture<ImportedDependenciesFixture>
{
    private Elsa3ImportHost Host => fixture.Host;

    [Fact]
    public async Task Dependency_is_published_before_its_caller()
    {
        var order = PublishOrder();
        Assert.True(order.IndexOf("inner-v1") < order.IndexOf("outer-v1"), string.Join(", ", order));

        var inner = await PublicationAsync("inner-v1");
        var outer = await PublicationAsync("outer-v1");
        Assert.True(inner.PublishedAt <= outer.PublishedAt, $"inner {inner.PublishedAt:O}, outer {outer.PublishedAt:O}");
    }

    [Fact]
    public async Task Versions_of_one_definition_publish_ascending_and_head_ends_on_highest()
    {
        var order = PublishOrder();
        Assert.True(order.IndexOf("inner-v1") < order.IndexOf("inner-v2"), string.Join(", ", order));
        var v1 = await PublicationAsync("inner-v1");
        var v2 = await PublicationAsync("inner-v2");
        Assert.Equal(("1.0.0", "2.0.0"), (v1.Version, v2.Version));
        Assert.True(v1.PublishedAt <= v2.PublishedAt, $"v1 {v1.PublishedAt:O}, v2 {v2.PublishedAt:O}");

        var definitionId = Elsa3ImportHost.Required(fixture.Import.Item("inner-v1"), "activityDefinitionId");
        var authoring = await Host.ReadAsync(services => services.GetRequiredService<IActivityDefinitionAuthoringStore>().FindAsync(definitionId));
        Assert.Equal(fixture.Import.VersionId("inner-v2"), authoring?.HeadVersionId);
    }

    [Fact]
    public async Task Published_versions_keep_the_plan_ids_so_wrappers_and_callers_resolve()
    {
        var states = PublicationEntry.Of(fixture.Import.Apply.Body);
        Assert.Equal(3, states.Count);
        Assert.All(states, state =>
        {
            Assert.Equal(ReusableActivityPublicationStatus.Published, state.Status);
            Assert.Equal(fixture.Import.VersionId(state.SourceVersionId), state.VersionId);
        });
        foreach (var state in states)
            await PublicationAsync(state.SourceVersionId);

        // outer's wrapper runs outer, whose body resolves inner v1 by the plan's id.
        var wrapper = await Host.PublishWorkflowAsync(Elsa3ImportHost.Required(fixture.Import.Item("outer-v1"), "workflowVersionId"));
        var run = await Host.RunAsync(wrapper);

        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        var innerType = Elsa3ImportHost.Required(fixture.Import.Item("inner-v1"), "activityTypeKey");
        Assert.Contains(run.Activities, state => state.Execution.ActivityType == innerType && state.Status == ActivityExecutionStatus.Completed);
    }

    [Fact]
    public async Task Older_version_selected_after_a_newer_one_is_published_is_refused_with_bump_insufficient()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var newer = await host.ImportSelectionAsync("import-inner-v2", ["reusable-calls-reusable"], ["inner-v2"]);
        Assert.Equal(ReusableActivityPublicationStatus.Published, Assert.Single(PublicationEntry.Of(newer.Apply.Body)).Status);

        // The upload is gone after an apply: the older version comes from the same export, uploaded again.
        var older = await host.ImportSelectionAsync("import-inner-v1", ["reusable-calls-reusable"], ["inner-v1"]);

        var state = Assert.Single(PublicationEntry.Of(older.Apply.Body));
        Assert.True((state.Status, state.ErrorCode) == (ReusableActivityPublicationStatus.Refused, "activity.version.bump-insufficient"), state.Json.ToJsonString());
        Assert.Equal(older.VersionId("inner-v1"), state.VersionId);
        var publication = await host.ReadAsync(services => services.GetRequiredService<IActivityDefinitionVersionPublicationStore>().FindAsync(state.VersionId));
        Assert.Null(publication);
    }

    private List<string> PublishOrder() =>
        PublicationEntry.Of(fixture.Import.Apply.Body).Select(state => state.SourceVersionId).ToList();

    private async Task<ActivityDefinitionVersionPublication> PublicationAsync(string sourceVersionId)
    {
        var versionId = fixture.Import.VersionId(sourceVersionId);
        return await Host.ReadAsync(services => services.GetRequiredService<IActivityDefinitionVersionPublicationStore>().FindAsync(versionId))
               ?? throw new InvalidOperationException($"'{sourceVersionId}' ({versionId}) has no publication: {fixture.Import.Apply.Body.ToJsonString()}");
    }
}
