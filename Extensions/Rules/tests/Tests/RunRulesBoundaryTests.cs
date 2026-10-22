using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Runtime;
using Elsa.Rules.Workflows.Runtime.Activities;
using Elsa.Rules.Workflows.Runtime.Models;
using Elsa.Rules.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using static Elsa.Rules.Tests.RuleTestData;
using ActivityTransitionKind = Elsa.Activities.Runtime.Core.Models.ActivityTransitionKind;

namespace Elsa.Rules.Tests;

public sealed class RunRulesBoundaryTests
{
    [Fact]
    public async Task Execute_freezes_published_pin_and_resolves_omitted_round_budget()
    {
        var fixture = new Fixture(Witness(), ["seed"]);
        var suspension = await fixture.ExecuteAsync();
        var request = RulesNativeBoundary.ReadState(suspension.StatePayload.Deserialize<RunRulesState>()!);

        Assert.Equal(7, request.MaxRounds);
        Assert.Equal(fixture.Pin.Artifact.Sha256, request.Program.Artifact.Sha256);
        Assert.Equal(RuleRecordCodec.PinHash(fixture.Pin.Implementation), RuleRecordCodec.PinHash(request.Program.Implementation));
        Assert.Equal(fixture.Source, fixture.Activity.Program);
    }

    [Fact]
    public async Task Execute_keeps_an_outside_vocabulary_fact_on_a_positive_derived_budget()
    {
        var fixture = new Fixture(Program(["seed"], []), ["seed", "outside"]);
        var suspension = await fixture.ExecuteAsync();
        var request = RulesNativeBoundary.ReadState(suspension.StatePayload.Deserialize<RunRulesState>()!);

        Assert.Equal(1, request.MaxRounds);
        Assert.Equal(["outside", "seed"], request.InitialFacts.Select(fact => fact.Atom).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Execute_suspends_for_invalid_explicit_budget_and_resume_faults_without_a_rules_row()
    {
        var fixture = new Fixture(Witness(), ["seed"], maxRounds: 0);
        var suspension = await fixture.ExecuteAsync();
        var requestState = suspension.StatePayload.Deserialize<RunRulesState>()!;
        var request = RulesNativeBoundary.ReadState(requestState);
        Assert.Equal(0, request.MaxRounds);

        var address = fixture.Address(request);
        var rulesDeliveryId = RulesNativeBoundary.InvalidDeliveryId(request, address);
        var nativeDeliveryId = "native-command-invalid-budget";
        var completion = new RunRulesCompletion(1, address, rulesDeliveryId, null, RulesNativeBoundary.InvalidRoundLimit);
        fixture.SetResumeState(suspension, address, completion, nativeDeliveryId);

        var transition = await fixture.ResumeAsync(suspension, completion, nativeDeliveryId);
        var fault = Assert.IsAssignableFrom<IActivityFaultTransition>(transition);
        Assert.Equal(ActivityTransitionKind.Fault, transition.Kind);
        Assert.Equal(RulesNativeBoundary.InvalidRoundLimit, fault.Fault.Code);
        Assert.Equal(1, fixture.FindCalls);
        Assert.Equal(RuleRunReadDisposition.NotFound, fixture.LastRunReadDisposition);
    }

    [Fact]
    public async Task Resume_roundtrips_through_type_erased_stateful_interface_and_compares_canonical_result_facts()
    {
        var fixture = new Fixture(Witness(), ["seed"]);
        var suspension = await fixture.ExecuteAsync();
        var request = RulesNativeBoundary.ReadState(suspension.StatePayload.Deserialize<RunRulesState>()!);
        var address = fixture.Address(request);
        var expected = Terminal(["final", "joined", "left", "right", "seed"]);
        var carried = Terminal(["seed", "right", "left", "joined", "final"]);
        var rulesDeliveryId = "rules-delivery-1";
        var nativeDeliveryId = "native-command-1";
        var completion = new RunRulesCompletion(1, address, rulesDeliveryId, TerminalElement(carried), null);
        fixture.SetRunResult(request, address, expected);
        fixture.SetResumeState(suspension, address, completion, nativeDeliveryId);

        var transition = await fixture.ResumeAsync(suspension, completion, nativeDeliveryId);

        var result = Assert.IsAssignableFrom<IActivityCompletionTransition>(transition).Result;
        Assert.Equal(ActivityTransitionKind.Complete, transition.Kind);
        var terminal = Assert.IsType<RuleTerminalResult>(result);
        Assert.Equal(RuleTerminalKind.Succeeded, terminal.Kind);
        Assert.Equal(["final", "joined", "left", "right", "seed"],
            terminal.ResultFacts.Select(fact => fact.Atom));
    }

    [Fact]
    public async Task Resume_rejects_altered_result_facts_even_when_their_digest_is_recomputed()
    {
        var fixture = new Fixture(Witness(), ["seed"]);
        var suspension = await fixture.ExecuteAsync();
        var request = RulesNativeBoundary.ReadState(suspension.StatePayload.Deserialize<RunRulesState>()!);
        var address = fixture.Address(request);
        var expected = Terminal(["final", "joined", "left", "right", "seed"]);
        var altered = Terminal(["final", "joined", "left", "right", "tampered"]);
        var rulesDeliveryId = "rules-delivery-2";
        var nativeDeliveryId = "native-command-2";
        var completion = new RunRulesCompletion(1, address, rulesDeliveryId, TerminalElement(altered), null);
        fixture.SetRunResult(request, address, expected);
        fixture.SetResumeState(suspension, address, completion, nativeDeliveryId);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await fixture.ResumeAsync(suspension, completion, nativeDeliveryId));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Resume_rejects_wrong_frozen_destination_or_consumed_delivery_receipt(
        bool wrongDestination, bool wrongReceipt)
    {
        var fixture = new Fixture(Witness(), ["seed"]);
        var suspension = await fixture.ExecuteAsync();
        var request = RulesNativeBoundary.ReadState(suspension.StatePayload.Deserialize<RunRulesState>()!);
        var address = fixture.Address(request);
        var terminal = Terminal(["final", "joined", "left", "right", "seed"]);
        var rulesDeliveryId = "rules-delivery-3";
        var nativeDeliveryId = "native-command-3";
        var completionAddress = wrongDestination
            ? new RuleNativeResultAddress(address.FormatVersion, address.WorkflowExecutionId, address.ActivityExecutionId,
                address.ExecutableId, address.ExecutableHash, address.NodeId, "other-bookmark", address.ResumeTargetId,
                address.RegistrationId, address.StimulusType, address.StimulusHash, address.ResultContractId,
                address.ResultSchemaVersion, address.DeduplicationPolicy, address.ProviderId)
            : address;
        var completion = new RunRulesCompletion(1, completionAddress, rulesDeliveryId,
            TerminalElement(terminal), null);
        fixture.SetRunResult(request, address, terminal);
        fixture.SetResumeState(suspension, address, completion, nativeDeliveryId,
            receiptRegistrationId: wrongReceipt ? "other-registration" : Fixture.RegistrationId);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await fixture.ResumeAsync(suspension, completion, nativeDeliveryId));
    }

    private static RuleTerminalResult Terminal(string[] facts)
    {
        var values = Facts(facts);
        return new("terminal-1", RuleTerminalKind.Succeeded, 4, 1, values,
            RuleRecordCodec.FactsHash(values), null);
    }

    private static JsonElement TerminalElement(RuleTerminalResult terminal)
    {
        using var document = JsonDocument.Parse(RuleRecordCodec.TerminalBytes(terminal));
        return document.RootElement.Clone();
    }

    private sealed class Fixture
    {
        private const string WorkflowId = "workflow-1";
        private const string ActivityId = "activity-1";
        private const string AttemptId = "attempt-1";
        private const string NodeId = "rules-node";
        private const string Partition = "rules-partition";
        private const string BookmarkId = "bookmark-1";
        private const string ExecutableId = "executable-1";
        private const string ExecutableHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        internal const string RegistrationId = "registration-1";
        private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
        private readonly ActivityStateStore _activityStates;
        private readonly RulesViewReader _reader;
        private readonly IStatefulActivity<RuleTerminalResult, RunRulesState, RunRulesCompletion> _typedActivity;
        private readonly WorkflowExecutableIdentity _executableIdentity;
        private readonly WorkflowExecutable _executable;

        public Fixture(RuleProgram program, string[] initialFacts, int? maxRounds = null)
        {
            var programJson = ProgramJson(program);
            var implementation = Invocation().ExpectedImplementation;
            Pin = new(1, RuleWorkflowProgramPin.SourceHash(programJson), RulesCodec.CreateArtifact(program),
                implementation, program.Vocabulary.Count);
            Source = programJson;
            _executableIdentity = new(ExecutableId, "definition-1", "version-1", "1.0.0", ExecutableHash);

            var type = new ValueTypeDescriptor("String");
            var binding = new RuntimeInputBinding(nameof(RunRules.Program), type,
                ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(Source), ValueProtectionPolicy.InstanceInline));
            var targetId = WorkflowExecutableResumeTarget.ComposeScopedId(NodeId, "rules-completed");
            var node = new ExecutableNode(NodeId, "authored-rules", RunRules.ActivityType, "1.0.0",
                "test.clr-activity", JsonSerializer.SerializeToElement(new { type = RunRules.ActivityType }),
                new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal) { [nameof(RunRules.Program)] = binding },
                new Dictionary<string, string>(StringComparer.Ordinal) { [RunRules.PinMetadataKey] = Pin.Serialize() });
            var target = new WorkflowExecutableResumeTarget(targetId, NodeId, "ResumeAsync",
                new Dictionary<string, string>(StringComparer.Ordinal), "rules-completed");
            _executable = new(_executableIdentity, node,
                new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal) { [targetId] = target },
                Now, new Dictionary<string, string>(StringComparer.Ordinal),
                new IncidentStrategyReference("test", "1"));

