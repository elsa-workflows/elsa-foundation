using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Workflows.Runtime.Activities;
using Elsa.Rules.Workflows.Runtime.Models;
using Elsa.Rules.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using static Elsa.Rules.Tests.RuleTestData;
using ActivityAttemptReason = Elsa.Workflows.Runtime.Core.Models.ActivityAttemptReason;
using ActivityTransitionKind = Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind;

namespace Elsa.Rules.Tests;

public sealed class RulesNativeDeliveryProofTests
{
    [Fact]
    public async Task Only_exact_live_bookmark_or_final_consumed_outcome_is_provable()
    {
        var fixture = new RulesNativeDeliveryTestFixture();
        var waiting = await fixture.InspectAsync();
        Assert.Null(waiting.Proof);
        Assert.Equal(fixture.Address.BookmarkId, waiting.Bookmark?.BookmarkId);

        fixture.SetInFlightConsumedReceipt();
        var inFlight = await fixture.InspectAsync();
        Assert.Null(inFlight.Proof);
        Assert.Null(inFlight.Bookmark);

        fixture.SetCompleted(retirePrivateState: true, retireReceiptPayload: true);
        var completed = await fixture.InspectAsync();
        Assert.Equal(RuleDeliveryProofKind.AlreadyConsumed, completed.Proof?.Kind);
        Assert.Null(completed.Bookmark);
        Assert.Equal(fixture.Envelope.Result.TerminalId, completed.Proof?.TerminalId);
    }

    [Fact]
    public async Task Enqueue_receipt_and_completed_activity_without_final_consumed_attempt_are_not_proof()
    {
        var fixture = new RulesNativeDeliveryTestFixture();
        fixture.SetCompleted(retirePrivateState: true, retireReceiptPayload: false, receiptStatus: ActivityTriggerDeliveryStatus.Received);

        var inspection = await fixture.InspectAsync();

        Assert.Null(inspection.Proof);
        Assert.Null(inspection.Bookmark);
    }

    [Fact]
    public async Task Recomputed_digest_does_not_make_altered_completion_facts_match_the_frozen_result()
    {
        var fixture = new RulesNativeDeliveryTestFixture();
        var alteredFacts = Facts("final", "joined", "left", "right", "tampered");
        var altered = new RuleTerminalResult(fixture.Envelope.Result.TerminalId, RuleTerminalKind.Succeeded,
            fixture.Envelope.Result.CommittedRounds, fixture.Envelope.Result.ResultSchemaVersion,
            alteredFacts, RuleRecordCodec.FactsHash(alteredFacts), fixture.Envelope.Result.Diagnostic);
        fixture.SetCompleted(activityResult: altered, retirePrivateState: true, retireReceiptPayload: true);

        Assert.Null((await fixture.InspectAsync()).Proof);
    }

    [Fact]
    public async Task External_activity_completion_result_is_loaded_and_compared_canonically()
    {
        var fixture = new RulesNativeDeliveryTestFixture();
        fixture.SetCompleted(retirePrivateState: true, retireReceiptPayload: true, externalCompletionResult: true);

        var inspection = await fixture.InspectAsync();

        Assert.Equal(RuleDeliveryProofKind.AlreadyConsumed, inspection.Proof?.Kind);
        Assert.Null(inspection.Bookmark);
        Assert.Equal(1, fixture.ExternalPayloads.ReadCalls);
    }

    [Fact]
    public async Task Successful_completion_allows_projector_time_before_final_attempt_end_but_not_before_attempt_start()
    {
        var fixture = new RulesNativeDeliveryTestFixture();
        fixture.SetCompleted(retirePrivateState: true, retireReceiptPayload: true);

        Assert.Equal(RuleDeliveryProofKind.AlreadyConsumed, (await fixture.InspectAsync()).Proof?.Kind);

        var completed = fixture.Activities.State!;
        fixture.Activities.State = completed with
        {
            Completion = new(completed.Completion!.InvocationId, completed.Completion.AttemptId,
                completed.Completion.Result, completed.Completion.OutcomeKey,
                RulesNativeDeliveryTestFixture.Now.AddSeconds(2), completed.Completion.ContractFingerprint)
        };

        Assert.Null((await fixture.InspectAsync()).Proof);
    }

