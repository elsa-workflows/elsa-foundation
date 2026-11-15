using System.Text.Json;
using ActivityTransitionKind = Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Workflows.Runtime.Activities;
using Elsa.Rules.Workflows.Runtime.Models;
using Elsa.Rules.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using static Elsa.Rules.Tests.RuleTestData;

namespace Elsa.Rules.Tests;

public sealed class RulesIntentHandlerTests
{
    [Fact]
    public async Task Matching_start_replay_is_settled_by_identity_before_native_wait_lookup()
    {
        var fixture = new Fixture();
        var invocation = RulesNativeBoundary.Invocation(fixture.Request, fixture.Address);
        fixture.Rules.RunRead = new(RuleRunReadDisposition.Found,
            new(invocation.Identity, RuleRecordCodec.Fingerprint(invocation), invocation.Artifact.ArtifactId,
                invocation.Artifact.Sha256, RuleRecordCodec.PinHash(invocation.ExpectedImplementation),
                RuleRunStatus.Running, 1, 0, invocation.InitialFacts.Count, null, null, null));

        await fixture.Handler.HandleAsync(fixture.StartIntent);

        Assert.Equal(1, fixture.Rules.FindCalls);
        Assert.Equal(0, fixture.Rules.AdmitCalls);
        Assert.Equal(0, fixture.Workflows.FindCalls);
        Assert.Equal(0, fixture.Activities.FindCalls);
    }

    [Fact]
    public async Task New_start_requires_the_exact_live_bookmark_then_admits_the_frozen_invocation()
    {
        var fixture = new Fixture();

        await fixture.Handler.HandleAsync(fixture.StartIntent);

        Assert.Equal(1, fixture.Rules.AdmitCalls);
        Assert.Equal(0, fixture.Rules.CancelCalls);
        Assert.Equal(1, fixture.Workflows.FindCalls);
        Assert.Equal(1, fixture.Activities.FindCalls);
    }

    [Fact]
    public async Task Conflicting_start_replay_and_malformed_intent_route_are_permanent()
    {
        var fixture = new Fixture();
        var invocation = RulesNativeBoundary.Invocation(fixture.Request, fixture.Address);
        fixture.Rules.RunRead = new(RuleRunReadDisposition.Found,
            new(invocation.Identity, new(new string('d', 64)), invocation.Artifact.ArtifactId,
                invocation.Artifact.Sha256, RuleRecordCodec.PinHash(invocation.ExpectedImplementation),
                RuleRunStatus.Running, 1, 0, invocation.InitialFacts.Count, null, null, null));

        var conflict = await Assert.ThrowsAsync<RuntimePostCommitDeliveryException>(async () =>
            await fixture.Handler.HandleAsync(fixture.StartIntent));
        Assert.Equal(PostCommitFailureKind.Permanent, conflict.Kind);
        Assert.Equal("rules.intent-fingerprint-conflict", conflict.Code);
        Assert.Equal(0, fixture.Rules.AdmitCalls);

        fixture.Rules.FindCalls = 0;
        var badRoute = new RuntimePostCommitIntent(fixture.StartIntent.IntentId, "workflow-other",
            fixture.StartIntent.Kind, fixture.StartIntent.RecordedAt, fixture.StartIntent.ActivityExecutionId,
            fixture.StartIntent.IdempotencyKey, fixture.StartIntent.Payload);
        var malformed = await Assert.ThrowsAsync<RuntimePostCommitDeliveryException>(async () =>
            await fixture.Handler.HandleAsync(badRoute));
        Assert.Equal(PostCommitFailureKind.Permanent, malformed.Kind);
        Assert.Equal("rules.intent-invalid", malformed.Code);
        Assert.Equal(0, fixture.Rules.FindCalls);
    }

    [Fact]
    public async Task Cancel_intent_writes_a_pre_admission_tombstone_before_native_activity_is_terminal()
    {
        var fixture = new Fixture(activityStatus: ActivityExecutionStatus.Suspended);
        var intent = RulesNativeBoundary.Intent(fixture.Request, fixture.Address, cancel: true, Fixture.Now);

        await fixture.Handler.HandleAsync(intent);

        Assert.Equal(1, fixture.Rules.CancelCalls);
        Assert.Equal(0, fixture.Rules.AdmitCalls);
        Assert.Equal(fixture.Request.RunId, fixture.Rules.LastCancellation!.Invocation.Identity.InvocationId);
        Assert.Equal(fixture.Address, fixture.Rules.LastCancellation.Invocation.NativeDestination);
    }

