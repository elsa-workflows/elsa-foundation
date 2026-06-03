using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit.DraftMutationCommandTests;

/// <summary>
/// Unit C FR-021 + FR-023: the <c>WorkflowDefinitionDraftValidation</c> sibling is upserted in
/// the same transaction as the Draft's state. Errors collected by validators on
/// <c>OnDraftValidating</c> persist; subsequent mutations rewrite the sibling's <c>Errors</c>
/// wholesale (delete-and-re-add). The promotion gate (Unit D's
/// <c>IPromoteDraftToVersionCommand</c>) reads this sibling — without persistence here, the
/// gate has nothing to gate against.
/// </summary>
public sealed class ValidationSiblingPersistenceTests
{
    [Fact]
    public async Task CreateDraft_persists_an_empty_validation_sibling()
    {
        using var host = WorkflowsDesignTestHost.Create();

        var draftId = await CreateDraft(host, "wf-1");

        using var ctx = host.CreateContext();
        var sibling = await ctx.WorkflowDefinitionDraftValidations
            .FirstOrDefaultAsync(v => v.WorkflowDefinitionDraftId == draftId);

        Assert.NotNull(sibling);
        Assert.Empty(sibling!.Errors);
    }

    [Fact]
    public async Task Validator_contribution_persists_to_the_validation_sibling()
    {
        using var host = WorkflowsDesignTestHost.Create();

        // Validator stub — every OnDraftValidating gets one error contributed.
        var stubbedError = new ValidationError(
            Path: "$workflow",
            Type: "Graph/StartActivity",
            Message: "No start activity"
        );
        host.EventPublisher.OnPublish = evt =>
        {
            if (evt is OnDraftValidating validating)
                validating.Errors.Add(stubbedError);
        };

        var draftId = await CreateDraft(host, "wf-1");

        using var ctx = host.CreateContext();
        var sibling = await ctx.WorkflowDefinitionDraftValidations
            .FirstAsync(v => v.WorkflowDefinitionDraftId == draftId);

        Assert.Single(sibling.Errors);
        Assert.Equal(stubbedError, sibling.Errors[0]);
    }

    [Fact]
    public async Task Subsequent_mutation_rewrites_the_sibling_wholesale()
    {
        using var host = WorkflowsDesignTestHost.Create();

        // First validator pass yields an error.
        var initialError = new ValidationError("$workflow", "Graph/StartActivity", "No start activity");
        host.EventPublisher.OnPublish = evt =>
        {
            if (evt is OnDraftValidating validating)
                validating.Errors.Add(initialError);
        };

        var draftId = await CreateDraft(host, "wf-1");

        // Subsequent pass: no error contributed. The sibling's Errors must be rewritten to empty
        // — FR-023 delete-and-re-add (not appended to the existing list).
        host.EventPublisher.OnPublish = null;

        // A subsequent coarse update (FR-001 full-state) drives the next validation pass.
        await Update(host, draftId, State(activities: [Node("node-1")]));

        using var ctx = host.CreateContext();
        var sibling = await ctx.WorkflowDefinitionDraftValidations
            .FirstAsync(v => v.WorkflowDefinitionDraftId == draftId);

        Assert.Empty(sibling.Errors);
    }

    [Fact]
    public async Task OnDraftValidated_carries_the_persisted_error_set()
    {
        using var host = WorkflowsDesignTestHost.Create();

        var stubbedError = new ValidationError("$workflow", "Graph/StartActivity", "No start activity");
        host.EventPublisher.OnPublish = evt =>
        {
            if (evt is OnDraftValidating validating)
                validating.Errors.Add(stubbedError);
        };

        var draftId = await CreateDraft(host, "wf-1");

        var validated = host.EventPublisher.LastOf<OnDraftValidated>();
        Assert.NotNull(validated);
        Assert.True(validated!.HasErrors);
        Assert.Single(validated.Errors);
        Assert.Equal(stubbedError, validated.Errors[0]);
    }

    [Fact]
    public async Task Sibling_uses_cascade_delete_when_draft_is_removed()
    {
        // Sanity check the FR-029 cascade. Phase 10 lands IDiscardDraftCommand; here we
        // simulate the deletion directly via the DbContext and assert the cascade fires.
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await CreateDraft(host, "wf-1");

        using (var ctx = host.CreateContext())
        {
            var draft = await ctx.WorkflowDefinitionDrafts.FirstAsync(d => d.Id == draftId);
            ctx.WorkflowDefinitionDrafts.Remove(draft);
            await ctx.SaveChangesAsync();
        }

        using var verifyCtx = host.CreateContext();
        var sibling = await verifyCtx.WorkflowDefinitionDraftValidations
            .FirstOrDefaultAsync(v => v.WorkflowDefinitionDraftId == draftId);

        Assert.Null(sibling);
    }

    // --- helpers ---

    private static async Task<string> CreateDraft(WorkflowsDesignTestHost host, string workflowDefinitionId)
    {
        await host.EnsureDefinition(workflowDefinitionId);
        using var scope = host.Services.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<ICreateDraftCommand>();
        return await command.Execute(workflowDefinitionId);
    }
}
