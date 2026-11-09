using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Internal.Policies;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence;
using Elsa.Activities.Sequence.Models;
using Elsa.Activities.Testing;
using Elsa.Events;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Models;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore;
using Elsa.Rules.Persistence.EntityFrameworkCore.Entities;
using Elsa.Rules.Runtime;
using Elsa.Rules.Workflows.Design;
using Elsa.Rules.Workflows.Runtime;
using Elsa.Rules.Workflows.Runtime.Activities;
using Elsa.Rules.Workflows.Runtime.Models;
using Elsa.Rules.Workflows.Runtime.Services;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Publishing;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Resumption;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;
using ActivityTransitionKind = Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind;

namespace Elsa.Rules.Tests;

/// <summary>
/// Exercises one persisted native Rules continuation inside real Sequence/Flowchart execution. The executable is
/// assembled by the test and passed through the registered Rules metadata enricher; this is runtime evidence, not
/// the full workflow compiler/publication endpoint or package-provenance acceptance.
/// </summary>
public sealed class RulesPersistedNativeFlowchartTests : IAsyncLifetime
{
    private const string RulesNodeId = "rules";
    private const string HoldNodeId = "hold";
    private const string FlowchartNodeId = "flowchart";
    private const string SequenceNodeId = "sequence";
    private const string ForkNodeId = "fork";
    private const string JoinNodeId = "join";
    private const string FinalNodeId = "final";
    private const string EventName = "rules-native-release";
    private const string SourceReferenceId = "rules-native-persisted-source";
    private const string ProgramJson = "{\"formatVersion\":1,\"languageId\":\"elsa.rules.finite-positive\",\"languageVersion\":1,\"vocabulary\":[\"derived\",\"seed\"],\"rules\":[{\"id\":\"derive\",\"premises\":[\"seed\"],\"conclusion\":\"derived\"}]}";

    private readonly string _workflowExecutionId = $"rules-native-{Guid.NewGuid():N}";
    private readonly FinalCounter _finalCounter = new();
    private RulesPersistedNativeFixture _fixture = null!;
    private WorkflowExecutable _executable = null!;
    private RuleSemanticPin _implementation = null!;

    public async Task InitializeAsync()
    {
        // This semantic identity is intentionally synthetic. It lets the real finite evaluator execute the
        // retained artifact, but it is not evidence that the loaded assemblies came from a package host.
        _implementation = RuleTestData.Invocation().ExpectedImplementation;
        var root = BuildGraph();
        root = await EnrichThroughRegisteredEventPipelineAsync(root, _implementation);

        var hasher = new WorkflowExecutableHasher();
        var artifactHash = hasher.ComputeHash(root);
        var identity = new WorkflowExecutableIdentity(
            hasher.CreateArtifactId("rules-native-", artifactHash),
            "rules-native-definition",
            "rules-native-version",
            "1.0.0",
            artifactHash);
        _executable = new WorkflowExecutable(
            identity,
            root,
            ResumeTargets(),
            WorkflowExecutionHarness.Timestamp,
            new Dictionary<string, string>(StringComparer.Ordinal),
            IncidentStrategyBuiltIns.FaultReference);

        _fixture = await CreateFixtureAsync();
    }

    public async Task DisposeAsync()
    {
        if (_fixture is not null)
            await _fixture.DisposeAsync();
    }

    [Fact]
    public async Task Rules_terminal_and_native_completion_do_not_release_parallel_join_until_sibling_resumes()
    {
        var reference = await _fixture.PublishAsync(SourceReferenceId);

        var start = await _fixture.StartPublishedAsync(reference);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, start.CommandDispatch.Status);

        // The Start intent is committed with the native suspension. Sweep delivery is a separate generation.
        await _fixture.SweepPartitionAsync();

        var initialActivities = await _fixture.ActivityStatesAsync();
        var rulesActivity = RulesPersistedNativeFixture.StateFor(initialActivities, RulesNodeId);
        var holdActivity = RulesPersistedNativeFixture.StateFor(initialActivities, HoldNodeId);
        Assert.Equal(ActivityExecutionStatus.Suspended, rulesActivity.Status);
        Assert.Equal(ActivityExecutionStatus.Suspended, holdActivity.Status);
        Assert.Equal(0, _finalCounter.Count);

        var request = RulesNativeBoundary.ReadState(rulesActivity);
        var identity = new RuleRunIdentity(request.Scope, request.RunId);
        Assert.Equal(RulesPersistedNativeFixture.Partition.Value, identity.Scope.TenantId);
        Assert.Equal(RulesPersistedNativeFixture.Partition.Value, identity.Scope.PersistencePartition);