    [Fact]
    public async Task Unavailable_identity_read_is_transient_and_does_not_inspect_or_mutate_native_state()
    {
        var fixture = new Fixture();
        fixture.Rules.RunRead = new(RuleRunReadDisposition.Unavailable, null);

        var exception = await Assert.ThrowsAsync<RuntimePostCommitDeliveryException>(async () =>
            await fixture.Handler.HandleAsync(fixture.StartIntent));

        Assert.Equal(PostCommitFailureKind.Transient, exception.Kind);
        Assert.Equal("rules.intent-read-unavailable", exception.Code);
        Assert.Equal(0, fixture.Rules.AdmitCalls);
        Assert.Equal(0, fixture.Workflows.FindCalls);
    }

    [Fact]
    public async Task Invalid_round_limit_dispatch_acknowledgement_without_consumption_proof_remains_retryable()
    {
        var fixture = new Fixture(maxRounds: 0);
        fixture.Dispatcher.OnDispatch = (_, _) => ValueTask.FromResult(
            new BookmarkResumeDispatchResult(BookmarkResumeDispatchStatus.Duplicate,
                fixture.Request.WorkflowExecutionId, reason: "duplicate command"));

        var exception = await Assert.ThrowsAsync<RuntimePostCommitDeliveryException>(async () =>
            await fixture.Handler.HandleAsync(fixture.StartIntent));

        Assert.Equal(PostCommitFailureKind.Transient, exception.Kind);
        Assert.Equal("rules.intent-invalid-budget-outcome-unproven", exception.Code);
        var dispatch = Assert.IsType<BookmarkResumeDispatchRequest>(fixture.Dispatcher.LastRequest);
        Assert.Equal(fixture.Address.BookmarkId, dispatch.BookmarkId);
        Assert.Equal(RulesNativeBoundary.InvalidDeliveryId(fixture.Request, fixture.Address), dispatch.IdempotencyKey);
        Assert.Equal(RulesNativeBoundary.CompletionType, dispatch.PayloadType);
        Assert.Equal(RulesNativeBoundary.ProviderId, dispatch.ProviderId);
        var completion = Assert.IsType<RunRulesCompletion>(dispatch.Input!.Value.Deserialize<RunRulesCompletion>());
        Assert.Equal(fixture.Address, completion.Destination);
        Assert.Equal(RulesNativeBoundary.InvalidDeliveryId(fixture.Request, fixture.Address), completion.DeliveryId);
        Assert.Equal(RulesNativeBoundary.InvalidRoundLimit, completion.InvalidCode);
        Assert.Null(completion.Result);
        Assert.Equal(0, fixture.Rules.AdmitCalls);
        Assert.Equal(0, fixture.Rules.CancelCalls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Invalid_round_limit_retires_only_after_exact_consumed_fault_receipt_and_final_resume_attempt(
        bool payloadRetired,
        bool retryBeforeFinal)
    {
        var fixture = new Fixture(maxRounds: RuleLimits.MaxRounds + 1);
        fixture.Dispatcher.OnDispatch = (_, _) =>
        {
            fixture.Activities.State = fixture.InvalidBudgetFault(payloadRetired, retryBeforeFinal);
            return ValueTask.FromResult(new BookmarkResumeDispatchResult(
                BookmarkResumeDispatchStatus.Duplicate, fixture.Request.WorkflowExecutionId, reason: "duplicate command"));
        };

        await fixture.Handler.HandleAsync(fixture.StartIntent);

        Assert.Equal(0, fixture.Rules.AdmitCalls);
        Assert.Equal(0, fixture.Rules.CancelCalls);
        Assert.Equal(1, fixture.Dispatcher.Calls);
        Assert.Equal(2, fixture.Rules.FindCalls);
    }

    [Fact]
    public async Task Invalid_round_limit_does_not_retire_when_resume_attempt_history_overlaps()
    {
        var fixture = new Fixture(maxRounds: RuleLimits.MaxRounds + 1);
        fixture.Dispatcher.OnDispatch = (_, _) =>
        {
            var state = fixture.InvalidBudgetFault(payloadRetired: false, retryBeforeFinal: true);
            fixture.Activities.State = state with
            {
                Attempts =
                [
                    new("resume-attempt-1", "activity-1", 2, ActivityAttemptReason.Resume,
                        Fixture.Now.AddSeconds(1), Fixture.Now.AddSeconds(5), "native-command-1", ActivityTransitionKind.Fault),
                    new("resume-attempt-2", "activity-1", 3, ActivityAttemptReason.Resume,
                        Fixture.Now.AddSeconds(3), Fixture.Now.AddSeconds(4), "native-command-1", ActivityTransitionKind.Fault)
                ]
            };
            return ValueTask.FromResult(new BookmarkResumeDispatchResult(
                BookmarkResumeDispatchStatus.Duplicate, fixture.Request.WorkflowExecutionId, reason: "duplicate command"));
        };

        var exception = await Assert.ThrowsAsync<RuntimePostCommitDeliveryException>(async () =>
            await fixture.Handler.HandleAsync(fixture.StartIntent));

        Assert.Equal(PostCommitFailureKind.Transient, exception.Kind);
        Assert.Equal("rules.intent-invalid-budget-outcome-unproven", exception.Code);
        Assert.Equal(0, fixture.Rules.AdmitCalls);
    }

    private sealed class Fixture
    {
        internal static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-22T10:00:00Z");
        private const string WorkflowId = "workflow-1";
        private const string ActivityId = "activity-1";
        private const string ExecutableId = "executable-1";
        private const string ExecutableHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string NodeId = "rules-node";
        private const string BookmarkId = "bookmark-actual-id";
        private const string RegistrationId = "registration-actual-id";
        private const string Partition = "partition-1";
        private const string TenantId = "tenant-1";
        private static readonly IReadOnlyDictionary<string, string> EmptyMetadata = new Dictionary<string, string>(StringComparer.Ordinal);

        internal Fixture(int maxRounds = 4, ActivityExecutionStatus activityStatus = ActivityExecutionStatus.Suspended,
            bool includeBookmark = true)
        {
            var sample = Invocation();
            var program = new RuleWorkflowProgramPin(1, new string('c', 64), sample.Artifact,
                sample.ExpectedImplementation, vocabularyCount: 7);
            var scope = new RuleScope(TenantId, Partition);
            Request = new(scope, RulesNativeBoundary.RunId(scope, WorkflowId, ActivityId), program,
                Facts("seed"), maxRounds, WorkflowId, ActivityId, ExecutableId, ExecutableHash,
                NodeId, "resume-actual-id");
            Address = new(1, WorkflowId, ActivityId, ExecutableId, ExecutableHash, NodeId,
                BookmarkId, Request.ResumeTargetId, RegistrationId, RulesNativeBoundary.StimulusType,
                RulesNativeBoundary.StimulusHash(Request), RulesNativeBoundary.CompletionType.Alias,
                RulesNativeBoundary.FormatVersion, "once", RulesNativeBoundary.ProviderId);
            StartIntent = RulesNativeBoundary.Intent(Request, Address, cancel: false, Now);
            Parent = new WorkflowExecutionState(WorkflowId,
                new(ExecutableId, "definition-1", "version-1", "1.0.0", ExecutableHash),
                WorkflowExecutionStatus.Running, null, Now, Now, Now, null, null, null, TenantId, EmptyMetadata)
            {
                Partition = new WorkflowExecutionPartition(Partition)
            };
            Activity = CreateActivityState(activityStatus, includeBookmark);
            Workflows = new(Parent);
            Activities = new(Activity);
            Bookmarks = new(includeBookmark
                ? new BookmarkState(BookmarkId, WorkflowId, ActivityId, NodeId, Request.ResumeTargetId,
                    Address.StimulusType, Address.StimulusHash, null, EmptyMetadata, Now, null)
                : null);
            Rules = new();
            Dispatcher = new();
            Handler = new(Rules, Rules, Workflows, Activities, Bookmarks,
                new Access(PersistenceAccessContext.Scoped(new PersistenceScope(Partition))), Dispatcher);
        }

        internal RunRulesRequest Request { get; }
        internal RuleNativeResultAddress Address { get; }
        internal RuntimePostCommitIntent StartIntent { get; }
        internal WorkflowExecutionState Parent { get; }
        internal ActivityExecutionState Activity { get; }
        internal RulesStore Rules { get; }
        internal WorkflowStore Workflows { get; }
        internal ActivityStore Activities { get; }
        internal BookmarkStore Bookmarks { get; }
        internal ResumeDispatcher Dispatcher { get; }
        internal RulesIntentHandler Handler { get; }

        internal ActivityExecutionState InvalidBudgetFault(bool payloadRetired, bool retryBeforeFinal)
        {
            const string nativeDeliveryId = "native-command-1";
            var completion = new RunRulesCompletion(1, Address,
                RulesNativeBoundary.InvalidDeliveryId(Request, Address), null,
                RulesNativeBoundary.InvalidRoundLimit);
            var payload = payloadRetired
                ? ValueEnvelope.Absent(RulesNativeBoundary.CompletionType, ValueProtectionPolicy.InstanceInline)
                : ValueEnvelope.Inline(RulesNativeBoundary.CompletionType,
                    JsonSerializer.SerializeToElement(completion), ValueProtectionPolicy.InstanceInline);
            var receipt = new ActivityTriggerDelivery(nativeDeliveryId, Address.RegistrationId,
                RulesNativeBoundary.CompletionType, payload, RulesNativeBoundary.ProviderId, Now,
                RulesNativeBoundary.DeliveryDeduplicationKey(Address, completion.DeliveryId),
                ActivityTriggerDeliveryStatus.Consumed);
            ActivityAttempt[] attempts = retryBeforeFinal
                ?
                [
                    new("resume-attempt-1", ActivityId, 2, ActivityAttemptReason.Resume,
                        Now.AddSeconds(1), Now.AddSeconds(2), nativeDeliveryId, ActivityTransitionKind.Fault),
                    new("resume-attempt-2", ActivityId, 3, ActivityAttemptReason.Resume,
                        Now.AddSeconds(3), Now.AddSeconds(4), nativeDeliveryId, ActivityTransitionKind.Fault)
                ]
                :
                [
                    new("resume-attempt-1", ActivityId, 2, ActivityAttemptReason.Resume,
                        Now.AddSeconds(1), Now.AddSeconds(2), nativeDeliveryId, ActivityTransitionKind.Fault)
                ];

            return Activities.State! with
            {
                Status = ActivityExecutionStatus.Faulted,
                BookmarkIds = [],
                TriggerRegistrations = [],
                TriggerDeliveries = [receipt],
                Attempts = attempts,
                Fault = new(RulesNativeBoundary.InvalidRoundLimit, "Elsa.Rules.Tests.InvalidLimit",
                    "Invalid round limit.", null, isRetryable: false)
            };
        }

        private ActivityExecutionState CreateActivityState(ActivityExecutionStatus status, bool includeBookmark)
        {
            var state = new RunRulesState(RulesNativeBoundary.FormatVersion, RulesNativeBoundary.Write(Request));
            var privateState = new ActivityPrivateState(ActivityId, 1,
                ValueEnvelope.Inline(new(typeof(RunRulesState).FullName!, schemaVersion: 1),
                    JsonSerializer.SerializeToElement(state), ValueProtectionPolicy.InstanceInline),
                "initial-attempt-1", Now);
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RulesNativeBoundary.AddressMetadataKey] = RulesNativeBoundary.Write(Address).GetRawText()
            };
            string[] bookmarkIds = includeBookmark ? [BookmarkId] : [];
            ActivityTriggerRegistration[] registrations = includeBookmark
                ? [new ActivityTriggerRegistration(RegistrationId, ActivityId, Request.ResumeTargetId,
                    RulesNativeBoundary.CompletionType, Address.StimulusType, Address.StimulusHash,
                    ActivityTriggerDeduplicationPolicy.Once)]
                : [];

            return new(new(ActivityId, WorkflowId, NodeId, "authored-rules", RunRules.ActivityType, "1.0.0"),
                status, null, 1, Now, Now, null, null, "parent-activity", null, null,
                ActivitySchedulingProvenance.Empty, 1, bookmarkIds, [], 0, 0, metadata,
                PrivateState: privateState, TriggerRegistrations: registrations);
        }
    }

