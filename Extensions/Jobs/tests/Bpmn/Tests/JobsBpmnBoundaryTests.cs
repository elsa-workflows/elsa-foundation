using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Jobs.Bpmn.Services;
using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Activities;
using Elsa.Jobs.Workflows.Runtime.Contracts;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Jobs.Bpmn.Tests;

/// <summary>Public owner/grant contract controls. Actual native/BPMN runtime and package acceptance are separate.</summary>
public sealed class JobsBpmnBoundaryTests
{
    [Fact]
    public void Logical_process_gates_remain_distinct_from_content_material_and_exact_versions()
    {
        var first = new Fixture();
        var second = new Fixture(material: "material-b", version: "version-b");
        Assert.Equal("orders", first.Owner.DefinitionId);
        Assert.Equal(first.Owner.DefinitionId, second.Owner.DefinitionId);
        Assert.NotEqual(first.Owner.DefinitionVersion, second.Owner.DefinitionVersion);
        Assert.NotEqual(first.Owner.MaterialId, second.Owner.MaterialId);
        Assert.Equal(first.Address.Origin.Executable.ArtifactId, first.Owner.MaterialId);
        Assert.Equal(first.Address.Origin.Executable.DefinitionVersionId, first.Owner.DefinitionVersion);
    }

    [Theory]
    [InlineData("wrong-root")]
    [InlineData("retry-worker")]
    [InlineData("oversized-logical-id")]
    public void Unsupported_owner_shapes_cannot_fall_back_to_helper_native_ownership(string variant)
    {
        var f = new Fixture();
        var address = new BpmnNativeWorkAddress(1, f.Address.Origin, null,
            variant == "oversized-logical-id" ? new string('x', 257) : "orders", "service", "run-job-root",
            variant == "wrong-root" ? "Other.Activity" : RunJob.ActivityType, "1.0.0", f.Address.WorkerRootContract,
            variant == "retry-worker" ? new(1, BpmnServiceFaultDisposition.HoldForRetry, 2) : new(1, BpmnServiceFaultDisposition.FinalFault, 1));
        Assert.Equal("jobs.bpmn.invalid", Assert.Throws<JobException>(() => JobsBpmnBoundary.Owner(address)).Code);
    }

    [Fact]
    public async Task Helper_completion_without_accepted_root_input_keeps_jobs_delivery_pending()
    {
        var f = new Fixture();
        var source = new JobsBpmnAssociationSource(f.Consumer(new(f.Address, BpmnNativeWorkObservationKind.NotConsumed,
            1, RuntimeModelLifecycle.Running, null, null, null)));
        var result = await source.InspectAsync(f.Owner, f.Evidence);
        Assert.Equal(JobWorkflowOwnerDisposition.NotConsumed, result.Disposition);
        Assert.Null(result.ModelReceiptId);
        Assert.Null(result.ResultId);
    }

    [Fact]
    public async Task Exact_root_terminal_proof_can_settle_without_a_manufactured_native_job_receipt()
    {
        var f = new Fixture(JobOutcome.Cancelled);
        // A cancellation-settlement input and positive root terminal proof can coexist.
        var accepted = new BpmnNativeWorkInput(f.Address.Origin.ResultInputId, new string('e', 64), 3, Fixture.At, f.Projected);
        var observation = new BpmnNativeWorkObservation(f.Address, BpmnNativeWorkObservationKind.AcceptedRootInput, 3,
            RuntimeModelLifecycle.TerminalSettled, "Cancelled", "input-proof", accepted, "terminal-proof");
        var source = new JobsBpmnAssociationSource(f.Consumer(observation));
        var result = await source.InspectAsync(f.Owner, null);
        Assert.Equal(JobWorkflowOwnerDisposition.ProvenTerminal, result.Disposition);
        Assert.Equal("terminal-proof", result.ModelReceiptId);
        Assert.Null(result.ResultId);
    }

