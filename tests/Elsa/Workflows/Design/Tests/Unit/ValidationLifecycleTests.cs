using Elsa.Workflows.Design.Tests.Infrastructure;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Validators;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// SC-013 + SC-022 + Unit C FR-023. End-to-end exercise of the delete-and-re-add validation
/// lifecycle, now driven through the single coarse <see cref="Persistence.Core.Contracts.IUpdateDraftCommand"/>:
/// introducing a forbidden condition results in the persisted sibling carrying the error;
/// removing the condition causes the next update to clear it (the sibling is rewritten
/// wholesale, not appended to).
/// </summary>
/// <remarks>
/// Uses the actual <see cref="OrphanActivityValidator"/> wired into the
/// <c>CapturingEventPublisher.OnPublish</c> hook — this exercises the validator's real logic
/// end-to-end against the pipeline's persistence flow, rather than re-testing the sibling
/// wholesale-rewrite mechanism (which <c>ValidationSiblingPersistenceTests</c> already covers
/// with a stub error).
/// </remarks>
public sealed class ValidationLifecycleTests
{
    [Fact]
    public async Task Orphan_activity_lifecycle_round_trips_through_sibling()
    {
        using var host = WorkflowsDesignTestHost.Create();

        // Wire the real OrphanActivityValidator into the capturing sender's hook so every
        // OnDraftValidating dispatch runs the production validator code against the snapshot.
        var validator = new OrphanActivityValidator();
        host.EventPublisher.OnPublish = evt =>
        {
            if (evt is OnDraftValidating validating)
                foreach (var error in validator.Validate(validating.Draft, CancellationToken.None).GetAwaiter().GetResult())
                    validating.Errors.Add(error);
        };

        var draftId = await SeedEmptyDraft(host);

        // 1. Desired state holds a graph with one disconnected child activity, so the validator
        // emits an orphan error for that graph member.
        await Update(host, draftId, State(
            activities: [Node("orphan"), Node("start", isStart: true), Node("connected")],
            connections: [Connection("start", "connected")]));

        await AssertSiblingErrors(host, draftId, expectedTypes: ["Graph/OrphanActivity"]);

        // 2. Add a composition start activity AND wire the orphan to it → orphan condition is gone.
        // The next validation pass rewrites the sibling wholesale.
        await Update(host, draftId, State(
            activities: [Node("orphan"), Node("start", isStart: true), Node("connected")],
            connections: [Connection("start", "orphan"), Connection("orphan", "connected")]));

        await AssertSiblingErrors(host, draftId, expectedTypes: []);
    }

    private static async Task AssertSiblingErrors(WorkflowsDesignTestHost host, string draftId, string[] expectedTypes)
    {
        using var ctx = host.CreateContext();
        var sibling = await ctx.WorkflowDefinitionDraftValidations
            .FirstAsync(v => v.WorkflowDefinitionDraftId == draftId);

        var actualTypes = sibling.Errors.Select(e => e.Type).ToArray();

        Assert.Equal(expectedTypes.Length, actualTypes.Length);
        foreach (var expected in expectedTypes)
            Assert.Contains(expected, actualTypes);
    }
}
