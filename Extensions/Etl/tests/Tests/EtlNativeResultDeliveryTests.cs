using Elsa.Activities.Runtime.Core.Models;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Workflows.Runtime.Models;
using Elsa.Etl.Workflows.Runtime.Services;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

public sealed class EtlNativeResultDeliveryTests
{
    [Theory]
    [InlineData(BookmarkResumeDispatchStatus.Dispatched)]
    [InlineData(BookmarkResumeDispatchStatus.Deferred)]
    [InlineData(BookmarkResumeDispatchStatus.Duplicate)]
    [InlineData(BookmarkResumeDispatchStatus.NotFound)]
    public async Task Mailbox_dispatch_status_alone_never_acknowledges_the_durable_result(BookmarkResumeDispatchStatus status)
    {
        await using var fixture = await EtlNativeTestFixture.CreateAsync();
        fixture.Dispatches.Respond = (_, _) => ValueTask.FromResult(fixture.DispatchResult(status));

        var disposition = await fixture.DeliverAsync();

        Assert.Null(disposition);
        Assert.Single(fixture.Dispatches.Requests);
        var outbox = await fixture.ReadOutboxAsync();
        Assert.Null(outbox.Disposition);
        Assert.Null(outbox.AcknowledgedAtUtcTicks);
    }

    [Fact]
    public async Task Consumed_typed_receipt_and_absent_bookmark_prove_delivery()
    {
        await using var fixture = await EtlNativeTestFixture.CreateAsync();
        fixture.Dispatches.Respond = async (_, cancellationToken) =>
        {
            await fixture.Bookmarks.DeleteAsync(fixture.Invocation.ResultAddress.WorkflowExecutionId,
                fixture.Invocation.ResultAddress.BookmarkId, cancellationToken);
            var activity = await fixture.Activities.FindAsync(fixture.Invocation.ResultAddress.WorkflowExecutionId,
                fixture.Invocation.ResultAddress.ActivityExecutionId, cancellationToken)
                ?? throw new InvalidOperationException("The fixture activity is missing.");
            var completion = new RunEtlCompletion(EtlNativeBoundary.FormatVersion,
                EtlRuntimeJson.WriteElement(fixture.Invocation), EtlRuntimeJson.WriteElement(fixture.Delivery.Result));
            var type = EtlNativeBoundary.CompletionType;
            var receipt = new ActivityTriggerDelivery(fixture.Delivery.DeliveryId,
                fixture.Invocation.ResultAddress.RegistrationId, type,
                ValueEnvelope.Inline(type, System.Text.Json.JsonSerializer.SerializeToElement(completion), ValueProtectionPolicy.InstanceInline),
                fixture.Invocation.ResultAddress.ProviderId, EtlNativeTestFixture.Now, fixture.Delivery.DeliveryId,
                ActivityTriggerDeliveryStatus.Consumed);
            await fixture.Activities.SaveAsync(activity with { TriggerRegistrations = [], TriggerDeliveries = [receipt] }, cancellationToken);
            return fixture.DispatchResult(BookmarkResumeDispatchStatus.Dispatched);
        };

        var disposition = await fixture.DeliverAsync();

        Assert.Equal(EtlDeliveryDisposition.Consumed, disposition);
        Assert.Null((await fixture.ReadOutboxAsync()).Disposition);
        Assert.Equal(EtlStoreStatus.Applied, await fixture.Store.AcknowledgeDeliveryAsync(fixture.Delivery, disposition!.Value));
        Assert.NotNull((await fixture.ReadOutboxAsync()).AcknowledgedAtUtcTicks);
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("activity")]
    public async Task Terminal_native_state_proves_delivery_without_dispatch(string terminalState)
    {
        await using var fixture = await EtlNativeTestFixture.CreateAsync();
        if (terminalState == "parent")
        {
            var parent = await fixture.Workflows.FindAsync(fixture.Invocation.ResultAddress.WorkflowExecutionId)
                ?? throw new InvalidOperationException("The fixture parent is missing.");
            await fixture.Workflows.SaveAsync(parent with { Status = WorkflowExecutionStatus.Completed });
        }
        else
        {
            await fixture.Bookmarks.DeleteAsync(fixture.Invocation.ResultAddress.WorkflowExecutionId,
                fixture.Invocation.ResultAddress.BookmarkId);
            var activity = await fixture.Activities.FindAsync(fixture.Invocation.ResultAddress.WorkflowExecutionId,
                fixture.Invocation.ResultAddress.ActivityExecutionId)
                ?? throw new InvalidOperationException("The fixture activity is missing.");
            await fixture.Activities.SaveAsync(activity with { Status = ActivityExecutionStatus.Completed });
        }

        var disposition = await fixture.DeliverAsync();

        Assert.Equal(EtlDeliveryDisposition.ProvenParentTerminal, disposition);
        Assert.Empty(fixture.Dispatches.Requests);
        Assert.Equal(EtlStoreStatus.Applied, await fixture.Store.AcknowledgeDeliveryAsync(fixture.Delivery, disposition!.Value));
        Assert.NotNull((await fixture.ReadOutboxAsync()).AcknowledgedAtUtcTicks);
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("bookmark")]
    public async Task Missing_parent_or_suspended_activity_without_bookmark_leaves_delivery_owed(string missing)
    {
        await using var fixture = await EtlNativeTestFixture.CreateAsync();
        if (missing == "parent")
            await fixture.Workflows.DeleteAsync(fixture.Invocation.ResultAddress.WorkflowExecutionId);
        else
            await fixture.Bookmarks.DeleteAsync(fixture.Invocation.ResultAddress.WorkflowExecutionId,
                fixture.Invocation.ResultAddress.BookmarkId);

        var disposition = await fixture.DeliverAsync();

        Assert.Null(disposition);
        Assert.Empty(fixture.Dispatches.Requests);
        var outbox = await fixture.ReadOutboxAsync();
        Assert.Null(outbox.Disposition);
        Assert.Null(outbox.AcknowledgedAtUtcTicks);
    }

