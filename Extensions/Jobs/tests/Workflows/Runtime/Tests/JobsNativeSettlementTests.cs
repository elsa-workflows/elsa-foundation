using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Jobs.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Jobs.Workflows.Runtime.Tests;

public sealed class JobsNativeSettlementTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_adds_a_separate_required_settlement_source_only_for_direct_native_ownership(bool modelWorker)
    {
        var initial = NativeFixture.Activity();
        var (saved, admission, _) = await NativeFixture.SavedAsync(modelWorker ? NativeFixture.WithModelOwner(initial) : initial);
        var cancelled = saved with { Status = ActivityExecutionStatus.Cancelled, PrivateState = null, BookmarkIds = [], TriggerRegistrations = [] };
        var checkpoint = await new JobsCheckpointEnricher(new ActivityStore()).EnrichAsync(
            NativeFixture.Commit("activity-cancelled", cancelled));
        Assert.Single(checkpoint.PostCommitIntents, intent => intent.Kind == JobsNativeBoundary.CancelKind);
        if (modelWorker)
        {
            Assert.DoesNotContain(checkpoint.PostCommitIntents, intent => intent.Kind == JobsNativeBoundary.SettlementKind);
            Assert.Throws<JobException>(() => JobsNativeBoundary.SettlementIntent(admission));
        }
        else
        {
            var source = Assert.Single(checkpoint.PostCommitIntents, intent => intent.Kind == JobsNativeBoundary.SettlementKind);
            Assert.True(JobsNativeBoundary.Equivalent(admission, JobsNativeSettlementIntentHandler.Read(source)));
        }
    }

    [Theory]
    [InlineData(ActivityExecutionStatus.Completed, false)]
    [InlineData(ActivityExecutionStatus.Faulted, false)]
    [InlineData(ActivityExecutionStatus.Cancelled, false)]
    [InlineData(ActivityExecutionStatus.Completed, true)]
    [InlineData(ActivityExecutionStatus.Faulted, true)]
    [InlineData(ActivityExecutionStatus.Cancelled, true)]
    public async Task Consumed_result_terminal_checkpoint_retains_direct_history_without_adding_a_model_worker_ack_cycle(ActivityExecutionStatus status, bool modelWorker)
    {
        var initial = NativeFixture.Activity();
        var (saved, admission, _) = await NativeFixture.SavedAsync(modelWorker ? NativeFixture.WithModelOwner(initial) : initial);
        var (_, completion) = NativeFixture.Selected(admission);
        var binding = new JobNativeDeliveryBinding(1, completion, admission.Address.RegistrationId, "native-delivery",
            "native-deduplication-key", "native-final-attempt", admission.Address.Contract);
        var metadata = saved.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value);
        metadata[JobsNativeBoundary.DeliveryMetadataKey] = JobsNativeBoundary.Write(binding).GetRawText();
        var terminal = saved with { Status = status, PrivateState = null, BookmarkIds = [], TriggerRegistrations = [], Metadata = metadata };
        var checkpoint = await new JobsCheckpointEnricher(new ActivityStore()).EnrichAsync(NativeFixture.Commit("activity-terminal", terminal));
        Assert.Equal(modelWorker ? 0 : 1, checkpoint.PostCommitIntents.Count(intent => intent.Kind == JobsNativeBoundary.SettlementKind));
    }

    [Fact]
    public async Task Exact_cancel_tombstone_is_positive_no_admission_proof_but_unknown_or_retired_sender_is_not()
    {
        var (_, admission, _) = await NativeFixture.SavedAsync();
        var transport = new JobTransport { Origin = new(admission.Origin, admission.RequestSha256, true, false, null) };
        var settlement = new JobsNativeResultSettlement(transport, null!, null!);
        await settlement.SettleAdmissionAsync(admission, CancellationToken.None);
        transport.Origin = transport.Origin with { Retired = true };
        await Assert.ThrowsAsync<JobException>(() => settlement.SettleAdmissionAsync(admission, CancellationToken.None).AsTask());
        transport.Origin = null;
        await Assert.ThrowsAsync<JobException>(() => settlement.SettleAdmissionAsync(admission, CancellationToken.None).AsTask());
        Assert.Empty(transport.Acknowledgements); Assert.Empty(transport.Admissions);
    }

    [Fact]
    public async Task Cancellation_source_confirms_immediately_while_separate_settlement_remains_owed_for_an_active_job()
    {
        var (saved, admission, _) = await NativeFixture.SavedAsync(); var (transport, _) = NativeFixture.Selected(admission);
        transport.Snapshot = transport.Snapshot! with { State = JobState.AttemptActive, Result = null };
        transport.Delivery = null;
        var cancel = new JobsNativeIntentHandler(transport, transport, new WorkflowStore(NativeFixture.Parent()) { Missing = true },
            new ActivityStore(), new BookmarkStore(NativeFixture.Bookmark(saved)), new Access(), [], null!);
        await cancel.HandleIntentAsync(JobsNativeBoundary.Intent(admission, cancel: true));
        Assert.Single(transport.Cancellations); Assert.NotNull(transport.Origin!.Acceptance);
        var pending = new JobsNativeSettlementIntentHandler(new(transport, null!, null!), new Access(), null!);
        var error = await Assert.ThrowsAsync<RuntimePostCommitDeliveryException>(() =>
            pending.HandleIntentAsync(JobsNativeBoundary.SettlementIntent(admission)).AsTask());
        Assert.Equal(PostCommitFailureKind.Transient, error.Kind); Assert.Empty(transport.Acknowledgements);
    }

    [Fact]
    public async Task Already_acknowledged_canonical_winner_confirms_settlement_without_reattaching_the_proposed_job_hint()
    {
        var (_, admission, _) = await NativeFixture.SavedAsync(); var (transport, completion) = NativeFixture.Selected(admission);
        transport.Delivery = transport.Delivery! with { Acknowledgement = new(completion.ContinuationId,
            new(JobDeliveryProofKind.AlreadyConsumed, "actual-model-input-receipt", transport.Snapshot!.Result!.PayloadSha256)) };
        await new JobsNativeResultSettlement(transport, null!, null!).SettleAdmissionAsync(admission, CancellationToken.None);
        Assert.NotEqual(admission.ProposedJobId, transport.Delivery.JobId);
        Assert.Empty(transport.Admissions); Assert.Empty(transport.Cancellations); Assert.Empty(transport.Acknowledgements);
    }

    [Theory]
    [InlineData("job")]
    [InlineData("result")]
    [InlineData("continuation")]
    [InlineData("digest")]
    [InlineData("missing-permission")]
    public async Task Acknowledgement_shaped_data_cannot_replace_the_canonical_selected_binding(string changed)
    {
        var (_, admission, _) = await NativeFixture.SavedAsync(); var (transport, completion) = NativeFixture.Selected(admission);
        var delivery = transport.Delivery! with { Acknowledgement = new(completion.ContinuationId,
            new(JobDeliveryProofKind.Consumed, "actual-model-receipt", transport.Snapshot!.Result!.PayloadSha256)) };
        transport.Delivery = changed switch
        {
            "job" => delivery with { JobId = "different-job" },
            "result" => delivery with { ResultId = "different-result" },
            "continuation" => delivery with { Acknowledgement = delivery.Acknowledgement! with { ContinuationId = "different-continuation" } },
            "digest" => delivery with { Acknowledgement = delivery.Acknowledgement! with { Proof = delivery.Acknowledgement.Proof with { ResultSha256 = new('f', 64) } } },
            _ => delivery with { Permission = null }
        };
        await Assert.ThrowsAsync<JobException>(() => new JobsNativeResultSettlement(transport, null!, null!).SettleAdmissionAsync(admission, CancellationToken.None).AsTask());
        Assert.Empty(transport.Acknowledgements);
    }

    [Fact]
    public async Task Terminal_owner_acknowledgement_commit_followed_by_lost_reply_is_confirmed_by_exact_read_on_the_same_source()
    {
        var (saved, admission, _) = await NativeFixture.SavedAsync(); var (transport, _) = NativeFixture.Selected(admission);
        transport.AcknowledgementFailureAfterCommit = new IOException("protected-acknowledgement-canary");
        var services = new ServiceCollection(); services.AddPersistenceCore(NativeFixture.Scope.PersistencePartition);
        services.AddSingleton<IJobCommands>(transport); services.AddSingleton<IJobQueries>(transport);
        services.AddScoped<JobsNativeDeliveryInspector>(provider => new(new WorkflowStore(NativeFixture.Parent(WorkflowExecutionStatus.Cancelled)),
            null!, new ActivityStore(), new BookmarkStore(NativeFixture.Bookmark(saved)),
            provider.GetRequiredService<IPersistenceAccessContextAccessor>(), transport, [], provider.GetRequiredService<IPersistenceOperationScopeFactory>()));
        services.AddScoped<JobsResultDeliveryExecution>(); services.AddScoped<JobsNativeResultSettlement>();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var settlement = scope.ServiceProvider.GetRequiredService<JobsNativeResultSettlement>();
        await settlement.SettleAdmissionAsync(admission, CancellationToken.None);
        await settlement.SettleAdmissionAsync(admission, CancellationToken.None);
        Assert.Equal(JobDeliveryProofKind.ParentProvenTerminal, Assert.Single(transport.Acknowledgements).Proof.Kind);
        Assert.True(transport.Delivery!.IsAcknowledged); Assert.Empty(transport.Admissions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Terminal_cleanup_cannot_ack_an_unsupported_result_outcome_or_digest_consistent_wrong_success_schema(bool wrongSchema)
    {
        var (_, admission, _) = await NativeFixture.SavedAsync(); var (transport, _) = NativeFixture.Selected(admission);
        var snapshot = transport.Snapshot!; var result = snapshot.Result!;
        if (wrongSchema)
        {
            var payload = new JobPayload(new("different-result-schema", 1), result.Payload!.Value);
            result = result with { Payload = payload, PayloadSha256 = JobPayloadDigest.Compute(payload) };
        }
        else result = result with { Outcome = (JobOutcome)999 };
        transport.Snapshot = snapshot with { Result = result };
        await Assert.ThrowsAsync<JobException>(() => new JobsNativeResultSettlement(transport, null!, null!).SettleAdmissionAsync(admission, CancellationToken.None).AsTask());
        Assert.Empty(transport.Acknowledgements);
    }
}