        // Real store admission precedes model work; each direct RuleExecution step is one durable claim.
        var modelTerminal = await _fixture.DriveRulesToTerminalAsync(identity);
        Assert.Equal(RuleTerminalReadDisposition.Found, modelTerminal.Disposition);
        Assert.Equal(RuleTerminalKind.Succeeded, modelTerminal.Result!.Kind);
        Assert.Equal(new[] { "derived", "seed" }, modelTerminal.Result.ResultFacts
            .Select(fact => fact.Atom).Order(StringComparer.Ordinal));
        var beforeNativeDelivery = await _fixture.ActivityStatesAsync();
        Assert.Equal(ActivityExecutionStatus.Suspended, RulesPersistedNativeFixture.StateFor(beforeNativeDelivery, RulesNodeId).Status);
        Assert.Equal(ActivityExecutionStatus.Suspended, RulesPersistedNativeFixture.StateFor(beforeNativeDelivery, HoldNodeId).Status);
        Assert.Equal(WorkflowExecutionStatus.Running, (await _fixture.WorkflowStateAsync())!.Status);
        AssertNoCompletedJoinOrFinal(beforeNativeDelivery);
        Assert.Equal(0, _finalCounter.Count);

        // The real delivery executor dispatches through the persisted bookmark and independently proves consumption.
        var acknowledged = await _fixture.DeliverOneAsync("native-test-delivery");
        Assert.True(acknowledged, "Native delivery was not acknowledged. Persisted activities: " +
            string.Join(", ", (await _fixture.ActivityStatesAsync()).Select(state =>
                $"{state.Execution.ExecutableNodeId}={state.Status}, result schema={state.Completion?.Result.Type.SchemaVersion?.ToString() ?? "none"}")));

        var afterRules = await _fixture.ActivityStatesAsync();
        var completedRules = RulesPersistedNativeFixture.StateFor(afterRules, RulesNodeId);
        var stillWaiting = RulesPersistedNativeFixture.StateFor(afterRules, HoldNodeId);
        Assert.Equal(ActivityExecutionStatus.Completed, completedRules.Status);
        Assert.Equal(ActivityExecutionStatus.Suspended, stillWaiting.Status);
        Assert.Contains(completedRules.TriggerDeliveries!, item => item.Status == ActivityTriggerDeliveryStatus.Consumed);
        Assert.Equal(WorkflowExecutionStatus.Running, (await _fixture.WorkflowStateAsync())!.Status);
        AssertNoCompletedJoinOrFinal(afterRules);
        Assert.Equal(0, _finalCounter.Count);

        Assert.Equal(RuleDeliveryClaimDisposition.NoEligibleDelivery,
            await _fixture.ClaimDeliveryDispositionAsync("native-test-after-ack"));

        var holdBookmarkId = Assert.Single(stillWaiting.BookmarkIds);
        await using (var scope = await _fixture.CreatePartitionScopeAsync())
        {
            var bookmark = await scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>()
                .FindAsync(_workflowExecutionId, holdBookmarkId);
            Assert.NotNull(bookmark);
            Assert.Equal(HoldNodeId, bookmark.ExecutableNodeId);

            var resumed = await scope.ServiceProvider.GetRequiredService<IBookmarkResumeDispatcher>().DispatchAsync(
                new BookmarkResumeDispatchRequest(
                    _workflowExecutionId,
                    bookmark.StimulusType,
                    bookmark.StimulusHash,
                    JsonSerializer.SerializeToElement("release"),
                    idempotencyKey: "rules-native-release",
                    requestedBy: "rules-persisted-native-test", metadata: null, payloadType: Assert.Single(stillWaiting.TriggerRegistrations!).PayloadType, providerId: "rules-persisted-native-test",
                    bookmarkId: bookmark.BookmarkId, testRunDelivery: null));
            Assert.Equal(BookmarkResumeDispatchStatus.Dispatched, resumed.Status);
        }

