using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// #1659: the preflight plan's target-slot owner is a prediction of the activation authority's <c>ForeignSource</c>
/// refusal, and <see cref="WorkflowPublicationPreflightPlan.CanActivate"/> combines it with the trigger verdict.
/// </summary>
/// <remarks>
/// Each case asks the real authority what it would do with a publishing activation, so the preflight predicate and the
/// enforcement point cannot drift apart unnoticed: under-reporting brings back the late refusal after writes, and
/// over-reporting would block publishes activation accepts.
/// </remarks>
public sealed class WorkflowPublicationPreflightPlanTests
{
    private const string DefinitionId = "definition-1";
    private const string SlotName = "default";
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly WorkflowActivationSource ImportOwner = WorkflowActivationSource.ArtifactReconciliation("mounted-artifacts");
    private static readonly PublicationTriggerConflict Clash = new(
        "publication-blue", "blue", "Http", "get:/orders", PublicationTriggerCardinality.Exclusive, "claim-blue");

    private readonly InMemoryWorkflowActivationAuthority _authority = new();

    public enum SlotState
    {
        Absent,
        Publishing,
        Imported,
        Vacated
    }

    [Theory]
    [InlineData(SlotState.Absent, false)]
    [InlineData(SlotState.Publishing, false)]
    [InlineData(SlotState.Imported, true)]
    [InlineData(SlotState.Vacated, false)]
    public async Task The_plan_names_a_target_slot_owner_exactly_when_the_authority_refuses_publishing(SlotState state, bool refused)
    {
        await ArrangeAsync(state);
        var slot = await _authority.FindAsync(DefinitionId, SlotName);
        var plan = Plan(slot, new PublicationPreflightResult(CanActivate: true, [], []));

        var attempt = await _authority.TryActivateAsync(new WorkflowActivationSlotRequest(
            DefinitionId, SlotName, "publication-candidate", PublicationActivator.Source, slot?.Revision ?? 0, Now));

        Assert.Equal(refused, attempt.Conflict == WorkflowActivationConflict.ForeignSource);
        Assert.Equal(refused ? slot!.Source : null, plan.TargetSlotOwner);
        Assert.Equal(!refused, plan.CanActivate);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public async Task The_plan_can_activate_only_without_a_trigger_conflict_or_a_foreign_owner(
        bool triggerConflict,
        bool foreignOwner,
        bool canActivate)
    {
        await ArrangeAsync(foreignOwner ? SlotState.Imported : SlotState.Publishing);
        var result = new PublicationPreflightResult(!triggerConflict, [], triggerConflict ? [Clash] : []);

        var plan = Plan(await _authority.FindAsync(DefinitionId, SlotName), result);

        Assert.Equal(canActivate, plan.CanActivate);
        // The trigger verdict stays the core service's own: the owner is folded in on the plan, not into the result.
        Assert.Equal(!triggerConflict, plan.Result.CanActivate);
    }

    private async Task ArrangeAsync(SlotState state)
    {
        switch (state)
        {
            case SlotState.Publishing:
                await ActivateAsync(WorkflowActivationSource.Publishing);
                break;
            case SlotState.Imported:
                await ActivateAsync(ImportOwner);
                break;
            case SlotState.Vacated:
                var imported = await ActivateAsync(ImportOwner);
                var vacated = await _authority.TryDeactivateAsync(DefinitionId, SlotName, ImportOwner, imported.Revision, Now);
                Assert.True(vacated.Succeeded, vacated.Diagnostic);
                break;
        }
    }

    private async Task<WorkflowActivationSlot> ActivateAsync(WorkflowActivationSource source)
    {
        var transition = await _authority.TryActivateAsync(new WorkflowActivationSlotRequest(
            DefinitionId, SlotName, $"{source.Kind}:activation", source, ExpectedRevision: 0, Now));
        Assert.True(transition.Succeeded, transition.Diagnostic);
        return transition.Slot;
    }

    private static WorkflowPublicationPreflightPlan Plan(WorkflowActivationSlot? slot, PublicationPreflightResult result) =>
        new(
            new ResolvedPublicationAction(DefinitionId, "version-1", PublicationAction.Replace, SlotName, PublicationPolicySource.Host, PolicyRevision: 0),
            slot,
            result,
            CandidateClaims: []);
}