    [Fact]
    public async Task Fault_proof_uses_the_projected_fault_type_metadata_marker_and_exact_code()
    {
        var fixture = new RulesNativeDeliveryTestFixture();
        fixture.SetFaulted(retirePrivateState: true, retireReceiptPayload: true);
        var valid = await fixture.InspectAsync();
        Assert.Equal(RuleDeliveryProofKind.AlreadyConsumed, valid.Proof?.Kind);

        fixture.SetFaulted(retirePrivateState: true, retireReceiptPayload: true, wrongMarker: true);
        var wrongMarker = await fixture.InspectAsync();
        Assert.Null(wrongMarker.Proof);

        fixture.SetFaulted(retirePrivateState: true, retireReceiptPayload: true, wrongCode: true);
        Assert.Null((await fixture.InspectAsync()).Proof);
    }

    [Fact]
    public async Task Parent_terminal_proves_cancellation_but_cancelled_child_does_not()
    {
        var fixture = new RulesNativeDeliveryTestFixture();
        fixture.SetChildCancelled();

        var childOnly = await fixture.InspectAsync();
        Assert.Null(childOnly.Proof);

        fixture.SetParentTerminal(WorkflowExecutionStatus.Cancelled);
        var parent = await fixture.InspectAsync();
        Assert.Equal(RuleDeliveryProofKind.ParentProvenTerminal, parent.Proof?.Kind);
        Assert.StartsWith("rules-parent-terminal-v1:", parent.Proof!.ParentCommitOrTerminalId, StringComparison.Ordinal);
        Assert.Null(parent.Bookmark);
    }

    [Fact]
    public async Task Validator_rechecks_scope_pin_and_all_proof_fields_in_a_fresh_partition_scope()
    {
        var fixture = new RulesNativeDeliveryTestFixture();
        fixture.SetCompleted(retirePrivateState: true, retireReceiptPayload: true);
        var observed = (await fixture.InspectAsync()).Proof!;
        using var provider = fixture.CreateServiceProvider();
        var validator = new RulesNativeDeliveryProofValidator(provider.GetRequiredService<IPersistenceOperationScopeFactory>());

        var acceptedKindAlias = new RuleDeliveryProof(RuleDeliveryProofKind.Consumed, observed.DestinationIdentity,
            observed.TerminalId, observed.PayloadSha256, observed.ParentCommitOrTerminalId);
        var valid = await validator.ValidateAsync(fixture.Invocation, fixture.Envelope, acceptedKindAlias);
        Assert.Equal(RuleDeliveryProofValidationDisposition.Equivalent, valid.Disposition);

        var wrongEvidence = new RuleDeliveryProof(observed.Kind, observed.DestinationIdentity,
            observed.TerminalId, observed.PayloadSha256, "another-attempt");
        Assert.Equal(RuleDeliveryProofValidationDisposition.Invalid,
            (await validator.ValidateAsync(fixture.Invocation, fixture.Envelope, wrongEvidence)).Disposition);

        fixture.Workflows.State = fixture.Workflows.State! with
        {
            PinnedExecutable = fixture.Workflows.State.PinnedExecutable with { ArtifactHash = new string('d', 64) }
        };
        Assert.Equal(RuleDeliveryProofValidationDisposition.Invalid,
            (await validator.ValidateAsync(fixture.Invocation, fixture.Envelope, observed)).Disposition);
    }

