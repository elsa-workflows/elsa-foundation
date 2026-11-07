using System.Text.Json;
using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Jobs.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Events;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Jobs.Workflows.Runtime.Tests;

public sealed class JobsNativeResumeGuardTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_corrupt_retained_admission_is_unavailable_before_claim_rather_than_a_poisonable_payload_error(bool corrupt)
    {
        var (saved, admission, _) = await NativeFixture.SavedAsync(); var (transport, completion) = NativeFixture.Selected(admission);
        var metadata = saved.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value);
        metadata.Remove(JobsNativeBoundary.AdmissionMetadataKey);
        if (corrupt) metadata[JobsNativeBoundary.AdmissionMetadataKey] = "protected-retained-canary{";
        var context = new ActivityResumeValidating(saved with { Metadata = metadata }, Node(), Payload(admission, completion));
        var error = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() =>
            new JobsNativeResumeValidator(transport, new Access()).Handle(context, CancellationToken.None));
        Assert.Equal("jobs.native.admission-unavailable", error.Code);
        Assert.Equal("jobs.native.invalid", Assert.IsType<JobException>(error.InnerException).Code);
        Assert.DoesNotContain("protected-retained-canary", error.Message);
        Assert.Equal(0, transport.ExactPermissionReads); Assert.Empty(transport.Acknowledgements); Assert.Empty(transport.Admissions);
        Assert.Equal(ActivityExecutionStatus.Suspended, context.State.Status);
        Assert.Contains(admission.Address.BookmarkId, context.State.BookmarkIds);
    }

    [Fact]
    public async Task Persistence_access_read_failure_defers_preclaim_with_its_original_cause()
    {
        var (saved, admission, _) = await NativeFixture.SavedAsync(); var (transport, completion) = NativeFixture.Selected(admission);
        var cause = new IOException("protected-access-canary");
        var error = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() =>
            new JobsNativeResumeValidator(transport, new ThrowingAccess(cause)).Handle(
                new(saved, Node(), Payload(admission, completion)), CancellationToken.None));
        Assert.Same(cause, error.InnerException); Assert.DoesNotContain("protected-access-canary", error.Message);
        Assert.Equal(0, transport.ExactPermissionReads);
    }

    [Fact]
    public async Task Requested_preclaim_cancellation_keeps_the_callers_token()
    {
        var (saved, admission, _) = await NativeFixture.SavedAsync(); var (transport, completion) = NativeFixture.Selected(admission);
        using var cancellation = new CancellationTokenSource(); await cancellation.CancelAsync();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new JobsNativeResumeValidator(transport, new Access()).Handle(new(saved, Node(), Payload(admission, completion)), cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken); Assert.Equal(0, transport.ExactPermissionReads);
    }

    [Fact]
    public async Task Deliberately_wrong_incoming_bookmark_keeps_its_conflicting_payload_refusal_policy()
    {
        var (saved, admission, _) = await NativeFixture.SavedAsync(); var (transport, completion) = NativeFixture.Selected(admission);
        var error = await Assert.ThrowsAsync<JobException>(() => new JobsNativeResumeValidator(transport, new Access()).Handle(
            new(saved, Node(), Payload(admission, completion, "different-bookmark")), CancellationToken.None));
        Assert.Equal("jobs.native.invalid", error.Code); Assert.Equal(0, transport.ExactPermissionReads);
    }

    private static ExecutableNode Node() => new("node-one", "authored-one", JobsNativeBoundary.ActivityType, "1", "test-descriptor",
        JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), NativeFixture.Metadata);

    private static RuntimeResumeBookmarkCommandPayload Payload(JobNativeAdmission admission, RunJobCompletion completion, string? bookmark = null)
    {
        var address = admission.Address; var deliveryId = JobsNativeBoundary.DeliveryId(address, completion);
        return new(address.Executable, bookmark ?? address.BookmarkId, address.InvocationId, address.NodeId, address.ResumeTargetId,
            address.StimulusType, address.StimulusHash, JobsNativeBoundary.Write(completion), "Jobs preclaim regression",
            new(deliveryId, JobsNativeBoundary.CompletionType, JobsNativeBoundary.ProviderId, NativeFixture.Now,
                JobsNativeBoundary.DeduplicationKey(address, deliveryId)));
    }

    private sealed class ThrowingAccess(Exception cause) : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => throw cause; }
}