    [Fact]
    public async Task A_mismatched_live_bookmark_is_refused_before_dispatch()
    {
        await using var fixture = await EtlNativeTestFixture.CreateAsync();
        var address = fixture.Invocation.ResultAddress;
        var bookmark = await fixture.Bookmarks.FindAsync(address.WorkflowExecutionId, address.BookmarkId)
            ?? throw new InvalidOperationException("The fixture bookmark is missing.");
        await fixture.Bookmarks.SaveAsync(bookmark with { StimulusHash = "other-run" });

        await Assert.ThrowsAsync<InvalidDataException>(async () => await fixture.DeliverAsync());

        Assert.Empty(fixture.Dispatches.Requests);
        Assert.Null((await fixture.ReadOutboxAsync()).Disposition);
    }

    [Fact]
    public async Task A_forged_delivery_payload_hash_is_refused_without_settling_the_outbox()
    {
        await using var fixture = await EtlNativeTestFixture.CreateAsync();
        var forgedHash = fixture.Delivery.PayloadHash == new string('0', 64) ? new string('1', 64) : new string('0', 64);
        var forgedDelivery = fixture.Delivery with { PayloadHash = forgedHash };

        await Assert.ThrowsAsync<InvalidDataException>(async () => await fixture.DeliverAsync(forgedDelivery));

        Assert.Empty(fixture.Dispatches.Requests);
        var outbox = await fixture.ReadOutboxAsync();
        Assert.Null(outbox.Disposition);
        Assert.Null(outbox.AcknowledgedAtUtcTicks);
    }

