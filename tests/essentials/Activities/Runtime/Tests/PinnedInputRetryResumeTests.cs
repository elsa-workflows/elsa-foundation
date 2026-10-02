using System.Text.Json;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed partial class WorkflowInvokeActivitySchedulerWorkHandlerTests
{
    [Fact]
    public async Task HandleAsync_TypedRetry_ReusesPinnedSnapshotAndCreatesFreshAttempt()
    {
        var executable = NewTypedExecutable(literalText: "changed-after-snapshot");
        var state = NewTypedRunningState();
        var initialAttempt = Assert.Single(state.Attempts!);
        var endedInitialAttempt = new ActivityAttempt(
            initialAttempt.AttemptId,
            initialAttempt.InvocationId,
            initialAttempt.Ordinal,
            initialAttempt.Reason,
            initialAttempt.StartedAt,
            _now,
            transitionKind: Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Fault);
        state = state with { Attempts = [endedInitialAttempt] };
        var activator = new RecordingTypedActivator();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(state);
        await using var provider = NewProvider(activator, includeInspection: true);

        await NewHandler(provider).HandleAsync(NewInvokeWorkItem(NewIdentity()));

        var request = Assert.Single(activator.Requests);
        Assert.Equal("hello", request.Inputs.Values["text"].InlineValue!.Value.GetString());
        Assert.Equal("actexec-1", request.Inputs.InvocationId);
        Assert.Equal("actexec-1", request.Attempt.InvocationId);
        Assert.Equal("actexec-1:attempt:2", request.Attempt.AttemptId);
        Assert.Equal(2, request.Attempt.Ordinal);
        Assert.Equal(ActivityAttemptReason.Retry, request.Attempt.Reason);

        var completed = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(completed?.InputSnapshot);
        Assert.Equal("hello", completed.InputSnapshot.Values["text"].InlineValue!.Value.GetString());
        Assert.Collection(
            completed.Attempts!.OrderBy(attempt => attempt.Ordinal),
            attempt =>
            {
                Assert.Equal(ActivityAttemptReason.Initial, attempt.Reason);
                Assert.NotNull(attempt.EndedAt);
            },
            attempt =>
            {
                Assert.Equal(ActivityAttemptReason.Retry, attempt.Reason);
                Assert.NotNull(attempt.EndedAt);
                Assert.Equal(Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Complete, attempt.TransitionKind);
            });
    }

    /// <summary>
    /// A secret-bound input is resolved at each activation and never restored from persisted state (spec 188, T019):
    /// invoke delivers the value, the persisted snapshot keeps only the withheld reference, and a value changed while
    /// the activity was suspended reaches it on resume.
    /// </summary>
    [Fact]
    public async Task A_secret_input_resolves_on_invoke_and_again_on_resume_and_only_its_reference_is_persisted()
    {
        const string nodeId = "node-wait";
        var resolver = new FakeRuntimeSecretResolver { Respond = (_, _) => RuntimeSecretResolution.Success("before-rotation") };
        var recorder = new SecretValueRecorder();
        await using var harness = SecretResolutionTestSupport.NewHarness(resolver, recorder, ["actexec-wait"]);

        var suspended = (await harness.RunAsync(SecretResolutionTestSupport.NewWaitingExecutable(nodeId))).State(nodeId);

        Assert.Equal(ActivityExecutionStatus.Suspended, suspended.Status);
        Assert.Equal(["before-rotation"], recorder.Values);
        SecretResolutionTestSupport.AssertSnapshotWithheld(suspended);

        resolver.Respond = (_, _) => RuntimeSecretResolution.Success("after-rotation");
        var resumed = (await harness.ResumeAsync(
            WorkflowExecutionHarness.Identity,
            bookmarkId: Assert.Single(suspended.BookmarkIds),
            activityExecutionId: suspended.InvocationId,
            executableNodeId: nodeId,
            resumeTargetId: SecretResolutionTestSupport.WaitResumeTargetId(nodeId),
            stimulusType: SecretWaitingActivity.StimulusType,
            stimulusHash: SecretWaitingActivity.StimulusHash,
            input: JsonSerializer.SerializeToElement(new WaitTrigger(true)))).AssertCompleted(nodeId);

        Assert.Equal([(SecretValueRecorder.Execute, "before-rotation"), (SecretValueRecorder.Resume, "after-rotation")], recorder.Entries);
        SecretResolutionTestSupport.AssertSnapshotWithheld(resumed);
        await SecretResolutionTestSupport.AssertNotPersistedAsync(harness, recorder.Values);
        // The instance records no tenant, so both activations resolve under the partition the execution runs under.
        Assert.All(resolver.Requests, request => Assert.Equal(WorkflowExecutionPartition.DefaultValue, request.TenantId));
        Assert.Equal(2, resolver.Requests.Count);
    }

    /// <summary>
    /// A resolver that throws instead of returning a result faults the activity with <c>ResolverFailed</c>, and nothing
    /// it threw reaches the persisted fault or the incident (spec 188, FR-003). A cancellation-typed exception from the
    /// resolver's own timeout is no exception: the activation's token is live, so it is not the activation's cancellation.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_resolver_that_throws_faults_the_activity_with_ResolverFailed_and_persists_nothing_it_threw(bool cancellationTyped)
    {
        const string nodeId = "node-wait";
        const string sentinel = "resolver-detail-sentinel";
        Exception thrown = cancellationTyped ? new TaskCanceledException(sentinel) : new InvalidOperationException(sentinel);
        var resolver = new FakeRuntimeSecretResolver { Respond = (_, _) => throw thrown };
        await using var harness = SecretResolutionTestSupport.NewHarness(resolver, new SecretValueRecorder(), ["actexec-wait"]);

        var faulted = (await harness.RunAsync(SecretResolutionTestSupport.NewWaitingExecutable(nodeId))).State(nodeId);

        Assert.Equal(ActivityExecutionStatus.Faulted, faulted.Status);
        Assert.Equal(RuntimeSecretResolutionException.ResolverFailed, faulted.Fault!.Code);
        Assert.False(faulted.Fault.IsRetryable);
        Assert.Equal($"Secret '{SecretResolutionTestSupport.ReferenceName}' could not be resolved (ResolverFailed).", faulted.Fault.Message);
        var incident = Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>().ListAsync(harness.ExecutionId));
        foreach (var leak in new[] { sentinel, thrown.GetType().Name })
        {
            Assert.DoesNotContain(leak, JsonSerializer.Serialize(faulted), StringComparison.Ordinal);
            Assert.DoesNotContain(leak, JsonSerializer.Serialize(incident), StringComparison.Ordinal);
        }
    }
}