    [Fact]
    public async Task Validator_rejects_mismatched_retained_native_destination_before_opening_a_scope()
    {
        var fixture = new RulesNativeDeliveryTestFixture();
        fixture.SetCompleted(retirePrivateState: true, retireReceiptPayload: true);
        var invocation = new RuleInvocationEnvelope(fixture.Invocation.Identity, fixture.Invocation.Artifact,
            fixture.Invocation.InitialFacts, fixture.Invocation.MaxRounds, fixture.Invocation.ExpectedImplementation,
            fixture.Invocation.InvocationSchemaVersion, nativeDestination: null);
        var factory = new ThrowingScopeFactory(new InvalidOperationException("scope should not be opened"));
        var validator = new RulesNativeDeliveryProofValidator(factory);

        var validation = await validator.ValidateAsync(invocation, fixture.Envelope,
            (await fixture.InspectAsync()).Proof!);

        Assert.Equal(RuleDeliveryProofValidationDisposition.Invalid, validation.Disposition);
        Assert.Equal(0, factory.Calls);
    }

    [Fact]
    public async Task External_payload_provider_failure_is_unavailable_and_cancellation_propagates()
    {
        var fixture = new RulesNativeDeliveryTestFixture();
        fixture.SetCompleted(retirePrivateState: true, retireReceiptPayload: true, externalCompletionResult: true);
        fixture.ExternalPayloads.Failure = new IOException("temporarily unavailable");
        using var provider = fixture.CreateServiceProvider();
        var validator = new RulesNativeDeliveryProofValidator(provider.GetRequiredService<IPersistenceOperationScopeFactory>());
        var candidate = new RuleDeliveryProof(RuleDeliveryProofKind.AlreadyConsumed,
            RuleRecordCodec.DestinationIdentity(fixture.Address), fixture.Envelope.Result.TerminalId,
            fixture.Envelope.CanonicalPayloadSha256, "attempt-resume");

        Assert.Equal(RuleDeliveryProofValidationDisposition.Unavailable,
            (await validator.ValidateAsync(fixture.Invocation, fixture.Envelope, candidate)).Disposition);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await validator.ValidateAsync(fixture.Invocation, fixture.Envelope, candidate, cancellation.Token));
    }

    [Fact]
    public async Task Duplicate_or_overlapping_resume_attempt_history_cannot_prove_consumption()
    {
        var fixture = new RulesNativeDeliveryTestFixture();
        fixture.SetCompleted(retirePrivateState: true, retireReceiptPayload: true);
        var state = fixture.Activities.State!;
        fixture.Activities.State = state with
        {
            Attempts = state.Attempts!.Concat(
            [
                new ActivityAttempt("duplicate-ordinal", fixture.Address.ActivityExecutionId, 2,
                    ActivityAttemptReason.Retry, RulesNativeDeliveryTestFixture.Now.AddSeconds(1),
                    RulesNativeDeliveryTestFixture.Now.AddSeconds(2), transitionKind: ActivityTransitionKind.Fault)
            ]).ToArray()
        };

        Assert.Null((await fixture.InspectAsync()).Proof);

        fixture.SetCompleted(retirePrivateState: true, retireReceiptPayload: true, retryBeforeFinal: true, overlapRetry: true);
        Assert.Null((await fixture.InspectAsync()).Proof);
    }

    private sealed class ThrowingScopeFactory(Exception exception) : IPersistenceOperationScopeFactory
    {
        public int Calls { get; private set; }
        public ValueTask<PersistenceOperationScope> CreateAsync(PersistenceScope persistenceScope,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return ValueTask.FromException<PersistenceOperationScope>(exception);
        }
    }
}

