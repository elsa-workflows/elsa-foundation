using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.Tests;

/// <summary>
/// Provider-neutral workflow-design conformance scenarios. A concrete oracle or provider fixture
/// inherits this class and supplies only durable fixture materialization through
/// <see cref="CreateFixtureAsync"/>.
/// </summary>
public abstract class WorkflowDesignContractSuite
{
    protected abstract Task<IDesignPersistenceContractFixture> CreateFixtureAsync(CancellationToken cancellationToken = default);

    [Fact]
    public async Task Definition_draft_and_layout_round_trip_across_restart()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();

        var layout = DesignPersistenceFixtureData.WorkflowDraftLayout();
        using (var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA))
        {
            var services = scope.ServiceProvider;
            await AddActivityPrerequisiteAsync(services);

            await services.GetRequiredService<IAddWorkflowDefinitionCommand>().Execute(
                DesignPersistenceFixtureData.WorkflowDefinition(),
                DesignPersistenceFixtureData.WorkflowDraft(state: DesignPersistenceFixtureData.WorkflowState()),
                layout,
                CancellationToken.None);
        }

        var beforeRestart = await ReadDraftSnapshotAsync(fixture);
        await fixture.RestartAsync();
        var afterRestart = await ReadDraftSnapshotAsync(fixture);

        Assert.Equal(DesignPersistenceFixtureData.ResultHash(beforeRestart), DesignPersistenceFixtureData.ResultHash(afterRestart));
        Assert.Equal(DesignPersistenceFixtureData.WorkflowDefinitionId, afterRestart.DefinitionId);
        Assert.Equal(DesignPersistenceFixtureData.WorkflowDraftId, afterRestart.DraftId);
        Assert.Equal(layout, afterRestart.Layout);
    }

    [Fact]
    public async Task Promoted_version_preserves_authored_state_layout_identity_and_missing_outcomes()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();

        var layout = DesignPersistenceFixtureData.WorkflowDraftLayout();
        string versionId;
        using (var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA))
        {
            var services = scope.ServiceProvider;
            await AddActivityPrerequisiteAsync(services);
            await services.GetRequiredService<IAddWorkflowDefinitionCommand>().Execute(
                DesignPersistenceFixtureData.WorkflowDefinition(),
                DesignPersistenceFixtureData.WorkflowDraft(state: DesignPersistenceFixtureData.WorkflowState()),
                layout,
                CancellationToken.None);

            versionId = await services.GetRequiredService<IPromoteDraftToVersionCommand>()
                .Execute(DesignPersistenceFixtureData.WorkflowDraftId);
        }

        using (var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA))
        {
            var services = scope.ServiceProvider;
            var versions = services.GetRequiredService<IWorkflowDefinitionVersionStore>();
            var layouts = services.GetRequiredService<IWorkflowDefinitionVersionLayoutStore>();
            var version = await versions.FindByIdAsync(versionId);
            var latest = await versions.FindLatestVersionAsync(DesignPersistenceFixtureData.WorkflowDefinitionId);
            var versionLayout = await layouts.FindByVersionIdAsync(versionId);

            Assert.NotNull(version);
            Assert.NotNull(latest);
            Assert.NotNull(versionLayout);
            Assert.Equal(versionId, latest!.Id);
            Assert.Equal(DesignPersistenceFixtureData.WorkflowDefinitionId, version!.DefinitionId);
            Assert.Equal(DesignPersistenceFixtureData.WorkflowState().RootActivity, version.State.RootActivity);
            Assert.Equal(layout, versionLayout!.Records);
            Assert.Null(await versions.FindByIdAsync("missing-workflow-version"));
            Assert.Null(await layouts.FindByVersionIdAsync("missing-workflow-version"));
        }
    }

    [Fact]
    public async Task Draft_update_and_clone_preserve_layout_state_and_source_version()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();

        var updatedLayout = new[] { new DesignMetadataRecord("root", 42, 84, 220, 80) };
        string promotedVersionId;
        string cloneId;
        using (var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA))
        {
            var services = scope.ServiceProvider;
            await AddActivityPrerequisiteAsync(services);
            await services.GetRequiredService<IAddWorkflowDefinitionCommand>().Execute(
                DesignPersistenceFixtureData.WorkflowDefinition(),
                DesignPersistenceFixtureData.WorkflowDraft(state: DesignPersistenceFixtureData.WorkflowState()),
                DesignPersistenceFixtureData.WorkflowDraftLayout(),
                CancellationToken.None);

            await services.GetRequiredService<IUpdateDraftCommand>().Execute(
                new UpdateDraftRequest(DesignPersistenceFixtureData.WorkflowDraftId, DesignPersistenceFixtureData.WorkflowState(), updatedLayout));
            promotedVersionId = await services.GetRequiredService<IPromoteDraftToVersionCommand>()
                .Execute(DesignPersistenceFixtureData.WorkflowDraftId);
            cloneId = await services.GetRequiredService<ICloneDraftFromVersionCommand>().Execute(promotedVersionId);
        }

        using var readScope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        var draft = await readScope.ServiceProvider.GetRequiredService<IWorkflowDefinitionDraftStore>()
            .FindWithLayoutByIdAsync(cloneId);

        Assert.NotNull(draft);
        Assert.Equal(promotedVersionId, draft!.Draft.SourceVersionId);
        Assert.Equal(DesignPersistenceFixtureData.WorkflowState().RootActivity, draft.Draft.State.RootActivity);
        Assert.Equal(updatedLayout, draft.Layout);
    }

    [Fact]
    public async Task Submission_rejects_missing_root_activity_without_creating_a_definition()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();

        using var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        var services = scope.ServiceProvider;
        var submit = services.GetRequiredService<ISubmitWorkflowDefinitionCommand>();

        await Assert.ThrowsAsync<ArgumentException>(() => submit.Execute(
            "invalid workflow",
            null,
            WorkflowDefinitionState.Empty));

        var definitions = services.GetRequiredService<IWorkflowDefinitionStore>();
        Assert.Empty(await definitions.ListAsync(new WorkflowDefinitionFilter { Name = "invalid workflow" }));
    }

    [Fact]
    public async Task Discard_and_permanent_delete_leave_their_documented_missing_outcomes()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();

        using (var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA))
        {
            var services = scope.ServiceProvider;
            await services.GetRequiredService<IAddWorkflowDefinitionCommand>().Execute(
                DesignPersistenceFixtureData.WorkflowDefinition(),
                DesignPersistenceFixtureData.WorkflowDraft(state: WorkflowDefinitionState.Empty),
                DesignPersistenceFixtureData.WorkflowDraftLayout(),
                CancellationToken.None);

            await services.GetRequiredService<IDiscardDraftCommand>().Execute(DesignPersistenceFixtureData.WorkflowDraftId);
            Assert.Null(await services.GetRequiredService<IWorkflowDefinitionDraftStore>()
                .FindByIdAsync(DesignPersistenceFixtureData.WorkflowDraftId));

            var definition = await services.GetRequiredService<IWorkflowDefinitionStore>()
                .GetAsync(DesignPersistenceFixtureData.WorkflowDefinitionId);
            definition.DeletedAt = DesignPersistenceFixtureData.Epoch;
            definition.DeletedReason = "fixture deletion";
            await services.GetRequiredService<ISaveWorkflowDefinitionCommand>().Execute(definition);
            await services.GetRequiredService<IDeleteWorkflowDefinitionPermanentlyCommand>()
                .Execute(DesignPersistenceFixtureData.WorkflowDefinitionId);
        }

        using var readScope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        var definitions = readScope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();
        Assert.Null(await definitions.FindByIdAsync(DesignPersistenceFixtureData.WorkflowDefinitionId));
        Assert.Empty(await definitions.ListAsync(new WorkflowDefinitionFilter { Id = DesignPersistenceFixtureData.WorkflowDefinitionId }));
    }

    private static async Task AddActivityPrerequisiteAsync(IServiceProvider services)
    {
        await services.GetRequiredService<IAddActivityDefinitionCommand>().Execute(
            DesignPersistenceFixtureData.ActivityDefinition(),
            DesignPersistenceFixtureData.ActivityVersion(),
            CancellationToken.None);
    }

    private static async Task<WorkflowDraftSnapshot> ReadDraftSnapshotAsync(IDesignPersistenceContractFixture fixture)
    {
        using var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        var services = scope.ServiceProvider;
        var definition = await services.GetRequiredService<IWorkflowDefinitionStore>()
            .FindByIdAsync(DesignPersistenceFixtureData.WorkflowDefinitionId);
        var draft = await services.GetRequiredService<IWorkflowDefinitionDraftStore>()
            .FindWithLayoutByIdAsync(DesignPersistenceFixtureData.WorkflowDraftId);

        Assert.NotNull(definition);
        Assert.NotNull(draft);
        return new WorkflowDraftSnapshot(definition!.Id, definition.Name, definition.Description, draft!.Draft.Id, draft.Draft.WorkflowDefinitionId, draft.Layout);
    }

    private sealed record WorkflowDraftSnapshot(
        string DefinitionId,
        string Name,
        string? Description,
        string DraftId,
        string DraftDefinitionId,
        IReadOnlyCollection<DesignMetadataRecord> Layout);
}
