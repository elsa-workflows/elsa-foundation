using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class StatefulValueSerializationRegressionTests
{
    [Fact]
    public void Suspension_serializes_state_once_and_materializes_the_frozen_payload()
    {
        var state = new StatefulGetterState(40);

        var transition = ActivityTransition<int, StatefulGetterState>.Suspend<string>(
            state,
            [new ActivityTriggerRegistration<string>("continue", "test", "snapshot:1")]);
        var suspension = Assert.IsAssignableFrom<IStatefulActivitySuspensionTransition<StatefulGetterState>>(transition);
        var frozenValue = suspension.StatePayload.GetProperty(nameof(StatefulGetterState.Value)).GetInt32();

        Assert.Equal(1, state.GetterReads);
        Assert.Equal(40, frozenValue);
        Assert.Equal(frozenValue, suspension.State.StoredValue);
    }

    [Fact]
    public async Task Untyped_resume_uses_deserialized_values_without_serializing_them_again()
    {
        var statePayload = JsonSerializer.SerializeToElement(new TrackedValue(17));
        var triggerPayload = JsonSerializer.SerializeToElement(new TrackedValue(23));
        TrackedValueJsonConverter.ResetCounts();

        var activity = new ResumeActivity();
        var request = new ActivityResumeRequest(
            Context("resume-attempt"),
            Descriptor<TrackedValue>(),
            statePayload,
            Descriptor<TrackedValue>(),
            triggerPayload,
            "registration-1",
            "delivery-1");

        var transition = await ((IStatefulActivity)activity).ResumeAsync(request);

        Assert.IsAssignableFrom<IActivityCompletionTransition<TrackedValue>>(transition);
        Assert.Equal(0, TrackedValueJsonConverter.Writes);
        Assert.Equal(2, TrackedValueJsonConverter.Reads);
        Assert.Equal(new TrackedValue(17), activity.ObservedContext!.State);
        Assert.Equal(new TrackedValue(23), activity.ObservedContext.Trigger);
    }

    [Fact]
    public async Task Typed_start_uses_deserialized_trigger_without_serializing_it_again()
    {
        var triggerPayload = JsonSerializer.SerializeToElement(new TrackedValue(31));
        TrackedValueJsonConverter.ResetCounts();
        var context = new ActivityExecutionContext(
            "workflow-1",
            "invocation-1",
            "start-attempt",
            "node-1",
            CancellationToken.None,
            triggerPayload,
            "node-1");
        var activity = new StartActivity();

        var transition = await ((IStatefulActivity<TrackedValue, TrackedValue, TrackedValue>)activity).ExecuteAsync(context);

        Assert.IsAssignableFrom<IActivityCompletionTransition<TrackedValue>>(transition);
        Assert.Equal(0, TrackedValueJsonConverter.Writes);
        Assert.Equal(1, TrackedValueJsonConverter.Reads);
        Assert.Equal(new TrackedValue(31), activity.ObservedTrigger);
    }

    [Fact]
    public void Public_resume_context_keeps_defensive_copies_of_state_and_trigger()
    {
        var state = new MutableResumeValue { Values = [1] };
        var trigger = new MutableResumeValue { Values = [2] };
        var context = new ActivityResumeContext<MutableResumeValue, MutableResumeValue>(
            Context("resume-attempt"),
            state,
            trigger,
            "registration-1",
            "delivery-1");

        state.Values.Add(3);
        trigger.Values.Add(4);

        Assert.NotSame(state, context.State);
        Assert.NotSame(trigger, context.Trigger);
        Assert.Equal([1], context.State.Values);
        Assert.Equal([2], context.Trigger.Values);
    }

    [Fact]
    public async Task Repeated_invalid_type_validation_keeps_the_current_value_role_in_diagnostics()
    {
        var firstStateError = await GetInvalidTypeError<RejectedValue, string>();
        var repeatedStateError = await GetInvalidTypeError<RejectedValue, string>();
        var triggerError = await GetInvalidTypeError<string, RejectedValue>();

        Assert.Equal(firstStateError.Message, repeatedStateError.Message);
        Assert.Contains("private state", firstStateError.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(Action).ToString(), firstStateError.Message, StringComparison.Ordinal);
        Assert.Equal(firstStateError.Message.Replace("private state", "trigger", StringComparison.Ordinal), triggerError.Message);
        Assert.Equal("type", firstStateError.ParamName);
        Assert.Equal("type", triggerError.ParamName);
    }

    private static ValueTypeDescriptor Descriptor<T>() =>
        new(TypeAliasConvention.CanonicalAlias(typeof(T)), schemaVersion: 1);

    private static ActivityExecutionContext Context(string attemptId) =>
        new("workflow-1", "invocation-1", attemptId, "node-1", CancellationToken.None);

    private static async Task<ArgumentException> GetInvalidTypeError<TState, TTrigger>() =>
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await ((IStatefulActivity<int, TState, TTrigger>)new InvalidActivity<TState, TTrigger>())
                .ExecuteAsync(Context("invalid-attempt")));

    private sealed class StatefulGetterState
    {
        private int _storedValue;
        private int _getterReads;

        public StatefulGetterState() { }

        public StatefulGetterState(int storedValue) => _storedValue = storedValue;

        public int Value
        {
            get => _storedValue + _getterReads++;
            set
            {
                _storedValue = value;
                _getterReads = 0;
            }
        }

        [JsonIgnore]
        public int GetterReads => _getterReads;

        [JsonIgnore]
        public int StoredValue => _storedValue;
    }

    private sealed class ResumeActivity : StatefulActivity<TrackedValue, TrackedValue, TrackedValue>
    {
        public ActivityResumeContext<TrackedValue, TrackedValue>? ObservedContext { get; private set; }

        protected override ValueTask<ActivityTransition<TrackedValue, TrackedValue>> ExecuteAsync(ActivityExecutionContext context) =>
            throw new NotSupportedException();

        protected override ValueTask<ActivityTransition<TrackedValue, TrackedValue>> ResumeAsync(
            ActivityResumeContext<TrackedValue, TrackedValue> context)
        {
            ObservedContext = context;
            return ValueTask.FromResult(Complete(context.Trigger));
        }
    }

    [JsonConverter(typeof(TrackedValueJsonConverter))]
    public sealed record TrackedValue(int Value);

    public sealed class TrackedValueJsonConverter : JsonConverter<TrackedValue>
    {
        private static int _reads;
        private static int _writes;

        public static int Reads => Volatile.Read(ref _reads);
        public static int Writes => Volatile.Read(ref _writes);

        public static void ResetCounts()
        {
            Volatile.Write(ref _reads, 0);
            Volatile.Write(ref _writes, 0);
        }

        public override TrackedValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Interlocked.Increment(ref _reads);
            return new TrackedValue(reader.GetInt32());
        }

        public override void Write(Utf8JsonWriter writer, TrackedValue value, JsonSerializerOptions options)
        {
            Interlocked.Increment(ref _writes);
            writer.WriteNumberValue(value.Value);
        }
    }

    private sealed class StartActivity : StatefulTriggerActivity<TrackedValue, TrackedValue, TrackedValue>
    {
        public TrackedValue? ObservedTrigger { get; private set; }

        protected override ValueTask<ActivityTransition<TrackedValue, TrackedValue>> ExecuteAsync(ActivityStartContext<TrackedValue> context)
        {
            if (!context.TryGetTrigger(out var trigger))
                throw new InvalidOperationException("The targeted start trigger was not available.");

            ObservedTrigger = trigger;
            return ValueTask.FromResult(Complete(trigger));
        }

        protected override ValueTask<ActivityTransition<TrackedValue, TrackedValue>> ResumeAsync(
            ActivityResumeContext<TrackedValue, TrackedValue> context) =>
            throw new NotSupportedException();
    }

    private sealed class MutableResumeValue
    {
        public List<int> Values { get; set; } = [];
    }

    private sealed record RejectedValue(Action Callback);

    private sealed class InvalidActivity<TState, TTrigger> : StatefulActivity<int, TState, TTrigger>
    {
        protected override ValueTask<ActivityTransition<int, TState>> ExecuteAsync(ActivityExecutionContext context) =>
            throw new NotSupportedException();

        protected override ValueTask<ActivityTransition<int, TState>> ResumeAsync(
            ActivityResumeContext<TState, TTrigger> context) =>
            throw new NotSupportedException();
    }
}
