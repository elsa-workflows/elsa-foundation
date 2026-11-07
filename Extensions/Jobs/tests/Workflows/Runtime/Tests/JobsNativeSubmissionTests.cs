using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Activities;
using Elsa.Jobs.Workflows.Runtime.Contracts;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Jobs.Workflows.Runtime.Services;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using ActivityTransitionKind = Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind;

namespace Elsa.Jobs.Workflows.Runtime.Tests;

public sealed class JobsNativeSubmissionTests
{
    [Theory]
    [InlineData(JobAssociationMode.Independent, 2)]
    [InlineData(JobAssociationMode.Detached, 1)]
    public async Task Completion_atomically_freezes_materialized_submission_without_any_saved_wait(JobAssociationMode mode, int sources)
    {
        var prepared = SubmissionFixture.Completed(mode);
        var commit = await new JobsSubmissionCheckpointEnricher(new ActivityStore())
            .EnrichAsync(NativeFixture.Commit(RuntimeCheckpointNames.ActivityCompleted, prepared.Activity));
        var saved = Assert.Single(commit.StateChanges.ActivityExecutions).State;
        var admission = JobsNativeSubmissionBoundary.ReadAdmission(saved);
        Assert.Equal(sources, commit.PostCommitIntents.Count); Assert.Null(saved.PrivateState);
        Assert.Empty(saved.BookmarkIds); Assert.Empty(saved.TriggerRegistrations!); Assert.Null(admission.Request.Association!.Continuation);
        Assert.Equal(mode, admission.Request.Association.Mode); Assert.Equal(42, admission.Request.Input.Value.GetProperty("Count").GetInt32());
        Assert.Equal(prepared.Receipt.JobId, admission.ProposedJobId); Assert.Equal("PendingAdmission", prepared.Receipt.AdmissionState);
        Assert.True(JobsNativeBoundary.Equivalent(admission, JobsNativeSubmissionIntentHandler.Read(Assert.Single(commit.PostCommitIntents, i => i.Kind == JobsNativeSubmissionBoundary.StartKind))));
        await new JobsSubmissionCheckpointEnricher(new ActivityStore()).ValidateFinalAsync(commit);
    }

    [Theory]
    [InlineData(JobAssociationMode.Independent)]
    [InlineData(JobAssociationMode.Detached)]
    public async Task Admission_survives_normal_parent_completion_and_matching_lost_ack_replay(JobAssociationMode mode)
    {
        var (saved, admission, start, _) = await SubmissionFixture.SavedAsync(mode);
        var activities = new ActivityStore(); activities.States.Add(saved.InvocationId, saved);
        var parent = new WorkflowStore(NativeFixture.Parent(WorkflowExecutionStatus.Completed)); var jobs = new JobTransport();
        var handler = SubmissionFixture.Handler(jobs, parent, activities);
        await handler.HandleIntentAsync(start); parent.Missing = true; activities.States.Clear();
        await handler.HandleIntentAsync(start);
        var accepted = Assert.Single(jobs.Admissions);
        Assert.Equal(admission.ProposedJobId, accepted.ProposedJobId); Assert.Null(accepted.Request.Association!.Continuation);
        Assert.Equal(admission.Origin, jobs.Origin!.Origin); Assert.Empty(jobs.Cancellations); Assert.Empty(jobs.Acknowledgements);
    }

    [Theory]
    [InlineData(JobAssociationMode.Independent, 0, 1)]
    [InlineData(JobAssociationMode.Detached, 1, 0)]
    public async Task Explicit_parent_cancellation_is_inherited_only_by_independent_submission(JobAssociationMode mode, int admitted, int cancelled)
    {
        var (saved, _, start, _) = await SubmissionFixture.SavedAsync(mode);
        var activities = new ActivityStore(); activities.States.Add(saved.InvocationId, saved); var jobs = new JobTransport();
        await SubmissionFixture.Handler(jobs, new(NativeFixture.Parent(WorkflowExecutionStatus.Cancelled)), activities).HandleIntentAsync(start);
        Assert.Equal(admitted, jobs.Admissions.Count); Assert.Equal(cancelled, jobs.Cancellations.Count);
        Assert.Empty(jobs.Acknowledgements);
    }

