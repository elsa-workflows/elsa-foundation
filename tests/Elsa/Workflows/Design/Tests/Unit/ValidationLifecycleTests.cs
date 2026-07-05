using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Validators;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// SC-013 + SC-022 + Unit C FR-023. End-to-end exercise of the validation lifecycle, driven through
/// the single coarse <see cref="Persistence.Core.Contracts.IUpdateDraftCommand"/>. Errors are
/// derived state (no persisted sibling): introducing a forbidden condition makes the derived error
/// set (read via <see cref="IWorkflowDefinitionDraftStore.FindValidationErrorsByDraftIdAsync"/>)
/// carry the error; removing the condition makes the next derivation clear it (recomputed wholesale,
/// not appended).
/// </summary>
/// <remarks>
/// Uses the actual <see cref="VariableUniquenessValidator"/> wired into the
/// <c>CapturingEventPublisher.OnPublish</c> hook — this exercises the validator's real logic
/// end-to-end against every <c>OnDraftValidating</c> pass, including the one the derive port
/// (<c>FindValidationErrorsByDraftIdAsync</c>) fires when it recomputes errors on demand.
/// </remarks>
public sealed class ValidationLifecycleTests
{
    [Fact]
    public async Task Validation_error_lifecycle_round_trips_through_the_derive_port()
    {
        using var host = WorkflowsDesignTestHost.Create();

        // Wire the real VariableUniquenessValidator into the capturing publisher's hook so every
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

        await AssertDerivedErrors(host, draftId, expectedTypes: ["Variables/Uniqueness"]);

        // 2. Rename one variable; the next validation pass recomputes the error set wholesale.
        await Update(host, draftId, State(
            variables: [Variable("v1", "duplicate"), Variable("v2", "unique")],
            activities: [Node("start", isStart: true)]));

        await AssertDerivedErrors(host, draftId, expectedTypes: []);
    }

    private static async Task AssertDerivedErrors(WorkflowsDesignTestHost host, string draftId, string[] expectedTypes)
    {
        using var scope = host.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionDraftStore>();
        var errors = await store.FindValidationErrorsByDraftIdAsync(draftId);

        var actualTypes = errors.Select(e => e.Type).ToArray();

        Assert.Equal(expectedTypes.Length, actualTypes.Length);
        foreach (var expected in expectedTypes)
            Assert.Contains(expected, actualTypes);
    }
}
