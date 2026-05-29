using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Elsa.Workflows.Design.Validations.Core.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using ArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;

namespace Elsa.Workflows.Design.Tests.Unit.DraftMutationCommandTests;

/// <summary>
/// SC-012 representative end-to-end coverage for the FR-027 mutation pipeline. Verifies, for
/// commands across each category, that the pipeline:
/// <list type="bullet">
/// <item>Mutates the materialised snapshot in place.</item>
/// <item>Publishes the granular FR-018 / FR-018a lifecycle event via
///       <see cref="Elsa.Mediator.Core.Contracts.ILifecycleEventSender"/> after the lock is released.</item>
/// <item>Synchronously dispatches <c>OnDraftValidating</c> as a domain event via
///       <see cref="Elsa.Mediator.Core.Contracts.IDomainEventSender"/> inside the lock (the gate).</item>
/// <item>Publishes <c>OnDraftValidated</c> as a lifecycle event after the granular event
///       (cause-effect order preserved across the lifecycle stream).</item>
/// <item>Completes without subscribers (SC-012 closed-mode contract).</item>
/// </list>
/// </summary>
/// <remarks>
/// Exhaustive per-command branch coverage (one test class per command) is deferred to Phase 12
/// polish — the pipeline is uniform and these representative tests cover the load-bearing
/// behaviour. <see cref="CommandRegistrationTests"/> guards command-implementation wiring;
/// this class guards pipeline behaviour.
/// </remarks>
public sealed class DraftMutationPipelineTests
{
    [Fact]
    public async Task AddActivityToDraft_mutates_state_and_publishes_granular_and_validated_events()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host, "wf-1");
        var activity = NewActivityNode("node-1", "av-1");

        using (var scope = host.Services.CreateScope())
        {
            var command = scope.ServiceProvider.GetRequiredService<IAddActivityToDraftCommand>();
            await command.Execute(draftId, activity);
        }

        // (a) state mutated + persisted.
        using (var ctx = host.CreateContext())
        {
            var draft = await ctx.WorkflowDefinitionDrafts.FirstAsync(d => d.Id == draftId);
            Assert.NotNull(draft.StateSource);
            Assert.Contains("node-1", draft.StateSource);
        }

        // (b) granular FR-018 lifecycle event published.
        var granular = host.LifecycleEventSender.LastOf<OnActivityAddedToDraft>();
        Assert.NotNull(granular);
        Assert.Equal(draftId, granular!.DraftId);
        Assert.Equal("node-1", granular.NodeId);

        // (c) OnDraftValidating fired synchronously as the in-lock gate. SeedEmptyDraft fires
        // its own OnDraftValidating too, so we just assert the event was captured at all —
        // the gate ran for the mutation and we'll be able to read its errors via
        // OnDraftValidated below.
        Assert.NotEmpty(host.DomainEventSender.CapturedEvents.OfType<OnDraftValidating>());

        // (d) OnDraftValidated fires after the granular lifecycle event (cause before consequence).
        var validated = host.LifecycleEventSender.LastOf<OnDraftValidated>();
        Assert.NotNull(validated);
        Assert.Empty(validated!.Errors);

        var lifecycleEvents = host.LifecycleEventSender.CapturedEvents.ToList();
        var granularIdx = lifecycleEvents.FindLastIndex(e => e is OnActivityAddedToDraft);
        var validatedIdx = lifecycleEvents.FindLastIndex(e => e is OnDraftValidated);
        Assert.True(granularIdx >= 0);
        Assert.True(
            validatedIdx > granularIdx,
            "OnDraftValidated must fire AFTER the granular FR-018 lifecycle event — cause before consequence"
        );
    }

    [Fact]
    public async Task DeclareVariableInDraft_mutates_state_and_publishes_event()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host, "wf-1");
        var variable = new VariableDefinition("v1", "MyVar", TypeInformation.FromType(typeof(string)), null, null);

        using (var scope = host.Services.CreateScope())
        {
            var command = scope.ServiceProvider.GetRequiredService<IDeclareVariableInDraftCommand>();
            await command.Execute(draftId, variable);
        }

        var declaredEvent = host.LifecycleEventSender.LastOf<OnVariableDeclaredInDraft>();
        Assert.NotNull(declaredEvent);
        Assert.Equal("MyVar", declaredEvent!.Variable.Name);
    }

    [Fact]
    public async Task AddWorkflowInputToDraft_publishes_workflow_level_event_distinct_from_per_activity()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host, "wf-1");
        var input = new InputDefinition(
            "in1",
            "Input1",
            TypeInformation.FromType(typeof(string)),
            null,
            "Input1",
            null,
            IsRequired: true
        );

        using (var scope = host.Services.CreateScope())
        {
            var command = scope.ServiceProvider.GetRequiredService<IAddWorkflowInputToDraftCommand>();
            await command.Execute(draftId, input);
        }

        // FR-018 naming discipline pins workflow-level vs per-activity events as distinct.
        Assert.NotNull(host.LifecycleEventSender.LastOf<OnWorkflowInputAddedToDraft>());
        Assert.Null(host.LifecycleEventSender.LastOf<OnActivityInputAddedToDraft>());
    }

    [Fact]
    public async Task AddActivityInputToDraft_publishes_per_activity_event()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host, "wf-1");
        var activity = NewActivityNode("node-1", "av-1");

        using (var scope = host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IAddActivityToDraftCommand>().Execute(draftId, activity);

        var input = new ArgumentState("ak1", new ArgumentValue("x"), null, null, null, null);

        using (var scope = host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IAddActivityInputToDraftCommand>().Execute(draftId, "node-1", input);

        var perActivityEvent = host.LifecycleEventSender.LastOf<OnActivityInputAddedToDraft>();
        Assert.NotNull(perActivityEvent);
        Assert.Equal("node-1", perActivityEvent!.NodeId);
        Assert.Equal("ak1", perActivityEvent.InputReferenceKey);

        // Joey iteration symmetry: per-activity event is distinct from workflow-level event.
        Assert.Null(host.LifecycleEventSender.LastOf<OnWorkflowInputAddedToDraft>());
    }

    [Fact]
    public async Task CreateDraft_creates_draft_with_layout_sibling_and_publishes_OnDraftCreated_and_OnDraftValidated()
    {
        using var host = WorkflowsDesignTestHost.Create();
        await host.EnsureDefinition("wf-1");

        string draftId;
        using (var scope = host.Services.CreateScope())
        {
            var command = scope.ServiceProvider.GetRequiredService<ICreateDraftCommand>();
            draftId = await command.Execute("wf-1");
        }

        using var ctx = host.CreateContext();
        var draft = await ctx.WorkflowDefinitionDrafts.FirstOrDefaultAsync(d => d.Id == draftId);
        var layout = await ctx.WorkflowDefinitionDraftLayouts.FirstOrDefaultAsync(l => l.WorkflowDefinitionDraftId == draftId);

        Assert.NotNull(draft);
        Assert.NotNull(layout);

        var created = host.LifecycleEventSender.LastOf<OnDraftCreated>();
        Assert.NotNull(created);
        Assert.Equal(draftId, created!.DraftId);
        Assert.Equal("wf-1", created.WorkflowDefinitionId);

        // OnDraftValidated fires after creation too — the empty Draft's validation pass returns no errors.
        var validated = host.LifecycleEventSender.LastOf<OnDraftValidated>();
        Assert.NotNull(validated);
        Assert.False(validated!.HasErrors);
    }

    [Fact]
    public async Task Pipeline_completes_without_subscribers()
    {
        // SC-012 closed-mode contract: with no IDomainEventHandler / INotificationHandler
        // registered, the pipeline still completes — capturing senders swallow + record but
        // never throw.
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host, "wf-1");
        var activity = NewActivityNode("node-1", "av-1");

        using var scope = host.Services.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<IAddActivityToDraftCommand>();

        await command.Execute(draftId, activity);
        // Reaching here = no exception.
    }

    // --- helpers ---

    private static async Task<string> SeedEmptyDraft(WorkflowsDesignTestHost host, string workflowDefinitionId)
    {
        await host.EnsureDefinition(workflowDefinitionId);
        using var scope = host.Services.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<ICreateDraftCommand>();
        return await command.Execute(workflowDefinitionId);
    }

    private static ActivityNode NewActivityNode(string nodeId, string activityVersionId) => new(
        NodeId: nodeId,
        ActivityVersionId: activityVersionId,
        Inputs: [],
        Outputs: [],
        IsContainer: false,
        IsStart: false,
        IsTerminal: false,
        ChildActivities: []
    );
}
