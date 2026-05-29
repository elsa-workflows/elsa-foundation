using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// SC-023 + FR-034. A validator defined in a *separate assembly* (here: the test assembly,
/// simulating an activity feature like <c>Elsa.Http.Activities.Design</c> that ships its
/// own <c>IDomainEventHandler&lt;OnDraftValidating&gt;</c>) contributes a
/// <see cref="ValidationError"/> via <c>AddValidationError</c>; the mutation pipeline reads
/// <c>event.Errors</c> after dispatch and persists the contribution to the
/// <c>WorkflowDefinitionDraftValidation</c> sibling.
/// </summary>
/// <remarks>
/// The exact wiring of <see cref="IDomainEventSender"/> → handler dispatch via the
/// production <c>DomainEventPipeline</c> (Iterator + Shielding + Invoker middleware) is
/// covered by the deferred <c>Elsa.Mediator.Tests</c> follow-on (see Unit C plan.md
/// Complexity Tracking). This test verifies the contribution-flow contract end-to-end
/// against the validation sibling using the established <c>CapturingDomainEventSender.OnSend</c>
/// hook — the same pattern <see cref="DraftMutationCommandTests.ValidationSiblingPersistenceTests"/>
/// uses to simulate validator contributions without spinning up the full pipeline.
/// </remarks>
public sealed class CrossFeatureValidatorSubscriptionTests
{
    [Fact]
    public async Task Cross_feature_validator_contributes_errors_that_land_in_the_sibling()
    {
        using var host = WorkflowsDesignTestHost.Create();

        // Simulate a cross-feature validator: it's defined in this assembly (which is
        // separate from Elsa.Workflows.Design.Validations), so it stands in for an
        // activity-feature-co-located validator per FR-034.
        var crossFeatureValidator = new HttpActivityAuthPolicyValidatorStub();

        host.DomainEventSender.OnSend = evt =>
        {
            if (evt is OnDraftValidating validating)
                crossFeatureValidator.Handle(validating, CancellationToken.None).GetAwaiter().GetResult();
        };

        var draftId = await CreateDraft(host, "wf-1");

        using var ctx = host.CreateContext();
        var sibling = await ctx.WorkflowDefinitionDraftValidations
            .FirstAsync(v => v.WorkflowDefinitionDraftId == draftId);

        var error = Assert.Single(sibling.Errors);
        Assert.Equal("Http/AuthPolicyUnknown", error.Type);
        Assert.Equal(HttpActivityAuthPolicyValidatorStub.ExpectedPath, error.Path);
    }

    [Fact]
    public async Task Cross_feature_and_baseline_validators_both_contribute_in_the_same_pass()
    {
        using var host = WorkflowsDesignTestHost.Create();

        var crossFeatureValidator = new HttpActivityAuthPolicyValidatorStub();
        var baselineSimulation = new ValidationError("$workflow", "Graph/StartActivity", "No start activity");

        host.DomainEventSender.OnSend = evt =>
        {
            if (evt is OnDraftValidating validating)
            {
                // Baseline-style contribution (analogue of StartActivityValidator).
                validating.AddValidationError(baselineSimulation);
                // Cross-feature contribution (analogue of an Elsa.Http validator).
                crossFeatureValidator.Handle(validating, CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        var draftId = await CreateDraft(host, "wf-1");

        using var ctx = host.CreateContext();
        var sibling = await ctx.WorkflowDefinitionDraftValidations
            .FirstAsync(v => v.WorkflowDefinitionDraftId == draftId);

        Assert.Equal(2, sibling.Errors.Count);
        Assert.Contains(sibling.Errors, e => e.Type == "Graph/StartActivity");
        Assert.Contains(sibling.Errors, e => e.Type == "Http/AuthPolicyUnknown");
    }

    private static async Task<string> CreateDraft(WorkflowsDesignTestHost host, string workflowDefinitionId)
    {
        await host.EnsureDefinition(workflowDefinitionId);
        using var scope = host.Services.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<ICreateDraftCommand>();
        return await command.Execute(workflowDefinitionId);
    }

    /// <summary>
    /// Stand-in for a validator that an activity feature (e.g. <c>Elsa.Http.Activities.Design</c>)
    /// would ship per FR-034. Lives in the test assembly — deliberately *not* in
    /// <c>Elsa.Workflows.Design.Validations</c> — to exercise the cross-assembly subscription
    /// pattern.
    /// </summary>
    private sealed class HttpActivityAuthPolicyValidatorStub : IDomainEventHandler<OnDraftValidating>
    {
        public const string ExpectedPath = "$workflow";

        public ValueTask Handle(OnDraftValidating domainEvent, CancellationToken cancellationToken)
        {
            domainEvent.AddValidationError(new ValidationError(
                Path: ExpectedPath,
                Type: "Http/AuthPolicyUnknown",
                Message: "Configured authorization policy is unknown (simulated)."
            ));
            return ValueTask.CompletedTask;
        }
    }
}
