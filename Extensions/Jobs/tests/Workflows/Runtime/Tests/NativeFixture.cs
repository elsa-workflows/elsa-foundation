using System.Text.Json;
using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Activities;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Jobs.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Jobs.Workflows.Runtime.Tests;

internal static class NativeFixture
{
    internal static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-06T10:00:00Z");
    internal static readonly JobScope Scope = new("partition-one", "tenant-one");
    internal static readonly WorkflowExecutableIdentity Executable = new("artifact-one", "definition-one", "version-one", "1", new('a', 64));
    internal static readonly ActivityInvocationContractIdentity Contract = new(RunJob.ActivityType, "1", new('b', 64));
    internal static readonly IReadOnlyDictionary<string, string> Metadata = new Dictionary<string, string> { ["existing"] = "retained" };
    internal static readonly JobWorkflowHandlerPin Pin = new(1, new("reports", "1"), new("report-input", 1), new("report-result", 1), "reports",
        new(new(3, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1), 2, 0, true), TimeSpan.FromMinutes(1), TimeSpan.FromDays(30)), JobAssociationMode.Awaited);

    internal static ActivityExecutionState Activity()
    {
        var invocation = new RunJobInvocation(1, Scope, Executable, "workflow-one", "activity-one", "activity-one", "initial-attempt", "node-one", Contract,
            "actual-opaque-resume-target", Pin, new(Pin.InputSchema, JsonSerializer.SerializeToElement(new { Count = 42 })), null,
            new(1, Scope, "elsa.native", Executable.DefinitionId, Executable.DefinitionVersionId, "workflow-one", "activity-one",
                Executable.ArtifactId, Executable.ArtifactHash, null, null));
        var state = new RunJobState(1, JobsNativeBoundary.Write(invocation));
        return new(new("activity-one", "workflow-one", "node-one", "authored-one", RunJob.ActivityType, "1"),
            ActivityExecutionStatus.Suspended, "TriggerWaiting", 1, Now, Now, null, null, null, null, null,
            ActivitySchedulingProvenance.Empty, 0, ["actual-bookmark"], [], 0, 0, Metadata,
            ContractIdentity: Contract,
            PrivateState: new("activity-one", 1, ValueEnvelope.Inline(new(typeof(RunJobState).FullName!, schemaVersion: 1),
                JsonSerializer.SerializeToElement(state), ValueProtectionPolicy.InstanceInline), "initial-attempt", Now),
            TriggerRegistrations: [new("actual-registration", "activity-one", invocation.ResumeTargetId, JobsNativeBoundary.CompletionType,
                JobsNativeBoundary.StimulusType, JobsNativeBoundary.StimulusHash(invocation), ActivityTriggerDeduplicationPolicy.Once)]);
    }

    internal static BookmarkState Bookmark(ActivityExecutionState activity)
    {
        var invocation = JobsNativeBoundary.ReadState(activity);
        return new("actual-bookmark", invocation.WorkflowExecutionId, invocation.InvocationId, invocation.NodeId, invocation.ResumeTargetId,
            JobsNativeBoundary.StimulusType, JobsNativeBoundary.StimulusHash(invocation), null, Metadata, Now, null);
    }

    internal static WorkflowExecutionState Parent(WorkflowExecutionStatus status = WorkflowExecutionStatus.Suspended) =>
        new("workflow-one", Executable, status, null, Now, Now, Now, status.IsTerminal() ? Now.AddMinutes(1) : null,
            null, null, Scope.TenantId, Metadata) { Partition = new(Scope.PersistencePartition) };

    internal static RuntimeCheckpointCommit Commit(string name, ActivityExecutionState activity, BookmarkState? bookmark = null, WorkflowExecutionState? parent = null) =>
        new("commit-one", new("checkpoint-one", name, "workflow-one", Now, [activity.InvocationId], Metadata),
            new(parent is null ? null : new("workflow-one", RuntimeStateChangeOperation.Upsert, parent, Metadata), null,
                [new(activity.InvocationId, RuntimeStateChangeOperation.Upsert, activity, Metadata)],
                bookmark is null ? [] : [new(bookmark.BookmarkId, RuntimeStateChangeOperation.Upsert, bookmark, Metadata)], [], [], []), [], Metadata);

    internal static async Task<(ActivityExecutionState Activity, JobNativeAdmission Admission, RuntimePostCommitIntent Intent)> SavedAsync(ActivityExecutionState? source = null)
    {
        var activity = source ?? Activity();
        var checkpoint = await new JobsCheckpointEnricher(new ActivityStore()).EnrichAsync(Commit(RuntimeCheckpointNames.BookmarkCreated, activity, Bookmark(activity)));
        var saved = Assert.Single(checkpoint.StateChanges.ActivityExecutions).State;
        return (saved, JobsNativeBoundary.ReadAdmission(saved), Assert.Single(checkpoint.PostCommitIntents));
    }

    internal static ActivityExecutionState WithModelOwner(ActivityExecutionState activity)
    {
        var material = new WorkflowExecutableIdentity("model-material", "model-content-definition", "model-version", "1", new('c', 64));
        var origin = new RuntimeModelNativeWorkOrigin(1, new(Scope.PersistencePartition), "model-root", new("elsa.bpmn.direct", 1), material,
            "model-incarnation", "service-binding", "service-invocation", "service-attempt", Executable, activity.Execution.WorkflowExecutionId,
            "source-commit:start-intent", "start-intent", "Elsa.Bpmn.NativeWorkResult.v1", "exact-root-result-input");
        var invocation = JobsNativeBoundary.ReadState(activity) with { Owner = new(1, Scope, "elsa.bpmn", "logical-process", material.DefinitionVersionId,
            origin.ExecutionId, origin.InvocationId, material.ArtifactId, material.ArtifactHash, origin, JsonSerializer.SerializeToElement(new { FormatVersion = 1, Origin = origin })) };
        var previous = activity.PrivateState!;
        return activity with { PrivateState = new(previous.InvocationId, previous.StateVersion,
            ValueEnvelope.Inline(new(typeof(RunJobState).FullName!, schemaVersion: 1),
                JobsNativeBoundary.Write(new RunJobState(1, JobsNativeBoundary.Write(invocation))), ValueProtectionPolicy.InstanceInline),
            previous.ProducedByAttemptId, previous.CommittedAt) };
    }

    internal static (JobTransport Transport, RunJobCompletion Completion) Selected(JobNativeAdmission admission)
    {
        var payload = new JobPayload(Pin.ResultSchema, JsonSerializer.SerializeToElement(new { Total = 84 }));
        var result = new JobResult("selected-result", "canonical-winner-job", "job-attempt", JobOutcome.Succeeded, payload, null,
            JobPayloadDigest.Compute(payload), Now.AddSeconds(1));
        var permission = new JobContinuationPermission(JobsNativeBoundary.ContinuationId(admission.Address, result, admission.Request.Association!),
            result.JobId, result.ResultId, JobsNativeBoundary.ModelInputId(admission.Address, result), admission.Request.Association!, Now.AddSeconds(2));
        var transport = new JobTransport { Permission = permission, Snapshot = new(Scope, result.JobId, admission.RequestSha256, admission.Request,
            JobState.Terminal, 3, Now, Now, null, result, null, false, null),
            Origin = new(admission.Origin, admission.RequestSha256, false, false, new(result.JobId, Now, Now.AddDays(30), 3)),
            Delivery = new(result.JobId, result.ResultId, admission.Request.Association!, permission, null) };
        return (transport, new(1, result.JobId, admission.RequestSha256, result.ResultId, permission.ContinuationId, permission.ModelInputId,
            JobsNativeBoundary.Write(result), JobsNativeBoundary.Write(permission)));
    }
}
