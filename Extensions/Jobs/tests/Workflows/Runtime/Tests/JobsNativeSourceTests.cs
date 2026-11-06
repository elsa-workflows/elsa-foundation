using Elsa.Jobs.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Jobs.Workflows.Runtime.Tests;

public sealed class JobsNativeSourceTests
{
    [Fact]
    public async Task All_sender_kinds_require_a_real_durable_source_without_a_stimulus_recipient_protocol()
    {
        var services = new ServiceCollection(); services.AddJobsWorkflowsRuntime();
        await using var provider = services.BuildServiceProvider();
        var contributions = provider.GetServices<RuntimePostCommitIntentHandlerContribution>().ToArray();
        Assert.Equal(6, contributions.Length);
        Assert.Equal(new[] { JobsNativeBoundary.CancelKind, JobsNativeBoundary.SettlementKind, JobsNativeBoundary.StartKind,
                JobsNativeSubmissionBoundary.OwnershipKind, JobsNativeSubmissionBoundary.CancelKind, JobsNativeSubmissionBoundary.StartKind }.Order(StringComparer.Ordinal),
            contributions.Select(item => item.IntentKind).Order(StringComparer.Ordinal));
        Assert.All(contributions, item => { Assert.True(item.RequiresDurableSource); Assert.Null(item.StimulusDeliveryProtocolVersion); });
    }

