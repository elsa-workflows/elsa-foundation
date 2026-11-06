using System.Text.Json;
using Elsa.Events.Core.Contracts;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Api.StandaloneTriggers;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed class StandaloneTriggerDeliveryTests
{
    [Fact]
    public async Task Shared_Event_source_projects_only_authorized_resource_subset_and_other_resource_progress_does_not_change_its_revision()
    {
        var fixture = new Fixture();
        var view = await fixture.Operations.ReadAsync(TriggerDeliveryOperations.EncodeId(fixture.Port.Item.OutboxItemId, "trigger-a"), default);
        Assert.Equal("trigger-a", view.TriggerId);
        Assert.Equal(1, view.AcceptedCount); Assert.Equal(0, view.RefusedCount);
        Assert.Equal("recipient-a", Assert.Single(view.Recipients).Id);
        Assert.Null(view.Recipients[0].Target); Assert.Null(view.Recipients[0].PlannedExecutionIdentity);
        Assert.DoesNotContain("private-definition-b", JsonSerializer.Serialize(view));
        Assert.DoesNotContain("subscription-b", JsonSerializer.Serialize(view));
        fixture.Port.Item = fixture.Port.Item.WithSubscriptionDelivery(fixture.Port.Item.SubscriptionDelivery!.WithProgress(
            new("recipient-b", RuntimeRecipientPhase.Pending, attemptCount: 2, lastFailureCode: "private-b-failure")));
        var unchanged = await fixture.Operations.ReadAsync(view.Id, default);
        Assert.Equal(view.Revision, unchanged.Revision);
        var denied = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Operations.ReadAsync(
            TriggerDeliveryOperations.EncodeId(fixture.Port.Item.OutboxItemId, "trigger-b"), default).AsTask());
        Assert.Equal("missing", denied.Code);
        await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Operations.ReadAsync(fixture.Port.Item.OutboxItemId, default).AsTask());
    }

    [Fact]
    public async Task Filtered_physical_pages_advance_opaque_cursor_and_multi_resource_source_is_not_duplicated()
    {
        var fixture = new Fixture(); fixture.Port.FilteredFirst = true;
        var first = await fixture.Operations.ListAsync(null, 1, null, default);
        Assert.Equal("trigger-a", Assert.Single(first.Items).TriggerId);
        Assert.NotNull(first.Next); Assert.DoesNotContain(fixture.Port.Item.OutboxItemId, first.Next!);
        var next = await fixture.Operations.ListAsync(null, 1, first.Next, default);
        Assert.Empty(next.Items); Assert.Null(next.Next);
        Assert.True(fixture.Port.ListCalls >= 2);
        fixture.Access.Subject = "another-operator";
        var denied = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Operations.ListAsync(null, 1, first.Next, default).AsTask());
        Assert.Equal("validation", denied.Code);
    }

    [Fact]
    public async Task Controls_delegate_own_recipients_with_physical_progress_fingerprints_and_keep_resource_revision_then_replay_before_expired_preview()
    {
        var fixture = new Fixture();
        fixture.Store.Rows["trigger-a"] = fixture.Store.Rows["trigger-a"] with { Trigger = Resource("trigger-a", 1, TriggerDesiredState.Deleted) };
        var view = await fixture.Operations.ReadAsync(TriggerDeliveryOperations.EncodeId(fixture.Port.Item.OutboxItemId, "trigger-a"), default);
        var command = Command(view, "RetryRecipients", "recipient-a");
        var preview = await fixture.Management.PreviewAsync("trigger-a", Element(new { expectedRevision = 1, command }), default);
        var request = Element(new { requestId = "retry-once", expectedRevision = 1, command, previewId = preview.PreviewId });
        var first = await fixture.Management.ApplyAsync("trigger-a", request, default);
        Assert.Equal("Applied", first.Disposition); Assert.Equal(1, first.Revision);
        Assert.Equal(1, fixture.Port.LastRequest!.ExpectedTriggerRevision);
        Assert.Equal("recipient-a", Assert.Single(fixture.Port.LastRequest.Recipients).RecipientId);
        Assert.Equal(64, fixture.Port.LastRequest.Recipients[0].ProgressFingerprint.Length);
        Assert.Equal(1, fixture.Store.Rows["trigger-a"].Trigger.Revision); Assert.Equal(0, fixture.Store.Writes);
        fixture.Clock.Now = fixture.Clock.Now.AddHours(1);
        fixture.Store.Rows["trigger-a"] = fixture.Store.Rows["trigger-a"] with { Trigger = Resource("trigger-a", 2, TriggerDesiredState.Deleted) };
        var replay = await fixture.Management.ApplyAsync("trigger-a", request, default);
        Assert.Equal("AlreadyApplied", replay.Disposition); Assert.Equal(first.Revision, replay.Revision);
        Assert.Single(fixture.Port.Item.SubscriptionDelivery!.ControlFacts!.Receipts);
        Assert.Equal(0, fixture.Store.Writes);
    }

    [Theory]
    [InlineData("preview")]
    [InlineData("resource")]
    [InlineData("recipient")]
    [InlineData("foreign-recipient")]
    public async Task Altered_or_stale_delivery_control_review_never_reaches_provider_mutation(string change)
    {
        var fixture = new Fixture();
        var view = await fixture.Operations.ReadAsync(TriggerDeliveryOperations.EncodeId(fixture.Port.Item.OutboxItemId, "trigger-a"), default);
        var command = Command(view, "CancelPendingRecipients", "recipient-a");
        var preview = await fixture.Management.PreviewAsync("trigger-a", Element(new { expectedRevision = 1, command }), default);
        var token = preview.PreviewId;
        if (change == "preview") token += "altered";
        if (change == "resource") fixture.Store.Rows["trigger-a"] = fixture.Store.Rows["trigger-a"] with { Trigger = Resource("trigger-a", 2) };
        if (change == "recipient") fixture.Port.Item = fixture.Port.Item.WithSubscriptionDelivery(fixture.Port.Item.SubscriptionDelivery!.WithProgress(new("recipient-a", RuntimeRecipientPhase.Pending, attemptCount: 1)));
        if (change == "foreign-recipient") command = Command(view, "CancelPendingRecipients", "recipient-b");
        var error = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Management.ApplyAsync("trigger-a",
            Element(new { requestId = "stale-control", expectedRevision = 1, command, previewId = token }), default).AsTask());
        Assert.Equal("conflict", error.Code); Assert.Equal(0, fixture.Port.ControlCalls); Assert.Equal(0, fixture.Store.Writes);
    }

    [Fact]
    public async Task Raw_HTTP_status_locator_requires_current_ordinary_delivery_permission_and_unsupported_controls_refuse()
    {
        var fixture = new Fixture(external: true);
        Assert.Equal("trigger-a", (await fixture.Operations.ReadAsync(fixture.Port.Item.OutboxItemId, default)).TriggerId);
        fixture.Access.ReadDeliveries = false;
        var denied = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Operations.ReadAsync(fixture.Port.Item.OutboxItemId, default).AsTask());
        Assert.Equal("missing", denied.Code);
        fixture.Port.Controls = false;
        var command = Element(new { kind = "RetryRecipients" });
        var unavailable = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Management.PreviewAsync("trigger-a",
            Element(new { expectedRevision = 1, command }), default).AsTask());
        Assert.Equal("unavailable", unavailable.Code); Assert.Equal(0, fixture.Port.ControlCalls);
    }

    private static JsonElement Command(TriggerDeliveryView view, string kind, string recipientId) => Element(new { kind, deliveryId = view.Id,
        expectedDeliveryRevision = view.Revision, recipients = new[] { new { recipientId, expectedRevision = view.Recipients.First().Revision } } });
    private static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static StandaloneTrigger Resource(string id, long revision, TriggerDesiredState state = TriggerDesiredState.Paused) => new(id, id,
        "event", 1, Element(new { eventName = "orders" }), revision, 1, 0, state, new(TriggerManagementKind.Studio),
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "creator", tenantId: "actual-tenant");
    private sealed class Fixture
    {
        internal readonly Store Store = new(); internal readonly Access Access = new(); internal readonly Clock Clock = new();
        internal readonly Port Port; internal readonly TriggerDeliveryOperations Operations; internal readonly TriggerManagement Management;
        internal Fixture(bool external = false)
        {
            var protection = new EphemeralDataProtectionProvider(); var discovery = new Discovery();
            Port = new(Source(external)); Operations = new(Store, Access, discovery, protection, Clock, Port);
            Management = new(Store, new AuthoringTriggerCommitter(Store), Access, new Catalog(), discovery, new Mapper(), new Publisher(), protection, Clock, Operations);
        }
    }
    private static RuntimePostCommitOutboxItem Source(bool external)
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(1); var scope = new PersistenceScope("physical");
        var intent = new RuntimePostCommitIntent("event", "publisher-private", "Test.Event", now, null, "key", null);
        var actor = new WorkflowExecutionAuthoritySnapshot("source", "caller"); var schema = new ValueTypeDescriptor("Test.Signal", schemaVersion: 1);
        var payload = Element(new { payload = new { }, trigger = new { configuration = new { http = new { mode = "AsyncFanOut" } } } });
        var standalone = external ? new RuntimeStandaloneIngressSource(1, "admission", "dispatch", now, scope, "actual-tenant", new("physical"),
            "trigger-a", 1, 0, "Http", "request-key", RuntimeStandaloneIngressSource.ComputeRequestFingerprint(scope, "actual-tenant", "trigger-a", "Http", "request-key", schema, payload, actor),
            schema, payload, RuntimeIngressAdmissionPolicy.RequireAll, actor) : null;
        var id = standalone?.OutboxItemId ?? "source-z";
        var fingerprint = standalone?.Fingerprint ?? RuntimeCheckpointCommitFingerprint.ComputeIntent(intent);
        RuntimeSubscriptionRecipient Recipient(string suffix) => new("recipient-" + suffix, "subscription-" + suffix, 1, 1, "private-definition-" + suffix, "native",
            WorkflowActivationSlotIdentity.Create("private-definition-" + suffix, "default"), 1, "activation-" + suffix,
            new("artifact-" + suffix, "producer", "version", "1", "hash"), new("reference-" + suffix, "Published", "publication", "1", "private-definition-" + suffix,
                "version", "1", "activation-" + suffix, WorkflowActivationSlotIdentity.Create("private-definition-" + suffix, "default")), new string('a', 64),
            1, 1, new Dictionary<string, JsonElement>(), actor, "actual-tenant", new("physical"), new(1, scope, id, "subscription-" + suffix));
        var recipients = external ? new[] { Recipient("a") } : new[] { Recipient("a"), Recipient("b") };
        var delivery = new RuntimeSubscriptionDelivery(1, id, fingerprint, scope, recipients, external ? [] : [new("refused-b", 1, "Admission", "blocked-b")],
            recipients.Select(item => new RuntimeRecipientProgress(item.RecipientId, RuntimeRecipientPhase.Pending)).ToArray(),
            resourceCaptures: external ? null : [new("trigger-a", 1, 1, 0, ["subscription-a"], Element(new { name = "a" })),
                new("trigger-b", 1, 1, 0, ["subscription-b", "refused-b"], Element(new { name = "b" }))]);
        return standalone is not null ? RuntimePostCommitOutboxItem.CreateStandaloneIngress(standalone, delivery, RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1))) :
            new(id, intent, RuntimePostCommitOutboxStatus.Pending, now, now, null, 0, null, null, null, null, null, 0, null, 1, scope, null, null, 1, delivery);
    }
    private sealed class Clock : TimeProvider { internal DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(1); public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Access : ITriggerRequestAccess
    {
        public string Subject { get; set; } = "operator"; public TriggerScope Scope { get; } = new(new("physical"), "actual-tenant");
        internal bool ReadDeliveries = true;
        public ValueTask<bool> HasAsync(string permission, object? resource, CancellationToken ct) => ValueTask.FromResult(
            permission != "workflows/runtime:read" && (resource is not StandaloneTrigger trigger || trigger.Id == "trigger-a") &&
            (permission != TriggerPermissions.DeliveriesRead || ReadDeliveries));
        public async ValueTask RequireAsync(string permission, object? resource, CancellationToken ct, bool mask = false)
        { if (!await HasAsync(permission, resource, ct)) throw new TriggerOperationException(mask ? "missing" : "forbidden", "Denied"); }
        public ValueTask<bool> CanReadAsync(object? resource, CancellationToken ct) => HasAsync(TriggerPermissions.Read, resource, ct);
        public ValueTask<TriggerActor> ActorAsync(CancellationToken ct) => ValueTask.FromResult(new TriggerActor(Subject, true, true, true, true, ReadDeliveries, true, true));
    }
    private sealed class Store : IStandaloneTriggerStore
    {
        internal readonly Dictionary<string, TriggerAggregate> Rows = new() { ["trigger-a"] = new(Resource("trigger-a", 1), []), ["trigger-b"] = new(Resource("trigger-b", 1), []) };
        internal int Writes;
        public ValueTask<TriggerAggregate?> FindAsync(TriggerScope scope, string id, CancellationToken ct = default) => ValueTask.FromResult(Rows.GetValueOrDefault(id));
        public ValueTask<TriggerSubscription?> FindSubscriptionAsync(TriggerScope scope, string id, CancellationToken ct = default) => ValueTask.FromResult<TriggerSubscription?>(null);
        public ValueTask<TriggerResourcePage> ListAsync(TriggerScope scope, TriggerResourceQuery query, CancellationToken ct = default) => ValueTask.FromResult(new TriggerResourcePage(Rows.Values.Select(item => item.Trigger).ToArray(), null));
        public ValueTask<TriggerSubscriptionPage> ListSubscriptionsAsync(TriggerScope scope, TriggerSubscriptionQuery query, CancellationToken ct = default) => ValueTask.FromResult(new TriggerSubscriptionPage([], null));
        public ValueTask<TriggerMutationReceipt?> FindMutationAsync(TriggerScope scope, string actor, string request, CancellationToken ct = default) => ValueTask.FromResult<TriggerMutationReceipt?>(null);
        public ValueTask<TriggerMutationReceipt> WriteAsync(TriggerScope scope, TriggerStoreWrite write, CancellationToken ct = default)
        { Writes++; throw new InvalidOperationException("Delivery controls must not write the resource store."); }
    }
    private sealed class Port(RuntimePostCommitOutboxItem item) : IRuntimeIngressDeliveryStore
    {
        internal RuntimePostCommitOutboxItem Item = item; internal bool FilteredFirst, Controls = true;
        internal int ListCalls, ControlCalls; internal RuntimeIngressControlRequest? LastRequest;
        public bool SupportsInspection => true; public bool SupportsControls => Controls;
        public ValueTask<RuntimePostCommitOutboxItem?> FindDeliveryAsync(TriggerScope scope, string id, CancellationToken ct = default) => ValueTask.FromResult<RuntimePostCommitOutboxItem?>(id == Item.OutboxItemId ? Item : null);
        public ValueTask<RuntimeIngressDeliveryPage> ListDeliveriesAsync(TriggerScope scope, RuntimeIngressDeliveryQuery query, CancellationToken ct = default)
        { ListCalls++; return ValueTask.FromResult(FilteredFirst && query.After is null ? new RuntimeIngressDeliveryPage([], "filtered-row") :
            new RuntimeIngressDeliveryPage(query.After == Item.OutboxItemId ? [] : [Item], null)); }
        public ValueTask<RuntimeIngressControlResult> ControlAsync(RuntimeIngressControlRequest request, CancellationToken ct = default)
        {
            ControlCalls++; LastRequest = request; var delivery = Item.SubscriptionDelivery!; var facts = delivery.ControlFacts ?? new([], []);
            var old = facts.Receipts.SingleOrDefault(receipt => receipt.ActorId == request.ActorId && receipt.RequestId == request.RequestId);
            if (old is not null) return ValueTask.FromResult(new RuntimeIngressControlResult(Item, old, true));
            var receipt = new RuntimeIngressControlReceipt(request.ActorId, request.RequestId, request.RequestFingerprint, request.Kind,
                DateTimeOffset.UnixEpoch.AddDays(1), request.Recipients.Select(recipient => new RuntimeIngressRecipientControlResult(recipient.RecipientId,
                    RuntimeIngressControlOutcome.RetryScheduled)).ToArray());
            Item = Item.WithSubscriptionDelivery(delivery.WithControlFacts(new(facts.Receipts.Append(receipt).ToArray(), facts.Transport)));
            return ValueTask.FromResult(new RuntimeIngressControlResult(Item, receipt, false));
        }
    }
    private sealed class Discovery : IStandaloneTriggerTargetDiscovery
    {
        public ValueTask<TriggerTargetDescription?> DescribeAsync(TriggerScope scope, string id, string slot, CancellationToken ct = default) => ValueTask.FromResult<TriggerTargetDescription?>(null);
        public ValueTask<TriggerTargetPage> ListAsync(TriggerScope scope, TriggerTargetQuery query, CancellationToken ct = default) => ValueTask.FromResult(new TriggerTargetPage([], null));
    }
    private sealed class Catalog : IStandaloneTriggerTypeCatalog
    { public TriggerTypeDescriptor? Find(string key) => null; public IReadOnlyList<TriggerTypeDescriptor> List() => []; }
    private sealed class Mapper : ITriggerInputMapper
    {
        public ValueTask<WorkflowExecutableInputValidation> MapAsync(WorkflowExecutableInputContract contract, IReadOnlyList<TriggerInputMapping> mappings, JsonElement source, CancellationToken ct = default) => ValueTask.FromResult(WorkflowExecutableInputValidation.Success([]));
        public WorkflowExecutableInputValidation ValidateMappings(WorkflowExecutableInputContract contract, IReadOnlyList<TriggerInputMapping> mappings) => WorkflowExecutableInputValidation.Success([]);
    }
    private sealed class Publisher : IInlineEventPublisher { public Task Publish(IEvent value, CancellationToken ct = default) => Task.CompletedTask; }
}
