using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Persistence.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.Tests;

/// <summary>
/// Provider-neutral storage-scope, duplicate-identity, optimistic-concurrency, and restart
/// scenarios. Concrete provider fixtures supply the composed public Elsa contracts; this suite
/// never accesses a provider document store or provider-specific failure mechanism.
/// </summary>
public abstract class DesignIsolationAndRestartContractSuite
{
    protected abstract Task<IDesignPersistenceContractFixture> CreateFixtureAsync(CancellationToken cancellationToken = default);

    [Fact]
    public async Task Same_point_identities_resolve_only_their_own_scope()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();

        var scopeA = await SeedScopeAsync(fixture, DesignPersistenceFixtureData.ScopeA, "scope A");
        var scopeB = await SeedScopeAsync(fixture, DesignPersistenceFixtureData.ScopeB, "scope B");

        var observedA = await ReadScopeSnapshotAsync(fixture, DesignPersistenceFixtureData.ScopeA, scopeA.PromotedVersionId);
        var observedB = await ReadScopeSnapshotAsync(fixture, DesignPersistenceFixtureData.ScopeB, scopeB.PromotedVersionId);

        Assert.Equal(DesignPersistenceFixtureData.ScopeA, observedA.Workflow.Definition.TenantId);
        Assert.Equal("Order processing scope A", observedA.Workflow.Definition.Name);
        Assert.Equal(DesignPersistenceFixtureData.ScopeA, observedA.Activity.Definition.TenantId);
        Assert.Equal("Send HTTP request scope A", observedA.Activity.Definition.DisplayName);
        Assert.Equal(DesignPersistenceFixtureData.ScopeA, observedA.VersionLayout.Version.TenantId);