    [Theory]
    [InlineData(JobsNativeBoundary.StartKind)]
    [InlineData(JobsNativeBoundary.CancelKind)]
    [InlineData(JobsNativeBoundary.SettlementKind)]
    public async Task Bare_intent_delivery_cannot_invoke_a_required_sender(string kind)
    {
        var (_, admission, _) = await NativeFixture.SavedAsync();
        var intent = kind == JobsNativeBoundary.SettlementKind ? JobsNativeBoundary.SettlementIntent(admission)
            : JobsNativeBoundary.Intent(admission, kind == JobsNativeBoundary.CancelKind);
        IRuntimePostCommitIntentHandler handler = kind == JobsNativeBoundary.SettlementKind
            ? new JobsNativeSettlementIntentHandler(null!, null!, null!)
            : new JobsNativeIntentHandler(null!, null!, null!, null!, null!, null!, [], null!);
        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => handler.HandleAsync(intent).AsTask());
    }

    [Theory]
    [InlineData(JobsNativeBoundary.StartKind)]
    [InlineData(JobsNativeBoundary.CancelKind)]
    [InlineData(JobsNativeBoundary.SettlementKind)]
    public async Task Verified_physical_source_runs_the_exact_start_cancel_or_no_admission_settlement(string kind)
    {
        var (saved, admission, _) = await NativeFixture.SavedAsync();
        var intent = kind == JobsNativeBoundary.SettlementKind ? JobsNativeBoundary.SettlementIntent(admission)
            : JobsNativeBoundary.Intent(admission, kind == JobsNativeBoundary.CancelKind);
        var item = Item(intent);
        var lookup = new Lookup(item); var transport = new JobTransport();
        var verifier = new JobsNativeSourceVerifier(lookup, new Access(), new Clock(NativeFixture.Now));
        var activities = new ActivityStore(); activities.States.Add(saved.InvocationId, saved);
        if (kind == JobsNativeBoundary.SettlementKind) transport.Origin = new(admission.Origin, admission.RequestSha256, true, false, null);
        IRuntimePostCommitDurableSourceIntentHandler handler = kind == JobsNativeBoundary.SettlementKind
            ? new JobsNativeSettlementIntentHandler(new(transport, null!, null!), new Access(), verifier)
            : new JobsNativeIntentHandler(transport, transport, new WorkflowStore(NativeFixture.Parent()) { Missing = kind == JobsNativeBoundary.CancelKind },
                activities, new BookmarkStore(NativeFixture.Bookmark(saved)), new Access(), [], verifier);
        await handler.HandleAsync(Context(item));
        Assert.Equal(kind == JobsNativeBoundary.StartKind ? 1 : 0, transport.Admissions.Count);
        Assert.Equal(kind == JobsNativeBoundary.CancelKind ? 1 : 0, transport.Cancellations.Count);
        Assert.Equal(1, lookup.PhysicalReads); Assert.Empty(transport.Acknowledgements);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("scope")]
    [InlineData("fingerprint")]
    [InlineData("owner")]
    [InlineData("fence")]
    [InlineData("expiry")]
    public async Task Claimed_context_without_the_same_live_physical_source_cannot_cancel_or_admit(string changed)
    {
        var (saved, admission, _) = await NativeFixture.SavedAsync();
        var intent = JobsNativeBoundary.Intent(admission, cancel: true); var original = Item(intent);
        var current = changed switch
        {
            "missing" => null,
            "scope" => Item(intent, sourceScope: new("different-partition")),
            "fingerprint" => Item(new(intent.IntentId, intent.WorkflowExecutionId, intent.Kind, intent.RecordedAt.AddSeconds(1),
                intent.ActivityExecutionId, intent.IdempotencyKey, intent.Payload)),
            "owner" => Item(intent, owner: "successor-owner"),
            "fence" => Item(intent, fence: 2),
            _ => Item(intent, visibleAfter: NativeFixture.Now)
        };
        var transport = new JobTransport(); var lookup = new Lookup(current);
        var handler = new JobsNativeIntentHandler(transport, transport, new WorkflowStore(NativeFixture.Parent()), new ActivityStore(),
            new BookmarkStore(NativeFixture.Bookmark(saved)), new Access(), [], new(lookup, new Access(), new Clock(NativeFixture.Now)));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => handler.HandleAsync(Context(original)).AsTask());
        Assert.Empty(transport.Cancellations); Assert.Empty(transport.Admissions); Assert.Equal(1, lookup.PhysicalReads);
    }

    [Fact]
    public async Task Provider_local_cancellation_remains_safe_unavailable_source_with_its_original_cause()
    {
        var (_, admission, _) = await NativeFixture.SavedAsync();
        var item = Item(JobsNativeBoundary.Intent(admission, cancel: true));
        var cause = new OperationCanceledException("protected-provider-canary"); var lookup = new Lookup(item) { Failure = cause };
        var error = await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() =>
            new JobsNativeSourceVerifier(lookup, new Access(), new Clock(NativeFixture.Now)).VerifyAsync(Context(item), CancellationToken.None).AsTask());
        Assert.Same(cause, error.InnerException); Assert.True(error.IsUnavailable); Assert.DoesNotContain("protected-provider-canary", error.Message);
    }

    private static RuntimePostCommitOutboxItem Item(RuntimePostCommitIntent intent, string owner = "source-owner", long fence = 1,
        DateTimeOffset? visibleAfter = null, PersistenceScope? sourceScope = null) =>
        new("source-commit:" + intent.IntentId, intent, RuntimePostCommitOutboxStatus.Delivering, NativeFixture.Now, NativeFixture.Now,
            RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1)), 1, owner, NativeFixture.Now.AddSeconds(-1), null, null,
            NativeFixture.Metadata, fence, visibleAfter ?? NativeFixture.Now.AddMinutes(1), null, sourceScope ?? new(NativeFixture.Scope.PersistencePartition), null);

    private static PostCommitDeliveryContext Context(RuntimePostCommitOutboxItem item) => new(new(item, item.DeliveringOwnerId!, item.DeliveryFencingToken,
        item.DeliveryStartedAt!.Value, item.DeliveryVisibleAfter!.Value), new(NativeFixture.Scope.PersistencePartition));

    private sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private sealed class Lookup(RuntimePostCommitOutboxItem? current) : IPostCommitOutboxLookupStore
    {
        internal Exception? Failure { get; set; }
        internal int PhysicalReads { get; private set; }
        public ValueTask<RuntimePostCommitOutboxItem?> FindAsync(string outboxItemId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Working-set lookup cannot prove a committed source.");
        public ValueTask<RuntimePostCommitOutboxItem?> FindDurableAsync(string outboxItemId, CancellationToken cancellationToken = default)
        {
            PhysicalReads++;
            if (Failure is { } failure) throw failure;
            return ValueTask.FromResult(current is not null && current.OutboxItemId == outboxItemId ? current : null);
        }
    }
}
