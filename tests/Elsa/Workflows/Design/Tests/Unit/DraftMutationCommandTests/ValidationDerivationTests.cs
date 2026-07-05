using Elsa.Events.Strategies;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Elsa.Workflows.Design.Validations.Core;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit.DraftMutationCommandTests;

/// <summary>
/// Unit C FR-021/FR-023, superseded 2026-07-05. Validation errors are no longer persisted to a
/// <c>WorkflowDefinitionDraftValidation</c> sibling: they are derived state, recomputed on demand.
/// This replaces the former <c>ValidationSiblingPersistenceTests</c>, preserving the surviving
/// subjects against the new observation mechanisms:
/// <list type="bullet">
/// <item>a valid draft surfaces an empty error set on <c>OnDraftValidated</c>;</item>
/// <item>a validator's contribution appears on <c>OnDraftValidated</c> after a mutation;</item>
/// <item>a subsequent clean mutation yields an <c>OnDraftValidated</c> with the error gone
///       (wholesale recompute, not append);</item>
/// <item>the <see cref="DraftValidationGate"/> derives the current error set on demand (replacing
///       the old persisted-row read and the former store derive-port).</item>
/// </list>
/// Validator contributions are simulated with the <c>CapturingEventPublisher.OnPublish</c> hook.
/// </summary>
public sealed class ValidationDerivationTests
{
    private static readonly ValidationError StubError =
        new("$workflow", "Graph/StartActivity", "No start activity");

    [Fact]
    public async Task CreateDraft_surfaces_an_empty_error_set_on_OnDraftValidated()
    {
        using var host = WorkflowsDesignTestHost.Create();

        await CreateDraft(host, "wf-1");

        var validated = Assert.IsType<OnDraftValidated>(host.EventPublisher.LastOf<OnDraftValidated>());
        Assert.False(validated.HasErrors);
        Assert.Empty(validated.Errors);
    }

    [Fact]
    public async Task Validator_contribution_appears_on_OnDraftValidated_after_update()
    {
        using var host = WorkflowsDesignTestHost.Create();
        host.EventPublisher.ContributeError(StubError);

        var draftId = await CreateDraft(host, "wf-1");
        await Update(host, draftId, State(activities: [Node("node-1")]));

        var validated = Assert.IsType<OnDraftValidated>(host.EventPublisher.LastOf<OnDraftValidated>());
        Assert.True(validated.HasErrors);
        Assert.Equal(StubError, Assert.Single(validated.Errors));
    }

    [Fact]
    public async Task A_subsequent_clean_mutation_yields_OnDraftValidated_with_the_error_gone()
    {
        using var host = WorkflowsDesignTestHost.Create();

        // First pass yields an error.
        host.EventPublisher.ContributeError(StubError);
        var draftId = await CreateDraft(host, "wf-1");

        // Subsequent pass: no error contributed. Errors are recomputed wholesale (not appended).
        host.EventPublisher.OnPublish = null;
        await Update(host, draftId, State(activities: [Node("node-1")]));

        var validated = Assert.IsType<OnDraftValidated>(host.EventPublisher.LastOf<OnDraftValidated>());
        Assert.False(validated.HasErrors);
        Assert.Empty(validated.Errors);
    }

    [Fact]
    public async Task Gate_derives_the_current_error_set_on_demand()
    {
        using var host = WorkflowsDesignTestHost.Create();
        host.EventPublisher.ContributeError(StubError);

        var draftId = await CreateDraft(host, "wf-1");

        // Errors are derived on demand through the gate: load the draft, then DeriveValidationErrorsAsync
        // re-publishes OnDraftValidating on it and reads the accumulated errors back (replacing the old
        // store derive-port).
        using var scope = host.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionDraftStore>();
        var draft = await store.FindByIdAsync(draftId);
        Assert.NotNull(draft);
        var errors = await host.EventPublisher.DeriveValidationErrorsAsync(draft!, EventPublishingStrategy.Sequential, CancellationToken.None);

        Assert.Equal(StubError, Assert.Single(errors));
    }

    private static async Task<string> CreateDraft(WorkflowsDesignTestHost host, string workflowDefinitionId)
    {
        await host.EnsureDefinition(workflowDefinitionId);
        using var scope = host.Services.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<ICreateDraftCommand>();
        return await command.Execute(workflowDefinitionId);
    }
}
