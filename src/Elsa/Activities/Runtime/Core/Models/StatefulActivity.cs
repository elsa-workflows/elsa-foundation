using System.Collections;
using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Primitives.Models;

namespace Elsa.Activities.Runtime.Core.Models;

/// <summary>
/// Author-facing base for a transient activity that suspends with one typed private-state document
/// and resumes with one typed trigger payload.
/// </summary>
/// <remarks>
/// The object that receives <see cref="ResumeAsync"/> is required to be a new activation. CLR fields
/// and constructor-injected service instances are deliberately absent from the resume contract.
/// </remarks>
public abstract class StatefulActivity<TResult, TState, TTrigger> :
    ActivityBase,
    IStatefulActivity<TResult, TState, TTrigger>
{
    private int _attemptStarted;

    /// <summary>Runs the initial attempt.</summary>
    protected abstract ValueTask<ActivityTransition<TResult, TState>> ExecuteAsync(ActivityExecutionContext context);

    /// <summary>Runs a fresh resume attempt with the last committed state and one validated trigger.</summary>
    protected abstract ValueTask<ActivityTransition<TResult, TState>> ResumeAsync(ActivityResumeContext<TState, TTrigger> context);

    /// <summary>Returns one atomic successful result from the current attempt.</summary>
    protected static ActivityTransition<TResult, TState> Complete(TResult result, string outcome = "Done") =>
        ActivityTransition<TResult, TState>.Complete(result, outcome);

    /// <summary>Suspends with one complete state document and registrations of the declared trigger type.</summary>
    protected static ActivityTransition<TResult, TState> Suspend(
        TState state,
        IReadOnlyCollection<ActivityTriggerRegistration<TTrigger>> registrations,
        int stateSchemaVersion = 1) =>
        ActivityTransition<TResult, TState>.Suspend(state, registrations, stateSchemaVersion);

    /// <summary>Returns a normalized fault transition from the current attempt.</summary>
    protected static ActivityTransition<TResult, TState> Fault(ActivityFault fault) =>
        ActivityTransition<TResult, TState>.Fault(fault);

    /// <summary>Returns a cancellation transition from the current attempt.</summary>
    protected static ActivityTransition<TResult, TState> Cancel(string reason) =>
        ActivityTransition<TResult, TState>.Cancel(reason);

    protected sealed override async ValueTask<ActivityTransition> ExecuteTransitionAsync(IActivityExecutionContext context)
    {
        BeginAttempt();
        EnsureDeclaredValueTypes();
        return ValidateTransition(await ExecuteAsync(ActivityExecutionContext.FromLegacy(context)));
    }

    async ValueTask<ActivityTransition<TResult, TState>> IStatefulActivity<TResult, TState, TTrigger>.ExecuteAsync(
        ActivityExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BeginAttempt();
        EnsureDeclaredValueTypes();
        return ValidateTransition(await ExecuteAsync(context));
    }

    async ValueTask<ActivityTransition<TResult, TState>> IStatefulActivity<TResult, TState, TTrigger>.ResumeAsync(
        ActivityResumeContext<TState, TTrigger> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BeginAttempt();
        EnsureDeclaredValueTypes();
        return ValidateTransition(await ResumeAsync(context));
    }

    async ValueTask<ActivityTransition> IStatefulActivity.ResumeAsync(ActivityResumeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidatePortableType(request.StateType, typeof(TState), "private state");
        ValidatePortableType(request.TriggerType, typeof(TTrigger), "trigger");

        TState state;
        TTrigger trigger;
        try
        {
            state = request.StatePayload.Deserialize<TState>()
                ?? throw new InvalidOperationException("The committed activity private state resolved to null.");
            trigger = request.TriggerPayload.Deserialize<TTrigger>()
                ?? throw new InvalidOperationException("The validated activity trigger resolved to null.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new InvalidOperationException("The committed activity state or validated trigger payload does not match the declared stateful activity contract.", exception);
        }

        return await ((IStatefulActivity<TResult, TState, TTrigger>)this).ResumeAsync(
            new ActivityResumeContext<TState, TTrigger>(
                request.Attempt,
                state,
                trigger,
                request.RegistrationId,
                request.DeliveryId));
    }

    private void BeginAttempt()
    {
        if (Interlocked.Exchange(ref _attemptStarted, 1) != 0)
        {
            throw new InvalidOperationException(
                "A stateful activity activation can execute exactly one attempt. Resume and retry require a fresh activation.");
        }
    }

    private static ActivityTransition<TResult, TState> ValidateTransition(ActivityTransition<TResult, TState> transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        if (transition is IStatefulActivitySuspensionTransition suspension && suspension.TriggerType != typeof(TTrigger))
        {
            throw new InvalidOperationException(
                $"The suspension registered trigger type '{suspension.TriggerType}' but " +
                $"'{typeof(TTrigger)}' is declared by the stateful activity contract.");
        }

        return transition;
    }

    private static void EnsureDeclaredValueTypes()
    {
        PersistableActivityValue.ValidateType(typeof(TState), "private state");
        PersistableActivityValue.ValidateType(typeof(TTrigger), "trigger");
    }

    private static void ValidatePortableType(ValueTypeDescriptor actual, Type expectedClrType, string role)
    {
        var expected = PersistableActivityValue.Descriptor(expectedClrType, actual.SchemaVersion ?? 1);
        if (!StringComparer.Ordinal.Equals(actual.Alias, expected.Alias) ||
            actual.CollectionKind != expected.CollectionKind)
        {
            throw new InvalidOperationException(
                $"The committed activity {role} type '{actual.Alias}' does not match the declared type '{expected.Alias}'.");
        }
    }
}

/// <summary>
/// Frozen, engine-owned input to one stateful resume attempt. It contains no services, mutable
/// workflow values, or runtime persistence records.
/// </summary>
public sealed class ActivityResumeRequest
{
    public ActivityResumeRequest(
        ActivityExecutionContext attempt,
        ValueTypeDescriptor stateType,
        JsonElement statePayload,
        ValueTypeDescriptor triggerType,
        JsonElement triggerPayload,
        string registrationId,
        string deliveryId)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(stateType);
        ArgumentNullException.ThrowIfNull(triggerType);
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryId);

        if (statePayload.ValueKind is JsonValueKind.Undefined)
            throw new ArgumentException("Activity private state payload cannot be undefined.", nameof(statePayload));
        if (triggerPayload.ValueKind is JsonValueKind.Undefined)
            throw new ArgumentException("Activity trigger payload cannot be undefined.", nameof(triggerPayload));

        Attempt = attempt;
        StateType = stateType;
        StatePayload = statePayload.Clone();
        TriggerType = triggerType;
        TriggerPayload = triggerPayload.Clone();
        RegistrationId = registrationId;
        DeliveryId = deliveryId;
    }

    public ActivityExecutionContext Attempt { get; }
    public ValueTypeDescriptor StateType { get; }
    public JsonElement StatePayload { get; }
    public ValueTypeDescriptor TriggerType { get; }
    public JsonElement TriggerPayload { get; }
    public string RegistrationId { get; }
    public string DeliveryId { get; }
}

