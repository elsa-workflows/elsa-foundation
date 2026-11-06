using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Exceptions;

namespace Elsa.Jobs.Workflows.Runtime.Tests;

public sealed class JobsNativeRetentionTests
{
    [Fact]
    public async Task Compact_expiry_proof_confirms_the_same_already_settled_source_after_acknowledgement_loss()
    {
        var (_, admission, _) = await NativeFixture.SavedAsync(); var (jobs, completion) = NativeFixture.Selected(admission);
        var original = jobs.Snapshot!; jobs.Snapshot = Expired(original);
        jobs.Delivery = jobs.Delivery! with { Acknowledgement = new(completion.ContinuationId,
            new(JobDeliveryProofKind.AlreadyConsumed, "actual-owner-receipt", original.Result!.PayloadSha256)) };
        await new JobsNativeResultSettlement(jobs, null!, null!).SettleAdmissionAsync(admission, CancellationToken.None);
        // This is the existing completion in the retained source, authenticated against compact canonical metadata.
        var checkedResult = await new JobsNativeResultValidator(jobs).ValidateAsync(admission, completion, CancellationToken.None);
        Assert.Equal(original.Result, checkedResult); Assert.Null(jobs.Snapshot.Result); Assert.Null(jobs.Snapshot.Request);
        Assert.Empty(jobs.Acknowledgements); Assert.Empty(jobs.Admissions);
    }

    [Fact]
    public async Task An_expired_snapshot_cannot_authorize_a_live_result_delivery_without_exact_ack_proof()
    {
        var (_, admission, _) = await NativeFixture.SavedAsync(); var (jobs, completion) = NativeFixture.Selected(admission);
        jobs.Snapshot = Expired(jobs.Snapshot!);
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() =>
            new JobsNativeResultValidator(jobs).ValidateAsync(admission, completion, CancellationToken.None).AsTask());
        await Assert.ThrowsAsync<JobException>(() =>
            new JobsNativeResultSettlement(jobs, null!, null!).SettleAdmissionAsync(admission, CancellationToken.None).AsTask());
        Assert.Empty(jobs.Acknowledgements);
    }

    [Theory]
    [InlineData("request")]
    [InlineData("result")]
    [InlineData("expiry")]
    [InlineData("ack")]
    public async Task Corrupt_compact_identity_or_missing_positive_expiry_proof_cannot_retire_a_source(string changed)
    {
        var (_, admission, _) = await NativeFixture.SavedAsync(); var (jobs, completion) = NativeFixture.Selected(admission);
        var expired = Expired(jobs.Snapshot!);
        jobs.Snapshot = changed switch
        {
            "request" => expired with { RequestMetadata = expired.RequestMetadata! with { InputSha256 = new('f', 64) } },
            "result" => expired with { ResultMetadata = expired.ResultMetadata! with { PayloadSha256 = new('f', 64) } },
            "expiry" => expired with { PayloadRetention = expired.PayloadRetention! with { ResultExpiredAt = null } },
            _ => expired
        };
        jobs.Delivery = jobs.Delivery! with { Acknowledgement = new(completion.ContinuationId,
            new(JobDeliveryProofKind.AlreadyConsumed, "actual-owner-receipt", changed == "ack" ? new('f', 64) : expired.ResultMetadata!.PayloadSha256)) };
        await Assert.ThrowsAsync<JobException>(() =>
            new JobsNativeResultSettlement(jobs, null!, null!).SettleAdmissionAsync(admission, CancellationToken.None).AsTask());
        Assert.Empty(jobs.Acknowledgements);
    }

    private static JobSnapshot Expired(JobSnapshot source)
    {
        var due = source.Result!.CompletedAt.AddMinutes(1); var at = due.AddSeconds(1);
        return source with { Request = null, Result = null, RequestMetadata = JobRequestMetadata.From(source.Request!),
            ResultMetadata = JobResultMetadata.From(source.Result), InputPayloadState = JobPayloadState.Expired,
            ResultPayloadState = JobPayloadState.Expired, PayloadRetention = new(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), due, due, at, at) };
    }
}
