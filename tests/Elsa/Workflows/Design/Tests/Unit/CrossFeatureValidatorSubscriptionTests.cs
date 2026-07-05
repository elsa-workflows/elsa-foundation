using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// SC-023 + FR-034. A validator defined in a *separate assembly* (here: the test assembly,
/// simulating an activity feature like <c>Elsa.Http.Activities.Design</c> that ships its
/// own <see cref="IDraftValidator"/>) contributes a <see cref="ValidationError"/> by returning
/// it from <c>Validate</c>; the aggregating handler writes it onto <c>event.Errors</c> and the
/// mutation command surfaces that collection on the <c>OnDraftValidated</c> event after commit.
/// (Errors are derived state — there is no persisted validation sibling.)
/// </summary>
/// <remarks>
/// The exact wiring of <see cref="Elsa.Events.Core.Contracts.IEventPublisher"/> → handler dispatch
/// via the production event pipeline (invoker middleware) is covered by the deferred
/// <c>Elsa.Events.Tests</c> follow-on. This test verifies the contribution-flow contract
/// end-to-end using the established <c>CapturingEventPublisher.OnPublish</c> hook to simulate
/// validator contributions onto <c>OnDraftValidating</c>, observed on the resulting
/// <c>OnDraftValidated</c>.
/// </remarks>
public sealed class CrossFeatureValidatorSubscriptionTests
{
    [Fact]
    public async Task Cross_feature_validator_contributes_errors_surfaced_on_OnDraftValidated()
    {
        using var host = WorkflowsDesignTestHost.Create();

        // Simulate a cross-feature validator: it's defined in this assembly (which is
        // separate from Elsa.Workflows.Design.Validations), so it stands in for an
        // activity-feature-co-located validator per FR-034.
        var crossFeatureValidator = new HttpActivityAuthPolicyValidatorStub();

        host.EventPublisher.OnPublish = evt =>
        {
            if (evt is OnDraftValidating validating)
                foreach (var error in crossFeatureValidator.Validate(validating.Draft, CancellationToken.None).GetAwaiter().GetResult())
                    validating.Errors.Add(error);
        };

        await CreateDraft(host, "wf-1");

        var validated = Assert.IsType<OnDraftValidated>(host.EventPublisher.LastOf<OnDraftValidated>());
        var error = Assert.Single(validated.Errors);
        Assert.Equal("Http/AuthPolicyUnknown", error.Type);
        Assert.Equal(HttpActivityAuthPolicyValidatorStub.ExpectedPath, error.Path);
    }

    [Fact]
    public async Task Cross_feature_and_baseline_validators_both_contribute_in_the_same_pass()
    {
        using var host = WorkflowsDesignTestHost.Create();

        var crossFeatureValidator = new HttpActivityAuthPolicyValidatorStub();
        var baselineSimulation = new ValidationError("$workflow", "Graph/StartActivity", "No start activity");

        host.EventPublisher.OnPublish = evt =>
        {
            if (evt is OnDraftValidating validating)
            {
                // Baseline-style contribution (analogue of StartActivityValidator).
                validating.Errors.Add(baselineSimulation);
                // Cross-feature contribution (analogue of an Elsa.Http validator).
                foreach (var error in crossFeatureValidator.Validate(validating.Draft, CancellationToken.None).GetAwaiter().GetResult())
                    validating.Errors.Add(error);
            }
        };

        await CreateDraft(host, "wf-1");

        var validated = Assert.IsType<OnDraftValidated>(host.EventPublisher.LastOf<OnDraftValidated>());
        Assert.Equal(2, validated.Errors.Count);
        Assert.Contains(validated.Errors, e => e.Type == "Graph/StartActivity");
        Assert.Contains(validated.Errors, e => e.Type == "Http/AuthPolicyUnknown");
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
    /// <c>Elsa.Workflows.Design.Validations</c> — to exercise the cross-assembly contribution
    /// pattern. It returns its errors; the pipeline aggregates them onto <c>event.Errors</c>.
    /// </summary>
    private sealed class HttpActivityAuthPolicyValidatorStub : IDraftValidator
    {
        public const string ExpectedPath = "$workflow";

        public ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IEnumerable<ValidationError>>([
                new ValidationError(
                    Path: ExpectedPath,
                    Type: "Http/AuthPolicyUnknown",
                    Message: "Configured authorization policy is unknown (simulated).")
            ]);
    }
}