/// <summary>
/// Immutable value context supplied to one resume attempt. It contains identity, cancellation, the
/// last committed private state, and exactly one validated trigger—nothing else.
/// </summary>
public sealed class ActivityResumeContext<TState, TTrigger>
{
    public ActivityResumeContext(
        ActivityExecutionContext attempt,
        TState state,
        TTrigger trigger,
        string registrationId,
        string deliveryId)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryId);

        WorkflowExecutionId = attempt.WorkflowExecutionId;
        InvocationId = attempt.InvocationId;
        AttemptId = attempt.AttemptId;
        ExecutableNodeId = attempt.ExecutableNodeId;
        CancellationToken = attempt.CancellationToken;
        State = PersistableActivityValue.SnapshotAndMaterialize(state, "private state");
        Trigger = PersistableActivityValue.SnapshotAndMaterialize(trigger, "trigger");
        RegistrationId = registrationId;
        DeliveryId = deliveryId;
    }

    public string WorkflowExecutionId { get; }
    public string InvocationId { get; }
    public string AttemptId { get; }
    public string ExecutableNodeId { get; }
    public CancellationToken CancellationToken { get; }
    public TState State { get; }
    public TTrigger Trigger { get; }
    public string RegistrationId { get; }
    public string DeliveryId { get; }
}