    [Fact]
    public async Task Dispatch_carries_the_frozen_typed_address_and_binds_to_the_native_partition()
    {
        await using var fixture = await EtlNativeTestFixture.CreateAsync(new("tenant-a", "partition-distinct-from-default"));
        fixture.Dispatches.Respond = (_, _) => ValueTask.FromResult(fixture.DispatchResult(BookmarkResumeDispatchStatus.Dispatched));

        Assert.Null(await fixture.DeliverAsync()); // Dispatched is not yet a consumption proof.

        var request = Assert.Single(fixture.Dispatches.Requests);
        var address = fixture.Invocation.ResultAddress;
        Assert.Equal(address.WorkflowExecutionId, request.WorkflowExecutionId);
        Assert.Equal(address.StimulusType, request.StimulusType);
        Assert.Equal(address.StimulusHash, request.StimulusHash);
        Assert.Equal(address.BookmarkId, request.BookmarkId);
        Assert.Equal(address.ProviderId, request.RequestedBy);
        Assert.Equal(address.ProviderId, request.ProviderId);
        Assert.Equal(address.PayloadTypeAlias, request.PayloadType!.Alias);
        Assert.Equal(address.PayloadSchemaVersion, request.PayloadType.SchemaVersion);
        Assert.Equal(EtlNativeBoundary.CompletionType.Schema?.GetRawText(), request.PayloadType.Schema?.GetRawText());
        Assert.Equal(fixture.Delivery.DeliveryId, request.IdempotencyKey);
        var completion = System.Text.Json.JsonSerializer.Deserialize<RunEtlCompletion>(request.Input!.Value)
            ?? throw new InvalidDataException("The dispatched typed ETL completion is missing.");
        var dispatchedInvocation = EtlRuntimeJson.Read<EtlInvocation>(completion.Invocation, EtlNativeBoundary.MaximumRequestBytes);
        var dispatchedResult = EtlRuntimeJson.Read<EtlTerminalResult>(completion.Result, EtlNativeBoundary.MaximumResultBytes);
        Assert.Equal(EtlRuntimeIdentity.InvocationFingerprint(fixture.Invocation), EtlRuntimeIdentity.InvocationFingerprint(dispatchedInvocation));
        Assert.Equal(fixture.Delivery.Result, dispatchedResult);
        Assert.Equal(address.ResumeTargetId, fixture.Dispatches.LastResult!.Resolution!.ResumeTarget.ResumeTargetId);
        Assert.Equal(new PersistenceScope(fixture.Scope.PersistencePartition), fixture.Dispatches.ObservedScope);
        Assert.NotEqual(new PersistenceScope("default"), fixture.Dispatches.ObservedScope);
        Assert.NotEqual(new PersistenceScope(fixture.Scope.TenantId!), fixture.Dispatches.ObservedScope);
        Assert.Null((await fixture.ReadOutboxAsync()).Disposition);
    }

    [Fact]
    public async Task Lost_start_intent_ack_replays_from_the_exact_run_after_native_history_was_removed()
    {
        await using var fixture = await EtlNativeTestFixture.CreateAsync();
        var address = fixture.Invocation.ResultAddress;
        await fixture.Workflows.DeleteAsync(address.WorkflowExecutionId);
        fixture.ActivityReads.HideActivities = true;
        await fixture.Bookmarks.DeleteAsync(address.WorkflowExecutionId, address.BookmarkId);
        await using var nativeScope = await fixture.OpenNativeScopeAsync();
        var provider = nativeScope.ServiceProvider;
        var admission = new NeverAdmissionService();
        var handler = new EtlIntentHandler(admission, provider.GetRequiredService<IEtlRuntimeStore>(),
            fixture.Workflows, fixture.Activities, fixture.Bookmarks,
            provider.GetRequiredService<IPersistenceAccessContextAccessor>());
        var intent = EtlNativeBoundary.Intent(fixture.Invocation, cancel: false, EtlNativeTestFixture.Now);

        await handler.HandleAsync(intent);

        Assert.Equal(0, admission.Calls);
        Assert.Equal(0, fixture.ActivityReads.FindCalls);
        var conflictArtifact = new EtlArtifact(fixture.Invocation.Artifact.FormatVersion, "other-etl-artifact",
            fixture.Invocation.Artifact.ContentHash, fixture.Invocation.Artifact.DefinitionId,
            fixture.Invocation.Artifact.DefinitionVersion, fixture.Invocation.Artifact.Pipeline);
        var conflictingInvocation = fixture.Invocation with { Artifact = conflictArtifact };
        await Assert.ThrowsAsync<InvalidDataException>(async () => await handler.HandleAsync(
            EtlNativeBoundary.Intent(conflictingInvocation, cancel: false, EtlNativeTestFixture.Now)));
        Assert.Equal(0, admission.Calls);
        Assert.Null((await fixture.ReadOutboxAsync()).Disposition);
    }

    private sealed class NeverAdmissionService : IEtlRunAdmissionService
    {
        public int Calls { get; private set; }

        public ValueTask<EtlAdmissionResult> AdmitAsync(EtlInvocation invocation, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("The existing run should satisfy this exact start replay.");
        }
    }
}