    private sealed class RulesStore : IRuleRunStore, IRuleRunReader
    {
        internal RuleRunReadResult RunRead { get; set; } = new(RuleRunReadDisposition.NotFound, null);
        internal int FindCalls { get; set; }
        internal int AdmitCalls { get; private set; }
        internal int CancelCalls { get; private set; }
        internal RuleCancellationRequest? LastCancellation { get; private set; }

        public ValueTask<RuleRunReadResult> FindAsync(RuleRunIdentity identity, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FindCalls++;
            return ValueTask.FromResult(RunRead);
        }

        public ValueTask<RuleAdmissionConfirmation> AdmitAsync(RuleRunAdmission admission, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AdmitCalls++;
            return ValueTask.FromResult(new RuleAdmissionConfirmation(RuleAdmissionDisposition.Admitted,
                admission.Invocation.Identity, 1, null));
        }

        public ValueTask<RuleCancellationConfirmation> CancelAsync(RuleCancellationRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CancelCalls++;
            LastCancellation = request;
            return ValueTask.FromResult(new RuleCancellationConfirmation(RuleCancellationDisposition.Cancelled,
                request.Invocation.Identity, 1, "cancel-terminal"));
        }

        public ValueTask<RuleRefusalConfirmation> RefuseAdmissionAsync(RuleAdmissionRefusal refusal, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuleWorkClaimResult> ClaimWorkAsync(RuleClaimRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuleLeaseConfirmation> RenewAsync(RuleWorkLease lease, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuleLeaseConfirmation> ReleaseAsync(RuleWorkLease lease, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuleReceiptConfirmation> AcceptRoundAsync(RuleWorkToken token, RuleRoundSubmission submission, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuleTransitionConfirmation> ConsumeRoundAsync(RuleWorkToken token, RuleReceiptIdentity receipt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuleDeliveryClaimResult> ClaimDeliveryAsync(RuleDeliveryClaimRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuleTransitionConfirmation> AcknowledgeDeliveryAsync(RuleDeliveryLease lease, RuleDeliveryAcknowledgement acknowledgement, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuleWorkReadResult> ReadWorkAsync(RuleWorkToken token, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuleDeliveryReadResult> ReadDeliveryAsync(RuleDeliveryLease lease, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuleTerminalReadResult> ReadTerminalAsync(RuleRunIdentity identity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuleProgressReadResult> ReadProgressAsync(RuleRunIdentity identity, RuleProgressPosition? after, int take, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class WorkflowStore(WorkflowExecutionState? state) : IWorkflowExecutionStateStore
    {
        internal int FindCalls { get; private set; }
        public ValueTask<WorkflowExecutionState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FindCalls++;
            return ValueTask.FromResult(state);
        }
        public ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowExecutionStatePage> QueryPageAsync(WorkflowExecutionStatePageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> DeleteAsync(string workflowExecutionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ActivityStore(ActivityExecutionState? state) : IActivityExecutionStateStore
    {
        internal ActivityExecutionState? State { get; set; } = state;
        internal int FindCalls { get; private set; }
        public ValueTask<ActivityExecutionState?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FindCalls++;
            return ValueTask.FromResult(State);
        }
        public ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListPageAsync(ActivityExecutionStatePageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListByParentPageAsync(ActivityExecutionStateParentPageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class BookmarkStore(BookmarkState? state) : IBookmarkStateStore
    {
        public ValueTask<BookmarkState?> FindAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(state);
        }
        public ValueTask<BookmarkState> SaveAsync(BookmarkState value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> DeleteAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeStorePage<BookmarkState>> ListPageAsync(BookmarkStatePageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Access(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current => current;
    }

    private sealed class ResumeDispatcher : IBookmarkResumeDispatcher
    {
        internal Func<BookmarkResumeDispatchRequest, CancellationToken, ValueTask<BookmarkResumeDispatchResult>> OnDispatch { get; set; } =
            (request, _) => ValueTask.FromResult(new BookmarkResumeDispatchResult(
                BookmarkResumeDispatchStatus.Duplicate, request.WorkflowExecutionId, reason: "duplicate command"));
        internal BookmarkResumeDispatchRequest? LastRequest { get; private set; }
        internal int Calls { get; private set; }

        public ValueTask<BookmarkResumeDispatchResult> DispatchAsync(BookmarkResumeDispatchRequest request,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            LastRequest = request;
            return OnDispatch(request, cancellationToken);
        }
    }
}