/// <summary>Reusable native-state fixture for Rules executor and proof tests; no synthetic proof service is installed.</summary>
internal sealed class RulesNativeDeliveryTestFixture
{
    internal static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-22T10:00:00Z");
    internal const string WorkflowId = "native-workflow";
    internal const string ActivityId = "native-activity";
    internal const string ExecutableId = "native-executable";
    internal const string ExecutableHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    internal const string NodeId = "native-rules-node";
    internal const string BookmarkId = "native-bookmark";
    internal const string RegistrationId = "native-registration";
    internal const string Partition = "rules-native-partition";
    internal const string TenantId = "rules-native-tenant";
    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata = new Dictionary<string, string>(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    internal RulesNativeDeliveryTestFixture()
    {
        var sample = Invocation();
        var scope = new RuleScope(TenantId, Partition);
        var program = new RuleWorkflowProgramPin(1, new string('c', 64), sample.Artifact,
            sample.ExpectedImplementation, vocabularyCount: 7);
        var runId = RulesNativeBoundary.RunId(scope, WorkflowId, ActivityId);
        Request = new(scope, runId, program, Facts("seed"), 4, WorkflowId, ActivityId,
            ExecutableId, ExecutableHash, NodeId, "compiled-resume-target-id");
        Address = new(RulesNativeBoundary.FormatVersion, WorkflowId, ActivityId, ExecutableId, ExecutableHash,
            NodeId, BookmarkId, Request.ResumeTargetId, RegistrationId, RulesNativeBoundary.StimulusType,
            RulesNativeBoundary.StimulusHash(Request), RulesNativeBoundary.CompletionType.Alias,
            RulesNativeBoundary.FormatVersion, "once", RulesNativeBoundary.ProviderId);
        Invocation = RulesNativeBoundary.Invocation(Request, Address);
        var resultFacts = Facts("final", "joined", "left", "right", "seed");
        var result = new RuleTerminalResult("rules-terminal-1", RuleTerminalKind.Succeeded, 4, 1,
            resultFacts, RuleRecordCodec.FactsHash(resultFacts), null);
        Envelope = MakeEnvelope(result);
        Workflows = new(NewParent());
        Activities = new(CreateWaitingActivity());
        Bookmarks = new(NewBookmark());
        SetWaiting();
    }

    internal RunRulesRequest Request { get; }
    internal RuleNativeResultAddress Address { get; }
    internal RuleInvocationEnvelope Invocation { get; }
    internal RuleTerminalEnvelope Envelope { get; private set; }
    internal TestWorkflowStore Workflows { get; }
    internal TestActivityStore Activities { get; }
    internal TestBookmarkStore Bookmarks { get; }
    internal TestExternalPayloadStore ExternalPayloads { get; } = new();
    internal RulesNativeDeliveryInspector CreateInspector(bool includeExternalPayloadStore = true) =>
        new(Workflows, Activities, Bookmarks, new TestAccess(), includeExternalPayloadStore ? ExternalPayloads : null);

    internal async Task<RulesNativeDeliveryInspection> InspectAsync(CancellationToken cancellationToken = default) =>
        await CreateInspector().InspectAsync(Invocation.Identity, RuleRecordCodec.Fingerprint(Invocation), Envelope, cancellationToken);

    internal ServiceProvider CreateServiceProvider(
        bool includeExternalPayloadStore = true,
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddPersistenceCore(Partition);
        services.AddSingleton<IWorkflowExecutionStateStore>(Workflows);
        services.AddSingleton<IActivityExecutionStateStore>(Activities);
        services.AddSingleton<IBookmarkStateStore>(Bookmarks);
        if (includeExternalPayloadStore)
            services.AddSingleton<IExternalPayloadStore>(ExternalPayloads);
        services.AddScoped<RulesNativeDeliveryInspector>();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    internal void SetEnvelope(RuleTerminalEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Destination != Address)
            throw new ArgumentException("The Rules terminal envelope must use this fixture's frozen native address.", nameof(envelope));
        Envelope = envelope;
        SetWaiting();
    }

    internal void SetWaiting()
    {
        Activities.State = CreateWaitingActivity();
        Bookmarks.State = NewBookmark();
    }

    internal void SetInFlightConsumedReceipt()
    {
        var started = Now.AddSeconds(3);
        var receipt = NewReceipt(ActivityTriggerDeliveryStatus.Consumed, retired: true);
        Activities.State = CreateWaitingActivity() with
        {
            Status = ActivityExecutionStatus.Running,
            BookmarkIds = [],
            TriggerRegistrations = [],
            TriggerDeliveries = [receipt],
            Attempts =
            [
                InitialAttempt(),
                new ActivityAttempt("resume-attempt-active", ActivityId, 2, ActivityAttemptReason.Resume,
                    started, triggerDeliveryId: receipt.DeliveryId)
            ]
        };
        Bookmarks.State = null;
    }

    internal void SetCompleted(
        RuleTerminalResult? activityResult = null,
        bool retirePrivateState = true,
        bool retireReceiptPayload = false,
        bool externalCompletionResult = false,
        ActivityTriggerDeliveryStatus receiptStatus = ActivityTriggerDeliveryStatus.Consumed,
        bool retryBeforeFinal = false,
        bool overlapRetry = false,
        DateTimeOffset? completionAt = null)
    {
        var nativeDeliveryId = "native-delivery-1";
        var receipt = NewReceipt(receiptStatus, retireReceiptPayload, nativeDeliveryId);
        var finalStarted = Now.AddSeconds(retryBeforeFinal ? 5 : 3);
        var finalEnded = Now.AddSeconds(retryBeforeFinal ? 6 : 4);
        var final = new ActivityAttempt("resume-attempt-final", ActivityId, retryBeforeFinal ? 3 : 2,
            ActivityAttemptReason.Resume, finalStarted, finalEnded, nativeDeliveryId, ActivityTransitionKind.Complete);
        var attempts = new List<ActivityAttempt> { InitialAttempt() };
        if (retryBeforeFinal)
        {
            var retryStarted = Now.AddSeconds(2);
            var retryEnded = overlapRetry ? Now.AddSeconds(5.5) : Now.AddSeconds(2.5);
            attempts.Add(new("resume-attempt-retry", ActivityId, 2, ActivityAttemptReason.Resume,
                retryStarted, retryEnded, nativeDeliveryId, ActivityTransitionKind.Fault));
        }
        attempts.Add(final);

        activityResult ??= Envelope.Result;
        var resultType = new ValueTypeDescriptor(typeof(RuleTerminalResult).FullName!, schemaVersion: RulesNativeBoundary.FormatVersion);
        ValueEnvelope completionResult;
        if (externalCompletionResult)
        {
            var reference = new DurableValueExternalReference("native-test", "completion-result", EmptyMetadata);
            ExternalPayloads.Payloads[reference.Locator] = JsonSerializer.SerializeToElement(activityResult, WebJson);
            completionResult = ValueEnvelope.External(resultType, reference,
                new(DurableValueLifecycle.Instance, DurableValueStorage.External));
        }
        else
        {
            completionResult = ValueEnvelope.Inline(resultType, JsonSerializer.SerializeToElement(activityResult, WebJson),
                ValueProtectionPolicy.InstanceInline);
        }

        var activityCompletion = new ActivityCompletion(ActivityId, final.AttemptId, completionResult,
            activityResult.Kind == RuleTerminalKind.Refused ? "Refused" : "Done", completionAt ?? finalEnded.AddMilliseconds(-1),
            "native-run-rules-contract");
        var activity = CreateWaitingActivity() with
        {
            Status = ActivityExecutionStatus.Completed,
            CompletedAt = finalEnded,
            BookmarkIds = [],
            TriggerRegistrations = [],
            TriggerDeliveries = [receipt],
            Attempts = attempts,
            PrivateState = retirePrivateState ? null : CreatePrivateState(),
            Completion = activityCompletion
        };
        Activities.State = activity;
        Bookmarks.State = null;
    }

    internal void SetFaulted(bool retirePrivateState, bool retireReceiptPayload, bool wrongMarker = false,
        bool wrongCode = false)
    {
        var resultFacts = Facts("seed");
        var result = new RuleTerminalResult("rules-terminal-fault", RuleTerminalKind.Faulted, 0, 1,
            resultFacts, RuleRecordCodec.FactsHash(resultFacts), new("rules.native-fault"));
        Envelope = MakeEnvelope(result);
        var nativeDeliveryId = "native-delivery-fault";
        var finalStarted = Now.AddSeconds(3);
        var finalEnded = Now.AddSeconds(4);
        var final = new ActivityAttempt("resume-attempt-fault", ActivityId, 2, ActivityAttemptReason.Resume,
            finalStarted, finalEnded, nativeDeliveryId, ActivityTransitionKind.Fault);
        var receipt = NewReceipt(ActivityTriggerDeliveryStatus.Consumed, retireReceiptPayload, nativeDeliveryId);
        var completion = new RunRulesCompletion(RulesNativeBoundary.FormatVersion, Address, Envelope.DeliveryId,
            TerminalElement(result), InvalidCode: null);
        var marker = RulesNativeBoundary.TerminalMarker(completion, result);
        Activities.State = CreateWaitingActivity() with
        {
            Status = ActivityExecutionStatus.Faulted,
            CompletedAt = finalEnded,
            BookmarkIds = [],
            TriggerRegistrations = [],
            TriggerDeliveries = [receipt],
            Attempts = [InitialAttempt(), final],
            PrivateState = retirePrivateState ? null : CreatePrivateState(),
            Completion = null,
            Fault = new(wrongCode ? "other-fault" : "rules.native-fault", typeof(ActivityFault).FullName!,
                "Native Rules terminal fault.", null, false,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [ActivityFault.FaultTypeMetadataKey] = wrongMarker ? "rules.terminal.v1:" + new string('f', 64) : marker
                })
        };
        Bookmarks.State = null;
    }

