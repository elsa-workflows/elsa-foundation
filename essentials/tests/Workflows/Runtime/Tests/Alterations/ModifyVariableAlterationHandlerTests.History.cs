using System.Text.Json;
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

public sealed partial class ModifyVariableAlterationHandlerTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Rejected_multi_command_preflight_preserves_original_value_and_revision_in_actual_terminal_checkpoint(bool captured)
    {
        var now = DateTimeOffset.UnixEpoch.AddHours(1); var clock = new FakeTimeProvider(now);
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton<IPersistenceAccessContextAccessor>(new HistoryAccess());
        services.AddWorkflowRuntime(); services.AddWorkflowRuntimeHistory(); services.AddSingleton<TimeProvider>(clock);
        services.Configure<RuntimeRecoveryContinuationOptions>(options => options.SigningKey = "failed-alteration-history-tests-signing-key-at-least-32-bytes");
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope(); var selected = scope.ServiceProvider;
        var identity = new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash");
        var workflow = Workflow(identity, 4) with
        {
            HistoryCapturePolicy = captured ? new("runtime-history-v3", 3, true, ["DurableStateRecorded", "AlterationApplied", "RuntimeAlterationJob"]) : null,
            HistoryCaptureEnabledAtAdmission = captured
        };
        var native = selected.GetRequiredService<IWorkflowExecutionStateStore>(); await native.SaveAsync(workflow);
        var executables = selected.GetRequiredService<IWorkflowExecutableStore>(); await executables.SaveAsync(Executable(identity));
        var store = selected.GetRequiredService<IWorkflowAlterationStore>();
        await store.AdmitAsync(WorkflowAlterationPlanState.CreateCapturing("failed-history-plan", new("tenant", "system", "operator"),
            new("operator", null), "failed-key", "failed-request", new("key", "AES-256-GCM", "ciphertext"),
            WorkflowAlterationTargetSelector.ForExecutionIds(["workflow"]), now));
        var plan = await store.CaptureAsync("failed-history-plan", 0, [new("workflow", "tenant", new("1", 4, identity))], null);
        await store.SealAsync(plan.PlanId, plan.Revision, now);
        var job = (await store.ClaimNextAsync(plan.PlanId, "worker", now, TimeSpan.FromMinutes(1)))!;
        WorkflowAlterationEnvelope Variable(string key) => new("ModifyVariable", 1, JsonSerializer.SerializeToElement(new { variableKey = key, value = "replacement" }));
        var projected = new WorkflowAlterationProjectedState([workflow]);
        var executor = new WorkflowAlterationJobExecutor(new VariableResolver(new ModifyVariableAlterationHandler(executables)),
            selected.GetRequiredService<IWorkflowAlterationCheckpointWriter>(), clock);
        var result = await executor.ExecuteAsync(new(job, [Variable("stable-name"), Variable("undeclared")], projected));
        Assert.Equal(WorkflowAlterationJobStatus.Failed, result.Job.Status);
        Assert.Equal(new[] { WorkflowAlterationOutcomeStatus.Skipped, WorkflowAlterationOutcomeStatus.Failed }, result.Job.Outcomes.Select(item => item.Status));
        // Prove that the first preflight really changed its tentative projection before the second rejected it.
        Assert.Equal("replacement", projected.GetRequired<WorkflowExecutionState>().RootVariableFrame!.Values["stable-name"].InlineValue!.Value.GetString());
        var retained = (await native.FindAsync("workflow"))!;
        Assert.Equal("before", retained.RootVariableFrame!.Values["stable-name"].InlineValue!.Value.GetString());
        Assert.Equal(4, retained.RootVariableFrame.Revision);
        Assert.Equal(WorkflowAlterationJobStatus.Failed, (await store.FindJobAsync(job.JobId))!.Status);
        var page = await selected.GetRequiredService<IRuntimeHistoryStore>().ReadPageAsync(new("workflow"));
        Assert.DoesNotContain(page.Items, item => item.Name is "DurableStateRecorded" or "AlterationApplied");
        if (captured) Assert.Single(page.Items, item => item.Name == "RuntimeAlterationJob"); else Assert.Empty(page.Items);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Variable_assignment_replay_retains_the_terminal_logical_time_without_duplicate_mutations(bool captured)
    {
        var now = DateTimeOffset.UnixEpoch.AddHours(1); var clock = new FakeTimeProvider(now);
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton<IPersistenceAccessContextAccessor>(new HistoryAccess());
        services.AddWorkflowRuntime(); services.AddWorkflowRuntimeHistory(); services.AddSingleton<TimeProvider>(clock);
        services.Configure<RuntimeRecoveryContinuationOptions>(options => options.SigningKey = "variable-replay-tests-signing-key-at-least-32-bytes");
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope(); var selected = scope.ServiceProvider;
        var identity = new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash");
        var workflow = Workflow(identity, 4) with
        {
            HistoryCapturePolicy = captured ? new("runtime-history-v3", 3, true, ["DurableStateRecorded", "AlterationApplied", "RuntimeAlterationJob"]) : null,
            HistoryCaptureEnabledAtAdmission = captured
        };
        var native = selected.GetRequiredService<IWorkflowExecutionStateStore>(); await native.SaveAsync(workflow);
        var executables = selected.GetRequiredService<IWorkflowExecutableStore>(); await executables.SaveAsync(Executable(identity));
        var store = selected.GetRequiredService<IWorkflowAlterationStore>();
        await store.AdmitAsync(WorkflowAlterationPlanState.CreateCapturing("variable-replay-plan", new("tenant", "system", "operator"),
            new("operator", null), "variable-key", "variable-request", new("key", "AES-256-GCM", "ciphertext"),
            WorkflowAlterationTargetSelector.ForExecutionIds(["workflow"]), now));
        var plan = await store.CaptureAsync("variable-replay-plan", 0, [new("workflow", "tenant", new("1", 4, identity))], null);
        await store.SealAsync(plan.PlanId, plan.Revision, now);
        var job = (await store.ClaimNextAsync(plan.PlanId, "worker", now, TimeSpan.FromMinutes(1)))!;
        var staging = new Staging(); var projected = new WorkflowAlterationProjectedState([workflow]);
        var handler = new ModifyVariableAlterationHandler(executables);
        var preflight = await handler.PreflightAsync(new(job,
            new("ModifyVariable", 1, JsonSerializer.SerializeToElement(new { variableKey = "stable-name", value = "replacement" })), 0, projected, staging));
        Assert.True(preflight.IsAccepted, preflight.Failure?.Message);
        var terminal = new WorkflowAlterationJobTerminalChange(job.JobId, job.Claim!.Token, WorkflowAlterationJobStatus.Succeeded,
            [new(0, "ModifyVariable", 1, WorkflowAlterationOutcomeStatus.Succeeded, "Applied", null, now)],
            WorkflowAlterationIdentity.CreateCheckpointCommitId(job.JobId, job.AttemptCount), now);
        var request = new WorkflowAlterationCheckpointWriteRequest(job, staging.StagedChanges, terminal, projected);
        var writer = selected.GetRequiredService<IWorkflowAlterationCheckpointWriter>();
        clock.Advance(TimeSpan.FromSeconds(3)); await writer.CommitAsync(request);
        var retained = (await native.FindAsync("workflow"))!;
        Assert.Equal(5, retained.RootVariableFrame!.Revision);
        Assert.Equal("replacement", retained.RootVariableFrame.Values["stable-name"].InlineValue!.Value.GetString());
        var history = selected.GetRequiredService<IRuntimeHistoryStore>(); var page = await history.ReadPageAsync(new("workflow"));
        if (captured)
        {
            Assert.Equal(now, Assert.Single(page.Items, item => item.Name == "DurableStateRecorded").OccurredAt);
            Assert.Equal(now, Assert.Single(page.Items, item => item.Name == "AlterationApplied").OccurredAt);
            Assert.Equal(now, Assert.Single(page.Items, item => item.Name == "RuntimeAlterationJob").OccurredAt);
            Assert.DoesNotContain("stable-name", JsonSerializer.Serialize(page.Items), StringComparison.Ordinal);
            Assert.DoesNotContain("replacement", JsonSerializer.Serialize(page.Items), StringComparison.Ordinal);
        }
        else { Assert.Null(page.Head); Assert.Empty(page.Items); }
        var before = JsonSerializer.Serialize(new { Workflow = retained, Job = await store.FindJobAsync(job.JobId), page.Head, page.Items });
        clock.Advance(TimeSpan.FromSeconds(1)); await writer.CommitAsync(request);
        var afterPage = await history.ReadPageAsync(new("workflow"));
        Assert.Equal(before, JsonSerializer.Serialize(new { Workflow = await native.FindAsync("workflow"), Job = await store.FindJobAsync(job.JobId), afterPage.Head, afterPage.Items }));
    }

    private sealed class VariableResolver(ModifyVariableAlterationHandler handler) : IWorkflowAlterationHandlerResolver
    { public IWorkflowAlterationPreflightHandler Resolve(WorkflowAlterationEnvelope envelope) => handler; }
    private sealed class HistoryAccess : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new("tenant")); }
}