        Assert.Equal(DesignPersistenceFixtureData.ScopeB, observedB.Workflow.Definition.TenantId);
        Assert.Equal("Order processing scope B", observedB.Workflow.Definition.Name);
        Assert.Equal(DesignPersistenceFixtureData.ScopeB, observedB.Activity.Definition.TenantId);
        Assert.Equal("Send HTTP request scope B", observedB.Activity.Definition.DisplayName);
        Assert.Equal(DesignPersistenceFixtureData.ScopeB, observedB.VersionLayout.Version.TenantId);
    }

    [Fact]
    public async Task Foreign_point_reads_are_indistinguishable_from_missing_identities()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();

        var seeded = await SeedScopeAsync(fixture, DesignPersistenceFixtureData.ScopeA, "scope A");

        using var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeB);
        var services = scope.ServiceProvider;
        var workflowDefinitions = services.GetRequiredService<IWorkflowDefinitionStore>();
        var workflowVersions = services.GetRequiredService<IWorkflowDefinitionVersionStore>();
        var drafts = services.GetRequiredService<IWorkflowDefinitionDraftStore>();
        var versionLayouts = services.GetRequiredService<IWorkflowDefinitionVersionLayoutStore>();
        var activityDefinitions = services.GetRequiredService<IActivityDefinitionStore>();
        var activityVersions = services.GetRequiredService<IActivityDefinitionVersionStore>();

        Assert.Null(await workflowDefinitions.FindByIdAsync(DesignPersistenceFixtureData.WorkflowDefinitionId));
        Assert.Null(await workflowVersions.FindByIdAsync(seeded.PromotedVersionId));
        Assert.Null(await drafts.FindWithLayoutByIdAsync(DesignPersistenceFixtureData.WorkflowDraftId));
        Assert.Null(await versionLayouts.FindByVersionIdAsync(seeded.PromotedVersionId));
        Assert.Null(await activityDefinitions.FindAsync(new ActivityDefinitionFilter { Id = DesignPersistenceFixtureData.ActivityDefinitionId }));
        Assert.Null(await activityDefinitions.FindByIdOrActivityTypeKeyAsync(
            DesignPersistenceFixtureData.ActivityDefinitionId,
            DesignPersistenceFixtureData.ActivityDefinition().ActivityTypeKey));
        Assert.False(await activityDefinitions.ExistsByActivityTypeKeyAsync(DesignPersistenceFixtureData.ActivityDefinition().ActivityTypeKey));

        await AssertSameMissingOutcomeAsync(
            () => workflowDefinitions.GetAsync("missing-workflow-definition"),
            () => workflowDefinitions.GetAsync(DesignPersistenceFixtureData.WorkflowDefinitionId));
        await AssertSameMissingOutcomeAsync(
            () => activityVersions.GetAsync("missing-activity-version"),
            () => activityVersions.GetAsync(DesignPersistenceFixtureData.ActivityVersionId));
    }

    [Fact]
    public async Task Foreign_scope_point_writes_are_rejected_without_mutating_either_scope()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        await SeedScopeAsync(fixture, DesignPersistenceFixtureData.ScopeA, "scope A");

        var foreign = DesignPersistenceFixtureData.WorkflowDefinition(DesignPersistenceFixtureData.ScopeA);
        foreign.Name = "must not overwrite scope A";

        using (var scopeB = fixture.CreateScope(DesignPersistenceFixtureData.ScopeB))
        {
            var services = scopeB.ServiceProvider;
            var saver = services.GetRequiredService<ISaveWorkflowDefinitionCommand>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => saver.Execute(foreign));
            Assert.Null(await services.GetRequiredService<IWorkflowDefinitionStore>()
                .FindByIdAsync(DesignPersistenceFixtureData.WorkflowDefinitionId));

            var foreignActivity = DesignPersistenceFixtureData.ActivityDefinition(DesignPersistenceFixtureData.ScopeA);
            foreignActivity.Id = "foreign-activity-write";
            var foreignActivityVersion = DesignPersistenceFixtureData.ActivityVersion(
                id: "foreign-activity-write-v1",
                scope: DesignPersistenceFixtureData.ScopeA,
                definitionId: foreignActivity.Id);
            await Assert.ThrowsAsync<InvalidOperationException>(() => services.GetRequiredService<IAddActivityDefinitionCommand>().Execute(
                foreignActivity,
                foreignActivityVersion,
                CancellationToken.None));
            Assert.Null(await services.GetRequiredService<IActivityDefinitionStore>()
                .FindAsync(new ActivityDefinitionFilter { Id = foreignActivity.Id }));
        }

        using var scopeA = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        var persisted = await scopeA.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>()
            .GetAsync(DesignPersistenceFixtureData.WorkflowDefinitionId);
        Assert.Equal("Order processing scope A", persisted.Name);
        Assert.Null(await scopeA.ServiceProvider.GetRequiredService<IActivityDefinitionStore>()
            .FindAsync(new ActivityDefinitionFilter { Id = "foreign-activity-write" }));
    }

    [Fact]
    public async Task Duplicate_workflow_and_activity_identities_are_rejected_within_a_scope()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        await SeedScopeAsync(fixture, DesignPersistenceFixtureData.ScopeA, "scope A");

        using var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        var services = scope.ServiceProvider;
        var duplicateWorkflow = DesignPersistenceFixtureData.WorkflowDefinition();
        duplicateWorkflow.Name = "duplicate workflow";
        var duplicateDraft = DesignPersistenceFixtureData.WorkflowDraft(state: DesignPersistenceFixtureData.WorkflowState("duplicate-workflow-root"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => services.GetRequiredService<IAddWorkflowDefinitionCommand>().Execute(
            duplicateWorkflow,
            duplicateDraft,
            DesignPersistenceFixtureData.WorkflowDraftLayout(),
            CancellationToken.None));

        var duplicateActivityDefinition = DesignPersistenceFixtureData.ActivityDefinition();
        duplicateActivityDefinition.Id = "activity-http-request-duplicate";
        var duplicateActivityVersion = DesignPersistenceFixtureData.ActivityVersion(
            id: "activity-http-request-duplicate-v1",
            definitionId: duplicateActivityDefinition.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => services.GetRequiredService<IAddActivityDefinitionCommand>().Execute(
            duplicateActivityDefinition,
            duplicateActivityVersion,
            CancellationToken.None));

        var duplicateSemanticVersion = DesignPersistenceFixtureData.ActivityVersion(
            id: "activity-http-request-v1-duplicate");
        await Assert.ThrowsAsync<InvalidOperationException>(() => services.GetRequiredService<IAddCommand<ActivityDefinitionVersion>>().Add(
            duplicateSemanticVersion,
            CancellationToken.None));

        var workflow = await services.GetRequiredService<IWorkflowDefinitionStore>()
            .GetAsync(DesignPersistenceFixtureData.WorkflowDefinitionId);
        var activity = await services.GetRequiredService<IActivityDefinitionStore>()
            .GetAsync(DesignPersistenceFixtureData.ActivityDefinitionId);
        var versions = await services.GetRequiredService<IActivityDefinitionVersionStore>()
            .ListByDefinitionAsync(DesignPersistenceFixtureData.ActivityDefinitionId);
        Assert.Equal("Order processing scope A", workflow.Name);
        Assert.Equal("Send HTTP request scope A", activity.DisplayName);
        Assert.Single(versions, version => version.Version == "1.0.0");
    }

    [Fact]
    public async Task Reusable_activity_draft_rejects_a_stale_expected_revision_without_replacing_state_or_layout()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();

        using (var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA))
        {
            var services = scope.ServiceProvider;
            await services.GetRequiredService<ICreateActivityDefinitionCommand>().ExecuteAsync(
                DesignPersistenceFixtureData.ReusableActivityDefinition(),
                CancellationToken.None);

            var replacement = services.GetRequiredService<IReplaceActivityDraftCommand>();
            var committedState = DesignPersistenceFixtureData.ReusableActivityDraftState("committed");
            var committedLayout = DesignPersistenceFixtureData.ReusableActivityDraftLayout("committed-node");
            await replacement.ExecuteAsync(new(
                DesignPersistenceFixtureData.ReusableActivityDraftId,
                ExpectedRevision: 0,
                committedState,
                committedLayout));

            await Assert.ThrowsAsync<InvalidOperationException>(() => replacement.ExecuteAsync(new(
                DesignPersistenceFixtureData.ReusableActivityDraftId,
                ExpectedRevision: 0,
                DesignPersistenceFixtureData.ReusableActivityDraftState("stale"),
                DesignPersistenceFixtureData.ReusableActivityDraftLayout("stale-node"))));
        }

        using var readScope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        var servicesAfter = readScope.ServiceProvider;
        var draft = await servicesAfter.GetRequiredService<IActivityDefinitionDraftStore>()
            .FindAsync(DesignPersistenceFixtureData.ReusableActivityDraftId);
        var layout = await servicesAfter.GetRequiredService<IActivityDefinitionLayoutStore>()
            .FindDraftLayoutAsync(DesignPersistenceFixtureData.ReusableActivityDraftId);
        Assert.NotNull(draft);
        Assert.NotNull(layout);
        Assert.Equal(1, draft!.Revision);
        Assert.Equal(1, layout!.Revision);
        Assert.Equal("committed", draft.State.Options["label"]);
        Assert.Equal("committed-node", Assert.Single(layout.Records).NodeId);
    }

    [Fact]
    public async Task Workflow_draft_updates_preserve_the_intentional_last_writer_wins_policy()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        await SeedScopeAsync(fixture, DesignPersistenceFixtureData.ScopeA, "scope A");

        using (var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA))
        {
            var update = scope.ServiceProvider.GetRequiredService<IUpdateDraftCommand>();
            await update.Execute(new(
                DesignPersistenceFixtureData.WorkflowDraftId,
                DesignPersistenceFixtureData.WorkflowState("first-writer"),
                [new DesignMetadataRecord("first-writer", 1, 2, 3, 4)]));
            await update.Execute(new(
                DesignPersistenceFixtureData.WorkflowDraftId,
                DesignPersistenceFixtureData.WorkflowState("last-writer"),
                [new DesignMetadataRecord("last-writer", 5, 6, 7, 8)]));
        }

        using var readScope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        var persisted = await readScope.ServiceProvider.GetRequiredService<IWorkflowDefinitionDraftStore>()
            .FindWithLayoutByIdAsync(DesignPersistenceFixtureData.WorkflowDraftId);
        Assert.NotNull(persisted);
        Assert.Equal("last-writer", persisted!.Draft.State.RootActivity!.NodeId);
        Assert.Equal("last-writer", Assert.Single(persisted.Layout).NodeId);
    }

    [Fact]
    public async Task Scope_bound_point_read_snapshots_survive_restart()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();

        var scopeA = await SeedScopeAsync(fixture, DesignPersistenceFixtureData.ScopeA, "scope A");
        var scopeB = await SeedScopeAsync(fixture, DesignPersistenceFixtureData.ScopeB, "scope B");
        var beforeA = await ReadScopeSnapshotAsync(fixture, DesignPersistenceFixtureData.ScopeA, scopeA.PromotedVersionId);
        var beforeB = await ReadScopeSnapshotAsync(fixture, DesignPersistenceFixtureData.ScopeB, scopeB.PromotedVersionId);

        await fixture.RestartAsync();

        var afterA = await ReadScopeSnapshotAsync(fixture, DesignPersistenceFixtureData.ScopeA, scopeA.PromotedVersionId);
        var afterB = await ReadScopeSnapshotAsync(fixture, DesignPersistenceFixtureData.ScopeB, scopeB.PromotedVersionId);
        Assert.Equal(DesignPersistenceFixtureData.ResultHash(beforeA), DesignPersistenceFixtureData.ResultHash(afterA));
        Assert.Equal(DesignPersistenceFixtureData.ResultHash(beforeB), DesignPersistenceFixtureData.ResultHash(afterB));
        Assert.NotEqual(DesignPersistenceFixtureData.ResultHash(afterA), DesignPersistenceFixtureData.ResultHash(afterB));
    }

    private static async Task<SeededScope> SeedScopeAsync(
        IDesignPersistenceContractFixture fixture,
        string storageScope,
        string marker)
    {
        using var scope = fixture.CreateScope(storageScope);
        var services = scope.ServiceProvider;
        var activityDefinition = DesignPersistenceFixtureData.ActivityDefinition(storageScope);
        activityDefinition.DisplayName = $"Send HTTP request {marker}";
        var workflowDefinition = DesignPersistenceFixtureData.WorkflowDefinition(storageScope);
        workflowDefinition.Name = $"Order processing {marker}";

        await services.GetRequiredService<IAddActivityDefinitionCommand>().Execute(
            activityDefinition,
            DesignPersistenceFixtureData.ActivityVersion(scope: storageScope),
            CancellationToken.None);
        await services.GetRequiredService<IAddWorkflowDefinitionCommand>().Execute(
            workflowDefinition,
            DesignPersistenceFixtureData.WorkflowDraft(storageScope, DesignPersistenceFixtureData.WorkflowState()),
            DesignPersistenceFixtureData.WorkflowDraftLayout(),
            CancellationToken.None);
        var versionId = await services.GetRequiredService<IPromoteDraftToVersionCommand>()
            .Execute(DesignPersistenceFixtureData.WorkflowDraftId);
        return new(versionId);
    }

    private static async Task<ScopeSnapshot> ReadScopeSnapshotAsync(
        IDesignPersistenceContractFixture fixture,
        string storageScope,
        string workflowVersionId)
    {
        using var scope = fixture.CreateScope(storageScope);
        var services = scope.ServiceProvider;
        var workflowDefinition = await services.GetRequiredService<IWorkflowDefinitionStore>()
            .GetAsync(DesignPersistenceFixtureData.WorkflowDefinitionId);
        var draft = await services.GetRequiredService<IWorkflowDefinitionDraftStore>()
            .FindWithLayoutByIdAsync(DesignPersistenceFixtureData.WorkflowDraftId);
        var workflowVersion = await services.GetRequiredService<IWorkflowDefinitionVersionStore>()
            .GetAsync(workflowVersionId);
        var versionLayout = await services.GetRequiredService<IWorkflowDefinitionVersionLayoutStore>()
            .FindByVersionIdAsync(workflowVersionId);
        var activityDefinition = await services.GetRequiredService<IActivityDefinitionStore>()
            .GetAsync(DesignPersistenceFixtureData.ActivityDefinitionId);
        var activityVersion = await services.GetRequiredService<IActivityDefinitionVersionStore>()
            .GetAsync(DesignPersistenceFixtureData.ActivityVersionId);

        Assert.NotNull(draft);
        Assert.NotNull(versionLayout);
        return new(
            WorkflowDesignContractSuite.CanonicalSnapshot(workflowDefinition, draft!.Draft, draft.Layout),
            ActivityDesignContractSuite.CanonicalSnapshot(activityDefinition, activityVersion),
            WorkflowDesignContractSuite.CanonicalSnapshot(workflowVersion, versionLayout!));
    }

    private static async Task AssertSameMissingOutcomeAsync(Func<Task> missing, Func<Task> foreign)
    {
        var missingException = await Record.ExceptionAsync(missing);
        var foreignException = await Record.ExceptionAsync(foreign);

        Assert.NotNull(missingException);
        Assert.NotNull(foreignException);
        Assert.Equal(missingException.GetType(), foreignException.GetType());
    }

    private sealed record SeededScope(string PromotedVersionId);

    private sealed record ScopeSnapshot(
        WorkflowDesignContractSuite.WorkflowDraftSnapshot Workflow,
        ActivityDesignContractSuite.ActivitySnapshot Activity,
        WorkflowDesignContractSuite.WorkflowVersionSnapshot VersionLayout);
}