    [Fact]
    public async Task Accepted_root_input_requires_the_original_full_owner_and_final_native_binding()
    {
        var f = new Fixture();
        var input = new BpmnNativeWorkInput(f.Address.Origin.ResultInputId, new string('e', 64), 2, Fixture.At, f.Projected);
        var source = new JobsBpmnAssociationSource(f.Consumer(new(f.Address, BpmnNativeWorkObservationKind.AcceptedRootInput,
            2, RuntimeModelLifecycle.Running, null, "input-proof", input)));
        var proof = await source.InspectAsync(f.Owner, f.Evidence);
        Assert.Equal(JobWorkflowOwnerDisposition.AcceptedInput, proof.Disposition);
        Assert.Equal(f.Result.ResultId, proof.ResultId);
        Assert.Equal(f.Result.PayloadSha256, proof.ResultSha256);
        await Assert.ThrowsAsync<JobException>(() => source.InspectAsync(f.Owner with { MaterialId = "other-material" }, f.Evidence).AsTask());
        await Assert.ThrowsAsync<JobException>(() => source.InspectAsync(f.Owner, f.Evidence with { NativeAttemptId = "other-attempt" }).AsTask());
    }

    [Theory]
    [InlineData(JobOutcome.Succeeded)]
    [InlineData(JobOutcome.Cancelled)]
    [InlineData(JobOutcome.Failed)]
    [InlineData(JobOutcome.TimedOut)]
    public async Task Every_selected_job_outcome_requires_its_exact_existing_root_input_permission(JobOutcome outcome)
    {
        var f = new Fixture(outcome);
        var query = new QueryState();
        var validator = new JobsBpmnResultValidator(f.Consumer(), new Inspector(f.Evidence), query.Port());
        var missing = await Assert.ThrowsAsync<JobException>(() => validator.ValidateAsync(f.Context).AsTask());
        Assert.Equal("jobs.bpmn.invalid", missing.Code);
        Assert.Equal(1, query.Reads);
        query.Permission = f.Permission;
        await validator.ValidateAsync(f.Context);
        Assert.Equal(2, query.Reads);
        Assert.Equal(f.Owner.Scope, query.Scope);
        Assert.Equal(f.Result.JobId, query.JobId);
        Assert.Equal(f.Permission.ContinuationId, query.ContinuationId);
        query.Permission = f.Permission with { ModelInputId = "other-root-input" };
        await Assert.ThrowsAsync<JobException>(() => validator.ValidateAsync(f.Context).AsTask());
        query.Permission = f.Permission with { Association = f.Permission.Association with { DefinitionId = "other-process" } };
        await Assert.ThrowsAsync<JobException>(() => validator.ValidateAsync(f.Context).AsTask());
    }

