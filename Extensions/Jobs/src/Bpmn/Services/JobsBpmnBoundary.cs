using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Bpmn.Core;
using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Activities;
using Elsa.Jobs.Workflows.Runtime.Contracts;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Jobs.Bpmn.Services;

/// <summary>Interprets only public BPMN projections and public Jobs native proof; never the private BPMN document.</summary>
internal static class JobsBpmnBoundary
{
    // Jobs' version-one native result/permission bodies use the default property names.
    // BPMN's private camel-case document is never interpreted by this package.
    private static readonly JsonSerializerOptions Json = new()
    { MaxDepth = 64, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    internal static JobWorkflowOwner Owner(BpmnNativeWorkAddress address)
    {
        ValidateAddress(address);
        var origin = address.Origin;
        return new(1, new(origin.Scope.Value, address.TenantId), BpmnModelProtocol.Key, address.LogicalDefinitionId,
            origin.Executable.DefinitionVersionId, origin.ExecutionId, origin.InvocationId, origin.Executable.ArtifactId,
            origin.Executable.ArtifactHash!, origin, Write(address, 65_536));
    }

    internal static void RequireOwner(JobWorkflowOwner owner, BpmnNativeWorkAddress current)
    {
        var expected = Owner(current);
        if (owner.FormatVersion != expected.FormatVersion || owner.Scope != expected.Scope || owner.ModelKind != expected.ModelKind ||
            owner.DefinitionId != expected.DefinitionId || owner.DefinitionVersion != expected.DefinitionVersion || owner.ExecutionId != expected.ExecutionId ||
            owner.InvocationId != expected.InvocationId || owner.MaterialId != expected.MaterialId || owner.MaterialSha256 != expected.MaterialSha256 ||
            owner.Origin is null || !current.Origin.IsEquivalentTo(owner.Origin) || owner.ModelAddress is not { } address ||
            !JsonElement.DeepEquals(address, expected.ModelAddress!.Value)) throw Invalid();
    }

    internal static void RequireEvidence(BpmnNativeWorkAddress address, JobNativeDeliveryEvidence evidence, BpmnNativeWorkResultSnapshot result)
    {
        RequireOwner(evidence.Address.Owner, address);
        if (evidence.Address.Scope != new JobScope(address.Origin.Scope.Value, address.TenantId) ||
            evidence.Address.WorkflowExecutionId != address.Origin.WorkerExecutionId ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(evidence.Address.Executable, address.Origin.WorkerExecutable) ||
            evidence.Address.NodeId != address.WorkerRootNodeId || evidence.Address.Contract != address.WorkerRootContract ||
            evidence.Completion.ModelInputId != address.Origin.ResultInputId || evidence.Completion.ResultId != evidence.Result.ResultId ||
            evidence.Completion.JobId != evidence.Result.JobId) throw Invalid();
        var status = evidence.NativeStatus switch
        {
            ActivityExecutionStatus.Completed => WorkflowExecutionStatus.Completed,
            ActivityExecutionStatus.Faulted => WorkflowExecutionStatus.Faulted,
            ActivityExecutionStatus.Cancelled => WorkflowExecutionStatus.Cancelled,
            _ => throw Invalid()
        };
        if (result.Status != status || string.IsNullOrWhiteSpace(result.SourceOutboxItemId) ||
            status == WorkflowExecutionStatus.Completed && evidence.Result.Outcome != JobOutcome.Succeeded ||
            status == WorkflowExecutionStatus.Faulted && evidence.Result.Outcome is not (JobOutcome.Failed or JobOutcome.TimedOut) ||
            status == WorkflowExecutionStatus.Cancelled && evidence.Result.Outcome != JobOutcome.Cancelled) throw Invalid();
        if (result.Completion is { } completion)
        {
            if (completion.InvocationId != evidence.Address.InvocationId || completion.AttemptId != evidence.NativeAttemptId ||
                completion.ContractFingerprint != evidence.Address.Contract.SchemaFingerprint || completion.OutcomeKey != "Done" ||
                evidence.NativeResult is null || completion.ResultType != new ValueTypeDescriptor(typeof(RunJobResult).FullName!)) throw Invalid();
            // The native inspector has already proved the actual typed root completion. A BPMN-owned
            // redacted projection links that same invocation/attempt/contract without rehydrating its value.
            if (completion.IsRedacted)
            {
                if (completion.Result is not null) throw Invalid();
            }
            else if (completion.Result is not { } value || !JsonElement.DeepEquals(value, Write(evidence.NativeResult))) throw Invalid();
        }
        else if (status == WorkflowExecutionStatus.Completed || status == WorkflowExecutionStatus.Faulted && evidence.NativeFaultMarker is null)
            throw Invalid();
    }

    internal static void RequireSubmissionEvidence(BpmnNativeWorkAddress address, JobNativeSubmissionEvidence evidence,
        BpmnNativeWorkResultSnapshot result, bool terminalCleanup = false)
    {
        RequireOwner(evidence.Owner, address);
        if (address.WorkerRootActivityType != SubmitJob.ActivityType || evidence.WorkerExecutionId != address.Origin.WorkerExecutionId ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(evidence.WorkerExecutable, address.Origin.WorkerExecutable) ||
            evidence.NodeId != address.WorkerRootNodeId || evidence.Contract != address.WorkerRootContract ||
            string.IsNullOrWhiteSpace(evidence.InvocationId) ||
            string.IsNullOrWhiteSpace(result.SourceOutboxItemId) || !Enum.IsDefined(evidence.Disposition)) throw Invalid();
        if (evidence.Disposition == JobNativeSubmissionDisposition.CancelledBeforeAdmission && !terminalCleanup) throw Invalid();
        if (string.IsNullOrWhiteSpace(evidence.NativeAttemptId) &&
            (evidence.NativeStatus != ActivityExecutionStatus.Cancelled || string.IsNullOrWhiteSpace(evidence.NativeCancellationReceiptId))) throw Invalid();
        var completedCancellation = terminalCleanup && evidence.Disposition == JobNativeSubmissionDisposition.CancelledBeforeAdmission &&
            evidence.NativeStatus == ActivityExecutionStatus.Completed;
        if (evidence.Disposition == JobNativeSubmissionDisposition.Accepted || completedCancellation)
        {
            var submitted = evidence.Completion;
            if (evidence.NativeStatus != ActivityExecutionStatus.Completed || result.Status != WorkflowExecutionStatus.Completed ||
                submitted is not { FormatVersion: 1, AdmissionState: "PendingAdmission" } ||
                string.IsNullOrWhiteSpace(submitted.JobId) || evidence.JobId != submitted.JobId ||
                evidence.RequestSha256 is not { Length: 64 } digest || evidence.Origin is not { } origin || origin.PayloadSha256 != digest ||
                result.Completion is not { } completion || completion.InvocationId != evidence.InvocationId ||
                completion.AttemptId != evidence.NativeAttemptId || completion.ContractFingerprint != evidence.Contract.SchemaFingerprint ||
                completion.OutcomeKey != "Done" || completion.ResultType != new ValueTypeDescriptor(typeof(SubmitJobResult).FullName!)) throw Invalid();
            if (completedCancellation ? evidence.Acceptance is not null : evidence.Acceptance?.JobId != submitted.JobId) throw Invalid();
            if (completion.IsRedacted)
            {
                if (completion.Result is not null) throw Invalid();
            }
            else if (completion.Result is not { } value || !JsonElement.DeepEquals(value, Write(submitted))) throw Invalid();
            return;
        }
        if (evidence.Completion is not null || evidence.Acceptance is not null || result.Completion is not null) throw Invalid();
        if (evidence.Disposition == JobNativeSubmissionDisposition.RejectedBeforeAdmission)
        {
            if (evidence.NativeStatus != ActivityExecutionStatus.Faulted || result.Status != WorkflowExecutionStatus.Faulted ||
                result.FaultCode != "jobs.native.admission-invalid") throw Invalid();
        }
        else if (evidence.NativeStatus != ActivityExecutionStatus.Cancelled || result.Status != WorkflowExecutionStatus.Cancelled) throw Invalid();
    }

    internal static void ValidateAddress(BpmnNativeWorkAddress address)
    {
        if (address is null || address.FormatVersion != 1 || address.Origin is null ||
            address.Origin.Protocol != new RuntimeExecutionProtocol(BpmnModelProtocol.Key, BpmnModelProtocol.Version) ||
            address.WorkerRootActivityType is not (RunJob.ActivityType or SubmitJob.ActivityType) || address.WorkerRootContract is null ||
            address.WorkerRootContract.ActivityTypeKey != address.WorkerRootActivityType || address.FaultPolicy is not
                { FormatVersion: 1, Disposition: BpmnServiceFaultDisposition.FinalFault, MaxAttempts: 1 }) throw Invalid();
        foreach (var value in new[] { address.Origin.Scope.Value, address.LogicalDefinitionId, address.Origin.Executable.DefinitionVersionId,
                     address.Origin.ExecutionId, address.Origin.InvocationId, address.Origin.Executable.ArtifactId })
            if (string.IsNullOrWhiteSpace(value) || value.Length > 256) throw Invalid();
        if (address.TenantId is { } tenant && (string.IsNullOrWhiteSpace(tenant) || tenant.Length > 256)) throw Invalid();
        var hash = address.Origin.Executable.ArtifactHash;
        if (hash is null || hash.Length != 64 || hash.Any(value => value is not (>= '0' and <= '9' or >= 'a' and <= 'f'))) throw Invalid();
    }

    internal static JsonElement Write<T>(T value, int maximumBytes = 2 * JobPayloadLimits.MaximumBytes + 65_536)
    {
        try
        {
            var json = JsonSerializer.SerializeToElement(value, Json);
            if (Encoding.UTF8.GetByteCount(json.GetRawText()) > maximumBytes) throw Invalid();
            return json;
        }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        { throw new JobException("jobs.bpmn.invalid", "The Jobs BPMN integration document is invalid or unsupported.", error); }
    }

    internal static JobException Invalid() => new("jobs.bpmn.invalid", "The Jobs result does not match its exact retained BPMN owner.");
}