            var workflow = new WorkflowExecutionState(WorkflowId, _executableIdentity, WorkflowExecutionStatus.Running,
                null, Now, Now, Now, null, null, null, null, new Dictionary<string, string>(StringComparer.Ordinal))
            {
                Partition = new WorkflowExecutionPartition(Partition)
            };
            var workflowStore = new WorkflowStateStore(workflow);
            var executableStore = new ExecutableStore(_executable);
            _activityStates = new(NewActivityState());
            _reader = new();
            var access = new AccessContext(PersistenceAccessContext.Scoped(new PersistenceScope(Partition)));
            _typedActivity = CreateActivity(workflowStore, executableStore, access);
            Activity = (RunRules)_typedActivity;
            Activity.Program = Source;
            Activity.InitialFacts = initialFacts;
            Activity.MaxRounds = maxRounds;
        }

        public RuleWorkflowProgramPin Pin { get; }
        public string Source { get; }
        public RunRules Activity { get; }
        public int FindCalls => _reader.FindCalls;
        public RuleRunReadDisposition LastRunReadDisposition => _reader.LastRunRead.Disposition;

        public async Task<IStatefulActivitySuspensionTransition> ExecuteAsync()
        {
            var transition = await _typedActivity.ExecuteAsync(new(WorkflowId, ActivityId, AttemptId, NodeId,
                CancellationToken.None));
            Assert.Equal(ActivityTransitionKind.Suspend, transition.Kind);
            return Assert.IsAssignableFrom<IStatefulActivitySuspensionTransition>(transition);
        }

        public RuleNativeResultAddress Address(RunRulesRequest request) => new(1, WorkflowId, ActivityId,
            ExecutableId, ExecutableHash, NodeId, BookmarkId, request.ResumeTargetId, RegistrationId,
            RulesNativeBoundary.StimulusType, RulesNativeBoundary.StimulusHash(request),
            RulesNativeBoundary.CompletionType.Alias, 1, "once", RulesNativeBoundary.ProviderId);

        public void SetRunResult(RunRulesRequest request, RuleNativeResultAddress address, RuleTerminalResult terminal)
        {
            var invocation = RulesNativeBoundary.Invocation(request, address);
            var fingerprint = RuleRecordCodec.Fingerprint(invocation);
            _reader.RunRead = new(RuleRunReadDisposition.Found,
                new(invocation.Identity, fingerprint, invocation.Artifact.ArtifactId, invocation.Artifact.Sha256,
                    RuleRecordCodec.PinHash(invocation.ExpectedImplementation), RuleRunStatus.Succeeded,
                    terminal.CommittedRounds + 1L, terminal.CommittedRounds, terminal.ResultFacts.Count,
                    null, terminal.TerminalId, terminal.Kind));
            _reader.TerminalRead = new(RuleTerminalReadDisposition.Found, terminal);
        }

        public void SetResumeState(IStatefulActivitySuspensionTransition suspension, RuleNativeResultAddress storedAddress,
            RunRulesCompletion completion, string nativeDeliveryId, string? receiptRegistrationId = null)
        {
            var stateType = suspension.StateValueType;
            var stateValue = ValueEnvelope.Inline(stateType, suspension.StatePayload, ValueProtectionPolicy.InstanceInline);
            var privateState = new ActivityPrivateState(ActivityId, 1, stateValue, AttemptId, Now);
            var triggerType = RulesNativeBoundary.CompletionType;
            var triggerPayload = JsonSerializer.SerializeToElement(completion);
            var persistedPayload = ValueEnvelope.Inline(triggerType, triggerPayload, ValueProtectionPolicy.InstanceInline);
            var receipt = new ActivityTriggerDelivery(nativeDeliveryId, receiptRegistrationId ?? RegistrationId,
                triggerType, persistedPayload, RulesNativeBoundary.ProviderId, Now,
                RulesNativeBoundary.DeliveryDeduplicationKey(storedAddress, completion.DeliveryId),
                ActivityTriggerDeliveryStatus.Consumed);
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RulesNativeBoundary.AddressMetadataKey] = JsonSerializer.Serialize(storedAddress)
            };
            _activityStates.State = NewActivityState(metadata, privateState, null, [receipt], [storedAddress.BookmarkId]);
        }

        public async Task<ActivityTransition> ResumeAsync(IStatefulActivitySuspensionTransition suspension,
            RunRulesCompletion completion, string deliveryId)
        {
            var attempt = new ActivityExecutionContext(WorkflowId, ActivityId, "attempt-resume", NodeId,
                CancellationToken.None);
            var triggerPayload = JsonSerializer.SerializeToElement(completion);
            var request = new ActivityResumeRequest(attempt, suspension.StateValueType, suspension.StatePayload,
                RulesNativeBoundary.CompletionType, triggerPayload, RegistrationId, deliveryId);
            var freshActivation = CreateActivity(
                new WorkflowStateStore(new WorkflowExecutionState(WorkflowId, _executableIdentity, WorkflowExecutionStatus.Running,
                    null, Now, Now, Now, null, null, null, null, new Dictionary<string, string>(StringComparer.Ordinal))
                {
                    Partition = new WorkflowExecutionPartition(Partition)
                }),
                new ExecutableStore(_executable),
                new AccessContext(PersistenceAccessContext.Scoped(new PersistenceScope(Partition))));
            return await ((IStatefulActivity)freshActivation).ResumeAsync(request);
        }

        private RunRules CreateActivity(WorkflowStateStore workflowStore, ExecutableStore executableStore,
            AccessContext access) => new(workflowStore, executableStore, _activityStates, _reader, access);

        private static ActivityExecutionState NewActivityState(
            IReadOnlyDictionary<string, string>? metadata = null,
            ActivityPrivateState? privateState = null,
            IReadOnlyCollection<ActivityTriggerRegistration>? registrations = null,
            IReadOnlyCollection<ActivityTriggerDelivery>? deliveries = null,
            IReadOnlyCollection<string>? bookmarkIds = null) =>
            new(new(ActivityId, WorkflowId, NodeId, "authored-rules", RunRules.ActivityType, "1.0.0"),
                ActivityExecutionStatus.Waiting, null, Now, Now, null, null, null, null, null, null,
                bookmarkIds ?? [], [], 0, 0, metadata ?? new Dictionary<string, string>(StringComparer.Ordinal))
            {
                PrivateState = privateState,
                TriggerRegistrations = registrations,
                TriggerDeliveries = deliveries
            };

        private static string ProgramJson(RuleProgram program) => JsonSerializer.Serialize(new
        {
            formatVersion = program.FormatVersion,
            languageId = program.LanguageId,
            languageVersion = program.LanguageVersion,
            vocabulary = program.Vocabulary.Select(fact => fact.Atom).ToArray(),
            rules = program.Rules.Select(rule => new
            {
                id = rule.Id,
                premises = rule.Premises.Select(fact => fact.Atom).ToArray(),
                conclusion = rule.Conclusion.Atom
            }).ToArray()
        });

        private sealed class WorkflowStateStore(WorkflowExecutionState state) : IWorkflowExecutionStateStore
        {
            public ValueTask<WorkflowExecutionState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
                ValueTask.FromResult<WorkflowExecutionState?>(workflowExecutionId == state.WorkflowExecutionId ? state : null);

            public ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState value, CancellationToken cancellationToken = default) =>
                ValueTask.FromException<WorkflowExecutionState>(new NotSupportedException());
            public ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default) =>
                ValueTask.FromException<IReadOnlyCollection<WorkflowExecutionState>>(new NotSupportedException());
            public ValueTask<WorkflowExecutionStatePage> QueryPageAsync(WorkflowExecutionStatePageQuery query, CancellationToken cancellationToken = default) =>
                ValueTask.FromException<WorkflowExecutionStatePage>(new NotSupportedException());
            public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default) =>
                ValueTask.FromException<IReadOnlyCollection<string>>(new NotSupportedException());
            public ValueTask<bool> DeleteAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
                ValueTask.FromException<bool>(new NotSupportedException());
        }

        private sealed class ExecutableStore(WorkflowExecutable executable) : IWorkflowExecutableStore
        {
            public ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default) =>
                ValueTask.FromResult<WorkflowExecutable?>(artifactId == executable.Identity.ArtifactId ? executable : null);

            public ValueTask SaveAsync(WorkflowExecutable value, CancellationToken cancellationToken = default) =>
                ValueTask.FromException(new NotSupportedException());
            public ValueTask<bool> DeleteAsync(string artifactId, CancellationToken cancellationToken = default) =>
                ValueTask.FromException<bool>(new NotSupportedException());
            public ValueTask<WorkflowExecutableRootWriteLease?> TryAcquireRootWriteLeaseAsync(string artifactId, string leaseId,
                DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken = default) =>
                ValueTask.FromException<WorkflowExecutableRootWriteLease?>(new NotSupportedException());
            public ValueTask<bool> RenewRootWriteLeaseAsync(WorkflowExecutableRootWriteLease lease, DateTimeOffset expiresAt,
                DateTimeOffset now, CancellationToken cancellationToken = default) =>
                ValueTask.FromException<bool>(new NotSupportedException());
            public ValueTask ReleaseRootWriteLeaseAsync(WorkflowExecutableRootWriteLease lease, CancellationToken cancellationToken = default) =>
                ValueTask.FromException(new NotSupportedException());
            public ValueTask<WorkflowExecutableDeletionGuard?> TryBeginDeletionAsync(string artifactId, string operationId,
                DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken = default) =>
                ValueTask.FromException<WorkflowExecutableDeletionGuard?>(new NotSupportedException());
            public ValueTask<bool> CancelDeletionAsync(WorkflowExecutableDeletionGuard guard, CancellationToken cancellationToken = default) =>
                ValueTask.FromException<bool>(new NotSupportedException());
            public ValueTask<bool> DeleteAsync(WorkflowExecutableDeletionGuard guard, DateTimeOffset now,
                CancellationToken cancellationToken = default) => ValueTask.FromException<bool>(new NotSupportedException());
            public ValueTask<RuntimeStorePage<WorkflowExecutable>> ListPageAsync(RuntimeStorePageRequest request,
                CancellationToken cancellationToken = default) => ValueTask.FromException<RuntimeStorePage<WorkflowExecutable>>(new NotSupportedException());
        }

        private sealed class ActivityStateStore(ActivityExecutionState state) : IActivityExecutionStateStore
        {
            public ActivityExecutionState State { get; set; } = state;

            public ValueTask<ActivityExecutionState?> FindAsync(string workflowExecutionId, string activityExecutionId,
                CancellationToken cancellationToken = default) =>
                ValueTask.FromResult<ActivityExecutionState?>(workflowExecutionId == State.Execution.WorkflowExecutionId &&
                    activityExecutionId == State.InvocationId ? State : null);

            public ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState value, CancellationToken cancellationToken = default) =>
                ValueTask.FromException<ActivityExecutionState>(new NotSupportedException());
            public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListPageAsync(ActivityExecutionStatePageQuery query,
                CancellationToken cancellationToken = default) => ValueTask.FromException<RuntimeStorePage<ActivityExecutionState>>(new NotSupportedException());
            public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListByParentPageAsync(ActivityExecutionStateParentPageQuery query,
                CancellationToken cancellationToken = default) => ValueTask.FromException<RuntimeStorePage<ActivityExecutionState>>(new NotSupportedException());
        }

        private sealed class RulesViewReader : IRuleRunReader
        {
            public RuleRunReadResult LastRunRead { get; private set; } = new(RuleRunReadDisposition.NotFound, null);
            public RuleTerminalReadResult TerminalRead { get; set; } = new(RuleTerminalReadDisposition.NotFound, null);
            public RuleRunReadResult RunRead { get; set; } = new(RuleRunReadDisposition.NotFound, null);
            public int FindCalls { get; private set; }

            public ValueTask<RuleRunReadResult> FindAsync(RuleRunIdentity identity, CancellationToken cancellationToken = default)
            {
                FindCalls++;
                LastRunRead = RunRead;
                return ValueTask.FromResult(RunRead);
            }

            public ValueTask<RuleTerminalReadResult> ReadTerminalAsync(RuleRunIdentity identity, CancellationToken cancellationToken = default) =>
                ValueTask.FromResult(TerminalRead);
            public ValueTask<RuleWorkReadResult> ReadWorkAsync(RuleWorkToken token, CancellationToken cancellationToken = default) =>
                ValueTask.FromException<RuleWorkReadResult>(new NotSupportedException());
            public ValueTask<RuleDeliveryReadResult> ReadDeliveryAsync(RuleDeliveryLease lease, CancellationToken cancellationToken = default) =>
                ValueTask.FromException<RuleDeliveryReadResult>(new NotSupportedException());
            public ValueTask<RuleProgressReadResult> ReadProgressAsync(RuleRunIdentity identity, RuleProgressPosition? after,
                int take, CancellationToken cancellationToken = default) =>
                ValueTask.FromException<RuleProgressReadResult>(new NotSupportedException());
        }

        private sealed class AccessContext(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
        {
            public PersistenceAccessContext Current { get; } = current;
        }
    }
}
