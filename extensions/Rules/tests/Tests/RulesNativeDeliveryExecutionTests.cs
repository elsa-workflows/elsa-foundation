using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore;
using Elsa.Rules.Workflows.Runtime.Models;
using Elsa.Rules.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Rules.Tests;

// The Rules row, lease and acknowledgement transaction are real SQLite persistence. Native state
// stores and dispatch are controlled doubles; this does not claim persisted composite acceptance.
public sealed class RulesNativeDeliveryExecutionTests : IAsyncLifetime
{
    private readonly RuleStoreTestFixture database = new();
    private readonly RulesNativeDeliveryTestFixture native = new();
    private readonly RuleStoreTestInterceptors.SqliteClock clock = new(DateTimeOffset.UtcNow.UtcTicks);
    private readonly TestDispatcher dispatcher = new();
    private ServiceProvider provider = null!;
    private RecordingScopes scopes = null!;
    private EfRuleRunStore store = null!;
    private RulesNativeDeliveryExecution execution = null!;

    public async Task InitializeAsync()
    {
        await database.InitializeAsync();
        provider = native.CreateServiceProvider(configure: services => services.AddSingleton<IBookmarkResumeDispatcher>(dispatcher));
        scopes = new(provider.GetRequiredService<IPersistenceOperationScopeFactory>());
        store = new(new RuleStoreTestFixture.SqliteContextFactory(database.DatabasePath, [clock]),
            new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Refused, new("rules.test-refusal")),
            deliveryProofValidator: new RulesNativeDeliveryProofValidator(scopes));
        execution = new(store, store, scopes);
        Assert.Equal(RuleRefusalDisposition.Refused,
            (await store.RefuseAdmissionAsync(new(native.Invocation, new("rules.test-refusal")))).Disposition);
        var lease = Assert.IsType<RuleDeliveryLease>((await store.ClaimDeliveryAsync(new("setup", [1]))).Lease);
        native.SetEnvelope(Assert.IsType<RuleTerminalEnvelope>((await store.ReadDeliveryAsync(lease)).Envelope));
        clock.Set(lease.ExpiresAt.UtcTicks);
    }

    public async Task DisposeAsync()
    {
        if (provider is not null)
            await provider.DisposeAsync();
        await database.DisposeAsync();
    }

    [Theory]
    [InlineData(BookmarkResumeDispatchStatus.Duplicate)]
    [InlineData(BookmarkResumeDispatchStatus.Deferred)]
    [InlineData(BookmarkResumeDispatchStatus.Rejected)]
    public async Task Dispatch_status_without_native_completion_never_acknowledges(BookmarkResumeDispatchStatus status)
    {
        dispatcher.Status = status;

        Assert.False(await execution.ExecuteOneAsync("worker"));

        Assert.Equal(1, dispatcher.Calls);
        Assert.Equal(3, scopes.Created.Count);
        Assert.All(scopes.Created, scope => Assert.Throws<ObjectDisposedException>(() => scope.ServiceProvider));
        await AssertAcknowledgedAsync(false);
        var request = Assert.IsType<BookmarkResumeDispatchRequest>(dispatcher.LastRequest);
        Assert.Equal(native.Address.BookmarkId, request.BookmarkId);
        Assert.Equal(native.Address.StimulusType, request.StimulusType);
        Assert.Equal(native.Address.StimulusHash, request.StimulusHash);
        Assert.Equal(native.Envelope.DeliveryId, request.IdempotencyKey);
        Assert.Equal(RulesNativeBoundary.CompletionType, request.PayloadType);
        Assert.Equal(RulesNativeBoundary.ProviderId, request.ProviderId);
        var completion = RulesNativeBoundary.Read<RunRulesCompletion>(request.Input!.Value);
        Assert.Equal(native.Address, completion.Destination);
        Assert.Equal(native.Envelope.DeliveryId, completion.DeliveryId);
        Assert.Equal(RuleRecordCodec.TerminalBytes(native.Envelope.Result),
            RuleRecordCodec.TerminalBytes(RuleRecordCodec.ReadTerminal(
                System.Text.Encoding.UTF8.GetBytes(completion.Result!.Value.GetRawText()))));
    }

    [Fact]
    public async Task Completion_after_dispatch_is_rechecked_before_the_real_store_acknowledges()
    {
        dispatcher.OnDispatch = () => native.SetCompleted(retirePrivateState: true, retireReceiptPayload: true);

        Assert.True(await execution.ExecuteOneAsync("worker"));

        Assert.Equal(1, dispatcher.Calls);
        Assert.Equal(4, scopes.Created.Count);
        Assert.Equal(4, scopes.Created.Distinct().Count());
        await AssertAcknowledgedAsync(true);
        Assert.False(await execution.ExecuteOneAsync("worker"));
        Assert.Equal(RuleDeliveryClaimDisposition.NoEligibleDelivery,
            (await store.ClaimDeliveryAsync(new("another-worker", [1]))).Disposition);
    }

    [Fact]
    public async Task Consumed_inflight_receipt_retains_obligation_and_later_completion_survives_reclaim()
    {
        native.SetInFlightConsumedReceipt();
        Assert.False(await execution.ExecuteOneAsync("worker-one"));
        Assert.Equal(0, dispatcher.Calls);
        await AssertAcknowledgedAsync(false);
        await ExpireDeliveryAsync();
        native.SetCompleted(retirePrivateState: true, retireReceiptPayload: true, retryBeforeFinal: true);

        Assert.True(await execution.ExecuteOneAsync("worker-two"));

        Assert.Equal(0, dispatcher.Calls);
        await AssertAcknowledgedAsync(true);
    }

    [Fact]
    public async Task Actual_parent_terminal_state_can_settle_an_undelivered_obligation()
    {
        native.SetChildCancelled();
        Assert.False(await execution.ExecuteOneAsync("worker-one"));
        await AssertAcknowledgedAsync(false);
        await ExpireDeliveryAsync();
        native.SetParentTerminal(WorkflowExecutionStatus.Cancelled);

        Assert.True(await execution.ExecuteOneAsync("worker-two"));

        Assert.Equal(0, dispatcher.Calls);
        await AssertAcknowledgedAsync(true);
    }

    [Fact]
    public async Task Store_revalidation_rejects_native_proof_that_changed_after_initial_observation()
    {
        dispatcher.OnDispatch = () => native.SetCompleted(retirePrivateState: true, retireReceiptPayload: true);
        scopes.BeforeCreate = count =>
        {
            if (count == 4)
                native.SetInFlightConsumedReceipt();
        };

        Assert.False(await execution.ExecuteOneAsync("worker"));

        Assert.Equal(4, scopes.Created.Count);
        await AssertAcknowledgedAsync(false);
    }

    [Fact]
    public async Task Altered_native_completion_does_not_consume_the_frozen_terminal()
    {
        var facts = RuleTestData.Facts("tampered");
        var expected = native.Envelope.Result;
        var altered = new RuleTerminalResult(expected.TerminalId, expected.Kind, expected.CommittedRounds,
            expected.ResultSchemaVersion, facts, RuleRecordCodec.FactsHash(facts), expected.Diagnostic);
        native.SetCompleted(activityResult: altered, retirePrivateState: true, retireReceiptPayload: true);

        Assert.False(await execution.ExecuteOneAsync("worker"));

        Assert.Equal(0, dispatcher.Calls);
        await AssertAcknowledgedAsync(false);
    }

    [Fact]
    public async Task Unavailable_native_payload_preserves_delivery_for_a_later_worker()
    {
        native.SetCompleted(retirePrivateState: true, retireReceiptPayload: true, externalCompletionResult: true);
        native.ExternalPayloads.Failure = new IOException("temporarily unavailable");

        await Assert.ThrowsAsync<IOException>(async () => await execution.ExecuteOneAsync("worker-one"));

        await AssertAcknowledgedAsync(false);
        native.ExternalPayloads.Failure = null;
        await ExpireDeliveryAsync();
        Assert.True(await execution.ExecuteOneAsync("worker-two"));
        await AssertAcknowledgedAsync(true);
    }

    [Fact]
    public async Task Native_unavailability_during_store_validation_does_not_acknowledge()
    {
        native.SetCompleted(retirePrivateState: true, retireReceiptPayload: true, externalCompletionResult: true);
        scopes.BeforeCreate = count =>
        {
            if (count == 2)
                native.ExternalPayloads.Failure = new IOException("unavailable during acknowledgement validation");
        };

        Assert.False(await execution.ExecuteOneAsync("worker-one"));

        Assert.Equal(2, scopes.Created.Count);
        await AssertAcknowledgedAsync(false);
        native.ExternalPayloads.Failure = null;
        await ExpireDeliveryAsync();
        Assert.True(await execution.ExecuteOneAsync("worker-two"));
        await AssertAcknowledgedAsync(true);
    }

    private async Task AssertAcknowledgedAsync(bool expected)
    {
        await using var db = database.Contexts.CreateDbContext();
        var row = await db.Runs.AsNoTracking().SingleAsync();
        Assert.Equal(expected, row.DeliveryAcknowledged);
        Assert.Equal(expected, row.DeliveryAcknowledgementJson is not null);
        Assert.NotNull(row.DeliveryPayloadJson);
    }

    private async Task ExpireDeliveryAsync()
    {
        await using var db = database.Contexts.CreateDbContext();
        var row = await db.Runs.AsNoTracking().SingleAsync();
        clock.Set(row.DeliveryExpiresAtUtcTicks!.Value);
    }

    private sealed class RecordingScopes(IPersistenceOperationScopeFactory inner) : IPersistenceOperationScopeFactory
    {
        internal List<PersistenceOperationScope> Created { get; } = [];
        internal Action<int>? BeforeCreate { get; set; }
        public async ValueTask<PersistenceOperationScope> CreateAsync(PersistenceScope persistenceScope,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(RulesNativeDeliveryTestFixture.Partition, persistenceScope.Value);
            BeforeCreate?.Invoke(Created.Count + 1);
            var scope = await inner.CreateAsync(persistenceScope, cancellationToken);
            Created.Add(scope);
            return scope;
        }
    }

    private sealed class TestDispatcher : IBookmarkResumeDispatcher
    {
        internal Action? OnDispatch { get; set; }
        internal BookmarkResumeDispatchStatus Status { get; set; } = BookmarkResumeDispatchStatus.Duplicate;
        internal int Calls { get; private set; }
        internal BookmarkResumeDispatchRequest? LastRequest { get; private set; }
        public ValueTask<BookmarkResumeDispatchResult> DispatchAsync(BookmarkResumeDispatchRequest request,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            LastRequest = request;
            OnDispatch?.Invoke();
            return ValueTask.FromResult(new BookmarkResumeDispatchResult(Status, request.WorkflowExecutionId,
                reason: "Controlled native dispatch result; only persisted outcome can prove delivery."));
        }
    }
}