    [Fact]
    public async Task Unowned_native_result_skips_jobs_reads_and_temporary_query_failure_retains_its_cause()
    {
        var f = new Fixture();
        var query = new QueryState { Error = new JobPersistenceException("find-continuation", new IOException("owned test fault")) };
        await new JobsBpmnResultValidator(f.Consumer(), new Inspector(null), query.Port()).ValidateAsync(f.Context);
        Assert.Equal(0, query.Reads);
        var failure = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() =>
            new JobsBpmnResultValidator(f.Consumer(), new Inspector(f.Evidence), query.Port()).ValidateAsync(f.Context).AsTask());
        Assert.Equal("jobs.bpmn.result-unavailable", failure.Code);
        Assert.Same(query.Error, failure.InnerException);
    }

    [Fact]
    public async Task Temporary_native_evidence_failure_defers_before_any_jobs_query_and_preserves_its_cause()
    {
        var f = new Fixture();
        var query = new QueryState { Permission = f.Permission };
        var sourceFailure = new JobException("jobs.native.evidence-unavailable", "The native evidence read is temporarily unavailable.",
            new IOException("owned test read failure"));
        var failure = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() =>
            new JobsBpmnResultValidator(f.Consumer(), new UnavailableInspector(sourceFailure), query.Port()).ValidateAsync(f.Context).AsTask());
        Assert.Equal("jobs.bpmn.result-unavailable", failure.Code);
        Assert.Same(sourceFailure, failure.InnerException);
        Assert.Equal(0, query.Reads);
    }

    [Theory]
    [InlineData(BpmnNativeWorkInputDisposition.Accepted, RuntimeModelLifecycle.Running)]
    [InlineData(BpmnNativeWorkInputDisposition.Rejected, RuntimeModelLifecycle.TerminalPendingSettlement)]
    [InlineData(BpmnNativeWorkInputDisposition.Rejected, RuntimeModelLifecycle.TerminalSettled)]
    public async Task Delivery_contribution_confirms_native_settlement_after_accepted_input_or_positive_root_terminality(
        BpmnNativeWorkInputDisposition disposition, RuntimeModelLifecycle lifecycle)
    {
        var f = new Fixture();
        var settlement = new Settlement();
        var context = f.Delivery(disposition, lifecycle);
        await new JobsBpmnResultDeliveryContributor(f.Consumer(), new Inspector(f.Evidence), settlement).CompleteAsync(context);
        Assert.Equal(1, settlement.Calls);
        Assert.Same(f.Context.Worker, settlement.Worker);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rejected_transport_receipt_without_positive_terminal_proof_keeps_owned_result_owed(bool malformedTerminal)
    {
        var f = new Fixture();
        var settlement = new Settlement();
        var context = f.Delivery(BpmnNativeWorkInputDisposition.Rejected,
            malformedTerminal ? RuntimeModelLifecycle.TerminalSettled : RuntimeModelLifecycle.Running,
            omitTerminalProof: malformedTerminal);
        var error = await Assert.ThrowsAsync<RuntimePostCommitDeliveryException>(() =>
            new JobsBpmnResultDeliveryContributor(f.Consumer(), new Inspector(f.Evidence), settlement).CompleteAsync(context).AsTask());
        Assert.Equal(PostCommitFailureKind.Transient, error.Kind);
        Assert.Equal("jobs.bpmn.settlement-pending", error.Code);
        Assert.Equal(0, settlement.Calls);
    }

    [Fact]
    public async Task Delivery_contribution_defers_native_phase_classification_and_acknowledgement_loss_to_native_authority()
    {
        var f = new Fixture();
        var settlement = new Settlement { Error = new JobException("jobs.native.settlement-unavailable", "The exact origin proof remains owed.") };
        var contributor = new JobsBpmnResultDeliveryContributor(f.Consumer(), new Inspector(null), settlement);
        var context = f.Delivery(BpmnNativeWorkInputDisposition.Rejected, RuntimeModelLifecycle.Running);
        var error = await Assert.ThrowsAsync<RuntimePostCommitDeliveryException>(() => contributor.CompleteAsync(context).AsTask());
        Assert.Equal(PostCommitFailureKind.Transient, error.Kind);
        Assert.Same(settlement.Error, error.InnerException);
        Assert.Equal(1, settlement.Calls);
        settlement.Error = null;
        await contributor.CompleteAsync(context);
        Assert.Equal(2, settlement.Calls);
    }

    [Fact]
    public void Useful_native_result_larger_than_64KiB_keeps_the_shared_jobs_payload_envelope()
    {
        var f = new Fixture(value: new string('x', 70_000));
        Assert.True(f.Result.Payload!.Value.GetRawText().Length > 65_536);
        JobsBpmnBoundary.RequireEvidence(f.Address, f.Evidence, f.Projected);
        var forged = new BpmnNativeWorkResultSnapshot(f.Projected.SourceOutboxItemId, f.Projected.Status,
            new(f.Projected.Completion!.InvocationId, f.Projected.Completion.AttemptId, f.Projected.Completion.ContractFingerprint,
                "Other", f.Projected.Completion.ResultType, f.Projected.Completion.Result, false), null);
        Assert.Throws<JobException>(() => JobsBpmnBoundary.RequireEvidence(f.Address, f.Evidence, forged));
        var hidden = new BpmnNativeWorkResultSnapshot(f.Projected.SourceOutboxItemId, f.Projected.Status,
            new(f.Projected.Completion.InvocationId, f.Projected.Completion.AttemptId, f.Projected.Completion.ContractFingerprint,
                "Done", f.Projected.Completion.ResultType, null, true), null);
        JobsBpmnBoundary.RequireEvidence(f.Address, f.Evidence, hidden);
    }

    private sealed class Fixture
    {
        internal static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-11-06T00:00:00Z");
        internal BpmnNativeWorkAddress Address { get; }
        internal JobWorkflowOwner Owner { get; }
        internal JobResult Result { get; }
        internal JobContinuationPermission Permission { get; }
        internal JobNativeDeliveryEvidence Evidence { get; }
        internal BpmnNativeWorkResultSnapshot Projected { get; }
        internal BpmnNativeWorkResultValidation Context { get; }

        internal Fixture(JobOutcome outcome = JobOutcome.Succeeded, string material = "material-a", string version = "version-a", string? value = null)
        {
            var pin = new WorkflowExecutableIdentity(material, "bpmn-model:" + material, version, "1.0.0", new string('a', 64));
            var workerPin = new WorkflowExecutableIdentity("worker-material", "worker", "worker-version", "1.0.0", new string('b', 64));
            var origin = new RuntimeModelNativeWorkOrigin(1, new("partition"), "bpmn-root", new(BpmnModelProtocol.Key, BpmnModelProtocol.Version),
                pin, "incarnation", "worker-binding", "bpmn-service-invocation", "bpmn-service-attempt", workerPin,
                "native-worker", "model-start:worker-start", "worker-start", "Elsa.Bpmn.NativeWorkResult.v1", "root-result-input");
            var contract = new ActivityInvocationContractIdentity(RunJob.ActivityType, "1.0.0", new string('c', 64));
            Address = new(1, origin, null, "orders", "service", "run-job-root", RunJob.ActivityType, "1.0.0", contract,
                new(1, BpmnServiceFaultDisposition.FinalFault, 1));
            Owner = JobsBpmnBoundary.Owner(Address);
            var address = new JobNativeAddress(1, Owner.Scope, workerPin, origin.WorkerExecutionId, "native-invocation", "native-operation",
                "native-initial-attempt", Address.WorkerRootNodeId, contract, "bookmark", "registration", "resume-target", "stimulus", "stimulus-hash",
                "provider", new(typeof(RunJobCompletion).FullName!, schemaVersion: 1), ActivityTriggerDeduplicationPolicy.Once, Owner);
            var payload = outcome == JobOutcome.Succeeded ? new JobPayload(new("report.result", 1), JsonSerializer.SerializeToElement(new { total = 52m, value })) : null;
            Result = new("selected-result", "canonical-job", "job-attempt", outcome, payload, null, JobPayloadDigest.Compute(payload), At);
            var association = new JobAssociation(Owner.ModelKind, Owner.DefinitionId, Owner.DefinitionVersion, Owner.ExecutionId, Owner.InvocationId,
                Owner.MaterialId, Owner.MaterialSha256, JobAssociationMode.Awaited, new(new("elsa.jobs.native-address", 1), JsonSerializer.SerializeToElement(address)));
            Permission = new("continuation", Result.JobId, Result.ResultId, origin.ResultInputId, association, At);
            var completion = new RunJobCompletion(1, Result.JobId, new string('d', 64), Result.ResultId, Permission.ContinuationId,
                origin.ResultInputId, JsonSerializer.SerializeToElement(Result), JsonSerializer.SerializeToElement(Permission));
            var nativeResult = outcome == JobOutcome.Succeeded ? new RunJobResult(1, Result.JobId, Result.ResultId,
                Permission.ContinuationId, origin.ResultInputId, outcome, payload, Result.PayloadSha256, null) : null;
            var status = outcome == JobOutcome.Succeeded ? ActivityExecutionStatus.Completed : outcome == JobOutcome.Cancelled
                ? ActivityExecutionStatus.Cancelled : ActivityExecutionStatus.Faulted;
            Evidence = new(address, completion, Result, "native-delivery", "native-resume-attempt", status, nativeResult,
                status == ActivityExecutionStatus.Faulted ? "jobs.terminal.v1:retained-marker" : null);
            var workerStatus = status == ActivityExecutionStatus.Completed ? WorkflowExecutionStatus.Completed : status == ActivityExecutionStatus.Cancelled
                ? WorkflowExecutionStatus.Cancelled : WorkflowExecutionStatus.Faulted;
            Projected = new("native-terminal:result", workerStatus, nativeResult is not null
                ? new(address.InvocationId, Evidence.NativeAttemptId, contract.SchemaFingerprint, "Done", new(typeof(RunJobResult).FullName!),
                    JsonSerializer.SerializeToElement(nativeResult), false) : null, status == ActivityExecutionStatus.Faulted ? "jobs.job.failed" : null);
            var worker = new WorkflowExecutionState(origin.WorkerExecutionId, workerPin, workerStatus, null, At, At, At, At, null,
                null, null, new Dictionary<string, string>()) { Partition = new("partition"), ModelWorkOrigin = origin };
            Context = new(origin, origin.ResultInputId, new string('e', 64), Projected, worker);
        }

        internal IBpmnNativeWorkConsumer Consumer(BpmnNativeWorkObservation? observation = null) => new ConsumerReader(Address,
            observation ?? new(Address, BpmnNativeWorkObservationKind.NotConsumed, 1, RuntimeModelLifecycle.Running, null, null, null));

        internal BpmnNativeWorkResultDelivery Delivery(BpmnNativeWorkInputDisposition disposition, RuntimeModelLifecycle lifecycle,
            bool omitTerminalProof = false)
        {
            var receipt = new RuntimeModelInputReceipt(Address.Origin.Scope, Address.Origin.ExecutionId, Address.Origin.ResultInputId,
                "native-result", Context.ContentFingerprint, 2, At, Address.Origin.InvocationId, Address.Origin.AttemptId,
                JsonSerializer.SerializeToElement(new { status = disposition.ToString() }));
            var terminal = lifecycle is RuntimeModelLifecycle.TerminalPendingSettlement or RuntimeModelLifecycle.TerminalSettled;
            return new(Address.Origin, Context.Worker, Projected, receipt, disposition, 2, lifecycle,
                terminal ? "Cancelled" : null, terminal && !omitTerminalProof ? "exact-root-terminal-proof" : null);
        }
    }

    private sealed class ConsumerReader(BpmnNativeWorkAddress address, BpmnNativeWorkObservation observation) : IBpmnNativeWorkConsumer
    {
        public ValueTask<BpmnNativeWorkAddress> ResolveAsync(RuntimeModelNativeWorkOrigin origin, CancellationToken cancellationToken = default) => ValueTask.FromResult(address);
        public ValueTask<BpmnNativeWorkObservation> InspectAsync(RuntimeModelNativeWorkOrigin origin, CancellationToken cancellationToken = default) => ValueTask.FromResult(observation);
    }

    private sealed class Inspector(JobNativeDeliveryEvidence? evidence) : IJobNativeDeliveryInspector
    {
        public ValueTask<JobNativeDeliveryEvidence?> InspectWorkerAsync(WorkflowExecutionState worker, CancellationToken cancellationToken = default) => ValueTask.FromResult(evidence);
    }

    private sealed class UnavailableInspector(JobException failure) : IJobNativeDeliveryInspector
    {
        public ValueTask<JobNativeDeliveryEvidence?> InspectWorkerAsync(WorkflowExecutionState worker, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<JobNativeDeliveryEvidence?>(failure);
    }

    private sealed class Settlement : IJobNativeResultSettlement
    {
        internal int Calls;
        internal WorkflowExecutionState? Worker;
        internal JobException? Error;
        public ValueTask SettleWorkerAsync(WorkflowExecutionState worker, CancellationToken cancellationToken = default)
        {
            Calls++; Worker = worker;
            return Error is null ? ValueTask.CompletedTask : ValueTask.FromException(Error);
        }
    }

    private sealed class QueryState
    {
        internal JobContinuationPermission? Permission;
        internal Exception? Error;
        internal int Reads;
        internal JobScope? Scope;
        internal string? JobId;
        internal string? ContinuationId;
        internal IJobQueries Port()
        {
            var proxy = DispatchProxy.Create<IJobQueries, QueryProxy>();
            ((QueryProxy)proxy).Handle = (method, arguments) =>
            {
                Assert.Equal(nameof(IJobQueries.FindContinuationAsync), method.Name);
                Reads++; Scope = (JobScope)arguments![0]!; JobId = (string)arguments[1]!; ContinuationId = (string)arguments[2]!;
                if (Error is not null) throw Error;
                return ValueTask.FromResult(Permission);
            };
            return proxy;
        }
    }

    public class QueryProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handle = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handle(targetMethod!, args);
    }
}
