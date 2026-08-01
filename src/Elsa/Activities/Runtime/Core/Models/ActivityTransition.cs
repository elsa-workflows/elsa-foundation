using System.Text.Json;
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

/// <summary>
/// Typed transition returned by a stateful activity. Successful completion remains atomic while a
/// suspension additionally carries one complete private-state snapshot and typed registrations.
/// </summary>
public abstract record ActivityTransition<TResult, TState> : ActivityTransition<TResult>
{
    public static ActivityTransition<TResult, TState> Complete(TResult result, string outcome = "Done") =>
        new CompleteStatefulActivityTransition(result, outcome);

    public static ActivityTransition<TResult, TState> Suspend<TTrigger>(
        TState state,
        IReadOnlyCollection<ActivityTriggerRegistration<TTrigger>> registrations,
        int stateSchemaVersion = 1) =>
        new SuspendStatefulActivityTransition<TTrigger>(state, registrations, stateSchemaVersion);

    public static ActivityTransition<TResult, TState> Fault(ActivityFault fault) =>
        new FaultStatefulActivityTransition(fault);

    public static ActivityTransition<TResult, TState> Cancel(string reason) =>
        new CancelStatefulActivityTransition(reason);

    private sealed record CompleteStatefulActivityTransition : ActivityTransition<TResult, TState>, IActivityCompletionTransition<TResult>
    {
        public CompleteStatefulActivityTransition(TResult result, string outcome)
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

    private sealed record SuspendStatefulActivityTransition<TTrigger> :
        ActivityTransition<TResult, TState>,
        IStatefulActivitySuspensionTransition<TState>
    {
        private readonly TState _state;

        public SuspendStatefulActivityTransition(
            TState state,
            IReadOnlyCollection<ActivityTriggerRegistration<TTrigger>> registrations,
            int stateSchemaVersion)
        {
            ArgumentNullException.ThrowIfNull(registrations);
            if (registrations.Count == 0)
                throw new ArgumentException("A suspended activity must declare at least one typed trigger registration.", nameof(registrations));
            if (stateSchemaVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(stateSchemaVersion), stateSchemaVersion, "An activity state schema version must be positive.");

            StatePayload = PersistableActivityValue.Snapshot(state, "private state");
            _state = PersistableActivityValue.SnapshotAndMaterialize(state, "private state");
            StateValueType = PersistableActivityValue.Descriptor(typeof(TState), stateSchemaVersion);
            Registrations = Array.AsReadOnly<IActivityTriggerRegistration>(registrations.Cast<IActivityTriggerRegistration>().ToArray());
            Triggers = Array.AsReadOnly(registrations
                .Select(registration => new ActivityTriggerExpectation(
                    registration.ResumeTargetKey,
                    registration.StimulusType,
                    registration.PayloadType))
                .ToArray());
        }

        public override ActivityTransitionKind Kind => ActivityTransitionKind.Suspend;
        public TState State => _state;
        object IActivitySuspensionTransition.State => State!;
        public Type StateType => typeof(TState);
        public ValueTypeDescriptor StateValueType { get; }
        public JsonElement StatePayload { get; }
        public Type TriggerType => typeof(TTrigger);
        public IReadOnlyCollection<IActivityTriggerRegistration> Registrations { get; }
        public IReadOnlyCollection<ActivityTriggerExpectation> Triggers { get; }
    }

    private sealed record FaultStatefulActivityTransition : ActivityTransition<TResult, TState>, IActivityFaultTransition
    {
        public FaultStatefulActivityTransition(ActivityFault fault) => ActivityFault = fault ?? throw new ArgumentNullException(nameof(fault));
        public override ActivityTransitionKind Kind => ActivityTransitionKind.Fault;
        public ActivityFault ActivityFault { get; }
        ActivityFault IActivityFaultTransition.Fault => ActivityFault;
    }

    private sealed record CancelStatefulActivityTransition : ActivityTransition<TResult, TState>, IActivityCancellationTransition
    {
        public CancelStatefulActivityTransition(string reason)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
            Reason = reason;
        }

        public override ActivityTransitionKind Kind => ActivityTransitionKind.Cancel;
        public string Reason { get; }
    }
}

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

/// <summary>Engine-facing suspension view with a frozen state payload and typed registrations.</summary>
public interface IStatefulActivitySuspensionTransition : IActivitySuspensionTransition
{
    ValueTypeDescriptor StateValueType { get; }
    JsonElement StatePayload { get; }
    Type TriggerType { get; }
    IReadOnlyCollection<IActivityTriggerRegistration> Registrations { get; }
}

/// <summary>Author-facing strongly typed state view of a stateful suspension.</summary>
public interface IStatefulActivitySuspensionTransition<out TState> :
    IStatefulActivitySuspensionTransition,
    IActivitySuspensionTransition<TState>;

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
    /// <summary>Metadata key carrying <see cref="Category"/> onto the normalized durable fault record.</summary>
    public const string CategoryMetadataKey = "fault.category";

    /// <summary>Metadata key carrying <see cref="FaultType"/> onto the normalized durable fault record.</summary>
    public const string FaultTypeMetadataKey = "fault.type";

    public ActivityFault(string code, string message, bool isRetryable = false, string? category = null, string? faultType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Code = code;
        Message = message;
        IsRetryable = isRetryable;
        Category = Normalize(category);
        FaultType = Normalize(faultType);
    }

    public string Code { get; }
    public string Message { get; }
    public bool IsRetryable { get; }

    /// <summary>
    /// Optional author-chosen classification of the fault (Elsa 3's <c>Fault.Category</c>). Carried onto the
    /// durable fault record's metadata under <see cref="CategoryMetadataKey"/>; null when unclassified.
    /// </summary>
    public string? Category { get; }

    /// <summary>
    /// Optional author-chosen fault type (Elsa 3's <c>Fault.FaultType</c>), orthogonal to <see cref="Code"/>:
    /// the code identifies the failure, the type names the family it belongs to. Carried onto the durable
    /// fault record's metadata under <see cref="FaultTypeMetadataKey"/>; null when unclassified.
    /// </summary>
    public string? FaultType { get; }

    /// <summary>
    /// The classification pair projected as durable-record metadata. Empty when neither is set, so an
    /// unclassified fault persists exactly the shape it did before classification existed.
    /// </summary>
    public IReadOnlyDictionary<string, string> ClassificationMetadata
    {
        get
        {
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
            if (Category is not null)
                metadata[CategoryMetadataKey] = Category;
            if (FaultType is not null)
                metadata[FaultTypeMetadataKey] = FaultType;
            return metadata;
        }
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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
