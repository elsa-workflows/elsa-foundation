using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests.Alterations;

public sealed class WorkflowAlterationActorIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly WorkflowAlterationEphemeralKeyRing TestKeyRing = new();

    [Fact]
    public void RuntimeComposition_RegistersActorRouteCheckpointWriterAndBackgroundTasksAsScopedServices()
    {
        var services = new ServiceCollection().AddWorkflowRuntime();
        services.AddLogging();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.IsType<RuntimeWorkflowAlterationCheckpointWriter>(scope.ServiceProvider.GetRequiredService<IWorkflowAlterationCheckpointWriter>());
        Assert.IsType<WorkflowAlterationActorCommandExecutor>(scope.ServiceProvider.GetRequiredService<IWorkflowAlterationActorCommandExecutor>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<WorkflowAlterationTargetCaptureTask>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<WorkflowAlterationJobTask>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<WorkflowAlterationPlanReconciliationTask>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<WorkflowSchedulerCommandRouter>());
        Assert.Contains(provider.GetServices<IRecurringTask>(), task => task is WorkflowAlterationOrchestrationPumpTask);
    }

    [Fact]
    public async Task OrchestrationSweep_BoundsDiscoveryCapturesAndDispatches_AndPumpLifecycleIsCooperative()
    {
        var store = new InMemoryWorkflowAlterationStore();
        var workflowStore = new InMemoryWorkflowExecutionStateStore();
        await workflowStore.SaveAsync(NewWorkflow());
        await AdmitPlanAsync(store);
        var dispatcher = new RecordingDispatcher();
        var sweep = new WorkflowAlterationOrchestrationSweep(
            store,
            new WorkflowAlterationTargetCaptureTask(store, workflowStore, TimeProvider.System),
            new WorkflowAlterationJobTask(store, dispatcher, TimeProvider.System),
            new WorkflowAlterationPlanReconciliationTask(store, TimeProvider.System),
            new WorkflowAlterationPlanService(new WorkflowAlterationRegistry([]), NewProtector(), store, TimeProvider.System));
        var options = Options.Create(new WorkflowAlterationOrchestrationOptions
        {
            SweepInterval = TimeSpan.FromSeconds(1),
            MaxBackoffInterval = TimeSpan.FromSeconds(2),
            MaxPlansPerSweep = 1,
            CapturePageSize = 1,
            MaxJobClaimsPerSweep = 1,
            JobLeaseDuration = TimeSpan.FromMinutes(1),
            WorkerId = "test-worker"
        });
        var pump = WorkflowAlterationOrchestrationPumpTask.CreateForSweep(
            sweep,
            options,
            TimeProvider.System,
            NullLogger<WorkflowAlterationOrchestrationPumpTask>.Instance);

        await pump.StartAsync(CancellationToken.None);
        await pump.ExecuteAsync(CancellationToken.None);
        await pump.StopAsync(CancellationToken.None);

        var plan = await store.FindPlanAsync("plan-1");
        Assert.Equal(WorkflowAlterationPlanStatus.Running, plan!.Status);
        Assert.Single(dispatcher.Jobs);
        Assert.Equal("test-worker", dispatcher.Jobs[0].Claim!.OwnerId);
        Assert.Equal(TimeSpan.FromSeconds(1), pump.CurrentSweepInterval);
    }

    [Fact]
    public async Task OrchestrationSweep_RotatesAcrossActivePlanPages()
    {
        var store = new InMemoryWorkflowAlterationStore();
        var workflowStore = new InMemoryWorkflowExecutionStateStore();
        await workflowStore.SaveAsync(NewWorkflow("workflow-1"));
        await workflowStore.SaveAsync(NewWorkflow("workflow-2"));
        await AdmitPlanAsync(store, "plan-1", "workflow-1");
        await AdmitPlanAsync(store, "plan-2", "workflow-2");
        var dispatcher = new RecordingDispatcher();
        var sweep = new WorkflowAlterationOrchestrationSweep(
            store,
            new WorkflowAlterationTargetCaptureTask(store, workflowStore, TimeProvider.System),
            new WorkflowAlterationJobTask(store, dispatcher, TimeProvider.System),
            new WorkflowAlterationPlanReconciliationTask(store, TimeProvider.System),
            new WorkflowAlterationPlanService(new WorkflowAlterationRegistry([]), NewProtector(), store, TimeProvider.System));
        var options = new WorkflowAlterationOrchestrationOptions
        {
            MaxPlansPerSweep = 1,
            CapturePageSize = 1,
            MaxJobClaimsPerSweep = 1,
            JobLeaseDuration = TimeSpan.FromMinutes(1)
        };

        var pump = WorkflowAlterationOrchestrationPumpTask.CreateForSweep(
            sweep,
            Options.Create(options),
            TimeProvider.System,
            NullLogger<WorkflowAlterationOrchestrationPumpTask>.Instance);

        await pump.ExecuteAsync(CancellationToken.None);
        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Equal(
            ["plan-1", "plan-2"],
            dispatcher.Jobs.Select(job => job.PlanId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task OrchestrationSweep_SharesItsClaimBudgetAcrossDiscoveredPlans()
    {
        var store = new InMemoryWorkflowAlterationStore();
        var workflowStore = new InMemoryWorkflowExecutionStateStore();
        await workflowStore.SaveAsync(NewWorkflow("workflow-1"));
        await workflowStore.SaveAsync(NewWorkflow("workflow-2"));
        await AdmitPlanAsync(store, "plan-1", "workflow-1");
        await AdmitPlanAsync(store, "plan-2", "workflow-2");
        var dispatcher = new RecordingDispatcher();
        var sweep = new WorkflowAlterationOrchestrationSweep(
            store,
            new WorkflowAlterationTargetCaptureTask(store, workflowStore, TimeProvider.System),
            new WorkflowAlterationJobTask(store, dispatcher, TimeProvider.System),
            new WorkflowAlterationPlanReconciliationTask(store, TimeProvider.System),
            new WorkflowAlterationPlanService(new WorkflowAlterationRegistry([]), NewProtector(), store, TimeProvider.System));

        await sweep.ExecuteAsync(
            new WorkflowAlterationOrchestrationOptions
            {
                MaxPlansPerSweep = 2,
                CapturePageSize = 1,
                MaxJobClaimsPerSweep = 2,
                JobLeaseDuration = TimeSpan.FromMinutes(1)
            },
            "fair-worker");

        Assert.Equal(["plan-1", "plan-2"], dispatcher.Jobs.Select(job => job.PlanId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task OrchestrationSweep_RedeliversExpiredRunningWorkWhilePlanIsCancelling()
    {
        var store = new InMemoryWorkflowAlterationStore();
        var originalClaim = await AdmitSealAndClaimAsync(store);
        var later = new FixedTimeProvider(Now.AddMinutes(2));
        var planService = new WorkflowAlterationPlanService(
            new WorkflowAlterationRegistry([]),
            NewProtector(),
            store,
            later);
        await planService.CancelAsync(originalClaim.PlanId);
        var dispatcher = new RecordingDispatcher();
        var workflowStore = new InMemoryWorkflowExecutionStateStore();
        var sweep = new WorkflowAlterationOrchestrationSweep(
            store,
            new WorkflowAlterationTargetCaptureTask(store, workflowStore, later),
            new WorkflowAlterationJobTask(store, dispatcher, later),
            new WorkflowAlterationPlanReconciliationTask(store, later),
            planService,
            later);

        await sweep.ExecuteAsync(
            new WorkflowAlterationOrchestrationOptions
            {
                MaxPlansPerSweep = 1,
                CapturePageSize = 1,
                MaxJobClaimsPerSweep = 1,
                JobLeaseDuration = TimeSpan.FromMinutes(1)
            },
            "replacement-worker");

        var redelivered = Assert.Single(dispatcher.Jobs);
        Assert.Equal(originalClaim.JobId, redelivered.JobId);
        Assert.NotEqual(originalClaim.Claim!.Token, redelivered.Claim!.Token);
        Assert.Equal("replacement-worker", redelivered.Claim.OwnerId);
        Assert.Equal(WorkflowAlterationPlanStatus.Cancelling, (await store.FindPlanAsync(originalClaim.PlanId))!.Status);
    }

    [Fact]
    public async Task ActorCommand_LoadsProtectedPlan_AndCommitsWorkflowAndTerminalEvidenceInOneMandatoryCheckpoint()
    {
        var alterationStore = new InMemoryWorkflowAlterationStore();
        var workflowStore = new InMemoryWorkflowExecutionStateStore();
        var workflow = NewWorkflow();
        await workflowStore.SaveAsync(workflow);
        var claim = await AdmitSealAndClaimAsync(alterationStore);
        var checkpointStore = new InMemoryRuntimeCheckpointCommitStore(
            workflowExecutionStateStore: workflowStore,
            rootWriteLeaseManager: PassThroughWorkflowExecutableRootWriteLeaseManager.Instance,
            alterationStore: alterationStore);
        var committer = new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), checkpointStore, new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), [], []);
        var checkpointWriter = new RuntimeWorkflowAlterationCheckpointWriter(committer, alterationStore, TimeProvider.System);
        var handler = new CompletingHandler();
        var jobExecutor = new WorkflowAlterationJobExecutor(new SingleHandlerResolver(handler), checkpointWriter, TimeProvider.System);
        var actorExecutor = new WorkflowAlterationActorCommandExecutor(
            alterationStore,
            NewProtector(),
            workflowStore,
            jobExecutor);

        await actorExecutor.ExecuteAsync(NewEnvelope(claim));

        var checkpoint = Assert.Single(checkpointStore.ListCommits()).Commit;
        Assert.Equal(RuntimeCheckpointNames.RuntimeAlterationJob, checkpoint.Checkpoint.Name);
        Assert.Equal(RuntimeCheckpointPersistenceMode.Immediate, checkpointStore.ListCommits().Single().Decision.Mode);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await workflowStore.FindAsync("workflow-1"))!.Status);
        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, (await alterationStore.FindJobAsync(claim.JobId))!.Status);
        Assert.NotNull(checkpoint.StateChanges.AlterationJobTerminalChange);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ActorCommand_DuplicateAfterAcknowledgementLoss_DoesNotReplayHandlers_AndWriterReconcilesTheExactClaim()
    {
        var alterationStore = new InMemoryWorkflowAlterationStore();
        var workflowStore = new InMemoryWorkflowExecutionStateStore();
        await workflowStore.SaveAsync(NewWorkflow());
        var claim = await AdmitSealAndClaimAsync(alterationStore);
        var checkpointStore = new InMemoryRuntimeCheckpointCommitStore(
            workflowExecutionStateStore: workflowStore,
            rootWriteLeaseManager: PassThroughWorkflowExecutableRootWriteLeaseManager.Instance,
            alterationStore: alterationStore);
        var checkpointWriter = new RuntimeWorkflowAlterationCheckpointWriter(
            new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), checkpointStore, new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), [], []), alterationStore, TimeProvider.System);
        var handler = new CompletingHandler();
        var actorExecutor = new WorkflowAlterationActorCommandExecutor(
            alterationStore,
            NewProtector(),
            workflowStore,
            new WorkflowAlterationJobExecutor(new SingleHandlerResolver(handler), checkpointWriter, TimeProvider.System));
        var envelope = NewEnvelope(claim);

        await actorExecutor.ExecuteAsync(envelope);
        await actorExecutor.ExecuteAsync(envelope);

        var evidence = await checkpointWriter.FindCommittedAsync(WorkflowAlterationIdentity.CreateCheckpointCommitId(claim.JobId, claim.AttemptCount));
        Assert.NotNull(evidence);
        Assert.Equal(claim.Claim!.Token, evidence!.TerminalJobChange.ClaimToken);
        Assert.Single(checkpointStore.ListCommits());
        Assert.Equal(1, handler.CallCount);
    }

    private static async Task<WorkflowAlterationJobState> AdmitSealAndClaimAsync(InMemoryWorkflowAlterationStore store)
    {
        var submission = new WorkflowAlterationSubmission(
            new WorkflowAlterationAuthorityScope("tenant-a", "runtime", "operator"),
            WorkflowAlterationTargetSelector.ForExecutionIds(["workflow-1"]),
            [new WorkflowAlterationEnvelope("Contoso.Complete", 1, JsonSerializer.SerializeToElement(new { }))]);
        var canonical = WorkflowAlterationRequestCanonicalizer.Canonicalize(submission);
        var hash = WorkflowAlterationRequestCanonicalizer.Hash(submission);
        const string planId = "plan-1";
        var plan = WorkflowAlterationPlanState.CreateCapturing(
            planId,
            submission.AuthorityScope,
            new WorkflowAlterationOperatorProvenance("operator", null),
            $"key-hash-{planId}",
            hash,
            NewProtector().Protect(planId, "tenant-a", hash, canonical),
            submission.Target,
            Now);
        await store.AdmitAsync(plan);
        var captured = await store.CaptureAsync(planId, 0, [new WorkflowAlterationCapturedTarget("workflow-1", "tenant-a")], null);
        await store.SealAsync(planId, captured.Revision, Now);
        return (await store.ClaimNextAsync(planId, "worker", Now, TimeSpan.FromMinutes(1)))!;
    }

    private static async Task AdmitPlanAsync(
        InMemoryWorkflowAlterationStore store,
        string planId = "plan-1",
        string workflowExecutionId = "workflow-1")
    {
        var submission = new WorkflowAlterationSubmission(
            new WorkflowAlterationAuthorityScope("tenant-a", "runtime", "operator"),
            WorkflowAlterationTargetSelector.ForExecutionIds([workflowExecutionId]),
            [new WorkflowAlterationEnvelope("Contoso.Complete", 1, JsonSerializer.SerializeToElement(new { }))]);
        var canonical = WorkflowAlterationRequestCanonicalizer.Canonicalize(submission);
        var hash = WorkflowAlterationRequestCanonicalizer.Hash(submission);
        await store.AdmitAsync(WorkflowAlterationPlanState.CreateCapturing(
            planId,
            submission.AuthorityScope,
            new WorkflowAlterationOperatorProvenance("operator", null),
            $"key-hash-{planId}",
            hash,
            NewProtector().Protect(planId, "tenant-a", hash, canonical),
            submission.Target,
            Now));
    }

    private static AesGcmWorkflowAlterationPayloadProtector NewProtector() =>
        new(Options.Create(new WorkflowAlterationPayloadProtectionOptions { AllowEphemeralDevelopmentKey = true }), TestKeyRing);

    private static WorkflowExecutionState NewWorkflow(string workflowExecutionId = "workflow-1") => new(
        workflowExecutionId,
        new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
        WorkflowExecutionStatus.Running,
        null,
        Now,
        Now,
        Now,
        null,
        null,
        null,
        "tenant-a",
        new Dictionary<string, string>())
    {
        Partition = new WorkflowExecutionPartition("tenant-a"),
        Authority = new WorkflowExecutionAuthoritySnapshot("runtime", "operator")
    };

    private static WorkflowExecutionCommandEnvelope NewEnvelope(WorkflowAlterationJobState job)
    {
        var command = new WorkflowExecutionCommand(
            "command-1",
            job.WorkflowExecutionId,
            WorkflowExecutionCommandKind.AlterWorkflow,
            Now,
            JsonSerializer.SerializeToElement(new WorkflowAlterationActorCommand(job.JobId, job.Claim!.Token)),
            new Dictionary<string, string>());
        return new WorkflowExecutionCommandEnvelope(
            "envelope-1",
            job.WorkflowExecutionId,
            command,
            "alter-job-1",
            WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            Now,
            partition: new WorkflowExecutionPartition("tenant-a"));
    }

    private sealed class CompletingHandler : IWorkflowAlterationPreflightHandler
    {
        public int CallCount { get; private set; }

        public ValueTask<WorkflowAlterationPreflightResult> PreflightAsync(WorkflowAlterationPreflightContext context, CancellationToken cancellationToken = default)
        {
            CallCount++;
            var workflow = context.ProjectedState.GetRequired<WorkflowExecutionState>();
            context.Staging.Stage(new CompleteWorkflowStagedChange(workflow with { Status = WorkflowExecutionStatus.Completed, CompletedAt = Now, UpdatedAt = Now }));
            return ValueTask.FromResult(WorkflowAlterationPreflightResult.Accepted());
        }
    }

    private sealed record CompleteWorkflowStagedChange(WorkflowExecutionState Workflow) : IWorkflowAlterationRuntimeCheckpointStagedChange
    {
        public string ChangeKey => "complete-workflow";
        public void Apply(WorkflowAlterationRuntimeCheckpointStateBuilder builder) => builder.SetWorkflowExecution(Workflow);
    }

    private sealed class SingleHandlerResolver(CompletingHandler handler) : IWorkflowAlterationHandlerResolver
    {
        public IWorkflowAlterationPreflightHandler Resolve(WorkflowAlterationEnvelope envelope) =>
            envelope.Kind == "Contoso.Complete" && envelope.SchemaVersion == 1
                ? handler
                : throw new InvalidOperationException("Unexpected alteration envelope.");
    }

    private sealed class RecordingDispatcher : IWorkflowAlterationJobDispatcher
    {
        public List<WorkflowAlterationJobState> Jobs { get; } = [];

        public ValueTask<WorkflowAlterationActorDispatchResult> DispatchAsync(WorkflowAlterationJobState job, CancellationToken cancellationToken = default)
        {
            Jobs.Add(job);
            return ValueTask.FromResult(new WorkflowAlterationActorDispatchResult(
                job.JobId,
                new WorkflowExecutionCommandDispatchResult("envelope", job.WorkflowExecutionId, WorkflowExecutionCommandDispatchStatus.Accepted, Now),
                new WorkflowExecutionActorDescriptor(job.WorkflowExecutionId, "actor", "test", WorkflowExecutionActorStatus.Active, WorkflowExecutionActorCapabilities.None, Now)));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
