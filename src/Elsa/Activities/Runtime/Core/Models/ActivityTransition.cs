using Elsa.Primitives.Models;

namespace Elsa.Activities.Runtime.Core.Models;

/// <summary>Exactly one closed decision returned by one transient activity attempt.</summary>
public abstract record ActivityTransition
{
    protected ActivityTransition()
    {
    }

    public abstract ActivityTransitionKind Kind { get; }

    public static ActivityTransition<TResult> Complete<TResult>(TResult result, string outcome = "Done") =>
        new CompleteActivityTransition<TResult>(result, outcome);

    public static ActivityTransition<TResult> Suspend<TResult, TState>(
        TState state,
        IReadOnlyCollection<ActivityTriggerExpectation> triggers) =>
        new SuspendActivityTransition<TResult, TState>(state, triggers);

    public static ActivityTransition<TResult> Fault<TResult>(ActivityFault fault) =>
        new FaultActivityTransition<TResult>(fault);

    public static ActivityTransition<TResult> Cancel<TResult>(string reason) =>
        new CancelActivityTransition<TResult>(reason);

    private sealed record CompleteActivityTransition<TResult> : ActivityTransition<TResult>, IActivityCompletionTransition<TResult>
    {
        public CompleteActivityTransition(TResult result, string outcome)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(outcome);
            Result = result;
            Outcome = outcome;
        }

        public override ActivityTransitionKind Kind => ActivityTransitionKind.Complete;
        public TResult Result { get; }
        public string Outcome { get; }
        object? IActivityCompletionTransition.Result => Result;
        public Type ResultType => typeof(TResult);
    }

    private sealed record SuspendActivityTransition<TResult, TState> : ActivityTransition<TResult>, IActivitySuspensionTransition<TState>
    {
        public SuspendActivityTransition(TState state, IReadOnlyCollection<ActivityTriggerExpectation> triggers)
        {
            ArgumentNullException.ThrowIfNull(state);
            ArgumentNullException.ThrowIfNull(triggers);
            if (triggers.Count == 0)
                throw new ArgumentException("A suspended activity must declare at least one typed trigger expectation.", nameof(triggers));

            State = state;
            Triggers = Array.AsReadOnly(triggers.ToArray());
        }

        public override ActivityTransitionKind Kind => ActivityTransitionKind.Suspend;
        public TState State { get; }
        public IReadOnlyCollection<ActivityTriggerExpectation> Triggers { get; }
        object IActivitySuspensionTransition.State => State!;
        public Type StateType => typeof(TState);
    }

    private sealed record FaultActivityTransition<TResult> : ActivityTransition<TResult>, IActivityFaultTransition
    {
        public FaultActivityTransition(ActivityFault fault) => Fault = fault ?? throw new ArgumentNullException(nameof(fault));
        public override ActivityTransitionKind Kind => ActivityTransitionKind.Fault;
        public ActivityFault Fault { get; }
    }

    private sealed record CancelActivityTransition<TResult> : ActivityTransition<TResult>, IActivityCancellationTransition
    {
        public CancelActivityTransition(string reason)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
            Reason = reason;
        }

        public override ActivityTransitionKind Kind => ActivityTransitionKind.Cancel;
        public string Reason { get; }
    }
}

public abstract record ActivityTransition<TResult> : ActivityTransition;

/// <summary>Engine-facing, type-erased view of a successful activity completion.</summary>
public interface IActivityCompletionTransition
{
    object? Result { get; }
    Type ResultType { get; }
    string Outcome { get; }
}

/// <summary>Author-facing, strongly typed view of a successful activity completion.</summary>
public interface IActivityCompletionTransition<out TResult> : IActivityCompletionTransition
{
    new TResult Result { get; }
}

/// <summary>Engine-facing, type-erased view of a suspended activity.</summary>
public interface IActivitySuspensionTransition
{
    object State { get; }
    Type StateType { get; }
    IReadOnlyCollection<ActivityTriggerExpectation> Triggers { get; }
}

/// <summary>Author-facing, strongly typed view of a suspended activity.</summary>
public interface IActivitySuspensionTransition<out TState> : IActivitySuspensionTransition
{
    new TState State { get; }
}

public interface IActivityFaultTransition
{
    ActivityFault Fault { get; }
}

public interface IActivityCancellationTransition
{
    string Reason { get; }
}

public readonly record struct ActivityUnit
{
    public static ActivityUnit Value { get; } = new();
}

public enum ActivityTransitionKind
{
    Complete,
    Suspend,
    Fault,
    Cancel
}

public sealed record ActivityFault
{
    public ActivityFault(string code, string message, bool isRetryable = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Code = code;
        Message = message;
        IsRetryable = isRetryable;
    }

    public string Code { get; }
    public string Message { get; }
    public bool IsRetryable { get; }
}

public sealed record ActivityTriggerExpectation
{
    public ActivityTriggerExpectation(string key, string stimulusType, ValueTypeDescriptor payloadType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);
        ArgumentNullException.ThrowIfNull(payloadType);
        Key = key;
        StimulusType = stimulusType;
        PayloadType = payloadType;
    }

    public string Key { get; }
    public string StimulusType { get; }
    public ValueTypeDescriptor PayloadType { get; }
}
