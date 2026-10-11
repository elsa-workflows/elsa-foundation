using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Values;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// How the masking test activities fail (spec 188, slice 8): at which step, and whether they throw the exception
/// <see cref="Exception"/> builds, or return the fault <see cref="Fault"/> builds, from the value their secret-bound
/// input was hydrated with. By default the exception's message and its inner exception's message carry the value, and so
/// does the fault's message. Once <see cref="Cancellation"/> is set they cancel the work instead, and once
/// <see cref="DisposalFailure"/> is set their disposal fails.
/// </summary>
public sealed class SecretFailurePlan
{
    /// <summary>Suspend when executed, and fail when resumed, instead of failing when executed.</summary>
    public bool FailOnResume { get; init; }

    /// <summary>Return <see cref="Fault"/> instead of throwing <see cref="Exception"/>.</summary>
    public bool ReturnFault { get; init; }

    public Func<string, Exception> Exception { get; init; } =
        value => new InvalidOperationException($"refused {value}", new FormatException($"inner {value}"));

    public Func<string, ActivityFault> Fault { get; init; } = value => new ActivityFault("secret.echoed", $"returned {value}");

    /// <summary>When set, fail by canceling this source and throwing its cancellation, as work canceled while it runs does.</summary>
    public CancellationTokenSource? Cancellation { get; set; }

    /// <summary>When set, disposing the activity throws the exception this returns.</summary>
    public Func<Exception>? DisposalFailure { get; set; }

    /// <summary>Fails the step: cancels, returns the fault, or throws the exception, in that order of precedence.</summary>
    public ValueTask<TResult> FailAsync<TResult>(string value, Func<ActivityFault, TResult> returnFault)
    {
        if (Cancellation is { } cancellation)
        {
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
        }

        return ReturnFault ? ValueTask.FromResult(returnFault(Fault(value))) : throw Exception(value);
    }

    public void ThrowIfDisposalFails()
    {
        if (DisposalFailure is { } failure)
            throw failure();
    }
}

/// <summary>
/// A leaf with a secret-bound input that fails as its <see cref="SecretFailurePlan"/> says, with the input's value in the
/// failure text. With <see cref="SecretFailurePlan.FailOnResume"/> it first suspends on the same resume target as
/// <see cref="SecretWaitingActivity"/>.
/// </summary>
public sealed class SecretFailingActivity(SecretFailurePlan plan) : StatefulActivity<ActivityUnit, WaitState, WaitTrigger>, IDisposable
{
    [ActivityInput(Key = SecretResolutionTestSupport.InputKey)]
    public string Token { get; set; } = null!;

    protected override ValueTask<ActivityTransition<ActivityUnit, WaitState>> ExecuteAsync(ActivityExecutionContext context) =>
        plan.FailOnResume
            ? ValueTask.FromResult(Suspend(
                new WaitState("waiting"),
                [new ActivityTriggerRegistration<WaitTrigger>(SecretWaitingActivity.ResumeTargetKey, SecretWaitingActivity.StimulusType, SecretWaitingActivity.StimulusHash)]))
            : FailAsync();

    protected override ValueTask<ActivityTransition<ActivityUnit, WaitState>> ResumeAsync(ActivityResumeContext<WaitState, WaitTrigger> context) =>
        FailAsync();

    private ValueTask<ActivityTransition<ActivityUnit, WaitState>> FailAsync() => plan.FailAsync(Token, Fault);

    public void Dispose() => plan.ThrowIfDisposalFails();
}

/// <summary>
/// A structural parent with a secret-bound input that schedules its only child, then fails as its
/// <see cref="SecretFailurePlan"/> says when the child notifies it or completes: by throwing, or by returning a faulted
/// continuation.
/// </summary>
public sealed class SecretFailingParentActivity(SecretFailurePlan plan) : StructuralActivity,
    IRuntimeStructuralActivity,
    IRuntimeActivityChildCompletionHandler,
    IRuntimeActivityChildNotificationHandler,
    IDisposable
{
    [ActivityInput(Key = SecretResolutionTestSupport.InputKey)]
    public string Token { get; set; } = null!;

    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        var child = Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities);
        context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildNotifiedAsync(IRuntimeActivityExecutionContext context, ActivityChildNotifiedContext notification) =>
        FailAsync();

    public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) => FailAsync();

    private ValueTask<RuntimeStructuralContinuation> FailAsync() => plan.FailAsync(Token, RuntimeStructuralContinuation.Faulted);

    public void Dispose() => plan.ThrowIfDisposalFails();
}