/// <summary>A typed, persistable trigger expectation emitted by a suspended activity.</summary>
public sealed record ActivityTriggerRegistration<TTrigger> : IActivityTriggerRegistration
{
    public ActivityTriggerRegistration(
        string resumeTargetKey,
        string stimulusType,
        string stimulusHash,
        ActivityTriggerDeduplicationMode deduplicationMode = ActivityTriggerDeduplicationMode.Once,
        int payloadSchemaVersion = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resumeTargetKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusHash);
        PersistableActivityValue.ValidateType(typeof(TTrigger), "trigger");

        ResumeTargetKey = resumeTargetKey;
        StimulusType = stimulusType;
        StimulusHash = stimulusHash;
        DeduplicationMode = deduplicationMode;
        PayloadType = PersistableActivityValue.Descriptor(typeof(TTrigger), payloadSchemaVersion);
    }

    public string ResumeTargetKey { get; }
    public string StimulusType { get; }
    public string StimulusHash { get; }
    public ActivityTriggerDeduplicationMode DeduplicationMode { get; }
    public ValueTypeDescriptor PayloadType { get; }
    public Type TriggerType => typeof(TTrigger);
}

/// <summary>Type-erased registration view consumed when projecting a suspension into durable state.</summary>
public interface IActivityTriggerRegistration
{
    string ResumeTargetKey { get; }
    string StimulusType { get; }
    string StimulusHash { get; }
    ActivityTriggerDeduplicationMode DeduplicationMode { get; }
    ValueTypeDescriptor PayloadType { get; }
    Type TriggerType { get; }
}

public enum ActivityTriggerDeduplicationMode
{
    Once,
    IdempotencyKey
}

internal static class PersistableActivityValue
{
    public static ValueTypeDescriptor Descriptor(Type type, int schemaVersion = 1) =>
        new(TypeAliasConvention.CanonicalAlias(type), schemaVersion: schemaVersion);

    public static T SnapshotAndMaterialize<T>(T value, string role)
    {
        var payload = Snapshot(value, role);

        try
        {
            return payload.Deserialize<T>()
                   ?? throw new ArgumentException($"The activity {role} must be a non-null persistable value.", nameof(value));
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new ArgumentException($"The activity {role} must be a persistable typed document.", nameof(value), exception);
        }
    }

    public static JsonElement Snapshot<T>(T value, string role)
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateType(typeof(T), role);

        try
        {
            return JsonSerializer.SerializeToElement(value, typeof(T));
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new ArgumentException($"The activity {role} must be a persistable typed document.", nameof(value), exception);
        }
    }

    public static void ValidateType(Type type, string role) => ValidateType(type, role, new HashSet<Type>());

    private static void ValidateType(Type type, string role, ISet<Type> visited)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (!visited.Add(type) || IsScalar(type))
            return;

        if (type == typeof(object) ||
            type.ContainsGenericParameters ||
            type.IsPointer ||
            type.IsByRef ||
            type.IsByRefLike ||
            typeof(Delegate).IsAssignableFrom(type) ||
            typeof(IServiceProvider).IsAssignableFrom(type) ||
            typeof(IActivityExecutionContext).IsAssignableFrom(type) ||
            typeof(IDictionary).IsAssignableFrom(type) ||
            ImplementsGenericDictionary(type))
        {
            throw new ArgumentException(
                $"The activity {role} type '{type}' is not a persistable typed document.",
                nameof(type));
        }

        if (type.IsArray)
        {
            ValidateType(type.GetElementType()!, role, visited);
            return;
        }

        foreach (var argument in type.GetGenericArguments())
            ValidateType(argument, role, visited);

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetMethod is not null && property.GetIndexParameters().Length == 0)
                ValidateType(property.PropertyType, role, visited);
        }

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            ValidateType(field.FieldType, role, visited);
    }

    private static bool ImplementsGenericDictionary(Type type) =>
        type.GetInterfaces().Any(candidate =>
            candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>));

    private static bool IsScalar(Type type) =>
        type.IsPrimitive ||
        type.IsEnum ||
        type == typeof(string) ||
        type == typeof(decimal) ||
        type == typeof(Guid) ||
        type == typeof(DateTime) ||
        type == typeof(DateTimeOffset) ||
        type == typeof(DateOnly) ||
        type == typeof(TimeOnly) ||
        type == typeof(TimeSpan) ||
        type == typeof(JsonElement);
}
