using Elsa.Workflows.Design.Tests.Infrastructure;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Validators;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// SC-013 + SC-022 + Unit C FR-023. End-to-end exercise of the validation
/// lifecycle, now driven through the single coarse <see cref="Persistence.Core.Contracts.IUpdateDraftCommand"/>:
/// introducing a forbidden condition results in the persisted sibling carrying the error;
/// removing the condition causes the next update to clear it (the sibling is rewritten
/// wholesale, not appended to).
/// </summary>
/// <remarks>
/// Uses the actual <see cref="VariableUniquenessValidator"/> wired into the
/// <c>CapturingEventPublisher.OnPublish</c> hook — this exercises the validator's real logic
/// end-to-end against the pipeline's persistence flow, rather than re-testing the sibling
/// wholesale-rewrite mechanism (which <c>ValidationSiblingPersistenceTests</c> already covers
/// with a stub error).
/// </remarks>
public sealed class ValidationLifecycleTests
{
    [Fact]
    public async Task Validation_error_lifecycle_round_trips_through_sibling()
    {
        using var host = WorkflowsDesignTestHost.Create();

        // Wire the real VariableUniquenessValidator into the capturing sender's hook so every
        // OnDraftValidating dispatch runs the production validator code against the snapshot.
        var validator = new VariableUniquenessValidator();
        host.EventPublisher.OnPublish = evt =>
        {
            if (evt is OnDraftValidating validating)
                foreach (var error in validator.Validate(validating.Draft, CancellationToken.None).GetAwaiter().GetResult())
                    validating.Errors.Add(error);
        };

        var draftId = await SeedEmptyDraft(host);

        // 1. Desired state has duplicate variable names, so the validator emits an error.
        await Update(host, draftId, State(
            variables: [Variable("v1", "duplicate"), Variable("v2", "Duplicate")],
            activities: [Node("start", isStart: true)]));

        await AssertSiblingErrors(host, draftId, expectedTypes: ["Variables/Uniqueness"]);

        // 2. Rename one variable; the next validation pass rewrites the sibling wholesale.
        await Update(host, draftId, State(
            variables: [Variable("v1", "duplicate"), Variable("v2", "unique")],
            activities: [Node("start", isStart: true)]));

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