        var finalActivities = await _fixture.ActivityStatesAsync();
        var completedHold = RulesPersistedNativeFixture.StateFor(finalActivities, HoldNodeId);
        Assert.Equal(ActivityExecutionStatus.Completed, completedHold.Status);
        Assert.Equal(ActivityExecutionStatus.Completed, RulesPersistedNativeFixture.StateFor(finalActivities, JoinNodeId).Status);
        Assert.Equal(ActivityExecutionStatus.Completed, RulesPersistedNativeFixture.StateFor(finalActivities, FinalNodeId).Status);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await _fixture.WorkflowStateAsync())!.Status);
        Assert.Equal(1, _finalCounter.Count);
    }

    [Fact]
    public async Task Store_revalidates_real_consumed_completion_before_acknowledging_delivery()
    {
        var reference = await _fixture.PublishAsync(SourceReferenceId);
        var start = await _fixture.StartPublishedAsync(reference);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, start.CommandDispatch.Status);
        await _fixture.SweepPartitionAsync();

        var rulesActivity = RulesPersistedNativeFixture.StateFor(await _fixture.ActivityStatesAsync(), RulesNodeId);
        var request = RulesNativeBoundary.ReadState(rulesActivity);
        var identity = new RuleRunIdentity(request.Scope, request.RunId);
        Assert.Equal(RuleTerminalReadDisposition.Found,
            (await _fixture.DriveRulesToTerminalAsync(identity)).Disposition);

        await using var ruleScope = _fixture.Harness.Services.CreateAsyncScope();
        var runs = ruleScope.ServiceProvider.GetRequiredService<IRuleRunStore>();
        var reader = ruleScope.ServiceProvider.GetRequiredService<IRuleRunReader>();
        var claim = await runs.ClaimDeliveryAsync(new("persisted-proof-before-ack", [RulesNativeBoundary.FormatVersion]));
        Assert.Equal(RuleDeliveryClaimDisposition.Claimed, claim.Disposition);
        var lease = Assert.IsType<RuleDeliveryLease>(claim.Lease);
        var delivery = await reader.ReadDeliveryAsync(lease);
        Assert.Equal(RuleDeliveryReadDisposition.Ready, delivery.Disposition);
        var envelope = Assert.IsType<RuleTerminalEnvelope>(delivery.Envelope);
        var run = await reader.FindAsync(identity);
        Assert.Equal(RuleRunReadDisposition.Found, run.Disposition);
        var fingerprint = Assert.IsType<RuleRunView>(run.View).Fingerprint;

        using var terminalDocument = JsonDocument.Parse(RuleRecordCodec.TerminalBytes(envelope.Result));
        var completion = new RunRulesCompletion(RulesNativeBoundary.FormatVersion, envelope.Destination,
            envelope.DeliveryId, terminalDocument.RootElement.Clone(), InvalidCode: null);
        await using (var dispatchScope = await _fixture.CreatePartitionScopeAsync())
        {
            var dispatched = await dispatchScope.ServiceProvider.GetRequiredService<IBookmarkResumeDispatcher>()
                .DispatchAsync(new BookmarkResumeDispatchRequest(
                    envelope.Destination.WorkflowExecutionId,
                    envelope.Destination.StimulusType,
                    envelope.Destination.StimulusHash,
                    JsonSerializer.SerializeToElement(completion),
                    envelope.DeliveryId,
                    requestedBy: "rules-persisted-proof-test",
                    metadata: null,
                    payloadType: RulesNativeBoundary.CompletionType,
                    providerId: RulesNativeBoundary.ProviderId,
                    bookmarkId: envelope.Destination.BookmarkId,
                    testRunDelivery: null));
            Assert.Equal(BookmarkResumeDispatchStatus.Dispatched, dispatched.Status);
        }

        var originalActivity = RulesPersistedNativeFixture.StateFor(await _fixture.ActivityStatesAsync(), RulesNodeId);
        Assert.Equal(ActivityExecutionStatus.Completed, originalActivity.Status);
        var waiting = RulesPersistedNativeFixture.StateFor(await _fixture.ActivityStatesAsync(), HoldNodeId);
        Assert.Equal(ActivityExecutionStatus.Suspended, waiting.Status);
        Assert.Equal(WorkflowExecutionStatus.Running, (await _fixture.WorkflowStateAsync())!.Status);

        RuleDeliveryProof proof;
        await using (var proofScope = await _fixture.CreatePartitionScopeAsync())
        {
            var inspection = await proofScope.ServiceProvider.GetRequiredService<RulesNativeDeliveryInspector>()
                .InspectAsync(identity, fingerprint, envelope);
            proof = Assert.IsType<RuleDeliveryProof>(inspection.Proof);
            Assert.Equal(RuleDeliveryProofKind.AlreadyConsumed, proof.Kind);
        }

        var originalCompletion = Assert.IsType<ActivityCompletion>(originalActivity.Completion);
        var alteredFacts = new[] { new RuleFact("seed") };
        var alteredResult = new RuleTerminalResult(envelope.Result.TerminalId, envelope.Result.Kind,
            envelope.Result.CommittedRounds, envelope.Result.ResultSchemaVersion, alteredFacts,
            RuleRecordCodec.FactsHash(alteredFacts), envelope.Result.Diagnostic);
        Assert.NotEqual(RuleRecordCodec.TerminalBytes(envelope.Result), RuleRecordCodec.TerminalBytes(alteredResult));
        var webJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var alteredValue = ValueEnvelope.Inline(originalCompletion.Result.Type,
            JsonSerializer.SerializeToElement(alteredResult, webJson), originalCompletion.Result.Policy);
        var tamperedActivity = originalActivity with
        {
            Completion = new ActivityCompletion(originalCompletion.InvocationId, originalCompletion.AttemptId,
                alteredValue, originalCompletion.OutcomeKey, originalCompletion.CompletedAt,
                originalCompletion.ContractFingerprint)
        };
        await SaveActivityStateAsync(tamperedActivity);

        var rejected = await runs.AcknowledgeDeliveryAsync(lease, new(proof));
        Assert.Equal(RuleTransitionDisposition.Invalid, rejected.Disposition);
        Assert.Equal(RuleDeliveryReadDisposition.Ready, (await reader.ReadDeliveryAsync(lease)).Disposition);
        var persistedTamper = RulesPersistedNativeFixture.StateFor(await _fixture.ActivityStatesAsync(), RulesNodeId);
        var persistedAlteredResult = ReadNativeResult(persistedTamper);
        Assert.Equal(RuleRecordCodec.TerminalBytes(alteredResult), RuleRecordCodec.TerminalBytes(persistedAlteredResult));
        Assert.Equal(WorkflowExecutionStatus.Running, (await _fixture.WorkflowStateAsync())!.Status);
        AssertNoCompletedJoinOrFinal(await _fixture.ActivityStatesAsync());

        // Restore the exact native completion, then reuse the still-current lease and the proof observed before
        // corruption. A fresh validator read must now permit the same durable obligation to settle once.
        await SaveActivityStateAsync(originalActivity);
        var acknowledged = await runs.AcknowledgeDeliveryAsync(lease, new(proof));
        Assert.Equal(RuleTransitionDisposition.Acknowledged, acknowledged.Disposition);
        Assert.Equal(envelope.DeliveryId, acknowledged.TransitionId);
        Assert.Equal(envelope.Result.TerminalId, acknowledged.TerminalId);
        Assert.Equal(RuleDeliveryClaimDisposition.NoEligibleDelivery,
            await _fixture.ClaimDeliveryDispositionAsync("persisted-proof-after-ack"));

        var settledActivities = await _fixture.ActivityStatesAsync();
        Assert.Equal(ActivityExecutionStatus.Suspended,
            RulesPersistedNativeFixture.StateFor(settledActivities, HoldNodeId).Status);
        Assert.Equal(WorkflowExecutionStatus.Running, (await _fixture.WorkflowStateAsync())!.Status);
        AssertNoCompletedJoinOrFinal(settledActivities);
    }

    [Fact]
    public async Task Consumed_completion_is_reclaimed_and_acknowledged_without_redispatch_while_sibling_waits()
    {
        var unavailableGate = new OneShotProofUnavailableGate();
        var proofProbe = new ProofValidationProbe();
        var dispatchProbe = new BookmarkResumeDispatchProbe();

        // This case needs a short real database-time lease so the still-unacknowledged terminal result can be
        // reclaimed through the same Rules delivery executor after its first proof validation is unavailable.
        await _fixture.DisposeAsync();
        _fixture = await CreateFixtureAsync(services =>
        {
            services.RemoveAll<IRuleDeliveryProofValidator>();
            services.AddSingleton(unavailableGate);
            services.AddSingleton(proofProbe);
            services.AddScoped<IRuleDeliveryProofValidator>(provider => new OneShotUnavailableProofValidator(
                new RulesNativeDeliveryProofValidator(provider.GetRequiredService<IPersistenceOperationScopeFactory>()),
                provider.GetRequiredService<OneShotProofUnavailableGate>(),
                provider.GetRequiredService<ProofValidationProbe>()));

            services.AddSingleton(dispatchProbe);
            services.AddScoped<BookmarkResumeDispatcher>();
            services.RemoveAll<IBookmarkResumeDispatcher>();
            services.AddScoped<IBookmarkResumeDispatcher>(provider => new CountingBookmarkResumeDispatcher(
                provider.GetRequiredService<BookmarkResumeDispatcher>(),
                provider.GetRequiredService<BookmarkResumeDispatchProbe>()));
        }, deliveryLeaseSeconds: 10);

        var reference = await _fixture.PublishAsync($"{SourceReferenceId}-redelivery");
        var start = await _fixture.StartPublishedAsync(reference);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, start.CommandDispatch.Status);
        await _fixture.SweepPartitionAsync();

        var pendingActivities = await _fixture.ActivityStatesAsync();
        var pendingRules = RulesPersistedNativeFixture.StateFor(pendingActivities, RulesNodeId);
        var pendingSibling = RulesPersistedNativeFixture.StateFor(pendingActivities, HoldNodeId);
        Assert.Equal(ActivityExecutionStatus.Suspended, pendingRules.Status);
        Assert.Equal(ActivityExecutionStatus.Suspended, pendingSibling.Status);
        Assert.Equal(WorkflowExecutionStatus.Running, (await _fixture.WorkflowStateAsync())!.Status);
        AssertNoCompletedJoinOrFinal(pendingActivities);

        var request = RulesNativeBoundary.ReadState(pendingRules);
        var identity = new RuleRunIdentity(request.Scope, request.RunId);
        var terminal = await _fixture.DriveRulesToTerminalAsync(identity);
        Assert.Equal(RuleTerminalReadDisposition.Found, terminal.Disposition);
        Assert.Equal(RuleTerminalKind.Succeeded, terminal.Result!.Kind);
        var canonicalTerminal = RuleRecordCodec.TerminalBytes(terminal.Result);
        var initialRun = await _fixture.FindRunAsync(identity);
        Assert.Equal(RuleRunReadDisposition.Found, initialRun.Disposition);
        var modelRevision = initialRun.View!.ModelRevision;

        unavailableGate.Arm();
        Assert.False(await _fixture.DeliverOneAsync("persisted-terminal-redelivery"));
        Assert.Equal(1, dispatchProbe.DispatchCount);
        var firstProofValidation = Assert.Single(proofProbe.Observations);
        Assert.Equal(RuleDeliveryProofKind.Consumed, firstProofValidation.Kind);
        Assert.Equal(RuleDeliveryProofValidationDisposition.Unavailable, firstProofValidation.Disposition);

        var afterUnavailableAck = await _fixture.ActivityStatesAsync();
        var completedRules = RulesPersistedNativeFixture.StateFor(afterUnavailableAck, RulesNodeId);
        var waitingSibling = RulesPersistedNativeFixture.StateFor(afterUnavailableAck, HoldNodeId);
        Assert.Equal(ActivityExecutionStatus.Completed, completedRules.Status);
        Assert.Equal(ActivityExecutionStatus.Suspended, waitingSibling.Status);
        Assert.Equal(WorkflowExecutionStatus.Running, (await _fixture.WorkflowStateAsync())!.Status);
        AssertNoCompletedJoinOrFinal(afterUnavailableAck);
        Assert.Equal(0, _finalCounter.Count);

        var consumedReceipt = Assert.Single(completedRules.TriggerDeliveries!,
            item => item.Status == ActivityTriggerDeliveryStatus.Consumed);
        var nativeResult = ReadNativeResult(completedRules);
        Assert.Equal(canonicalTerminal, RuleRecordCodec.TerminalBytes(nativeResult));
        var resumeAttempts = ResumeAttempts(completedRules);
        var attemptSnapshot = resumeAttempts.Select(attempt =>
            (attempt.AttemptId, attempt.Ordinal, attempt.Reason, attempt.StartedAt, attempt.EndedAt,
                attempt.TriggerDeliveryId, attempt.TransitionKind)).ToArray();
        Assert.Single(attemptSnapshot);
        Assert.Equal(ActivityTransitionKind.Complete, resumeAttempts[0].TransitionKind);
        Assert.Equal(consumedReceipt.DeliveryId, resumeAttempts[0].TriggerDeliveryId);

        var rowBeforeReclaim = await ReadRunRecordAsync(_fixture, identity);
        Assert.False(rowBeforeReclaim.DeliveryAcknowledged);
        Assert.Null(rowBeforeReclaim.DeliveryAcknowledgementJson);
        Assert.NotNull(rowBeforeReclaim.DeliveryOwner);
        Assert.NotNull(rowBeforeReclaim.DeliveryExpiresAtUtcTicks);

        var runBeforeReclaim = await _fixture.FindRunAsync(identity);
        Assert.Equal(modelRevision, runBeforeReclaim.View!.ModelRevision);
        Assert.Equal(canonicalTerminal, RuleRecordCodec.TerminalBytes(
            (await _fixture.ReadTerminalAsync(identity)).Result!));

        await WaitForDeliveryLeaseExpiryAsync(_fixture, identity);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
        var acknowledged = false;
        while (!acknowledged && DateTimeOffset.UtcNow < deadline)
        {
            acknowledged = await _fixture.DeliverOneAsync("persisted-terminal-redelivery");
            if (!acknowledged)
                await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        Assert.True(acknowledged, "The unacknowledged native completion was not reclaimed after its database-time lease expired.");
        Assert.Equal(1, dispatchProbe.DispatchCount);
        Assert.Equal(2, proofProbe.Observations.Count);
        Assert.Equal(RuleDeliveryProofKind.AlreadyConsumed, proofProbe.Observations[1].Kind);
        Assert.Equal(RuleDeliveryProofValidationDisposition.Equivalent, proofProbe.Observations[1].Disposition);

        var afterReclaim = await _fixture.ActivityStatesAsync();
        var unchangedRules = RulesPersistedNativeFixture.StateFor(afterReclaim, RulesNodeId);
        Assert.Equal(ActivityExecutionStatus.Completed, unchangedRules.Status);
        Assert.Equal(canonicalTerminal, RuleRecordCodec.TerminalBytes(ReadNativeResult(unchangedRules)));
        Assert.Equal(attemptSnapshot, ResumeAttempts(unchangedRules).Select(attempt =>
            (attempt.AttemptId, attempt.Ordinal, attempt.Reason, attempt.StartedAt, attempt.EndedAt,
                attempt.TriggerDeliveryId, attempt.TransitionKind)).ToArray());
        var sameReceipt = Assert.Single(unchangedRules.TriggerDeliveries!,
            item => item.Status == ActivityTriggerDeliveryStatus.Consumed);
        Assert.Equal(consumedReceipt.DeliveryId, sameReceipt.DeliveryId);
        Assert.Equal(consumedReceipt.RegistrationId, sameReceipt.RegistrationId);
        Assert.Equal(consumedReceipt.DeduplicationKey, sameReceipt.DeduplicationKey);
        Assert.Equal(ActivityExecutionStatus.Suspended,
            RulesPersistedNativeFixture.StateFor(afterReclaim, HoldNodeId).Status);
        Assert.Equal(WorkflowExecutionStatus.Running, (await _fixture.WorkflowStateAsync())!.Status);
        AssertNoCompletedJoinOrFinal(afterReclaim);
        Assert.Equal(0, _finalCounter.Count);

        var settledRun = await _fixture.FindRunAsync(identity);
        Assert.Equal(modelRevision, settledRun.View!.ModelRevision);
        Assert.Equal(canonicalTerminal, RuleRecordCodec.TerminalBytes(
            (await _fixture.ReadTerminalAsync(identity)).Result!));
        var rowAfterAck = await ReadRunRecordAsync(_fixture, identity);
        Assert.True(rowAfterAck.DeliveryAcknowledged);
        Assert.NotNull(rowAfterAck.DeliveryAcknowledgementJson);
        Assert.Null(rowAfterAck.DeliveryOwner);
        Assert.Null(rowAfterAck.DeliveryExpiresAtUtcTicks);
        Assert.Equal(RuleDeliveryClaimDisposition.NoEligibleDelivery,
            await _fixture.ClaimDeliveryDispositionAsync("persisted-terminal-redelivery-after-ack"));
    }

    private async Task<RulesPersistedNativeFixture> CreateFixtureAsync(
        Action<IServiceCollection>? configureServices = null,
        int deliveryLeaseSeconds = 60) =>
        await RulesPersistedNativeFixture.CreateAsync(
            _executable,
            _workflowExecutionId,
            services =>
            {
                services.AddSingleton(_finalCounter);
                configureServices?.Invoke(services);
            },
            _implementation,
            deliveryLeaseSeconds);

    private static RuleTerminalResult ReadNativeResult(ActivityExecutionState activity)
    {
        Assert.Equal(new ValueTypeDescriptor(typeof(RuleTerminalResult).FullName!), activity.Completion!.Result.Type);
        Assert.True(activity.Completion.Result.InlineValue.HasValue,
            "The compiled CLR result contract declares the bounded RuleTerminalResult inline.");
        return JsonSerializer.Deserialize<RuleTerminalResult>(
                   activity.Completion.Result.InlineValue!.Value,
                   new JsonSerializerOptions(JsonSerializerDefaults.Web))
               ?? throw new InvalidDataException("The persisted RuleTerminalResult completion could not be read.");
    }

    private static ActivityAttempt[] ResumeAttempts(ActivityExecutionState activity) =>
        activity.Attempts!.Where(attempt => attempt.Reason == ActivityAttemptReason.Resume)
            .OrderBy(attempt => attempt.Ordinal).ToArray();

    private static async Task<RuleRunRecord> ReadRunRecordAsync(
        RulesPersistedNativeFixture fixture,
        RuleRunIdentity identity)
    {
        await using var scope = fixture.Harness.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RulesDbContext>();
        var runKey = RuleRecordCodec.Hash(RuleRecordCodec.ScopeBytes(identity));
        return await db.Runs.AsNoTracking().SingleAsync(row => row.RunKey == runKey);
    }

    private static async Task WaitForDeliveryLeaseExpiryAsync(
        RulesPersistedNativeFixture fixture,
        RuleRunIdentity identity)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var row = await ReadRunRecordAsync(fixture, identity);
            if (row.DeliveryExpiresAtUtcTicks is { } expiresAt)
            {
                await using var scope = fixture.Harness.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<RulesDbContext>();
                if (await db.ReadUtcTicksAsync(CancellationToken.None) >
                    expiresAt + TimeSpan.FromMilliseconds(100).Ticks)
                    return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }

        throw new TimeoutException("The persisted Rules delivery lease did not expire within the bounded test interval.");
    }

    private sealed class OneShotProofUnavailableGate
    {
        private int _armed;

        public void Arm() => Interlocked.Exchange(ref _armed, 1);

        public bool TryConsume() => Interlocked.Exchange(ref _armed, 0) == 1;
    }

    private sealed class ProofValidationProbe
    {
        private readonly object _sync = new();
        private readonly List<(RuleDeliveryProofKind Kind, RuleDeliveryProofValidationDisposition Disposition)> _observations = [];

        public IReadOnlyList<(RuleDeliveryProofKind Kind, RuleDeliveryProofValidationDisposition Disposition)> Observations
        {
            get
            {
                lock (_sync)
                    return _observations.ToArray();
            }
        }

        public void Record(RuleDeliveryProof proof, RuleDeliveryProofValidationDisposition disposition)
        {
            lock (_sync)
                _observations.Add((proof.Kind, disposition));
        }
    }

    private sealed class OneShotUnavailableProofValidator(
        IRuleDeliveryProofValidator inner,
        OneShotProofUnavailableGate gate,
        ProofValidationProbe probe) : IRuleDeliveryProofValidator
    {
        public async ValueTask<RuleDeliveryProofValidationResult> ValidateAsync(
            RuleInvocationEnvelope invocation,
            RuleTerminalEnvelope envelope,
            RuleDeliveryProof proof,
            CancellationToken cancellationToken = default)
        {
            if (gate.TryConsume())
            {
                probe.Record(proof, RuleDeliveryProofValidationDisposition.Unavailable);
                return new(RuleDeliveryProofValidationDisposition.Unavailable);
            }

            var result = await inner.ValidateAsync(invocation, envelope, proof, cancellationToken);
            probe.Record(proof, result.Disposition);
            return result;
        }
    }

    private sealed class BookmarkResumeDispatchProbe
    {
        private int _dispatchCount;
        public int DispatchCount => Volatile.Read(ref _dispatchCount);
        public void RecordDispatch() => Interlocked.Increment(ref _dispatchCount);
    }

    private sealed class CountingBookmarkResumeDispatcher(
        BookmarkResumeDispatcher inner,
        BookmarkResumeDispatchProbe probe) : IBookmarkResumeDispatcher
    {
        public ValueTask<BookmarkResumeDispatchResult> DispatchAsync(
            BookmarkResumeDispatchRequest request,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
            CancellationToken cancellationToken = default)
        {
            probe.RecordDispatch();
            return inner.DispatchAsync(request, dispatchOptions, cancellationToken);
        }
    }

    private ExecutableNode BuildGraph()
    {
        var rulesContract = ClrActivityContractTestBuilder.BuildContract(typeof(RunRules));
        var rulesBindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal)
        {
            [nameof(RunRules.Program)] = Literal(rulesContract.Inputs[nameof(RunRules.Program)], ProgramJson),
            [nameof(RunRules.InitialFacts)] = Literal(rulesContract.Inputs[nameof(RunRules.InitialFacts)], new[] { "seed" }),
            [nameof(RunRules.MaxRounds)] = Literal(rulesContract.Inputs[nameof(RunRules.MaxRounds)], 4)
        };
        var rules = Node(RulesNodeId, typeof(RunRules), rulesContract, rulesBindings);

        var holdContract = ClrActivityContractTestBuilder.BuildContract(typeof(Event));
        var holdBindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal)
        {
            [nameof(Event.EventName)] = Literal(holdContract.Inputs[nameof(Event.EventName)], EventName),
            [nameof(Event.CanStartWorkflow)] = Literal(holdContract.Inputs[nameof(Event.CanStartWorkflow)], false),
            [nameof(Event.CorrelationId)] = Literal(holdContract.Inputs[nameof(Event.CorrelationId)], "")
        };
        var hold = Node(HoldNodeId, typeof(Event), holdContract, holdBindings);
        var fork = Node<JoinPassActivity>(ForkNodeId);
        var join = Node<JoinPassActivity>(JoinNodeId);
        var flowchartContract = ClrActivityContractTestBuilder.BuildContract(typeof(FlowchartActivity));
        var flowchart = new ExecutableNode(
            FlowchartNodeId, $"authored-{FlowchartNodeId}", typeof(FlowchartActivity).FullName!, "1.0.0",
            WellKnownRuntimeActivityConsumers.ClrActivity, flowchartContract.DescriptorPayload,
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>(StringComparer.Ordinal),
            [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, [fork, rules, hold, join])],
            new ExecutableActivityStructure(
                FlowchartActivity.StructureKind,
                FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new FlowchartStructure(
                    [Connection(ForkNodeId, RulesNodeId), Connection(ForkNodeId, HoldNodeId),
                     Connection(RulesNodeId, JoinNodeId), Connection(HoldNodeId, JoinNodeId)],
                    ForkNodeId,
                    new Dictionary<string, FlowchartNodeMetadata>(StringComparer.Ordinal)
                    {
                        [ForkNodeId] = new(FlowchartPolicyKinds.ParallelFork)
                    }))),
            activityContract: flowchartContract);

        var final = Node<FinalCounterActivity>(FinalNodeId);
        var sequenceContract = ClrActivityContractTestBuilder.BuildContract(typeof(SequenceActivity));
        return new ExecutableNode(
            SequenceNodeId, $"authored-{SequenceNodeId}", typeof(SequenceActivity).FullName!, "1.0.0",
            WellKnownRuntimeActivityConsumers.ClrActivity, sequenceContract.DescriptorPayload,
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>(StringComparer.Ordinal),
            [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, [flowchart, final])],
            new ExecutableActivityStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new SequenceExecutableStructure([FlowchartNodeId, FinalNodeId]))),
            activityContract: sequenceContract);
    }

    private static ExecutableNode Node<TActivity>(string nodeId) where TActivity : IActivity
    {
        var contract = ClrActivityContractTestBuilder.BuildContract(typeof(TActivity));
        return Node(nodeId, typeof(TActivity), contract, new Dictionary<string, RuntimeInputBinding>());
    }

    private static ExecutableNode Node(string nodeId, Type activityType, ActivityContract contract,
        IReadOnlyDictionary<string, RuntimeInputBinding> bindings) =>
        new(nodeId, $"authored-{nodeId}", activityType.FullName!, "1.0.0",
            WellKnownRuntimeActivityConsumers.ClrActivity, contract.DescriptorPayload, bindings,
            new Dictionary<string, string>(StringComparer.Ordinal), activityContract: contract);

    private static RuntimeInputBinding Literal(ActivityInputContract input, object value) =>
        new(input.Key, input.Type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(input.Type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));

    private static IReadOnlyDictionary<string, WorkflowExecutableResumeTarget> ResumeTargets()
    {
        var rulesTarget = new WorkflowExecutableResumeTarget(
            WorkflowExecutableResumeTarget.ComposeScopedId(RulesNodeId, RulesNativeBoundary.ResumeTargetKey),
            RulesNodeId, "ResumeAsync", new Dictionary<string, string>(StringComparer.Ordinal), RulesNativeBoundary.ResumeTargetKey);
        var holdTarget = new WorkflowExecutableResumeTarget(
            WorkflowExecutableResumeTarget.ComposeScopedId(HoldNodeId, Event.ResumeTargetId),
            HoldNodeId, "ResumeAsync", new Dictionary<string, string>(StringComparer.Ordinal), Event.ResumeTargetId);
        return new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal)
        {
            [rulesTarget.ResumeTargetId] = rulesTarget,
            [holdTarget.ResumeTargetId] = holdTarget
        };
    }

    private static async Task<ExecutableNode> EnrichThroughRegisteredEventPipelineAsync(
        ExecutableNode root,
        RuleSemanticPin implementation)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRuleImplementationIdentitySource>(new TestImplementationIdentitySource(implementation));
        new EventsFeature().ConfigureServices(services);
        new WorkflowsPublishingFeature().ConfigureServices(services);
        new RulesRuntimeFeature().ConfigureServices(services);
        new RulesWorkflowsDesignFeature().ConfigureServices(services);

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var enricher = scope.ServiceProvider.GetRequiredService<IExecutableNodeMetadataEnricher>();
        var request = new WorkflowExecutableCompileRequest(
            "rules-native-version", WorkflowExecutableReferenceScope.Published,
            WorkflowExecutionHarness.Timestamp, WorkflowExecutionHarness.Timestamp, null, "rules-native-");
        var source = new WorkflowExecutableCompileSource(
            "rules-native-definition", "rules-native-version", "1.0.0", WorkflowDefinitionState.Empty,
            "WorkflowDefinitionVersion", "rules-native-version", "1.0.0");
        var enrichment = await enricher.EnrichCompilationAsync(request, source, root);
        Assert.True(enrichment.RootActivity.DescendantsAndSelf()
            .Single(node => node.ExecutableNodeId == RulesNodeId).Metadata.ContainsKey(RunRules.PinMetadataKey));
        return enrichment.RootActivity;
    }

    private static FlowchartConnection Connection(string source, string target) =>
        new(new FlowchartEndpoint(source), new FlowchartEndpoint(target));

    private async Task SaveActivityStateAsync(ActivityExecutionState state)
    {
        await using var scope = await _fixture.CreatePartitionScopeAsync();
        await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>().SaveAsync(state);
    }

    private static void AssertNoCompletedJoinOrFinal(IReadOnlyCollection<ActivityExecutionState> states)
    {
        Assert.DoesNotContain(states, item => item.Execution.ExecutableNodeId == JoinNodeId && item.Status == ActivityExecutionStatus.Completed);
        Assert.DoesNotContain(states, item => item.Execution.ExecutableNodeId == FinalNodeId && item.Status == ActivityExecutionStatus.Completed);
    }

    private sealed class TestImplementationIdentitySource(RuleSemanticPin implementation) : IRuleImplementationIdentitySource
    {
        public ValueTask<RuleSemanticPin?> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<RuleSemanticPin?>(implementation);
        }
    }

    public sealed class FinalCounter
    {
        private int _count;
        internal int Count => Volatile.Read(ref _count);
        internal void Increment() => Interlocked.Increment(ref _count);
    }

    public sealed class JoinPassActivity : Activity<ActivityUnit>
    {
        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }

    public sealed class FinalCounterActivity(FinalCounter counter) : Activity<ActivityUnit>
    {
        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
        {
            counter.Increment();
            return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
        }
    }
}
