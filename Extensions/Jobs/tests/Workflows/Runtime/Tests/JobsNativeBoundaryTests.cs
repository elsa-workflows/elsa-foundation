using System.Text.Json;
using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Jobs.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Jobs.Workflows.Runtime.Tests;

public sealed class JobsNativeBoundaryTests
{
    [Fact]
    public async Task Saved_wait_and_start_capture_actual_addresses_with_the_original_input_and_protected_origin()
    {
        var (activity, admission, intent) = await NativeFixture.SavedAsync();
        Assert.Equal("actual-bookmark", admission.Address.BookmarkId); Assert.Equal("actual-registration", admission.Address.RegistrationId);
        Assert.Equal("actual-opaque-resume-target", admission.Address.ResumeTargetId);
        Assert.Equal("retained", activity.Metadata["existing"]);
        Assert.Equal(42, admission.Request.Input.Value.GetProperty("Count").GetInt32());
        Assert.Equal(JobRequestDigest.Compute(admission.Request), admission.RequestSha256);
        Assert.Equal(admission.RequestSha256, admission.Origin.PayloadSha256);
        Assert.Equal(JobsNativeBoundary.StartKind, intent.Kind); Assert.Equal(NativeFixture.Now, intent.RecordedAt);
        Assert.Equal(admission.Address.Owner.MaterialSha256, admission.Request.Association!.MaterialSha256);
        Assert.Equal(JobsNativeBoundary.ContinuationSchema, admission.Request.Association.Continuation!.Schema.Alias);
        Assert.DoesNotContain("RequestSha256", admission.Request.Association.Continuation.Value.GetRawText());
    }

    [Fact]
    public async Task Replacing_the_bookmark_cannot_rebind_an_already_sealed_admission()
    {
        var (activity, _, _) = await NativeFixture.SavedAsync();
        var replacement = NativeFixture.Bookmark(activity) with { BookmarkId = "replacement-bookmark" };
        activity = activity with { BookmarkIds = [replacement.BookmarkId] };
        await Assert.ThrowsAsync<JobException>(() => new JobsCheckpointEnricher(new ActivityStore()).EnrichAsync(
            NativeFixture.Commit(RuntimeCheckpointNames.BookmarkCreated, activity, replacement)).AsTask());
    }

    [Fact]
    public async Task Cancellation_keeps_the_frozen_origin_after_wait_and_private_payload_retirement_and_uses_stable_source_time()
    {
        var (activity, admission, _) = await NativeFixture.SavedAsync();
        var retired = activity with { Status = ActivityExecutionStatus.Cancelled, BookmarkIds = [], TriggerRegistrations = [], PrivateState = null };
        var enricher = new JobsCheckpointEnricher(new ActivityStore());
        var first = await enricher.EnrichAsync(NativeFixture.Commit(RuntimeCheckpointNames.ActivityCancelled, retired));
        var later = NativeFixture.Commit(RuntimeCheckpointNames.ActivityCancelled, retired);
        later = later with { Checkpoint = later.Checkpoint with { OccurredAt = NativeFixture.Now.AddHours(1) } };
        var second = await enricher.EnrichAsync(later);
        var intent = Assert.Single(first.PostCommitIntents, item => item.Kind == JobsNativeBoundary.CancelKind);
        Assert.True(intent.IsEquivalentTo(Assert.Single(second.PostCommitIntents, item => item.Kind == JobsNativeBoundary.CancelKind)));
        Assert.Equal(JobsNativeBoundary.CancelKind, intent.Kind); Assert.Equal(NativeFixture.Now, intent.RecordedAt);
        Assert.Equal(admission.Origin, JobsNativeIntentHandler.Read(intent).Admission.Origin);
        var settlement = Assert.Single(first.PostCommitIntents, item => item.Kind == JobsNativeBoundary.SettlementKind);
        Assert.True(settlement.IsEquivalentTo(Assert.Single(second.PostCommitIntents, item => item.Kind == JobsNativeBoundary.SettlementKind)));
    }

