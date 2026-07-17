using System.Text.Json;
using Elsa.Activities.Primitives.Activation;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class ClrActivityActivatorTests
{
    private static readonly ValueTypeDescriptor StringType = new("String");
    private static readonly ValueTypeDescriptor Int32Type = new("Int32");

    [Fact]
    public async Task Each_attempt_gets_a_fresh_hydrated_activity_and_scoped_service()
    {
        ScopedDependency.Reset();
        await using var root = Services().BuildServiceProvider();
        var (activator, contract) = Activator(root);

        await using var first = await activator.ActivateAsync(Request(contract, "attempt-1", "hello"));
        await using var second = await activator.ActivateAsync(Request(contract, "attempt-2", "hello"));
        var firstActivity = Assert.IsType<ServiceBearingActivity>(first.Activity);
        var secondActivity = Assert.IsType<ServiceBearingActivity>(second.Activity);

        Assert.NotSame(firstActivity, secondActivity);
        Assert.NotSame(firstActivity.Dependency, secondActivity.Dependency);
        Assert.Equal("hello", firstActivity.Message);
        Assert.Equal("hello", secondActivity.Message);

        await first.DisposeAsync();
        await second.DisposeAsync();
        Assert.Equal(2, ScopedDependency.DisposeCount);
    }

    [Fact]
    public async Task Hydration_failure_disposes_the_attempt_scope()
    {
        ScopedDependency.Reset();
        await using var root = Services().BuildServiceProvider();
        var (activator, contract) = Activator(root);
        var snapshot = new ActivityInputSnapshot(
            "invocation-1",
            contract.SchemaFingerprint,
            "bindings",
            new Dictionary<string, ValueEnvelope>(),
            DateTimeOffset.UtcNow);
        var request = new ActivityActivationRequest(
            contract,
            snapshot,
            new ActivityAttempt("attempt-1", "invocation-1", 1, ActivityAttemptReason.Initial, DateTimeOffset.UtcNow),
            Descriptor: RuntimeDescriptor(contract));

        await Assert.ThrowsAsync<InvalidOperationException>(() => activator.ActivateAsync(request).AsTask());
        Assert.Equal(1, ScopedDependency.DisposeCount);
    }

    [Fact]
    public async Task ActivationLease_WhenActivityAndScopeDisposalFail_AttemptsBothAndAggregatesFailures()
    {
        var activity = new ThrowingDisposableActivity();
        var scope = new ThrowingAsyncDisposableScope();
        var lease = new ActivityActivationLease(activity, scope);

        var exception = await Assert.ThrowsAsync<AggregateException>(() => lease.DisposeAsync().AsTask());

        Assert.True(activity.DisposeAttempted);
        Assert.True(scope.DisposeAttempted);
        Assert.Collection(
            exception.InnerExceptions,
            inner => Assert.Equal("Activity disposal failed.", inner.Message),
            inner => Assert.Equal("Scope disposal failed.", inner.Message));
    }

    [Fact]
    public void Hydrator_rejects_a_second_write_to_the_same_activity_instance()
    {
        var dependency = new ScopedDependency();
        var activity = new ServiceBearingActivity(dependency);
        var (_, contract) = Activator(Services().BuildServiceProvider());
        var snapshot = Snapshot(contract, "hello");
        var hydrator = new ActivityInputHydrator();

        hydrator.Hydrate(activity, contract, snapshot);

        Assert.Throws<InvalidOperationException>(() => hydrator.Hydrate(activity, contract, snapshot));
        dependency.Dispose();
    }

    [Fact]
    public void Hydrator_replays_pinned_optional_absence_across_changed_property_initializers()
    {
        var contract = new ActivityContract(
            "test/optional-initializer",
            "1.0.0",
            "test",
            JsonSerializer.SerializeToElement(new { }),
            [new ActivityInputContract("message", "Message", StringType, false, false, null, ActivityValuePolicy.Default)],
            new ActivityResultContract(new ValueTypeDescriptor("Unit"), false, ActivityValuePolicy.Default, []),
            ["Done"],
            new ActivityActivationRequirement("test", "test/optional-initializer"));
        var snapshot = new ActivityInputSnapshot(
            "invocation-1",
            contract.SchemaFingerprint,
            "bindings",
            new Dictionary<string, ValueEnvelope>
            {
                ["message"] = ValueEnvelope.Absent(StringType, ValueProtectionPolicy.InstanceInline)
            },
            DateTimeOffset.UtcNow);

        OptionalInitializerActivity.Initializer = "version-one-initializer";
        var firstAttempt = new OptionalInitializerActivity();
        OptionalInitializerActivity.Initializer = "version-two-initializer";
        var retriedAfterDeployment = new OptionalInitializerActivity();
        var hydrator = new ActivityInputHydrator();
        hydrator.Hydrate(firstAttempt, contract, snapshot);
        hydrator.Hydrate(retriedAfterDeployment, contract, snapshot);

        Assert.Null(firstAttempt.Message);
        Assert.Null(retriedAfterDeployment.Message);
    }

    [Fact]
    public void Hydrator_accepts_explicit_null_for_a_required_nullable_reference_input()
    {
        var contract = new ActivityContract(
            "test/required-nullable",
            "1.0.0",
            "test",
            JsonSerializer.SerializeToElement(new { }),
            [new ActivityInputContract("message", "Message", StringType, true, false, null, ActivityValuePolicy.Default) { IsNullable = true }],
            new ActivityResultContract(new ValueTypeDescriptor("Unit"), false, ActivityValuePolicy.Default, []),
            ["Done"],
            new ActivityActivationRequirement("test", "test/required-nullable"));
        var snapshot = new ActivityInputSnapshot(
            "invocation-1",
            contract.SchemaFingerprint,
            "bindings",
            new Dictionary<string, ValueEnvelope>
            {
                ["message"] = ValueEnvelope.Null(StringType, ValueProtectionPolicy.InstanceInline)
            },
            DateTimeOffset.UtcNow);
        var activity = new RequiredNullableActivity();

        new ActivityInputHydrator().Hydrate(activity, contract, snapshot);

        Assert.Null(activity.Message);
    }

    [Fact]
    public void Hydrator_honors_pinned_non_nullable_contract_when_the_current_clr_property_is_nullable()
    {
        var contract = new ActivityContract(
            "test/pinned-non-nullable",
            "1.0.0",
            "test",
            JsonSerializer.SerializeToElement(new { }),
            [new ActivityInputContract("message", "Message", StringType, false, false, null, ActivityValuePolicy.Default) { IsNullable = false }],
            new ActivityResultContract(new ValueTypeDescriptor("Unit"), false, ActivityValuePolicy.Default, []),
            ["Done"],
            new ActivityActivationRequirement("test", "test/pinned-non-nullable"));
        var snapshot = new ActivityInputSnapshot(
            "invocation-1",
            contract.SchemaFingerprint,
            "bindings",
            new Dictionary<string, ValueEnvelope>
            {
                ["message"] = ValueEnvelope.Null(StringType, ValueProtectionPolicy.InstanceInline)
            },
            DateTimeOffset.UtcNow);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ActivityInputHydrator().Hydrate(new RequiredNullableActivity(), contract, snapshot));

        Assert.Contains("does not accept null", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Hydrator_uses_inherited_input_key_to_assign_hidden_derived_property()
    {
        var contract = new ActivityContract(
            "test/hidden-input",
            "1.0.0",
            "test",
            JsonSerializer.SerializeToElement(new { }),
            [new ActivityInputContract("inheritedKey", "Value", StringType, false, false, null, ActivityValuePolicy.Default) { IsNullable = false }],
            new ActivityResultContract(new ValueTypeDescriptor("Unit"), false, ActivityValuePolicy.Default, []),
            ["Done"],
            new ActivityActivationRequirement("test", "test/hidden-input"));
        var snapshot = new ActivityInputSnapshot(
            "invocation-1",
            contract.SchemaFingerprint,
            "bindings",
            new Dictionary<string, ValueEnvelope>
            {
                ["inheritedKey"] = ValueEnvelope.Inline(
                    StringType,
                    JsonSerializer.SerializeToElement("hydrated"),
                    ValueProtectionPolicy.InstanceInline)
            },
            DateTimeOffset.UtcNow);
        var activity = new HiddenInputActivity();

        new ActivityInputHydrator().Hydrate(activity, contract, snapshot);

        Assert.Equal("hydrated", activity.Value);
        Assert.Null(((HiddenInputActivityBase)activity).Value);
    }

    [Fact]
    public void Hydrator_rejects_absence_for_an_optional_non_nullable_value_input()
    {
        var contract = new ActivityContract(
            "test/optional-int32",
            "1.0.0",
            "test",
            JsonSerializer.SerializeToElement(new { }),
            [new ActivityInputContract("value", "Value", Int32Type, false, false, null, ActivityValuePolicy.Default)],
            new ActivityResultContract(new ValueTypeDescriptor("Unit"), false, ActivityValuePolicy.Default, []),
            ["Done"],
            new ActivityActivationRequirement("test", "test/optional-int32"));
        var snapshot = new ActivityInputSnapshot(
            "invocation-1",
            contract.SchemaFingerprint,
            "bindings",
            new Dictionary<string, ValueEnvelope>
            {
                ["value"] = ValueEnvelope.Absent(Int32Type, ValueProtectionPolicy.InstanceInline)
            },
            DateTimeOffset.UtcNow);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ActivityInputHydrator().Hydrate(new OptionalInt32Activity(), contract, snapshot));

        Assert.Contains("does not accept absence", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Hydrator_rejects_absence_and_explicit_null_for_an_optional_non_nullable_reference_input(bool absent)
    {
        var contract = new ActivityContract(
            "test/optional-non-nullable-reference",
            "1.0.0",
            "test",
            JsonSerializer.SerializeToElement(new { }),
            [new ActivityInputContract("message", "Message", StringType, false, false, null, ActivityValuePolicy.Default)],
            new ActivityResultContract(new ValueTypeDescriptor("Unit"), false, ActivityValuePolicy.Default, []),
            ["Done"],
            new ActivityActivationRequirement("test", "test/optional-non-nullable-reference"));
        var envelope = absent
            ? ValueEnvelope.Absent(StringType, ValueProtectionPolicy.InstanceInline)
            : ValueEnvelope.Null(StringType, ValueProtectionPolicy.InstanceInline);
        var snapshot = new ActivityInputSnapshot(
            "invocation-1",
            contract.SchemaFingerprint,
            "bindings",
            new Dictionary<string, ValueEnvelope> { ["message"] = envelope },
            DateTimeOffset.UtcNow);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ActivityInputHydrator().Hydrate(new OptionalNonNullableReferenceActivity(), contract, snapshot));

        Assert.Contains(absent ? "does not accept absence" : "does not accept null", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task External_input_is_dereferenced_only_for_activation()
    {
        await using var root = Services().BuildServiceProvider();
        var store = new FixedExternalPayloadStore("from-external-store");
        var (activator, contract) = Activator(root, store);
        var policy = new ValueProtectionPolicy(
            DurableValueLifecycle.Instance,
            DurableValueStorage.External,
            isSensitive: true,
            requiresEncryption: true,
            redactionMode: "Full",
            retentionPolicy: "P30D");
        var persistedSnapshot = new ActivityInputSnapshot(
            "invocation-1",
            contract.SchemaFingerprint,
            "bindings",
            new Dictionary<string, ValueEnvelope>
            {
                ["message"] = ValueEnvelope.External(
                    StringType,
                    new DurableValueExternalReference("encrypted", "payloads/message", new Dictionary<string, string>()),
                    policy)
            },
            DateTimeOffset.UtcNow);

        await using var lease = await activator.ActivateAsync(new ActivityActivationRequest(
            contract,
            persistedSnapshot,
            new ActivityAttempt("attempt-1", "invocation-1", 1, ActivityAttemptReason.Initial, DateTimeOffset.UtcNow),
            Descriptor: RuntimeDescriptor(contract)));

        Assert.Equal("from-external-store", Assert.IsType<ServiceBearingActivity>(lease.Activity).Message);
        Assert.NotNull(persistedSnapshot.Values["message"].ExternalReference);
        Assert.Null(persistedSnapshot.Values["message"].InlineValue);
        Assert.Equal("payloads/message", Assert.Single(store.Reads).Locator);
    }

    private static IServiceCollection Services() =>
        new ServiceCollection().AddScoped<ScopedDependency>();

    private static (IActivityActivator Activator, ActivityContract Contract) Activator(
        IServiceProvider services,
        IExternalPayloadStore? externalPayloadStore = null)
    {
        var registry = new WellKnownTypeRegistry();
        var alias = typeof(ServiceBearingActivity).FullName!;
        registry.RegisterType(typeof(ServiceBearingActivity), alias);
        var serializer = new JsonPayloadSerializer(new JsonPayloadConverterRegistry());
        var descriptor = serializer.SerializeToElement(new ClrActivityDescriptor(alias));
        var contract = new ActivityContract(
            alias,
            "1.0.0",
            typeof(ClrActivityDescriptor).FullName!,
            descriptor,
            [new ActivityInputContract("message", "Message", StringType, true, false, null, ActivityValuePolicy.Default)],
            new ActivityResultContract(new ValueTypeDescriptor("Unit"), false, ActivityValuePolicy.Default, []),
            ["Done"],
            new ActivityActivationRequirement(typeof(ClrActivityDescriptor).FullName!, "constructor-injection"));
        var strategy = new ClrActivityActivator(
            services.GetRequiredService<IServiceScopeFactory>(),
            registry,
            serializer);
        return (new ActivityActivator(
            [strategy],
            new ActivityInputHydrator(),
            externalPayloadStore), contract);
    }

    private static ActivityActivationRequest Request(ActivityContract contract, string attemptId, string message) =>
        new(
            contract,
            Snapshot(contract, message),
            new ActivityAttempt(attemptId, "invocation-1", attemptId == "attempt-1" ? 1 : 2, ActivityAttemptReason.Initial, DateTimeOffset.UtcNow),
            Descriptor: RuntimeDescriptor(contract));

    private static RuntimeActivityDescriptor RuntimeDescriptor(ActivityContract contract) =>
        new(
            WellKnownRuntimeActivityConsumers.ClrActivity,
            RuntimeActivityDescriptor.InitialSchemaVersion,
            contract.DescriptorPayload);

    private static ActivityInputSnapshot Snapshot(ActivityContract contract, string message) =>
        new(
            "invocation-1",
            contract.SchemaFingerprint,
            "bindings",
            new Dictionary<string, ValueEnvelope>
            {
                ["message"] = ValueEnvelope.Inline(
                    StringType,
                    JsonSerializer.SerializeToElement(message),
                    ValueProtectionPolicy.InstanceInline)
            },
            DateTimeOffset.UtcNow);

    private sealed class ServiceBearingActivity(ScopedDependency dependency) : Activity
    {
        public ScopedDependency Dependency { get; } = dependency;

        [ActivityInput(Key = "message")]
        public string Message { get; set; } = null!;

        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }

    private sealed class OptionalInitializerActivity : Activity
    {
        public static string Initializer { get; set; } = "initializer";

        [ActivityInput(Key = "message")]
        public string? Message { get; set; } = Initializer;

        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }

    private sealed class RequiredNullableActivity : Activity
    {
        [ActivityInput(Key = "message")]
        public string? Message { get; set; } = "initializer";

        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }

    private sealed class OptionalInt32Activity : Activity
    {
        [ActivityInput(Key = "value")]
        public int Value { get; set; } = 42;

        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }

    private sealed class OptionalNonNullableReferenceActivity : Activity
    {
        [ActivityInput(Key = "message")]
        public string Message { get; set; } = "initializer";

        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }

    private abstract class HiddenInputActivityBase : Activity
    {
        [ActivityInput(Key = "inheritedKey")]
        public string? Value { get; set; }

        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }

    private sealed class HiddenInputActivity : HiddenInputActivityBase
    {
        public new string Value { get; set; } = string.Empty;
    }

    private sealed class ThrowingDisposableActivity : Activity, IDisposable
    {
        public bool DisposeAttempted { get; private set; }

        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));

        public void Dispose()
        {
            DisposeAttempted = true;
            throw new InvalidOperationException("Activity disposal failed.");
        }
    }

    private sealed class ThrowingAsyncDisposableScope : IAsyncDisposable
    {
        public bool DisposeAttempted { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeAttempted = true;
            return ValueTask.FromException(new InvalidOperationException("Scope disposal failed."));
        }
    }

    private sealed class ScopedDependency : IDisposable
    {
        private static int _disposeCount;
        public static int DisposeCount => _disposeCount;
        public static void Reset() => _disposeCount = 0;
        public void Dispose() => Interlocked.Increment(ref _disposeCount);
    }

    private sealed class FixedExternalPayloadStore(string value) : IExternalPayloadStore
    {
        public List<DurableValueExternalReference> Reads { get; } = [];

        public ValueTask<DurableValueExternalReference> WriteAsync(
            ExternalPayloadWriteRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<JsonElement> ReadAsync(
            DurableValueExternalReference reference,
            CancellationToken cancellationToken = default)
        {
            Reads.Add(reference);
            return ValueTask.FromResult(JsonSerializer.SerializeToElement(value));
        }
    }
}
