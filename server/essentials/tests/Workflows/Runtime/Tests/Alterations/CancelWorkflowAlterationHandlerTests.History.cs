using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests.Alterations;

public sealed partial class CancelWorkflowAlterationHandlerTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_cancel_checkpoint_preserves_exact_empty_inspection_delta_and_terminal_evidence(bool captured)
    {
        var now = DateTimeOffset.UnixEpoch.AddHours(1); var clock = new FakeTimeProvider(now);
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton<IPersistenceAccessContextAccessor>(new HistoryAccess());
        services.AddWorkflowRuntime(); services.AddWorkflowRuntimeHistory(); services.AddSingleton<TimeProvider>(clock);
        services.Configure<RuntimeRecoveryContinuationOptions>(options => options.SigningKey = "cancel-alteration-history-tests-signing-key-at-least-32-bytes");
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope(); var selected = scope.ServiceProvider;
        var workflow = Workflow(WorkflowExecutionStatus.Running) with
        {
            HistoryCapturePolicy = captured ? new("runtime-history-v3", 3, true,
                ["ValueCaptured", "WorkflowCancelled", "ActivityCancelled", "ControlApplied", "AlterationApplied", "RuntimeAlterationJob"]) : null,
            HistoryCaptureEnabledAtAdmission = captured
        };
        var native = selected.GetRequiredService<IWorkflowExecutionStateStore>(); await native.SaveAsync(workflow);
        var root = new ExecutableNode("node", "authored", "test", "1", "consumer", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
        await selected.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(new WorkflowExecutable(workflow.PinnedExecutable, root,
            new Dictionary<string, WorkflowExecutableResumeTarget>(), now, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference));
        var activity = new ActivityExecutionState(new("cancel-activity", workflow.WorkflowExecutionId, "node", "authored", "test", "1"),
            ActivityExecutionStatus.Running, null, 1, now, now, null, null, null, null, null,
            ActivitySchedulingProvenance.From(workflow.WorkflowExecutionId, null, null, null, null, null, null, "test"),
            0, [], [], 0, 0, new Dictionary<string, string>());
        var activities = selected.GetRequiredService<IActivityExecutionStateStore>(); await activities.SaveAsync(activity);
        var store = selected.GetRequiredService<IWorkflowAlterationStore>();
        await store.AdmitAsync(WorkflowAlterationPlanState.CreateCapturing("cancel-history-plan", new("tenant", "system", "operator"),
            new("operator", null), "cancel-key", "cancel-request", new("key", "AES-256-GCM", "ciphertext"),
            WorkflowAlterationTargetSelector.ForExecutionIds([workflow.WorkflowExecutionId]), now));
        var plan = await store.CaptureAsync("cancel-history-plan", 0, [new(workflow.WorkflowExecutionId, "tenant")], null);
        await store.SealAsync(plan.PlanId, plan.Revision, now);
        var job = (await store.ClaimNextAsync(plan.PlanId, "worker", now, TimeSpan.FromMinutes(1)))!;
        var context = new WorkflowAlterationPreflightContext(job, new("CancelWorkflow", 1, JsonSerializer.SerializeToElement(new { })), 0,
            new WorkflowAlterationProjectedState([workflow, new WorkflowAlterationActorCommandExecutor.WorkflowAlterationActivityExecutionProjection([activity])]), new Staging());
        var handler = ActivatorUtilities.CreateInstance<CancelWorkflowAlterationHandler>(selected);
        var decision = await handler.PreflightAsync(context); Assert.True(decision.IsAccepted, decision.Failure?.Message);
        clock.Advance(TimeSpan.FromSeconds(7)); var committedAt = clock.GetUtcNow();
        var commitId = WorkflowAlterationIdentity.CreateCheckpointCommitId(job.JobId, job.AttemptCount);
        var terminal = new WorkflowAlterationJobTerminalChange(job.JobId, job.Claim!.Token, WorkflowAlterationJobStatus.Succeeded,
            [new(0, "CancelWorkflow", 1, WorkflowAlterationOutcomeStatus.Succeeded, "Applied", null, committedAt)], commitId, committedAt);
        await selected.GetRequiredService<IWorkflowAlterationCheckpointWriter>().CommitAsync(new(job, context.Staging.StagedChanges, terminal, context.ProjectedState));
        Assert.Equal(WorkflowExecutionStatus.Cancelled, (await native.FindAsync(workflow.WorkflowExecutionId))!.Status);
        Assert.Equal(ActivityExecutionStatus.Cancelled, (await activities.FindAsync(workflow.WorkflowExecutionId, activity.InvocationId))!.Status);
        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, (await store.FindJobAsync(job.JobId))!.Status);
        var page = await selected.GetRequiredService<IRuntimeHistoryStore>().ReadPageAsync(new(workflow.WorkflowExecutionId));
        Assert.DoesNotContain(page.Items, item => item.Name == "ValueCaptured");
        if (captured)
        {
            Assert.Equal(now, Assert.Single(page.Items, item => item.Name == "WorkflowCancelled").OccurredAt);
            Assert.Equal(now, Assert.Single(page.Items, item => item.Name == "ActivityCancelled").OccurredAt);
            Assert.Contains(page.Items, item => item.Name == "AlterationApplied");
            var projection = await selected.GetRequiredService<IActivityExecutionInspectionStore>().FindAsync(workflow.WorkflowExecutionId, activity.InvocationId);
            Assert.NotNull(projection); Assert.Equal("checkpoint:" + commitId, projection.LastCheckpointId);
        }
        else Assert.Empty(page.Items);
    }

    private sealed class HistoryAccess : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new("tenant")); }
}