    internal void SetChildCancelled()
    {
        Activities.State = CreateWaitingActivity() with
        {
            Status = ActivityExecutionStatus.Cancelled,
            CompletedAt = Now.AddSeconds(4),
            BookmarkIds = [],
            TriggerRegistrations = [],
            PrivateState = null,
            TriggerDeliveries = [NewReceipt(ActivityTriggerDeliveryStatus.Consumed, retired: true)],
            Attempts = [InitialAttempt(), new ActivityAttempt("cancel-attempt", ActivityId, 2,
                ActivityAttemptReason.Resume, Now.AddSeconds(3), Now.AddSeconds(4), "native-delivery-1", ActivityTransitionKind.Cancel)]
        };
        Bookmarks.State = null;
    }

    internal void SetParentTerminal(WorkflowExecutionStatus status)
    {
        Workflows.State = Workflows.State! with { Status = status, CompletedAt = Now.AddSeconds(10) };
    }

    private WorkflowExecutionState NewParent() => new(WorkflowId,
        new(ExecutableId, "native-definition", "native-version", "1.0.0", ExecutableHash),
        WorkflowExecutionStatus.Running, null, Now, Now, Now, null, null, null, TenantId, EmptyMetadata)
    {
        Partition = new WorkflowExecutionPartition(Partition)
    };

