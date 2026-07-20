using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core.Events;
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
        Assert.Equal(
            DesignPersistenceFixtureData.ResultHash(DesignPersistenceFixtureData.WorkflowState()),
            DesignPersistenceFixtureData.ResultHash(beforeRestart.Draft.State));

        await fixture.RestartAsync();
        var afterRestart = await ReadDraftSnapshotAsync(fixture);

        Assert.Equal(DesignPersistenceFixtureData.ResultHash(beforeRestart), DesignPersistenceFixtureData.ResultHash(afterRestart));
        Assert.Equal(DesignPersistenceFixtureData.WorkflowDefinitionId, afterRestart.Definition.Id);
        Assert.Equal(DesignPersistenceFixtureData.WorkflowDraftId, afterRestart.Draft.Id);
        Assert.Equal(CanonicalLayout(layout), afterRestart.Layout.Records);
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

        var beforeRestart = await ReadVersionSnapshotAsync(fixture, versionId);
        await fixture.RestartAsync();
        var afterRestart = await ReadVersionSnapshotAsync(fixture, versionId);

        Assert.Equal(
            DesignPersistenceFixtureData.ResultHash(beforeRestart),
            DesignPersistenceFixtureData.ResultHash(afterRestart));

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

        var updatedState = DesignPersistenceFixtureData.WorkflowState("updated-root");
        var updatedLayout = new[] { new DesignMetadataRecord("updated-root", 42, 84, 220, 80) };
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

            fixture.ClearObservedEvents();
            await services.GetRequiredService<IUpdateDraftCommand>().Execute(
                new UpdateDraftRequest(DesignPersistenceFixtureData.WorkflowDraftId, updatedState, updatedLayout));
            promotedVersionId = await services.GetRequiredService<IPromoteDraftToVersionCommand>()
                .Execute(DesignPersistenceFixtureData.WorkflowDraftId);
            cloneId = await services.GetRequiredService<ICloneDraftFromVersionCommand>().Execute(promotedVersionId);
        }

        using var readScope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        var draft = await readScope.ServiceProvider.GetRequiredService<IWorkflowDefinitionDraftStore>()
            .FindWithLayoutByIdAsync(cloneId);

        Assert.NotNull(draft);
        Assert.Equal(promotedVersionId, draft!.Draft.SourceVersionId);
        Assert.Equal(updatedState.RootActivity, draft.Draft.State.RootActivity);
        Assert.Equal(updatedLayout, draft.Layout);

        var events = await fixture.ReadObservedEventsAsync();
        var created = Assert.Single(events.OfType<OnDraftCreated>());
        Assert.Equal(cloneId, created.DraftId);
        Assert.Equal(DesignPersistenceFixtureData.WorkflowDefinitionId, created.WorkflowDefinitionId);
        Assert.Equal(promotedVersionId, created.SourceVersionId);

        var validations = events.OfType<OnDraftValidated>().ToArray();
        var updatedValidation = Assert.Single(validations.Where(x => x.Draft.Id == DesignPersistenceFixtureData.WorkflowDraftId));
        var clonedValidation = Assert.Single(validations.Where(x => x.Draft.Id == cloneId));
        Assert.Equal(DesignPersistenceFixtureData.WorkflowDefinitionId, updatedValidation.Draft.WorkflowDefinitionId);
        Assert.Equal(updatedState, updatedValidation.Draft.State);
        Assert.False(updatedValidation.HasErrors);
        Assert.Empty(updatedValidation.Errors);
        Assert.Equal(DesignPersistenceFixtureData.WorkflowDefinitionId, clonedValidation.Draft.WorkflowDefinitionId);
        Assert.Equal(updatedState, clonedValidation.Draft.State);
        Assert.False(clonedValidation.HasErrors);
        Assert.Empty(clonedValidation.Errors);

        fixture.ClearObservedEvents();
        await readScope.ServiceProvider.GetRequiredService<IUpdateDraftCommand>().Execute(
            new UpdateDraftRequest(cloneId, WorkflowDefinitionState.Empty, []));

        var invalidValidation = Assert.Single((await fixture.ReadObservedEventsAsync()).OfType<OnDraftValidated>());
        Assert.Equal(cloneId, invalidValidation.Draft.Id);
        Assert.Equal(DesignPersistenceFixtureData.WorkflowDefinitionId, invalidValidation.Draft.WorkflowDefinitionId);
        Assert.Equal(WorkflowDefinitionState.Empty, invalidValidation.Draft.State);
        Assert.True(invalidValidation.HasErrors);
        Assert.Contains(invalidValidation.Errors, error => error.Type == "RootActivity/Missing");

        var invalidDraft = await readScope.ServiceProvider.GetRequiredService<IWorkflowDefinitionDraftStore>()
            .FindWithLayoutByIdAsync(cloneId);
        Assert.NotNull(invalidDraft);
        Assert.Equal(WorkflowDefinitionState.Empty, invalidDraft!.Draft.State);
        Assert.Empty(invalidDraft.Layout);
    }

    [Fact]
    public async Task Submission_creates_a_complete_durable_aggregate()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();

        var state = DesignPersistenceFixtureData.WorkflowState("submitted-root");
        string definitionId;
        string draftId;
        string versionId;
        using (var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA))
        {
            var writeServices = scope.ServiceProvider;
            await AddActivityPrerequisiteAsync(writeServices);
            var submitted = await writeServices.GetRequiredService<ISubmitWorkflowDefinitionCommand>().Execute(
                "Submitted order processing",
                "Creates a submitted workflow aggregate.",
                state);
            definitionId = submitted.DefinitionId;
            draftId = submitted.DraftId;
            versionId = submitted.VersionId;
        }

        await fixture.RestartAsync();

        using var readScope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        var services = readScope.ServiceProvider;
        var definition = await services.GetRequiredService<IWorkflowDefinitionStore>().FindByIdAsync(definitionId);
        var draft = await services.GetRequiredService<IWorkflowDefinitionDraftStore>().FindWithLayoutByIdAsync(draftId);
        var version = await services.GetRequiredService<IWorkflowDefinitionVersionStore>().FindByIdAsync(versionId);
        var layout = await services.GetRequiredService<IWorkflowDefinitionVersionLayoutStore>().FindByVersionIdAsync(versionId);

        Assert.NotNull(definition);
        Assert.NotNull(draft);
        Assert.NotNull(version);
        Assert.NotNull(layout);
        Assert.Equal("Submitted order processing", definition!.Name);
        Assert.Equal(definitionId, draft!.Draft.WorkflowDefinitionId);
        Assert.Equal(definitionId, version!.DefinitionId);
        Assert.Equal("1.0.0", version.Version);
        Assert.Equal(state, draft.Draft.State);
        Assert.Equal(state, version.State);
        Assert.Empty(draft.Layout);
        Assert.Empty(layout!.Records);
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
    public async Task Discard_removes_the_draft_and_publishes_its_documented_outcome()
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

            fixture.ClearObservedEvents();
            await services.GetRequiredService<IDiscardDraftCommand>().Execute(DesignPersistenceFixtureData.WorkflowDraftId);
            Assert.Null(await services.GetRequiredService<IWorkflowDefinitionDraftStore>()
                .FindByIdAsync(DesignPersistenceFixtureData.WorkflowDraftId));

            var discarded = Assert.Single((await fixture.ReadObservedEventsAsync()).OfType<OnDraftDiscarded>());
            Assert.Equal(DesignPersistenceFixtureData.WorkflowDraftId, discarded.DraftId);
            Assert.Equal(DesignPersistenceFixtureData.WorkflowDefinitionId, discarded.WorkflowDefinitionId);
        }
    }

    [Fact]
    public async Task Permanent_delete_removes_the_complete_workflow_aggregate()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();

        string versionId;
        using (var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA))
        {
            var services = scope.ServiceProvider;
            await AddActivityPrerequisiteAsync(services);
            await services.GetRequiredService<IAddWorkflowDefinitionCommand>().Execute(
                DesignPersistenceFixtureData.WorkflowDefinition(),
                DesignPersistenceFixtureData.WorkflowDraft(state: DesignPersistenceFixtureData.WorkflowState()),
                DesignPersistenceFixtureData.WorkflowDraftLayout(),
                CancellationToken.None);
            versionId = await services.GetRequiredService<IPromoteDraftToVersionCommand>()
                .Execute(DesignPersistenceFixtureData.WorkflowDraftId);

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
        var drafts = readScope.ServiceProvider.GetRequiredService<IWorkflowDefinitionDraftStore>();
        var versions = readScope.ServiceProvider.GetRequiredService<IWorkflowDefinitionVersionStore>();
        var layouts = readScope.ServiceProvider.GetRequiredService<IWorkflowDefinitionVersionLayoutStore>();
        Assert.Null(await definitions.FindByIdAsync(DesignPersistenceFixtureData.WorkflowDefinitionId));
        Assert.Empty(await definitions.ListAsync(new WorkflowDefinitionFilter { Id = DesignPersistenceFixtureData.WorkflowDefinitionId }));
        Assert.Null(await drafts.FindByIdAsync(DesignPersistenceFixtureData.WorkflowDraftId));
        Assert.Empty(await drafts.ListByWorkflowDefinitionIdAsync(DesignPersistenceFixtureData.WorkflowDefinitionId));
        Assert.Null(await versions.FindByIdAsync(versionId));
        Assert.Empty(await versions.ListByDefinitionAsync(DesignPersistenceFixtureData.WorkflowDefinitionId));
        Assert.Null(await layouts.FindByVersionIdAsync(versionId));
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
        return CanonicalSnapshot(definition!, draft!.Draft, draft.Layout);
    }

    private static async Task<WorkflowVersionSnapshot> ReadVersionSnapshotAsync(
        IDesignPersistenceContractFixture fixture,
        string versionId)
    {
        using var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        var services = scope.ServiceProvider;
        var version = await services.GetRequiredService<IWorkflowDefinitionVersionStore>()
            .FindByIdAsync(versionId);
        var layout = await services.GetRequiredService<IWorkflowDefinitionVersionLayoutStore>()
            .FindByVersionIdAsync(versionId);

        Assert.NotNull(version);
        Assert.NotNull(layout);
        return CanonicalSnapshot(version!, layout!);
    }

    internal static WorkflowDraftSnapshot CanonicalSnapshot(
        WorkflowDefinition definition,
        WorkflowDefinitionDraft draft,
        IReadOnlyCollection<DesignMetadataRecord> layout) =>
        new(
            new WorkflowDefinitionSnapshot(
                definition.Id,
                definition.CreatedAt,
                definition.LastModifiedAt,
                definition.TenantId,
                definition.Name,
                definition.Description,
                definition.DeletedAt,
                definition.DeletedReason,
                definition.IsSourceOwned),
            new WorkflowDefinitionDraftSnapshot(
                draft.Id,
                draft.CreatedAt,
                draft.LastModifiedAt,
                draft.TenantId,
                draft.WorkflowDefinitionId,
                draft.SourceVersionId,
                draft.State),
            new WorkflowDefinitionDraftLayoutSnapshot(CanonicalLayout(layout)));

    internal static WorkflowVersionSnapshot CanonicalSnapshot(
        WorkflowDefinitionVersion version,
        WorkflowDefinitionVersionLayout layout) =>
        new(
            new WorkflowDefinitionVersionSnapshot(
                version.Id,
                version.CreatedAt,
                version.LastModifiedAt,
                version.TenantId,
                version.Version,
                version.SemVerSortKey,
                version.DefinitionId,
                version.SourceDraftId,
                version.State,
                version.SourceCreatedAt),
            new WorkflowDefinitionVersionLayoutSnapshot(
                layout.Id,
                layout.CreatedAt,
                layout.LastModifiedAt,
                layout.TenantId,
                layout.WorkflowDefinitionVersionId,
                CanonicalLayout(layout.Records)));

    private static IReadOnlyList<DesignMetadataSnapshot> CanonicalLayout(
        IEnumerable<DesignMetadataRecord> layout) =>
        layout
            .OrderBy(record => record.NodeId, StringComparer.Ordinal)
            .Select(record => new DesignMetadataSnapshot(
                record.NodeId,
                record.X,
                record.Y,
                record.Width,
                record.Height,
                record.AdditionalProperties))
            .ToArray();

    internal sealed record WorkflowDraftSnapshot(
        WorkflowDefinitionSnapshot Definition,
        WorkflowDefinitionDraftSnapshot Draft,
        WorkflowDefinitionDraftLayoutSnapshot Layout);

    internal sealed record WorkflowDefinitionSnapshot(
        string Id,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastModifiedAt,
        string? TenantId,
        string Name,
        string? Description,
        DateTimeOffset? DeletedAt,
        string? DeletedReason,
        bool IsSourceOwned);

    internal sealed record WorkflowDefinitionDraftSnapshot(
        string Id,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastModifiedAt,
        string? TenantId,
        string WorkflowDefinitionId,
        string? SourceVersionId,
        WorkflowDefinitionState State);

    internal sealed record WorkflowDefinitionDraftLayoutSnapshot(
        IReadOnlyList<DesignMetadataSnapshot> Records);

    internal sealed record WorkflowVersionSnapshot(
        WorkflowDefinitionVersionSnapshot Version,
        WorkflowDefinitionVersionLayoutSnapshot Layout);

    internal sealed record WorkflowDefinitionVersionSnapshot(
        string Id,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastModifiedAt,
        string? TenantId,
        string Version,
        string SemVerSortKey,
        string DefinitionId,
        string? SourceDraftId,
        WorkflowDefinitionState State,
        DateTimeOffset? SourceCreatedAt);

    internal sealed record WorkflowDefinitionVersionLayoutSnapshot(
        string Id,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastModifiedAt,
        string? TenantId,
        string WorkflowDefinitionVersionId,
        IReadOnlyList<DesignMetadataSnapshot> Records);

    internal sealed record DesignMetadataSnapshot(
        string NodeId,
        double X,
        double Y,
        double? Width,
        double? Height,
        System.Text.Json.JsonElement? AdditionalProperties);
}
