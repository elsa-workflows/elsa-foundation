using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Microsoft.Extensions.Time.Testing;

namespace Elsa.Workflows.Runtime.Tests.Alterations;

public sealed class WorkflowAlterationActorIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly WorkflowAlterationEphemeralKeyRing TestKeyRing = new();

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
        var later = new FakeTimeProvider(Now.AddMinutes(2));
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

    // #2667: an alteration takes the run's ownership lease as a drain does, so its checkpoint is fenced.
    [Fact]
    public async Task ActorCommand_CommitsUnderTheRunsOwnershipLease_AndReleasesIt()
    {
        var harness = await LeasedHarness.CreateAsync();

        await harness.Leased.ExecuteAsync(NewEnvelope(harness.Claim));

        var commit = Assert.Single(harness.Checkpoints.ListCommits()).Commit;
        Assert.NotNull(commit.ExpectedFence);
        Assert.Equal(1, commit.ExpectedFence!.FencingToken);
        Assert.Equal("node-alteration", commit.ExpectedFence.OwnerId);
        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, (await harness.Alterations.FindJobAsync(harness.Claim.JobId))!.Status);
        // Released: no lease is left for a later drain to mistake for a live owner.
        Assert.Null(Assert.Single(await harness.Liveness.ListAsync("workflow-1")).ExecutionLease);
        Assert.Null(harness.OwnershipContext.Current);
    }

    [Fact]
    public async Task ActorCommand_TakesTheRunOver_AndTheDrainThatHeldItIsRefusedAtItsNextCommit()
    {
        var harness = await LeasedHarness.CreateAsync();
        var drainLease = await harness.OwnershipOf("node-drain").AcquireAsync("workflow-1");

        await harness.Leased.ExecuteAsync(NewEnvelope(harness.Claim));

        Assert.Equal(2, Assert.Single(harness.Checkpoints.ListCommits()).Commit.ExpectedFence!.FencingToken);
        using (harness.OwnershipContext.Push(drainLease))
        {
            await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(async () =>
                await harness.Committer.CommitAsync(NewDrainCommit("drain-commit")));
        }

        Assert.Single(harness.Checkpoints.ListCommits());
        Assert.Equal(WorkflowExecutionStatus.Completed, (await harness.Workflows.FindAsync("workflow-1"))!.Status);
    }

    [Fact]
    public async Task ActorCommand_ThatLosesTheRunBeforeItCommits_IsRefused_AndItsJobStaysClaimed()
    {
        LeasedHarness? harness = null;
        harness = await LeasedHarness.CreateAsync(new CompletingHandler(async () =>
            await harness!.OwnershipOf("node-drain").AcquireAsync("workflow-1")));

        await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(async () =>
            await harness.Leased.ExecuteAsync(NewEnvelope(harness.Claim)));

        Assert.Empty(harness.Checkpoints.ListCommits());
        Assert.Equal(WorkflowExecutionStatus.Running, (await harness.Workflows.FindAsync("workflow-1"))!.Status);
        var job = (await harness.Alterations.FindJobAsync(harness.Claim.JobId))!;
        Assert.Equal(WorkflowAlterationJobStatus.Running, job.Status);
        Assert.Equal(harness.Claim.Claim!.Token, job.Claim!.Token);
        // The new owner's lease is untouched by the loser's release.
        Assert.Equal("node-drain", Assert.Single(await harness.Liveness.ListAsync("workflow-1")).ExecutionLease!.OwnerId);
    }

    [Fact]
    public async Task ActorCommand_InsideAFlowThatHoldsTheRun_RunsUnderThatLease()
    {
        var harness = await LeasedHarness.CreateAsync();
        var held = await harness.OwnershipOf("node-alteration").AcquireAsync("workflow-1");

        using (harness.OwnershipContext.Push(held))
            await harness.Leased.ExecuteAsync(NewEnvelope(harness.Claim));

        var fence = Assert.Single(harness.Checkpoints.ListCommits()).Commit.ExpectedFence!;
        Assert.Equal(held.LeaseId, fence.LeaseId);
        Assert.Equal(1, fence.FencingToken);
        // Still held: the caller that acquired it releases it.
        Assert.Equal(held.LeaseId, Assert.Single(await harness.Liveness.ListAsync("workflow-1")).ExecutionLease!.LeaseId);
    }

    [Fact]
    public void RuntimeCore_ComposesTheLeasedAlterationExecutor()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();

        var descriptor = Assert.Single(services, candidate => candidate.ServiceType == typeof(IWorkflowAlterationActorCommandExecutor));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<LeasedWorkflowAlterationActorCommandExecutor>(descriptor.ImplementationFactory!(scope.ServiceProvider));
    }

    private sealed class LeasedHarness
    {
        private LeasedHarness(CompletingHandler handler)
        {
            Checkpoints = new InMemoryRuntimeCheckpointCommitStore(
                workflowExecutionStateStore: Workflows,
                operationalStateStore: Liveness,
                rootWriteLeaseManager: PassThroughWorkflowExecutableRootWriteLeaseManager.Instance,
                alterationStore: Alterations);
            Committer = new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), Checkpoints, OwnershipContext, [], []);
            var writer = new RuntimeWorkflowAlterationCheckpointWriter(Committer, Alterations, TimeProvider.System);
            var inner = new WorkflowAlterationActorCommandExecutor(
                Alterations,
                NewProtector(),
                Workflows,
                new WorkflowAlterationJobExecutor(new SingleHandlerResolver(handler), writer, TimeProvider.System));
            Leased = new LeasedWorkflowAlterationActorCommandExecutor(
                inner, OwnershipOf("node-alteration"), OwnershipContext, heartbeatScopes: null, TimeProvider.System);
        }

        public InMemoryWorkflowAlterationStore Alterations { get; } = new();
        public InMemoryWorkflowExecutionStateStore Workflows { get; } = new();
        public InMemoryExecutionLivenessStateStore Liveness { get; } = new();
        public AsyncLocalRuntimeExecutionOwnershipContextAccessor OwnershipContext { get; } = new();
        public InMemoryRuntimeCheckpointCommitStore Checkpoints { get; }
        public RuntimeCheckpointCommitter Committer { get; }
        public LeasedWorkflowAlterationActorCommandExecutor Leased { get; }
        public WorkflowAlterationJobState Claim { get; private set; } = null!;

        public RuntimeExecutionOwnershipService OwnershipOf(string ownerId) =>
            new(Liveness, TimeProvider.System, new RuntimeExecutionOwnershipOptions { OwnerId = ownerId });

        public static async Task<LeasedHarness> CreateAsync(CompletingHandler? handler = null)
        {
            var harness = new LeasedHarness(handler ?? new CompletingHandler());
            await harness.Workflows.SaveAsync(NewWorkflow());
            harness.Claim = await AdmitSealAndClaimAsync(harness.Alterations);
            return harness;
        }
    }

    // What a drain would commit next: any checkpoint of the run, carrying the drain's ambient lease as its fence.
    private static RuntimeCheckpointCommit NewDrainCommit(string commitId) =>
        new(
            commitId,
            new RuntimeCheckpoint($"checkpoint:{commitId}", RuntimeCheckpointNames.ActivityCompleted, "workflow-1", Now, [], new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: []),
            [],
            new Dictionary<string, string>());

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

    private sealed class CompletingHandler(Func<ValueTask>? beforeStaging = null) : IWorkflowAlterationPreflightHandler
    {
        public int CallCount { get; private set; }

        public async ValueTask<WorkflowAlterationPreflightResult> PreflightAsync(WorkflowAlterationPreflightContext context, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (beforeStaging is not null)
                await beforeStaging();
            var workflow = context.ProjectedState.GetRequired<WorkflowExecutionState>();
            context.Staging.Stage(new CompleteWorkflowStagedChange(workflow with { Status = WorkflowExecutionStatus.Completed, CompletedAt = Now, UpdatedAt = Now }));
            return WorkflowAlterationPreflightResult.Accepted();
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
}
