using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests.Alterations;

public sealed partial class RetryFaultedActivityAlterationHandlerTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task Real_retry_checkpoint_resolves_incident_and_terminal_job_with_original_history_or_legacy_behavior(bool captured) =>
        RunActualAlteration(captured, false, null);

    [Theory]
    [InlineData(false, false, true)] [InlineData(true, false, true)]
    [InlineData(false, false, false)] [InlineData(true, false, false)]
    [InlineData(false, true, true)] [InlineData(true, true, true)]
    [InlineData(false, true, false)] [InlineData(true, true, false)]
    public Task Retry_and_reschedule_record_only_the_descendants_they_actually_cancel(bool captured, bool reschedule, bool live) =>
        RunActualAlteration(captured, reschedule, live ? ActivityExecutionStatus.Waiting : ActivityExecutionStatus.Completed);

    [Theory]
    [InlineData(false, false, false)] [InlineData(true, false, false)]
    [InlineData(false, false, true)] [InlineData(true, false, true)]
    [InlineData(false, true, false)] [InlineData(true, true, false)]
    [InlineData(false, true, true)] [InlineData(true, true, true)]
    public Task Retry_and_reschedule_record_the_successor_lifecycle(bool captured, bool reschedule, bool pinned) =>
        RunActualAlteration(captured, reschedule, null, pinned);

    private static async Task RunActualAlteration(bool captured, bool reschedule, ActivityExecutionStatus? descendantStatus, bool pinned = false)
    {
        var now = DateTimeOffset.UnixEpoch.AddHours(1); var clock = new FakeTimeProvider(now);
        var services = new ServiceCollection();
        services.AddLogging(); services.AddSingleton<IPersistenceAccessContextAccessor>(new HistoryAccess());
        services.AddWorkflowRuntime(); services.AddWorkflowRuntimeHistory();
        services.AddSingleton<TimeProvider>(clock);
        services.Configure<RuntimeRecoveryContinuationOptions>(options => options.SigningKey = "alteration-history-tests-signing-key-at-least-32-bytes");
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope(); var selected = scope.ServiceProvider;
        var policy = captured ? new RuntimeHistoryCapturePolicy("runtime-history-v3", 3, true,
            ["IncidentResolved", "ActivityRetryScheduled", "ActivityCancelled", "ActivityStarted", "ActivityScheduled", "ControlApplied", "AlterationApplied", "RuntimeAlterationJob"]) : null;
        var workflow = Workflow() with { HistoryCapturePolicy = policy, HistoryCaptureEnabledAtAdmission = captured };
        var source = Source("source") with { ParentActivityExecutionId = null, SchedulingActivityExecutionId = null };
        if (reschedule) source = source with { IncidentIds = [] };
        source = source with { InputSnapshot = pinned ? new("source", "contract", "binding", new Dictionary<string, ValueEnvelope>(), now) : null };
        var descendant = descendantStatus is { } status ? Source("descendant") with
        {
            Status = status, ParentActivityExecutionId = source.InvocationId, SchedulingActivityExecutionId = source.InvocationId,
            IncidentIds = [], FaultCount = 0, AggregateFaultCount = 0, CompletedAt = status == ActivityExecutionStatus.Completed ? now.AddMinutes(-1) : null
        } : null;
        var kind = reschedule ? "RescheduleActivity" : RetryFaultedActivityAlterationHandler.Kind;
        await selected.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(workflow);
        await selected.GetRequiredService<IActivityExecutionStateStore>().SaveAsync(source);
        if (descendant is not null) await selected.GetRequiredService<IActivityExecutionStateStore>().SaveAsync(descendant);
        await selected.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(Executable());
        var incidents = selected.GetRequiredService<IIncidentStateStore>(); if (!reschedule) await incidents.SaveAsync(WaitingIncident("incident", "source"));
        var alterations = selected.GetRequiredService<IWorkflowAlterationStore>();
        await alterations.AdmitAsync(WorkflowAlterationPlanState.CreateCapturing("history-plan",
            new("tenant", "system", "operator"), new("operator-1", null), "history-key", "history-request",
            new("key-1", "AES-256-GCM", "ciphertext"), WorkflowAlterationTargetSelector.ForExecutionIds(["workflow"]), now));
        var plan = await alterations.CaptureAsync("history-plan", 0, [new("workflow", "tenant")], null);
        await alterations.SealAsync(plan.PlanId, plan.Revision, now);
        var job = (await alterations.ClaimNextAsync(plan.PlanId, "history-worker", now, TimeSpan.FromMinutes(1)))!;
        var staging = new Staging();
        var context = new WorkflowAlterationPreflightContext(job, new(kind, 1, reschedule ? System.Text.Json.JsonSerializer.SerializeToElement(new { sourceActivityExecutionId = "source" }) : Payload("source", "private operator reason")), 0,
            new WorkflowAlterationProjectedState([workflow, new WorkflowAlterationActorCommandExecutor.WorkflowAlterationActivityExecutionProjection(descendant is null ? [source] : [source, descendant])]), staging);
        IWorkflowAlterationPreflightHandler handler = reschedule ? ActivatorUtilities.CreateInstance<RescheduleActivityAlterationHandler>(selected) : new RetryFaultedActivityAlterationHandler(selected.GetRequiredService<IWorkflowExecutableStore>(), incidents, alterations,
            selected.GetRequiredService<ActivityExecutionSupersessionPlanner>(), selected.GetRequiredService<TimeProvider>());
        var preflight = await handler.PreflightAsync(context); Assert.True(preflight.IsAccepted, preflight.Failure?.Message);
        clock.Advance(TimeSpan.FromSeconds(7)); var committedAt = clock.GetUtcNow();
        var commitId = WorkflowAlterationIdentity.CreateCheckpointCommitId(job.JobId, job.AttemptCount);
        var terminal = new WorkflowAlterationJobTerminalChange(job.JobId, job.Claim!.Token, WorkflowAlterationJobStatus.Succeeded,
            [new(0, kind, 1, WorkflowAlterationOutcomeStatus.Succeeded, "Applied", null, committedAt)], commitId, committedAt);
        var writer = selected.GetRequiredService<IWorkflowAlterationCheckpointWriter>();
        var request = new WorkflowAlterationCheckpointWriteRequest(job, staging.StagedChanges, terminal, context.ProjectedState);
        var invalidTerminal = new WorkflowAlterationJobTerminalChange(job.JobId, job.Claim.Token, WorkflowAlterationJobStatus.Failed,
            [new(0, kind, 1, WorkflowAlterationOutcomeStatus.Failed, "Rejected", null, committedAt)], commitId + "-refused", committedAt);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await writer.CommitAsync(request with { TerminalJobChange = invalidTerminal }));
        Assert.Equal(ActivityExecutionStatus.Faulted, (await selected.GetRequiredService<IActivityExecutionStateStore>().FindAsync("workflow", "source"))!.Status);
        if (!reschedule) Assert.Equal(IncidentStatus.Blocking, (await incidents.FindAsync("workflow", "incident"))!.Status);
        Assert.Equal(WorkflowAlterationJobStatus.Running, (await alterations.FindJobAsync(job.JobId))!.Status);
        await writer.CommitAsync(request);
        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, (await alterations.FindJobAsync(job.JobId))!.Status);
        if (!reschedule) Assert.Equal(IncidentStatus.Resolved, (await incidents.FindAsync("workflow", "incident"))!.Status);
        var superseded = (await selected.GetRequiredService<IActivityExecutionStateStore>().FindAsync("workflow", "source"))!;
        Assert.Equal(ActivityExecutionStatus.Superseded, superseded.Status); Assert.NotNull(superseded.SupersededByActivityExecutionId);
        var history = selected.GetRequiredService<IRuntimeHistoryStore>(); var page = await history.ReadPageAsync(new("workflow"));
        if (descendant is not null)
            Assert.Equal(descendant.Status == ActivityExecutionStatus.Waiting ? ActivityExecutionStatus.Cancelled : descendant.Status,
                (await selected.GetRequiredService<IActivityExecutionStateStore>().FindAsync("workflow", descendant.InvocationId))!.Status);
        if (!captured) { Assert.Null(page.Head); Assert.Empty(page.Items); await AssertReplayUnchangedAsync(); return; }
        var successorId = superseded.SupersededByActivityExecutionId!;
        var successor = (await selected.GetRequiredService<IActivityExecutionStateStore>().FindAsync("workflow", successorId))!;
        Assert.Equal(pinned ? ActivityExecutionStatus.Running : ActivityExecutionStatus.Scheduled, successor.Status);
        var created = Assert.Single(page.Items, item => item.ActivityExecutionId == successorId &&
            item.Name == (pinned ? "ActivityStarted" : "ActivityScheduled"));
        Assert.Equal(now, created.OccurredAt);
        var sourceControl = Assert.Single(page.Items, item => item.Name == "ControlApplied" && item.ActivityExecutionId == "source");
        Assert.Equal(successorId, sourceControl.Metadata["history.relatedActivityExecutionId"]); Assert.Equal(now, sourceControl.OccurredAt);
        if (descendant?.Status == ActivityExecutionStatus.Waiting)
        {
            var cancelled = Assert.Single(page.Items, item => item.Name == "ActivityCancelled");
            Assert.Equal(descendant.InvocationId, cancelled.ActivityExecutionId); Assert.Equal(now, cancelled.OccurredAt);
        }
        else Assert.DoesNotContain(page.Items, item => item.Name == "ActivityCancelled");
        if (!reschedule)
        {
            Assert.Equal("incident", Assert.Single(page.Items, item => item.Name == "IncidentResolved").IncidentId);
            var retry = Assert.Single(page.Items, item => item.Name == "ActivityRetryScheduled");
            Assert.Equal("source", retry.ActivityExecutionId); Assert.Equal(superseded.SupersededByActivityExecutionId, retry.Metadata["history.relatedActivityExecutionId"]);
        }
        else Assert.DoesNotContain(page.Items, item => item.Name is "IncidentResolved" or "ActivityRetryScheduled");
        Assert.Single(page.Items, item => item.Name == "AlterationApplied"); Assert.Single(page.Items, item => item.Name == "RuntimeAlterationJob");
        Assert.DoesNotContain("private operator reason", System.Text.Json.JsonSerializer.Serialize(page.Items), StringComparison.Ordinal);
        await AssertReplayUnchangedAsync();

        async Task AssertReplayUnchangedAsync()
        {
            var original = await SnapshotAsync();
            clock.Advance(TimeSpan.FromSeconds(1));
            await writer.CommitAsync(request);
            Assert.Equal(original, await SnapshotAsync());
            // A later callback is equivalent; a changed logical request still conflicts with its original marker.
            var changedWorkflow = context.ProjectedState.GetRequired<WorkflowExecutionState>() with
            {
                SystemMetadata = new Dictionary<string, string>(workflow.SystemMetadata) { ["replay-test"] = "different-request" }
            };
            await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(async () => await writer.CommitAsync(
                request with { ProjectedState = new WorkflowAlterationProjectedState([changedWorkflow]) }));
            Assert.Equal(original, await SnapshotAsync());
        }

        async Task<string> SnapshotAsync()
        {
            var currentPage = await history.ReadPageAsync(new("workflow"));
            var activityStore = selected.GetRequiredService<IActivityExecutionStateStore>();
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                Workflow = await selected.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("workflow"),
                Source = await activityStore.FindAsync("workflow", "source"),
                Successor = await activityStore.FindAsync("workflow", superseded.SupersededByActivityExecutionId!),
                Descendant = descendant is null ? null : await activityStore.FindAsync("workflow", descendant.InvocationId),
                Incident = reschedule ? null : await incidents.FindAsync("workflow", "incident"),
                Job = await alterations.FindJobAsync(job.JobId), currentPage.Head, currentPage.Items
            });
        }
    }

    private sealed class HistoryAccess : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new("tenant")); }
}