    [Fact]
    public async Task Resume_claim_seals_consumed_delivery_identity_before_closed_attempt_payloads_can_be_retired()
    {
        var (activity, admission, _) = await NativeFixture.SavedAsync();
        var (_, completion) = NativeFixture.Selected(admission);
        var deliveryKey = JobsNativeBoundary.DeduplicationKey(admission.Address, JobsNativeBoundary.DeliveryId(admission.Address, completion));
        var metadata = activity.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value);
        metadata[RuntimeMetadataKeys.ActivityAttemptActivationClaim] = "resume-attempt";
        var claimed = activity with { Status = ActivityExecutionStatus.Running, Metadata = metadata,
            Attempts = [new("initial-attempt", activity.InvocationId, 1, ActivityAttemptReason.Initial, NativeFixture.Now, NativeFixture.Now, transitionKind: ActivityTransitionKind.Suspend),
                new("resume-attempt", activity.InvocationId, 2, ActivityAttemptReason.Resume, NativeFixture.Now.AddSeconds(3), triggerDeliveryId: "physical-delivery")],
            TriggerDeliveries = [new("physical-delivery", admission.Address.RegistrationId, JobsNativeBoundary.CompletionType,
                ValueEnvelope.Inline(JobsNativeBoundary.CompletionType, JsonSerializer.SerializeToElement(completion), ValueProtectionPolicy.InstanceInline),
                JobsNativeBoundary.ProviderId, NativeFixture.Now.AddSeconds(2), deliveryKey, ActivityTriggerDeliveryStatus.Consumed)] };
        var enriched = await new JobsCheckpointEnricher(new ActivityStore()).EnrichAsync(NativeFixture.Commit(RuntimeCheckpointNames.ActivityAttemptClaimed, claimed));
        var saved = Assert.Single(enriched.StateChanges.ActivityExecutions).State;
        var binding = JobsNativeBoundary.ReadString<JobNativeDeliveryBinding>(saved.Metadata[JobsNativeBoundary.DeliveryMetadataKey]);
        Assert.Equal("physical-delivery", binding.DeliveryId); Assert.Equal("resume-attempt", binding.AttemptId);
        Assert.True(JobsNativeBoundary.Equivalent(completion, binding.Completion));
        Assert.Empty(enriched.PostCommitIntents);
    }

    [Fact]
    public async Task Existing_grant_validates_the_canonical_winner_without_reattaching_the_proposed_job_hint()
    {
        var (_, admission, _) = await NativeFixture.SavedAsync(); var (transport, completion) = NativeFixture.Selected(admission);
        Assert.NotEqual(admission.ProposedJobId, completion.JobId);
        var result = await new JobsNativeResultValidator(transport).ValidateAsync(admission, completion, CancellationToken.None);
        Assert.Equal("canonical-winner-job", result.JobId); Assert.Equal(84, result.Payload!.Value.GetProperty("Total").GetInt32());
        Assert.Equal(1, transport.ExactPermissionReads); Assert.Empty(transport.Admissions); Assert.Empty(transport.Cancellations);
    }

    [Theory]
    [InlineData("request")]
    [InlineData("result")]
    [InlineData("continuation")]
    [InlineData("model-input")]
    [InlineData("payload")]
    [InlineData("association")]
    public async Task Permission_shaped_input_cannot_replace_any_exact_saved_or_selected_identity(string kind)
    {
        var (_, admission, _) = await NativeFixture.SavedAsync(); var (transport, completion) = NativeFixture.Selected(admission);
        completion = kind switch
        {
            "request" => completion with { RequestSha256 = new('f', 64) },
            "result" => completion with { ResultId = "different-result" },
            "continuation" => completion with { ContinuationId = "different-continuation" },
            "model-input" => completion with { ModelInputId = "different-input" },
            "payload" => completion with { Result = JobsNativeBoundary.Write(transport.Snapshot!.Result! with { Outcome = JobOutcome.Failed }) },
            _ => completion with { Permission = JobsNativeBoundary.Write(transport.Permission! with
                { Association = transport.Permission.Association with { ExecutionId = "different-owner" } }) }
        };
        await Assert.ThrowsAsync<JobException>(() => new JobsNativeResultValidator(transport).ValidateAsync(admission, completion, CancellationToken.None).AsTask());
        Assert.Empty(transport.Admissions);
    }

    [Fact]
    public async Task Missing_permission_or_unavailable_store_defers_the_owed_resume_without_a_business_result()
    {
        var (_, admission, _) = await NativeFixture.SavedAsync(); var (transport, completion) = NativeFixture.Selected(admission);
        transport.Permission = null;
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => new JobsNativeResultValidator(transport).ValidateAsync(admission, completion, CancellationToken.None).AsTask());
        transport.ReadFailure = new IOException("protected-provider-canary");
        var error = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => new JobsNativeResultValidator(transport).ValidateAsync(admission, completion, CancellationToken.None).AsTask());
        Assert.DoesNotContain("protected-provider-canary", error.Message); Assert.IsType<IOException>(error.InnerException);
        transport.ReadFailure = new OperationCanceledException("A provider-local timeout, without caller cancellation.");
        var timeout = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => new JobsNativeResultValidator(transport).ValidateAsync(admission, completion, CancellationToken.None).AsTask());
        Assert.IsType<OperationCanceledException>(timeout.InnerException);
    }

    [Theory]
    [InlineData(JobAssociationMode.Independent)]
    [InlineData(JobAssociationMode.Detached)]
    public void Awaited_activity_does_not_silently_invent_independent_or_detached_semantics(JobAssociationMode mode)
    {
        var invocation = JobsNativeBoundary.ReadState(NativeFixture.Activity()) with { HandlerPin = NativeFixture.Pin with { AssociationMode = mode } };
        Assert.Throws<JobException>(() => JobsNativeBoundary.ValidateInvocation(invocation));
    }

    [Fact]
    public void Handler_descriptor_is_bounded_and_preserves_logical_selection_without_a_handler_registry()
    {
        var json = NativeFixture.Pin.Serialize(); var parsed = JobWorkflowHandlerPin.Parse(json);
        Assert.Equal(NativeFixture.Pin, parsed);
        var error = Assert.Throws<JobException>(() => JobWorkflowHandlerPin.Parse("{")); Assert.NotNull(error.InnerException);
        Assert.Throws<JobException>(() => JobWorkflowHandlerPin.Parse(new string(' ', 32_769)));
    }

    [Fact]
    public void Explicit_initial_input_fault_is_positive_native_only_phase_proof()
    {
        var parent = NativeFixture.Parent(WorkflowExecutionStatus.Faulted);
        Assert.True(JobsNativeDeliveryInspector.PreAdmissionFailure(parent, InitialInputFault(parent)));
    }

    [Theory]
    [InlineData("marker")]
    [InlineData("private-state")]
    [InlineData("resume-attempt")]
    [InlineData("selected-result-marker")]
    public void Missing_metadata_or_a_consumed_result_fault_cannot_impersonate_pre_admission_failure(string kind)
    {
        var parent = NativeFixture.Parent(WorkflowExecutionStatus.Faulted); var faulted = InitialInputFault(parent);
        faulted = kind switch
        {
            "private-state" => faulted with { PrivateState = NativeFixture.Activity().PrivateState },
            "resume-attempt" => faulted with { Attempts = [new("initial-attempt", faulted.InvocationId, 1, ActivityAttemptReason.Resume,
                NativeFixture.Now, NativeFixture.Now.AddSeconds(3), transitionKind: ActivityTransitionKind.Fault)] },
            "selected-result-marker" => faulted with { Fault = WithFaultMetadata(faulted.Fault!, new Dictionary<string, string>
                { [Elsa.Activities.Runtime.Core.Models.ActivityFault.FaultTypeMetadataKey] = "jobs.terminal.v1:" + new string('c', 64) }) },
            _ => faulted with { Fault = WithFaultMetadata(faulted.Fault!, new Dictionary<string, string>()) }
        };
        Assert.False(JobsNativeDeliveryInspector.PreAdmissionFailure(parent, faulted));
    }

    [Fact]
    public async Task Native_owner_cancellation_needs_its_phase_marker_and_cannot_hide_a_consumed_job_result()
    {
        var (activity, _, _) = await NativeFixture.SavedAsync();
        var cancelled = activity with { Status = ActivityExecutionStatus.Cancelled, BookmarkIds = [], TriggerRegistrations = [] };
        var checkpoint = await new JobsCheckpointEnricher(new ActivityStore()).EnrichAsync(
            NativeFixture.Commit(RuntimeCheckpointNames.ActivityCancelled, cancelled));
        var marked = Assert.Single(checkpoint.StateChanges.ActivityExecutions).State;
        var parent = NativeFixture.Parent(WorkflowExecutionStatus.Cancelled);
        Assert.True(JobsNativeDeliveryInspector.NativeOwnerCancellation(parent, marked));
        var metadata = marked.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value); metadata.Remove(JobsNativeBoundary.OwnerCancellationMetadataKey);
        Assert.False(JobsNativeDeliveryInspector.NativeOwnerCancellation(parent, marked with { Metadata = metadata }));
        var receipt = new ActivityTriggerDelivery("consumed-job", "actual-registration", JobsNativeBoundary.CompletionType,
            ValueEnvelope.Absent(JobsNativeBoundary.CompletionType, ValueProtectionPolicy.InstanceInline), JobsNativeBoundary.ProviderId,
            NativeFixture.Now.AddSeconds(1), "actual-consumed-key", ActivityTriggerDeliveryStatus.Consumed);
        Assert.False(JobsNativeDeliveryInspector.NativeOwnerCancellation(parent, marked with { TriggerDeliveries = [receipt] }));
    }

    [Fact]
    public async Task Equivalent_model_address_property_reordering_keeps_request_continuation_and_delivery_identity()
    {
        var (_, admission, _) = await NativeFixture.SavedAsync(NativeFixture.WithModelOwner(NativeFixture.Activity()));
        var (transport, completion) = NativeFixture.Selected(admission); var result = transport.Snapshot!.Result!;
        var original = admission.Address.Owner.ModelAddress!.Value;
        var reordered = JsonSerializer.SerializeToElement(original.EnumerateObject().Reverse().ToDictionary(property => property.Name, property => property.Value));
        Assert.NotEqual(original.GetRawText(), reordered.GetRawText());
        var address = admission.Address with { Owner = admission.Address.Owner with { ModelAddress = reordered } };
        var association = admission.Request.Association! with { Continuation = new(admission.Request.Association!.Continuation!.Schema, JobsNativeBoundary.Write(address)) };
        Assert.True(JobsNativeBoundary.Equivalent(admission.Address, address));
        Assert.Equal(admission.RequestSha256, JobRequestDigest.Compute(admission.Request with { Association = association }));
        Assert.Equal(JobsNativeBoundary.ContinuationId(admission.Address, result, admission.Request.Association!),
            JobsNativeBoundary.ContinuationId(address, result, association));
        Assert.Equal(JobsNativeBoundary.DeliveryId(admission.Address, completion), JobsNativeBoundary.DeliveryId(address, completion));
    }

    [Fact]
    public void Canonical_native_identity_preserves_raw_numeric_lexemes_and_its_finite_envelope_bound()
    {
        using var integer = JsonDocument.Parse("{\"Version\":1}"); using var decimalValue = JsonDocument.Parse("{\"Version\":1.0}");
        Assert.NotEqual(JobsNativeBoundary.Hash(integer.RootElement), JobsNativeBoundary.Hash(decimalValue.RootElement));
        Assert.Throws<JobException>(() => JobsNativeBoundary.Hash(new string('x', JobsNativeBoundary.MaximumEnvelopeBytes + 1)));
    }

    private static ActivityExecutionState InitialInputFault(WorkflowExecutionState parent)
    {
        var activity = NativeFixture.Activity() with { Status = ActivityExecutionStatus.Faulted, PrivateState = null,
            BookmarkIds = [], TriggerRegistrations = [], TriggerDeliveries = [], CompletedAt = NativeFixture.Now.AddSeconds(3),
            Attempts = [new("initial-attempt", "activity-one", 1, ActivityAttemptReason.Initial, NativeFixture.Now,
                NativeFixture.Now.AddSeconds(3), transitionKind: ActivityTransitionKind.Fault)] };
        return activity with { Fault = new(JobsNativeBoundary.PreAdmissionFaultCode,
            typeof(Elsa.Activities.Runtime.Core.Models.ActivityFault).FullName!, "Safe input refusal.", null, false,
            new Dictionary<string, string> { [Elsa.Activities.Runtime.Core.Models.ActivityFault.FaultTypeMetadataKey] =
                JobsNativeBoundary.PreAdmissionMarker(parent, activity, "initial-attempt") }) };
    }

    private static NormalizedActivityFault WithFaultMetadata(NormalizedActivityFault fault, IReadOnlyDictionary<string, string> metadata) =>
        new(fault.Code, fault.ExceptionType, fault.Message, fault.SanitizedStackTrace, fault.IsRetryable, metadata);
}
