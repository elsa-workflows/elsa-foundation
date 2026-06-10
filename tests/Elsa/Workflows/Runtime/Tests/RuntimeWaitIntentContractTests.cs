using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeWaitIntentContractTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WaitRegistration_RecordsCorrelationBeforeDependentIntentDelivery()
    {
        var wait = NewWaitRegistration(
            status: RuntimeWaitRegistrationStatus.Reserved,
            dependsOnPostCommitIntentId: "intent-1",
            earlySignalPolicy: RuntimeEarlySignalPolicy.BufferUntilIntentDelivered);

        Assert.Equal("wait-1", wait.WaitRegistrationId);
        Assert.Equal("wfexec-1", wait.WorkflowExecutionId);
        Assert.Equal("actexec-1", wait.ActivityExecutionId);
        Assert.Equal("bookmark-1", wait.BookmarkId);
        Assert.Equal("order:123:reply", wait.CorrelationId);
        Assert.Equal("message-reply", wait.StimulusType);
        Assert.Equal("intent-1", wait.DependsOnPostCommitIntentId);
        Assert.Equal(RuntimeWaitRegistrationStatus.Reserved, wait.Status);
        Assert.True(wait.IsMatchable);
        Assert.False(wait.IsTerminal);
    }

    [Fact]
    public void ReservedWait_CanMatchEarlySignalByCorrelationWithoutGlobalInbox()
    {
        var wait = NewWaitRegistration(
            status: RuntimeWaitRegistrationStatus.Reserved,
            dependsOnPostCommitIntentId: "intent-1",
            earlySignalPolicy: RuntimeEarlySignalPolicy.SatisfyReservedWait);

        Assert.True(wait.IsMatchable);
        Assert.Equal("order:123:reply", wait.CorrelationId);
        Assert.Equal("message-reply", wait.MatchCriteria["stimulusType"]);
        Assert.Equal("order-123", wait.MatchCriteria["orderId"]);
        Assert.Equal(RuntimeEarlySignalPolicy.SatisfyReservedWait, wait.EarlySignalPolicy);
        Assert.DoesNotContain(
            typeof(RuntimeWaitRegistration).GetProperties().Select(property => property.Name),
            name => name.Contains("Inbox", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(RuntimeWaitRegistrationStatus.Satisfied)]
    [InlineData(RuntimeWaitRegistrationStatus.Cancelled)]
    [InlineData(RuntimeWaitRegistrationStatus.Expired)]
    [InlineData(RuntimeWaitRegistrationStatus.Faulted)]
    public void TerminalWaitRegistrations_AreNotMatchable(RuntimeWaitRegistrationStatus status)
    {
        var wait = NewWaitRegistration(status: status);

        Assert.True(wait.IsTerminal);
        Assert.False(wait.IsMatchable);
    }

    [Fact]
    public void ActiveWaitRegistration_RemainsMatchableAfterIntentDelivery()
    {
        var wait = NewWaitRegistration(
            status: RuntimeWaitRegistrationStatus.Active,
            dependsOnPostCommitIntentId: "intent-1");

        Assert.True(wait.IsMatchable);
        Assert.False(wait.IsTerminal);
        Assert.Equal("intent-1", wait.DependsOnPostCommitIntentId);
    }

    [Fact]
    public void WaitRegistration_RejectsInvalidCorrelationAndMatchCriteria()
    {
        Assert.Throws<ArgumentException>(() => NewWaitRegistration(correlationId: " "));
        Assert.Throws<ArgumentException>(() => NewWaitRegistration(matchCriteria: new Dictionary<string, string>()));
        Assert.Throws<ArgumentException>(() => NewWaitRegistration(matchCriteria: new Dictionary<string, string> { [" "] = "value" }));
        Assert.Throws<ArgumentException>(() => NewWaitRegistration(matchCriteria: new Dictionary<string, string> { ["orderId"] = " " }));
    }

    [Fact]
    public void WaitRegistration_SnapshotsMatchCriteriaAndMetadata()
    {
        var matchCriteria = new Dictionary<string, string> { ["orderId"] = "order-123" };
        var metadata = new Dictionary<string, string> { ["causality"] = "wait-dependent post-commit intent" };

        var wait = NewWaitRegistration(matchCriteria: matchCriteria, metadata: metadata);
        matchCriteria["orderId"] = "changed";
        metadata["causality"] = "changed";

        Assert.Equal("order-123", wait.MatchCriteria["orderId"]);
        Assert.Equal("wait-dependent post-commit intent", wait.Metadata["causality"]);
    }

    [Fact]
    public void WaitDependentPostCommitIntent_ReferencesWaitRegistrationAndFailurePolicy()
    {
        var intent = NewIntent(
            dependsOnWaitRegistrationId: "wait-1",
            failurePolicy: RuntimeWaitDependentIntentFailurePolicy.Compensate);

        Assert.True(intent.IsWaitDependent);
        Assert.Equal("wait-1", intent.DependsOnWaitRegistrationId);
        Assert.Equal(RuntimeWaitDependentIntentFailurePolicy.Compensate, intent.WaitFailurePolicy);
    }

    [Fact]
    public void WaitDependentPostCommitIntent_StillRequiresFailurePolicy()
    {
        var exception = Assert.Throws<ArgumentException>(() => NewIntent(dependsOnWaitRegistrationId: "wait-1"));

        Assert.Contains("wait-dependent post-commit intent", exception.Message);
    }

    [Fact]
    public void RuntimeWaitDependentIntentFailurePolicy_IncludesLockedOptions()
    {
        var names = Enum.GetNames<RuntimeWaitDependentIntentFailurePolicy>();

        Assert.Contains(nameof(RuntimeWaitDependentIntentFailurePolicy.FaultWorkflow), names);
        Assert.Contains(nameof(RuntimeWaitDependentIntentFailurePolicy.CompleteWithFailureResult), names);
        Assert.Contains(nameof(RuntimeWaitDependentIntentFailurePolicy.CancelWait), names);
        Assert.Contains(nameof(RuntimeWaitDependentIntentFailurePolicy.Compensate), names);
        Assert.Contains(nameof(RuntimeWaitDependentIntentFailurePolicy.KeepWaitingManualIntervention), names);
    }

    [Fact]
    public void RuntimeWaitDependentIntentFailurePolicy_PreservesExistingOrdinals()
    {
        Assert.Equal(0, (int)RuntimeWaitDependentIntentFailurePolicy.FaultWorkflow);
        Assert.Equal(1, (int)RuntimeWaitDependentIntentFailurePolicy.CompleteWithFailureResult);
        Assert.Equal(2, (int)RuntimeWaitDependentIntentFailurePolicy.CancelWait);
        Assert.Equal(3, (int)RuntimeWaitDependentIntentFailurePolicy.KeepWaitingManualIntervention);
        Assert.Equal(4, (int)RuntimeWaitDependentIntentFailurePolicy.Compensate);
    }

    private RuntimeWaitRegistration NewWaitRegistration(
        RuntimeWaitRegistrationStatus status = RuntimeWaitRegistrationStatus.Reserved,
        string? dependsOnPostCommitIntentId = null,
        RuntimeEarlySignalPolicy earlySignalPolicy = RuntimeEarlySignalPolicy.BufferUntilIntentDelivered,
        string correlationId = "order:123:reply",
        IReadOnlyDictionary<string, string>? matchCriteria = null,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            waitRegistrationId: "wait-1",
            workflowExecutionId: "wfexec-1",
            activityExecutionId: "actexec-1",
            bookmarkId: "bookmark-1",
            correlationId: correlationId,
            stimulusType: "message-reply",
            matchCriteria: matchCriteria ?? new Dictionary<string, string>
            {
                ["stimulusType"] = "message-reply",
                ["orderId"] = "order-123"
            },
            status: status,
            registeredAt: _now,
            expiresAt: _now.AddMinutes(5),
            failurePolicy: RuntimeWaitDependentIntentFailurePolicy.FaultWorkflow,
            earlySignalPolicy: earlySignalPolicy,
            dependsOnPostCommitIntentId: dependsOnPostCommitIntentId,
            metadata: metadata);

    private RuntimePostCommitIntent NewIntent(
        string? dependsOnWaitRegistrationId = null,
        RuntimeWaitDependentIntentFailurePolicy? failurePolicy = null)
    {
        using var document = JsonDocument.Parse("""{"signal":"sent"}""");
        return new RuntimePostCommitIntent(
            intentId: "intent-1",
            workflowExecutionId: "wfexec-1",
            kind: "DispatchSignal",
            recordedAt: _now,
            activityExecutionId: "actexec-1",
            idempotencyKey: "checkpoint-1:intent-1",
            payload: document.RootElement.Clone(),
            metadata: new Dictionary<string, string>(),
            dependsOnWaitRegistrationId: dependsOnWaitRegistrationId,
            waitFailurePolicy: failurePolicy);
    }
}