    [Fact]
    public async Task Independent_active_owner_obligation_outlives_delivered_admission_and_cancels_later()
    {
        var (saved, admission, start, ownership) = await SubmissionFixture.SavedAsync();
        var activities = new ActivityStore(); activities.States.Add(saved.InvocationId, saved);
        var parent = new WorkflowStore(NativeFixture.Parent()); var jobs = new JobTransport();
        var handler = SubmissionFixture.Handler(jobs, parent, activities);
        await handler.HandleIntentAsync(start);
        await Assert.ThrowsAsync<RuntimePostCommitDeliveryException>(() => handler.HandleIntentAsync(ownership!).AsTask());
        Assert.Single(jobs.Admissions); Assert.Empty(jobs.Cancellations);
        var ownerSource = SubmissionFixture.Source(ownership!, RuntimePostCommitOutboxStatus.FailedRetryable);
        var admissionSource = SubmissionFixture.Source(start, RuntimePostCommitOutboxStatus.Delivered);
        Assert.False(admissionSource.RetainsPublisherForRequiredDelivery(parent.State.WorkflowExecutionId));
        Assert.True(ownerSource.RetainsPublisherForRequiredDelivery(parent.State.WorkflowExecutionId));
        Assert.Equal(admission.Submission.InvocationId, ownerSource.Intent.ActivityExecutionId);
        parent.State = NativeFixture.Parent(WorkflowExecutionStatus.Cancelled);
        await handler.HandleIntentAsync(ownership!);
        Assert.Equal(admission.Origin, Assert.Single(jobs.Cancellations).Origin); Assert.True(jobs.Origin!.Cancelled);
        Assert.Single(jobs.Admissions); Assert.Empty(jobs.Acknowledgements);
    }

    [Theory]
    [InlineData(WorkflowExecutionStatus.Completed)]
    [InlineData(WorkflowExecutionStatus.Faulted)]
    public async Task Normal_terminal_owner_releases_independent_cancellation_without_awaiting_job_result(WorkflowExecutionStatus status)
    {
        var (saved, _, start, ownership) = await SubmissionFixture.SavedAsync();
        var activities = new ActivityStore(); activities.States.Add(saved.InvocationId, saved);
        var parent = new WorkflowStore(NativeFixture.Parent()); var jobs = new JobTransport();
        var handler = SubmissionFixture.Handler(jobs, parent, activities); await handler.HandleIntentAsync(start);
        parent.State = NativeFixture.Parent(status); await handler.HandleIntentAsync(ownership!);
        Assert.Null(jobs.Snapshot?.Result); Assert.Single(jobs.Admissions); Assert.Empty(jobs.Cancellations); Assert.Empty(jobs.Acknowledgements);
    }

    [Fact]
    public async Task Parent_cancel_checkpoint_finds_completed_independent_child_after_admission_source_delivered()
    {
        var (saved, admission, _, _) = await SubmissionFixture.SavedAsync();
        var activities = new ActivityStore(); activities.States.Add(saved.InvocationId, saved);
        var parent = NativeFixture.Parent(WorkflowExecutionStatus.Cancelled);
        var commit = NativeFixture.Commit(RuntimeCheckpointNames.WorkflowCancelled, saved, parent: parent);
        commit = commit with { StateChanges = new(new("workflow-one", RuntimeStateChangeOperation.Upsert, parent, NativeFixture.Metadata), null, [], [], [], [], []) };
        var enriched = await new JobsSubmissionCheckpointEnricher(activities).EnrichAsync(commit);
        var cancel = Assert.Single(enriched.PostCommitIntents); Assert.Equal(JobsNativeSubmissionBoundary.CancelKind, cancel.Kind);
        Assert.True(JobsNativeBoundary.Equivalent(admission, JobsNativeSubmissionIntentHandler.Read(cancel)));
    }

