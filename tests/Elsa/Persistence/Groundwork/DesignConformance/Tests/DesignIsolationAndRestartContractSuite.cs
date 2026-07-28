using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
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
    protected abstract DesignPersistenceContractProfile ContractProfile { get; }

    [SkippableFact]
    public async Task Same_point_identities_resolve_only_their_own_scope()
    {
        SkipIfNotApplicable(DesignPersistenceContractScenario.IsolationSamePointIdentities);
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();

        var scopeA = await SeedScopeAsync(fixture, DesignPersistenceFixtureData.ScopeA, "scope A");
        var scopeB = await SeedScopeAsync(fixture, DesignPersistenceFixtureData.ScopeB, "scope B");
        await AssertSubmittedAggregateOwnsScopeAsync(fixture, DesignPersistenceFixtureData.ScopeA);

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

    [SkippableFact]
    public async Task Foreign_point_reads_are_indistinguishable_from_missing_identities()
    {
        SkipIfNotApplicable(DesignPersistenceContractScenario.IsolationForeignPointReads);
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
            "missing-workflow-definition",
            () => workflowDefinitions.GetAsync("missing-workflow-definition"),
            DesignPersistenceFixtureData.WorkflowDefinitionId,
            () => workflowDefinitions.GetAsync(DesignPersistenceFixtureData.WorkflowDefinitionId));
        await AssertSameMissingOutcomeAsync(
            "missing-activity-version",
            () => activityVersions.GetAsync("missing-activity-version"),
            DesignPersistenceFixtureData.ActivityVersionId,
            () => activityVersions.GetAsync(DesignPersistenceFixtureData.ActivityVersionId));
    }

    [SkippableFact]
    public async Task Foreign_scope_point_writes_are_rejected_without_mutating_either_scope()
    {
        SkipIfNotApplicable(DesignPersistenceContractScenario.IsolationForeignScopeWrites);
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        await SeedScopeAsync(fixture, DesignPersistenceFixtureData.ScopeA, "scope A");

        var foreign = DesignPersistenceFixtureData.WorkflowDefinition(DesignPersistenceFixtureData.ScopeA);
        foreign.Name = "must not overwrite scope A";

        using (var scopeB = fixture.CreateScope(DesignPersistenceFixtureData.ScopeB))
        {
            var services = scopeB.ServiceProvider;
            var saver = services.GetRequiredService<ISaveWorkflowDefinitionCommand>();
            await AssertNonCancellationFailureAsync(() => saver.Execute(
                DesignPersistenceFixtureData.OperationKey("isolation-foreign-workflow-save"),
                foreign));
            Assert.Null(await services.GetRequiredService<IWorkflowDefinitionStore>()
                .FindByIdAsync(DesignPersistenceFixtureData.WorkflowDefinitionId));

            var foreignActivity = DesignPersistenceFixtureData.ActivityDefinition(DesignPersistenceFixtureData.ScopeA);
            foreignActivity.Id = "foreign-activity-write";
            var foreignActivityVersion = DesignPersistenceFixtureData.ActivityVersion(
                id: "foreign-activity-write-v1",
                scope: DesignPersistenceFixtureData.ScopeA,
                definitionId: foreignActivity.Id);
            await AssertNonCancellationFailureAsync(() => services.GetRequiredService<IAddActivityDefinitionCommand>().Execute(
                DesignPersistenceFixtureData.OperationKey("isolation-foreign-activity-create"),
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

    [SkippableFact]
    public async Task Duplicate_workflow_and_activity_identities_are_rejected_within_a_scope()
    {
        SkipIfNotApplicable(DesignPersistenceContractScenario.IsolationDuplicateIdentities);
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        var seeded = await SeedScopeAsync(fixture, DesignPersistenceFixtureData.ScopeA, "scope A");
        var before = await ReadScopeSnapshotAsync(fixture, DesignPersistenceFixtureData.ScopeA, seeded.PromotedVersionId);
        var beforeHash = DesignPersistenceFixtureData.ResultHash(before);

        bool duplicateWorkflowAccepted;
        using (var duplicateWorkflowScope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA))
        {
            var duplicateWorkflow = DesignPersistenceFixtureData.WorkflowDefinition();
            duplicateWorkflow.Name = "duplicate workflow";
            var duplicateDraft = DesignPersistenceFixtureData.WorkflowDraft(state: DesignPersistenceFixtureData.WorkflowState("duplicate-workflow-root"));
            duplicateWorkflowAccepted = await WasDuplicateAcceptedAsync(() => duplicateWorkflowScope.ServiceProvider.GetRequiredService<IAddWorkflowDefinitionCommand>().Execute(
                DesignPersistenceFixtureData.OperationKey("isolation-duplicate-workflow"),
                duplicateWorkflow,
                duplicateDraft,
                DesignPersistenceFixtureData.WorkflowDraftLayout(),
                CancellationToken.None));
        }

        bool duplicateActivityAccepted;
        using (var duplicateActivityScope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA))
        {
            var duplicateActivityDefinition = DesignPersistenceFixtureData.ActivityDefinition();
            duplicateActivityDefinition.Id = "activity-http-request-duplicate";
            var duplicateActivityVersion = DesignPersistenceFixtureData.ActivityVersion(
                id: "activity-http-request-duplicate-v1",
                definitionId: duplicateActivityDefinition.Id);
            duplicateActivityAccepted = await WasDuplicateAcceptedAsync(() => duplicateActivityScope.ServiceProvider.GetRequiredService<IAddActivityDefinitionCommand>().Execute(
                DesignPersistenceFixtureData.OperationKey("isolation-duplicate-activity"),
                duplicateActivityDefinition,
                duplicateActivityVersion,
                CancellationToken.None));
        }

        bool duplicateSemanticVersionAccepted;
        using (var duplicateSemanticVersionScope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA))
        {
            var duplicateSemanticVersion = DesignPersistenceFixtureData.ActivityVersion(
                id: "activity-http-request-v1-duplicate");
            duplicateSemanticVersionAccepted = await WasDuplicateAcceptedAsync(() => duplicateSemanticVersionScope.ServiceProvider
                .GetRequiredService<IAddActivityDefinitionVersionCommand>()
                .Execute(
                    DesignPersistenceFixtureData.OperationKey("isolation-duplicate-activity-version"),
                    duplicateSemanticVersion,
                    CancellationToken.None));
        }

        using var readScope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        var readServices = readScope.ServiceProvider;
        var workflow = await readServices.GetRequiredService<IWorkflowDefinitionStore>()
            .GetAsync(DesignPersistenceFixtureData.WorkflowDefinitionId);
        var activity = await readServices.GetRequiredService<IActivityDefinitionStore>()
            .GetAsync(DesignPersistenceFixtureData.ActivityDefinitionId);
        var duplicateActivity = await readServices.GetRequiredService<IActivityDefinitionStore>()
            .FindAsync(new ActivityDefinitionFilter { Id = "activity-http-request-duplicate" });
        var versionStore = readServices.GetRequiredService<IActivityDefinitionVersionStore>();
        var versions = await versionStore
            .ListByDefinitionAsync(DesignPersistenceFixtureData.ActivityDefinitionId);
        var allVersions = await versionStore.ListAsync();

        if (duplicateWorkflowAccepted || duplicateActivityAccepted || duplicateSemanticVersionAccepted)
        {
            Assert.True(
                duplicateWorkflowAccepted && workflow.Name == "duplicate workflow"
                || duplicateActivityAccepted && duplicateActivity is not null
                || duplicateSemanticVersionAccepted && versions.Any(version => version.Id == "activity-http-request-v1-duplicate"),
                "A duplicate write was acknowledged but none of its duplicate identities became observable.");
            throw new DesignDuplicateIdentityAcceptedException();
        }

        var after = await ReadScopeSnapshotAsync(fixture, DesignPersistenceFixtureData.ScopeA, seeded.PromotedVersionId);
        Assert.Equal(beforeHash, DesignPersistenceFixtureData.ResultHash(after));
        Assert.Null(duplicateActivity);
        Assert.DoesNotContain(allVersions, version => version.Id == "activity-http-request-duplicate-v1");
        Assert.DoesNotContain(allVersions, version => version.Id == "activity-http-request-v1-duplicate");
        Assert.Equal("Order processing scope A", workflow.Name);
        Assert.Equal("Send HTTP request scope A", activity.DisplayName);
        Assert.Single(versions, version => version.Version == "1.0.0");
    }

    [SkippableFact]
    public async Task Reusable_activity_draft_rejects_a_stale_expected_revision_without_replacing_state_or_layout()
    {
        SkipIfNotApplicable(DesignPersistenceContractScenario.IsolationReusableActivityDraftOcc);
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

            await AssertNonCancellationFailureAsync(() => replacement.ExecuteAsync(new(
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

    [SkippableFact]
    public async Task Workflow_draft_updates_preserve_the_intentional_last_writer_wins_policy()
    {
        SkipIfNotApplicable(DesignPersistenceContractScenario.IsolationWorkflowDraftLastWriterWins);
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        await SeedScopeAsync(fixture, DesignPersistenceFixtureData.ScopeA, "scope A");

        using (var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA))
        {
            var update = scope.ServiceProvider.GetRequiredService<IUpdateDraftCommand>();
            await update.Execute(
                DesignPersistenceFixtureData.OperationKey("isolation-workflow-first-writer"),
                new(
                DesignPersistenceFixtureData.WorkflowDraftId,
                DesignPersistenceFixtureData.WorkflowState("first-writer"),
                [new DesignMetadataRecord("first-writer", 1, 2, 3, 4)]));
            await update.Execute(
                DesignPersistenceFixtureData.OperationKey("isolation-workflow-last-writer"),
                new(
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

    [SkippableFact]
    public async Task Single_scope_point_read_snapshot_survives_restart()
    {
        SkipIfNotApplicable(DesignPersistenceContractScenario.IsolationSingleScopeRestart);
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();

        var scopeA = await SeedScopeAsync(fixture, DesignPersistenceFixtureData.ScopeA, "scope A");
        var beforeA = await ReadScopeSnapshotAsync(fixture, DesignPersistenceFixtureData.ScopeA, scopeA.PromotedVersionId);

        await fixture.RestartAsync();

        var afterA = await ReadScopeSnapshotAsync(fixture, DesignPersistenceFixtureData.ScopeA, scopeA.PromotedVersionId);
        Assert.Equal(DesignPersistenceFixtureData.ResultHash(beforeA), DesignPersistenceFixtureData.ResultHash(afterA));
    }

    [SkippableFact]
    public async Task Cross_scope_same_identity_point_read_snapshots_survive_restart()
    {
        SkipIfNotApplicable(DesignPersistenceContractScenario.IsolationCrossScopeSameIdentityRestart);
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
            DesignPersistenceFixtureData.OperationKey($"seed:{storageScope}:activity"),
            activityDefinition,
            DesignPersistenceFixtureData.ActivityVersion(scope: storageScope),
            CancellationToken.None);
        await services.GetRequiredService<IAddWorkflowDefinitionCommand>().Execute(
            DesignPersistenceFixtureData.OperationKey($"seed:{storageScope}:workflow"),
            workflowDefinition,
            DesignPersistenceFixtureData.WorkflowDraft(storageScope, DesignPersistenceFixtureData.WorkflowState()),
            DesignPersistenceFixtureData.WorkflowDraftLayout(),
            CancellationToken.None);
        var versionId = await services.GetRequiredService<IPromoteDraftToVersionCommand>()
            .Execute(
                DesignPersistenceFixtureData.OperationKey($"seed:{storageScope}:workflow-promote"),
                DesignPersistenceFixtureData.WorkflowDraftId);
        return new(versionId);
    }

    private static async Task AssertSubmittedAggregateOwnsScopeAsync(
        IDesignPersistenceContractFixture fixture,
        string storageScope)
    {
        SubmittedWorkflowDefinition submitted;
        using (var writeScope = fixture.CreateScope(storageScope))
        {
            submitted = await writeScope.ServiceProvider.GetRequiredService<ISubmitWorkflowDefinitionCommand>().Execute(
                DesignPersistenceFixtureData.OperationKey($"scope-submission:{storageScope}"),
                "Scoped submitted workflow",
                "Proves every submitted aggregate member owns the active storage scope.",
                DesignPersistenceFixtureData.WorkflowState());
        }

        using var readScope = fixture.CreateScope(storageScope);
        var services = readScope.ServiceProvider;
        var definition = await services.GetRequiredService<IWorkflowDefinitionStore>()
            .GetAsync(submitted.DefinitionId);
        var draft = await services.GetRequiredService<IWorkflowDefinitionDraftStore>()
            .FindWithLayoutByIdAsync(submitted.DraftId);
        var version = await services.GetRequiredService<IWorkflowDefinitionVersionStore>()
            .GetAsync(submitted.VersionId);
        var layout = await services.GetRequiredService<IWorkflowDefinitionVersionLayoutStore>()
            .FindByVersionIdAsync(submitted.VersionId);

        Assert.NotNull(draft);
        Assert.NotNull(layout);
        Assert.Equal(storageScope, definition.TenantId);
        Assert.Equal(storageScope, draft!.Draft.TenantId);
        Assert.Equal(storageScope, version.TenantId);
        Assert.Equal(storageScope, layout!.TenantId);
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

    private static async Task AssertNonCancellationFailureAsync(Func<Task> operation)
    {
        var exception = await Record.ExceptionAsync(operation);

        Assert.NotNull(exception);
        Assert.False(exception is OperationCanceledException, "A conflict or rejected write must not be reported as cancellation.");
    }

    private static async Task<bool> WasDuplicateAcceptedAsync(Func<Task> operation)
    {
        var exception = await Record.ExceptionAsync(operation);
        if (exception is null)
            return true;

        Assert.False(exception is OperationCanceledException, "A duplicate rejection must not be reported as cancellation.");
        return false;
    }

    private static async Task AssertSameMissingOutcomeAsync(
        string missingIdentity,
        Func<Task> missing,
        string foreignIdentity,
        Func<Task> foreign)
    {
        var missingException = await Record.ExceptionAsync(missing);
        var foreignException = await Record.ExceptionAsync(foreign);

        Assert.NotNull(missingException);
        Assert.NotNull(foreignException);
        Assert.Equal(
            ExceptionClassification(missingException, missingIdentity),
            ExceptionClassification(foreignException, foreignIdentity));
    }

    private static string ExceptionClassification(Exception exception, string requestedIdentity)
    {
        var values = exception
            .GetType()
            .GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            .Where(property =>
                property.DeclaringType != typeof(Exception) &&
                property.GetMethod is not null &&
                property.GetIndexParameters().Length == 0)
            .Select(property => (property.Name, Value: property.GetValue(exception)))
            .Where(item => item.Value is null || item.Value is string || item.Value.GetType().IsValueType)
            .Select(item => $"{item.Name}={Normalize(item.Value?.ToString() ?? "<null>", requestedIdentity)}")
            .Order(StringComparer.Ordinal)
            .Prepend($"message={Normalize(exception.Message, requestedIdentity)}")
            .Prepend($"hresult={exception.HResult}")
            .Prepend($"type={exception.GetType().FullName}")
            .ToArray();
        var classification = string.Join("|", values);

        Assert.DoesNotContain(DesignPersistenceFixtureData.ScopeA, classification, StringComparison.Ordinal);
        Assert.DoesNotContain(DesignPersistenceFixtureData.ScopeB, classification, StringComparison.Ordinal);
        return classification;
    }

    private static string Normalize(string value, string requestedIdentity) =>
        value.Replace(requestedIdentity, "<requested-identity>", StringComparison.Ordinal);

    private void SkipIfNotApplicable(DesignPersistenceContractScenario scenario)
    {
        var applicability = ContractProfile.GetApplicability(scenario);
        Xunit.Skip.If(!applicability.IsApplicable, applicability.NotApplicableReason!);
    }

    private sealed record SeededScope(string PromotedVersionId);

    private sealed record ScopeSnapshot(
        WorkflowDesignContractSuite.WorkflowDraftSnapshot Workflow,
        ActivityDesignContractSuite.ActivitySnapshot Activity,
        WorkflowDesignContractSuite.WorkflowVersionSnapshot VersionLayout);
}