    private ActivityExecutionState CreateWaitingActivity() => new(
        new(ActivityId, WorkflowId, NodeId, "authored-rules", RunRules.ActivityType, "1.0.0"),
        ActivityExecutionStatus.Suspended, null, 1, Now, Now, null, null, "parent-activity", null, null,
        ActivitySchedulingProvenance.Empty, 1, [BookmarkId], [], 0, 0,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RulesNativeBoundary.AddressMetadataKey] = RulesNativeBoundary.Write(Address).GetRawText()
        },
        PrivateState: CreatePrivateState(),
        TriggerRegistrations:
        [
            new ActivityTriggerRegistration(RegistrationId, ActivityId, Request.ResumeTargetId,
                RulesNativeBoundary.CompletionType, Address.StimulusType, Address.StimulusHash,
                ActivityTriggerDeduplicationPolicy.Once)
        ],
        Attempts: [InitialAttempt()],
        ContractIdentity: new ActivityInvocationContractIdentity(RunRules.ActivityType, "1.0.0", "native-run-rules-contract"));

    private ActivityPrivateState CreatePrivateState() => new(ActivityId, 1,
        ValueEnvelope.Inline(new(typeof(RunRulesState).FullName!, schemaVersion: RulesNativeBoundary.FormatVersion),
            JsonSerializer.SerializeToElement(new RunRulesState(RulesNativeBoundary.FormatVersion, RulesNativeBoundary.Write(Request))),
            ValueProtectionPolicy.InstanceInline),
        "initial-attempt", Now);

    private static ActivityAttempt InitialAttempt() => new("initial-attempt", ActivityId, 1,
        ActivityAttemptReason.Initial, Now, Now.AddSeconds(1), transitionKind: ActivityTransitionKind.Suspend);

    private ActivityTriggerDelivery NewReceipt(ActivityTriggerDeliveryStatus status, bool retired,
        string deliveryId = "native-delivery-1")
    {
        var completion = new RunRulesCompletion(RulesNativeBoundary.FormatVersion, Address, Envelope.DeliveryId,
            TerminalElement(Envelope.Result), InvalidCode: null);
        var payload = retired
            ? ValueEnvelope.Absent(RulesNativeBoundary.CompletionType, ValueProtectionPolicy.InstanceInline)
            : ValueEnvelope.Inline(RulesNativeBoundary.CompletionType, JsonSerializer.SerializeToElement(completion),
                ValueProtectionPolicy.InstanceInline);
        return new(deliveryId, Address.RegistrationId, RulesNativeBoundary.CompletionType, payload,
            RulesNativeBoundary.ProviderId, Now.AddSeconds(2),
            RulesNativeBoundary.DeliveryDeduplicationKey(Address, Envelope.DeliveryId), status);
    }

    private BookmarkState NewBookmark() => new(BookmarkId, WorkflowId, ActivityId, NodeId,
        Request.ResumeTargetId, Address.StimulusType, Address.StimulusHash, null, EmptyMetadata, Now, null);

    private RuleTerminalEnvelope MakeEnvelope(RuleTerminalResult result)
    {
        var bytes = RuleRecordCodec.DeliveryBytes("rules-delivery-1", Address, result);
        return new("rules-delivery-1", Address, result, RuleRecordCodec.Hash(bytes));
    }

    private static JsonElement TerminalElement(RuleTerminalResult result)
    {
        using var document = JsonDocument.Parse(RuleRecordCodec.TerminalBytes(result));
        return document.RootElement.Clone();
    }

    internal sealed class TestWorkflowStore(WorkflowExecutionState? state) : IWorkflowExecutionStateStore
    {
        internal WorkflowExecutionState? State { get; set; } = state;
        public ValueTask<WorkflowExecutionState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(State?.WorkflowExecutionId == workflowExecutionId ? State : null);
        }
        public ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowExecutionStatePage> QueryPageAsync(WorkflowExecutionStatePageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> DeleteAsync(string workflowExecutionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    internal sealed class TestActivityStore(ActivityExecutionState? state) : IActivityExecutionStateStore
    {
        internal ActivityExecutionState? State { get; set; } = state;
        public ValueTask<ActivityExecutionState?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(State?.Execution.WorkflowExecutionId == workflowExecutionId && State.InvocationId == activityExecutionId ? State : null);
        }
        public ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListPageAsync(ActivityExecutionStatePageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListByParentPageAsync(ActivityExecutionStateParentPageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    internal sealed class TestBookmarkStore(BookmarkState? state) : IBookmarkStateStore
    {
        internal BookmarkState? State { get; set; } = state;
        public ValueTask<BookmarkState?> FindAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(State?.WorkflowExecutionId == workflowExecutionId && State.BookmarkId == bookmarkId ? State : null);
        }
        public ValueTask<BookmarkState> SaveAsync(BookmarkState value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> DeleteAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeStorePage<BookmarkState>> ListPageAsync(BookmarkStatePageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    internal sealed class TestExternalPayloadStore : IExternalPayloadStore
    {
        internal Dictionary<string, JsonElement> Payloads { get; } = new(StringComparer.Ordinal);
        internal Exception? Failure { get; set; }
        internal int ReadCalls { get; private set; }
        public ValueTask<DurableValueExternalReference> WriteAsync(ExternalPayloadWriteRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<JsonElement> ReadAsync(DurableValueExternalReference reference, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCalls++;
            if (Failure is { } failure)
                return ValueTask.FromException<JsonElement>(failure);
            return Payloads.TryGetValue(reference.Locator, out var payload)
                ? ValueTask.FromResult(payload.Clone())
                : ValueTask.FromException<JsonElement>(new KeyNotFoundException(reference.Locator));
        }
    }

    private sealed class TestAccess : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new(Partition));
    }
}