    [Fact]
    public async Task Conflicting_canonical_job_id_never_confirms_the_returned_pending_receipt()
    {
        var (saved, admission, start, _) = await SubmissionFixture.SavedAsync();
        var jobs = new JobTransport { Origin = new(admission.Origin, admission.RequestSha256, false, false,
            new("different-canonical-job", NativeFixture.Now, NativeFixture.Now.AddDays(30), 1)) };
        var handler = SubmissionFixture.Handler(jobs, new(NativeFixture.Parent()) { Missing = true }, new());
        var error = await Assert.ThrowsAsync<RuntimePostCommitDeliveryException>(() => handler.HandleIntentAsync(start).AsTask());
        Assert.Equal(PostCommitFailureKind.Transient, error.Kind); Assert.Empty(jobs.Admissions); Assert.Empty(jobs.Cancellations);
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("material")]
    [InlineData("missing")]
    public async Task First_submission_requires_retained_exact_parent_and_completion(string change)
    {
        var (saved, _, start, _) = await SubmissionFixture.SavedAsync();
        var activities = new ActivityStore(); activities.States.Add(saved.InvocationId, saved); var jobs = new JobTransport();
        var state = change == "tenant" ? NativeFixture.Parent() with { TenantId = "other" }
            : change == "material" ? NativeFixture.Parent() with { PinnedExecutable = NativeFixture.Executable with { ArtifactHash = new('f', 64) } }
            : NativeFixture.Parent();
        await Assert.ThrowsAsync<RuntimePostCommitDeliveryException>(() => SubmissionFixture.Handler(jobs, new(state) { Missing = change == "missing" }, activities)
            .HandleIntentAsync(start).AsTask());
        Assert.Empty(jobs.Admissions); Assert.Empty(jobs.Cancellations);
    }

    [Theory]
    [InlineData(JobAssociationMode.Independent)]
    [InlineData(JobAssociationMode.Detached)]
    public async Task Nested_submission_keeps_actual_invocation_owner_and_full_definition_pin(JobAssociationMode mode)
    {
        var completed = SubmissionFixture.Completed(mode, nested: true);
        var owner = await new JobsNativeOwnerResolver([]).ResolveAsync(NativeFixture.Parent(), completed.Activity, CancellationToken.None);
        Assert.Equal("nested-submission", owner.InvocationId); Assert.Equal("workflow-one", owner.ExecutionId);
        Assert.Equal(NativeFixture.Executable.DefinitionId, owner.DefinitionId); Assert.Equal(NativeFixture.Executable.ArtifactHash, owner.MaterialSha256);
        Assert.Equal("PendingAdmission", completed.Receipt.AdmissionState);
        var submission = JobsNativeBoundary.Read<JobNativeSubmission>(completed.Receipt.Submission);
        Assert.Equal(owner, submission.Owner); Assert.Equal("container", completed.Activity.ParentActivityExecutionId);
    }

    [Fact]
    public async Task Prestart_cancellation_needs_positive_committed_phase_not_missing_submission_metadata()
    {
        var completed = SubmissionFixture.Completed();
        var cancelled = completed.Activity with { Status = ActivityExecutionStatus.Cancelled, Completion = null, Attempts = [], StartedAt = null };
        var parent = NativeFixture.Parent(WorkflowExecutionStatus.Cancelled);
        Assert.False(JobsNativeSubmissionInspector.PreAdmissionCancellation(parent, cancelled));
        var commit = await new JobsSubmissionCheckpointEnricher(new ActivityStore())
            .EnrichAsync(NativeFixture.Commit(RuntimeCheckpointNames.WorkflowCancelled, cancelled, parent: parent));
        var marked = Assert.Single(commit.StateChanges.ActivityExecutions).State;
        Assert.True(JobsNativeSubmissionInspector.PreAdmissionCancellation(parent, marked)); Assert.Empty(commit.PostCommitIntents);
        Assert.False(JobsNativeSubmissionInspector.PreAdmissionCancellation(parent with { PinnedExecutable = parent.PinnedExecutable with { ArtifactHash = new('f', 64) } }, marked));
    }

    [Theory]
    [InlineData(JobsNativeSubmissionBoundary.StartKind)]
    [InlineData(JobsNativeSubmissionBoundary.OwnershipKind)]
    [InlineData(JobsNativeSubmissionBoundary.CancelKind)]
    public async Task Bare_submission_source_never_authorizes_admission_or_cancellation(string kind)
    {
        var (_, admission, _, _) = await SubmissionFixture.SavedAsync();
        IRuntimePostCommitIntentHandler handler = new JobsNativeSubmissionIntentHandler(null!, null!, null!, null!, null!, [], null!);
        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => handler.HandleAsync(JobsNativeSubmissionBoundary.Intent(admission, kind)).AsTask());
    }

    [Fact]
    public void Submit_inputs_reject_secret_binding_on_fixed_and_persisted_channels()
    {
        var attributes = typeof(SubmitJob).GetCustomAttributes(typeof(RefusesSecretBindingAttribute), false).Cast<RefusesSecretBindingAttribute>().ToArray();
        Assert.Equal(8, attributes.Length);
        Assert.Equal(JobAssociationMode.Independent, new SubmitJob(null!, null!, null!, null!, [], []).AssociationMode);
        Assert.DoesNotContain(typeof(SubmitJob).GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic), method => method.GetCustomAttributes(typeof(ResumeTargetAttribute), true).Length != 0);
    }
}

