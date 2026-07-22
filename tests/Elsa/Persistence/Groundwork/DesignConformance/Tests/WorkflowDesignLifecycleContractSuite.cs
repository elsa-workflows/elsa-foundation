using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.Tests;

/// <summary>
/// Provider-neutral conversion (T070) of still-valid workflow-design lifecycle objectives that had no
/// EF-independent home in the T021–T039 contract suites: the metadata-update (PATCH) path, its
/// aggregate isolation, the promotion validation gate's rejection outcome, and the permanent-delete
/// guard for an active definition. Every scenario drives only Elsa core commands/stores through the
/// composed provider boundary; a concrete provider fixture supplies durable materialization through
/// <see cref="CreateFixtureAsync"/>. This suite references no provider SDK, so it stays valid after the
/// EF design store is removed (T072).
/// </summary>
public abstract class WorkflowDesignLifecycleContractSuite
{
    protected abstract Task<IDesignPersistenceContractFixture> CreateFixtureAsync(CancellationToken cancellationToken = default);

    [Fact]
    public async Task Metadata_update_persists_name_and_description_across_restart()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();

        string definitionId;
        using (var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA))
        {
            definitionId = (await SubmitAsync(scope.ServiceProvider, "Original", "Original description")).DefinitionId;
            await UpdateMetadataAsync(scope.ServiceProvider, definitionId, name: "Renamed", description: "Updated description");
        }

        await fixture.RestartAsync();

        using var readScope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        var definition = await readScope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>().GetAsync(definitionId);
        Assert.Equal("Renamed", definition.Name);
        Assert.Equal("Updated description", definition.Description);
    }

    [Fact]
    public async Task Metadata_partial_update_leaves_the_unspecified_field_untouched()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();

        using var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        var services = scope.ServiceProvider;
        var definitionId = (await SubmitAsync(services, "Keep", "Keep description")).DefinitionId;

        // Only the description is provided; the name must be preserved.
        await UpdateMetadataAsync(services, definitionId, name: null, description: "Only description changed");
        var afterDescriptionOnly = await services.GetRequiredService<IWorkflowDefinitionStore>().GetAsync(definitionId);
        Assert.Equal("Keep", afterDescriptionOnly.Name);
        Assert.Equal("Only description changed", afterDescriptionOnly.Description);

        // Now only the name is provided; the description must be preserved.
        await UpdateMetadataAsync(services, definitionId, name: "Renamed", description: null);
        var afterNameOnly = await services.GetRequiredService<IWorkflowDefinitionStore>().GetAsync(definitionId);
        Assert.Equal("Renamed", afterNameOnly.Name);
        Assert.Equal("Only description changed", afterNameOnly.Description);
    }

    [Fact]
    public async Task Metadata_update_bumps_last_modified_and_does_not_touch_versions_or_draft()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();

        using var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        var services = scope.ServiceProvider;
        var submitted = await SubmitAsync(services, "Unaffected", "desc");

        var versions = services.GetRequiredService<IWorkflowDefinitionVersionStore>();
        var drafts = services.GetRequiredService<IWorkflowDefinitionDraftStore>();
        var definitions = services.GetRequiredService<IWorkflowDefinitionStore>();

        var versionStateBefore = DesignPersistenceFixtureData.ResultHash((await versions.GetAsync(submitted.VersionId)).State);
        var draftStateBefore = DesignPersistenceFixtureData.ResultHash((await drafts.FindWithLayoutByIdAsync(submitted.DraftId))!.Draft.State);
        var modifiedBefore = (await definitions.GetAsync(submitted.DefinitionId)).LastModifiedAt;

        await UpdateMetadataAsync(services, submitted.DefinitionId, name: "Unaffected v2", description: "desc v2");

        var versionStateAfter = DesignPersistenceFixtureData.ResultHash((await versions.GetAsync(submitted.VersionId)).State);
        var draftStateAfter = DesignPersistenceFixtureData.ResultHash((await drafts.FindWithLayoutByIdAsync(submitted.DraftId))!.Draft.State);
        var modifiedAfter = (await definitions.GetAsync(submitted.DefinitionId)).LastModifiedAt;

        Assert.Equal(versionStateBefore, versionStateAfter);
        Assert.Equal(draftStateBefore, draftStateAfter);
        Assert.True(modifiedAfter >= modifiedBefore);
        Assert.Single(await versions.ListByDefinitionAsync(submitted.DefinitionId));
    }

    [Fact]
    public async Task Promotion_is_refused_when_the_draft_state_produces_validation_errors()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();

        using var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        var services = scope.ServiceProvider;

        // A draft whose state has no root activity is a baseline validation error, so the FR-024
        // promotion gate must refuse to promote it — the public rejection outcome.
        await services.GetRequiredService<IAddWorkflowDefinitionCommand>().Execute(
            DesignPersistenceFixtureData.OperationKey("promotion-gate-create"),
            DesignPersistenceFixtureData.WorkflowDefinition(),
            DesignPersistenceFixtureData.WorkflowDraft(state: WorkflowDefinitionState.Empty),
            DesignPersistenceFixtureData.WorkflowDraftLayout(),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<DraftHasValidationErrorsException>(() =>
            services.GetRequiredService<IPromoteDraftToVersionCommand>().Execute(
                DesignPersistenceFixtureData.OperationKey("promotion-gate-promote"),
                DesignPersistenceFixtureData.WorkflowDraftId));

        Assert.Equal(DesignPersistenceFixtureData.WorkflowDraftId, exception.DraftId);
        Assert.True(exception.ErrorCount > 0);

        // Issue #927: the exception carries the full derived error list (not just the count) so the
        // Promote endpoint can enrich the 409 ProblemDetails with the actual violations.
        Assert.Equal(exception.ErrorCount, exception.Errors.Count);
        Assert.Contains(exception.Errors, error => error.Type == "RootActivity/Missing");

        // The gate did not create a version.
        Assert.Empty(await services.GetRequiredService<IWorkflowDefinitionVersionStore>()
            .ListByDefinitionAsync(DesignPersistenceFixtureData.WorkflowDefinitionId));
    }

    [Fact]
    public async Task Permanent_delete_is_refused_for_an_active_definition()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();

        using var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        var services = scope.ServiceProvider;
        var submitted = await SubmitAsync(services, "Keep active", null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            services.GetRequiredService<IDeleteWorkflowDefinitionPermanentlyCommand>().Execute(
                DesignPersistenceFixtureData.OperationKey("permanent-delete-active"),
                submitted.DefinitionId));

        Assert.NotNull(await services.GetRequiredService<IWorkflowDefinitionStore>().FindByIdAsync(submitted.DefinitionId));
    }

    private static async Task<Workflows.Design.Persistence.Core.Models.SubmittedWorkflowDefinition> SubmitAsync(
        IServiceProvider services,
        string name,
        string? description)
    {
        await services.GetRequiredService<IAddActivityDefinitionCommand>().Execute(
            DesignPersistenceFixtureData.OperationKey($"lifecycle-prerequisite-activity:{name}"),
            DesignPersistenceFixtureData.ActivityDefinition(),
            DesignPersistenceFixtureData.ActivityVersion(),
            CancellationToken.None);

        return await services.GetRequiredService<ISubmitWorkflowDefinitionCommand>().Execute(
            DesignPersistenceFixtureData.OperationKey($"lifecycle-submit:{name}"),
            name,
            description,
            DesignPersistenceFixtureData.WorkflowState());
    }

    // Mirrors the PATCH handler: load via the store, apply the partial update, persist via the save command.
    private static async Task UpdateMetadataAsync(
        IServiceProvider services,
        string definitionId,
        string? name,
        string? description)
    {
        var definition = await services.GetRequiredService<IWorkflowDefinitionStore>().GetAsync(definitionId);
        if (name is not null)
            definition.Name = name.Trim();
        if (description is not null)
            definition.Description = description;

        await services.GetRequiredService<ISaveWorkflowDefinitionCommand>().Execute(
            DesignPersistenceFixtureData.OperationKey($"lifecycle-metadata-save:{definitionId}:{name}:{description}"),
            definition);
    }
}
