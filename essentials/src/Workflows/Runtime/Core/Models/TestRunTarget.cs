using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>One prepared external test target bound to the exact selected trigger and existing scope expiry.</summary>
public sealed class TestRunTarget
{
    public TestRunTarget(
        string testRunId,
        string driverId,
        string providerId,
        string modeId,
        WorkflowExecutableIdentity executable,
        string sourceReferenceId,
        WorkflowTriggerBinding binding,
        WorkflowTestScope scope,
        string requestedBy,
        string workflowExecutionId,
        string idempotencyKey,
        IReadOnlyDictionary<string, JsonElement> inputs,
        IReadOnlyDictionary<string, object?> variables,
        JsonElement? driverState,
        DateTimeOffset createdAt,
        TestRunTargetState state = TestRunTargetState.Prepared,
        string? stimulusDigest = null,
        TestRunTargetDispatchResult? dispatchResult = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testRunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(driverId);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(modeId);
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceReferenceId);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(variables);
        if (!StringComparer.Ordinal.Equals(testRunId, scope.ScopeId))
            throw new ArgumentException("The target id must match its TestRun scope.", nameof(scope));
        if (!StringComparer.Ordinal.Equals(executable.ArtifactId, binding.ArtifactId))
            throw new ArgumentException("The selected trigger binding must belong to the target executable.", nameof(binding));
        if (createdAt == default || createdAt >= scope.ExpiresAt)
            throw new ArgumentOutOfRangeException(nameof(createdAt));
        if (driverState is { ValueKind: JsonValueKind.Undefined })
            throw new ArgumentException("Driver state must be a defined JSON value.", nameof(driverState));
        if (!Enum.IsDefined(state))
            throw new ArgumentOutOfRangeException(nameof(state));
        if (stimulusDigest is not null && string.IsNullOrWhiteSpace(stimulusDigest))
            throw new ArgumentException("A stimulus digest cannot be blank when provided.", nameof(stimulusDigest));
        if (state is TestRunTargetState.Dispatching or TestRunTargetState.Dispatched && stimulusDigest is null)
            throw new ArgumentException("A dispatched target requires a stimulus digest.", nameof(stimulusDigest));
        if (state == TestRunTargetState.Dispatched && dispatchResult is null)
            throw new ArgumentException("A completed target dispatch requires its result.", nameof(dispatchResult));
        if (state != TestRunTargetState.Dispatched && dispatchResult is not null)
            throw new ArgumentException("Only a dispatched target can carry its result.", nameof(dispatchResult));

        TestRunId = testRunId;
        DriverId = driverId;
        ProviderId = providerId;
        ModeId = modeId;
        Executable = executable;
        SourceReferenceId = sourceReferenceId;
        Binding = binding;
        Scope = scope;
        RequestedBy = requestedBy;
        WorkflowExecutionId = workflowExecutionId;
        IdempotencyKey = idempotencyKey;
        Inputs = inputs.ToDictionary(item => item.Key, item => item.Value.Clone(), StringComparer.Ordinal);
        Variables = variables.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        DriverState = driverState?.Clone();
        CreatedAt = createdAt;
        State = state;
        StimulusDigest = stimulusDigest;
        DispatchResult = dispatchResult;
    }

    public string TestRunId { get; }
    public string DriverId { get; }
    public string ProviderId { get; }
    public string ModeId { get; }
    public WorkflowExecutableIdentity Executable { get; }
    public string SourceReferenceId { get; }
    public WorkflowTriggerBinding Binding { get; }
    public WorkflowTestScope Scope { get; }
    public string RequestedBy { get; }
    public string WorkflowExecutionId { get; }
    public string IdempotencyKey { get; }
    public IReadOnlyDictionary<string, JsonElement> Inputs { get; }
    public IReadOnlyDictionary<string, object?> Variables { get; }
    public JsonElement? DriverState { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset ExpiresAt => Scope.ExpiresAt;
    public TestRunTargetState State { get; }
    public string? StimulusDigest { get; }
    public TestRunTargetDispatchResult? DispatchResult { get; }

    public TestRunTarget WithDispatching(string stimulusDigest) =>
        Copy(TestRunTargetState.Dispatching, stimulusDigest, null);

    public TestRunTarget WithDispatchResult(TestRunTargetDispatchResult dispatchResult) =>
        Copy(TestRunTargetState.Dispatched, StimulusDigest, dispatchResult);

    public TestRunTarget WithStopped() =>
        Copy(TestRunTargetState.Stopped, StimulusDigest, null);

    private TestRunTarget Copy(
        TestRunTargetState state,
        string? stimulusDigest,
        TestRunTargetDispatchResult? dispatchResult) =>
        new(
            TestRunId,
            DriverId,
            ProviderId,
            ModeId,
            Executable,
            SourceReferenceId,
            Binding,
            Scope,
            RequestedBy,
            WorkflowExecutionId,
            IdempotencyKey,
            Inputs,
            Variables,
            DriverState,
            CreatedAt,
            state,
            stimulusDigest,
            dispatchResult);
}

public enum TestRunTargetState
{
    Prepared = 0,
    Dispatching = 1,
    Dispatched = 2,
    Stopped = 3
}

public enum TestRunTargetDisposition
{
    Ready = 0,
    AlreadyDispatched = 1,
    Stopped = 2,
    PayloadConflict = 3,
    NotFound = 4,
    Expired = 5
}

public sealed record TestRunTargetAdmission(
    TestRunTargetDisposition Disposition,
    TestRunTarget? Target);

public sealed record TestRunTargetDispatchResult(
    WorkflowExecutionStartDispatchResult Dispatch,
    bool AlreadyDispatched);