internal static class SubmissionFixture
{
    internal static readonly ActivityContract Contract = new(SubmitJob.ActivityType, "1", "test", JsonSerializer.SerializeToElement(new { }), [],
        new(new(typeof(SubmitJobResult).FullName!), false, ActivityValuePolicy.Default, []), ["Done"], new("test", "test"));
    internal static (ActivityExecutionState Activity, SubmitJobResult Receipt, JobNativeSubmission Submission) Completed(
        JobAssociationMode mode = JobAssociationMode.Independent, bool nested = false)
    {
        var id = nested ? "nested-submission" : "activity-one";
        var pin = NativeFixture.Pin with { AssociationMode = mode };
        var submission = new JobNativeSubmission(1, NativeFixture.Scope, NativeFixture.Executable, "workflow-one", id, id,
            "initial-attempt", "node-one", new(SubmitJob.ActivityType, "1", Contract.SchemaFingerprint), pin,
            new(pin.InputSchema, JsonSerializer.SerializeToElement(new { Count = 42 })), null,
            new(1, NativeFixture.Scope, "elsa.native", NativeFixture.Executable.DefinitionId, NativeFixture.Executable.DefinitionVersionId,
                "workflow-one", id, NativeFixture.Executable.ArtifactId, NativeFixture.Executable.ArtifactHash, null, null));
        var receipt = JobsNativeSubmissionBoundary.Receipt(submission);
        var ended = NativeFixture.Now.AddSeconds(1);
        var activity = NativeFixture.Activity() with { Execution = new(id, "workflow-one", "node-one", "authored-one", SubmitJob.ActivityType, "1"),
            Status = ActivityExecutionStatus.Completed, CompletedAt = ended, ParentActivityExecutionId = nested ? "container" : null,
            PrivateState = null, BookmarkIds = [], TriggerRegistrations = [], TriggerDeliveries = [], ContractIdentity = submission.Contract,
            Attempts = [new("initial-attempt", id, 1, ActivityAttemptReason.Initial, NativeFixture.Now, ended, null, ActivityTransitionKind.Complete)],
            Completion = new(id, "initial-attempt", ValueEnvelope.Inline(new(typeof(SubmitJobResult).FullName!), JobsNativeBoundary.Write(receipt),
                ValueProtectionPolicy.InstanceInline), "Done", ended, submission.Contract.SchemaFingerprint) };
        return (activity, receipt, submission);
    }

    internal static async Task<(ActivityExecutionState Activity, JobNativeSubmissionAdmission Admission,
        RuntimePostCommitIntent Start, RuntimePostCommitIntent? Ownership)> SavedAsync(JobAssociationMode mode = JobAssociationMode.Independent)
    {
        var prepared = Completed(mode);
        var commit = await new JobsSubmissionCheckpointEnricher(new ActivityStore())
            .EnrichAsync(NativeFixture.Commit(RuntimeCheckpointNames.ActivityCompleted, prepared.Activity));
        var saved = Assert.Single(commit.StateChanges.ActivityExecutions).State;
        return (saved, JobsNativeSubmissionBoundary.ReadAdmission(saved), commit.PostCommitIntents.Single(i => i.Kind == JobsNativeSubmissionBoundary.StartKind),
            commit.PostCommitIntents.SingleOrDefault(i => i.Kind == JobsNativeSubmissionBoundary.OwnershipKind));
    }

    internal static JobsNativeSubmissionIntentHandler Handler(JobTransport jobs, WorkflowStore parent, ActivityStore activities) =>
        new(jobs, jobs, parent, activities, new Access(), [], null!);

    internal static RuntimePostCommitOutboxItem Source(RuntimePostCommitIntent intent, RuntimePostCommitOutboxStatus status) =>
        new("source:" + intent.IntentId, intent, status, NativeFixture.Now, NativeFixture.Now,
            RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1)), 0, null, null, null, null, NativeFixture.Metadata,
            0, null, null, new(NativeFixture.Scope.PersistencePartition), null);
}