/// <summary>A leaf with two secret-bound inputs, <see cref="FirstKey"/> and <see cref="SecondKey"/>, resolved in that order.</summary>
public sealed class TwoSecretInputActivity : Activity
{
    public const string FirstKey = "first";
    public const string SecondKey = "second";

    [ActivityInput(Key = FirstKey)]
    public string First { get; set; } = null!;

    [ActivityInput(Key = SecondKey)]
    public string Second { get; set; } = null!;

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));

    /// <summary>A node of this activity whose two inputs are bound to <paramref name="firstReference"/> and <paramref name="secondReference"/>.</summary>
    public static ExecutableNode NewNode(string nodeId, string firstReference, string secondReference) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(TwoSecretInputActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test/secret",
            descriptorPayload: JsonSerializer.SerializeToElement(new { type = "secret" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                [FirstKey] = SecretResolutionTestSupport.SecretRead(FirstKey, firstReference),
                [SecondKey] = SecretResolutionTestSupport.SecretRead(SecondKey, secondReference)
            },
            metadata: new Dictionary<string, string>());
}

/// <summary>
/// Captures faults as the default policy does, and keeps every exception it is asked to capture: the exception object
/// the fault boundary handed on, which the activity fault recorder captures and any log line would render.
/// </summary>
public sealed class RecordingFaultCapturePolicy : IRuntimeFaultCapturePolicy
{
    private readonly DefaultRuntimeFaultCapturePolicy _inner = DefaultRuntimeFaultCapturePolicy.CreateDefault();
    private readonly List<Exception> _captured = [];

    public IReadOnlyList<Exception> Captured
    {
        get
        {
            lock (_captured)
                return _captured.ToArray();
        }
    }

    public RuntimeFaultInfo Capture(Exception exception)
    {
        lock (_captured)
            _captured.Add(exception);
        return _inner.Capture(exception);
    }
}

/// <summary>
/// A mask shared by every scope of a test host: it masks as the default does, and remembers which activity executions
/// registered a value, so a test can assert that a value was registered for an execution and released once its work
/// handler recorded the outcome.
/// </summary>
public sealed class ObservedSecretMask : IRuntimeSecretMask
{
    private readonly DefaultRuntimeSecretMask _inner = new();
    private readonly HashSet<string> _registered = new(StringComparer.Ordinal);

    public bool WasRegistered(string activityExecutionId)
    {
        lock (_registered)
            return _registered.Contains(activityExecutionId);
    }

    public void Register(string activityExecutionId, string referenceName, string value)
    {
        lock (_registered)
            _registered.Add(activityExecutionId);
        _inner.Register(activityExecutionId, referenceName, value);
    }

    public bool HasRegistrations(string activityExecutionId) => _inner.HasRegistrations(activityExecutionId);

    public string Mask(string activityExecutionId, string text) => _inner.Mask(activityExecutionId, text);

    public void Release(string activityExecutionId) => _inner.Release(activityExecutionId);
}

/// <summary>
/// A conversion executor that delegates to the runtime's until it is armed, and from then on cancels the armed source
/// and throws its cancellation when it converts: an activation canceled after a secret resolved and was registered with
/// the mask, before the activity was hydrated.
/// </summary>
public sealed class CancelingConversionExecutor : IRuntimeValueConversionExecutor
{
    private readonly RuntimeValueConversionExecutor _inner = new();

    public CancellationTokenSource? Cancellation { get; set; }

    public ValueEnvelope Convert(ValueEnvelope source, ValueConversionPlan plan)
    {
        if (Cancellation is not { } cancellation)
            return _inner.Convert(source, plan);

        cancellation.Cancel();
        cancellation.Token.ThrowIfCancellationRequested();
        throw new InvalidOperationException("The cancellation was not observed.");
    }
}
