using System.Text.Json;
using Elsa.Activities.Primitives.Activation;
using Elsa.Activities.Primitives.Binding;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class ClrActivityActivatorTests
{
    private static readonly ValueTypeDescriptor StringType = new("String");

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
            new ActivityAttempt("attempt-1", "invocation-1", 1, ActivityAttemptReason.Initial, DateTimeOffset.UtcNow));

        await Assert.ThrowsAsync<InvalidOperationException>(() => activator.ActivateAsync(request).AsTask());
        Assert.Equal(1, ScopedDependency.DisposeCount);
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

    private static IServiceCollection Services() =>
        new ServiceCollection().AddScoped<ScopedDependency>();

    private static (ClrActivityActivator Activator, ActivityContract Contract) Activator(IServiceProvider services)
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
        return (new ClrActivityActivator(
            services.GetRequiredService<IServiceScopeFactory>(),
            registry,
            serializer,
            new ActivityInputHydrator()), contract);
    }

    private static ActivityActivationRequest Request(ActivityContract contract, string attemptId, string message) =>
        new(
            contract,
            Snapshot(contract, message),
            new ActivityAttempt(attemptId, "invocation-1", attemptId == "attempt-1" ? 1 : 2, ActivityAttemptReason.Initial, DateTimeOffset.UtcNow));

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

    private sealed class ServiceBearingActivity(ScopedDependency dependency) : ActivityBase
    {
        public ScopedDependency Dependency { get; } = dependency;

        [ActivityInput(Key = "message")]
        public string Message { get; set; } = null!;
    }

    private sealed class ScopedDependency : IDisposable
    {
        private static int _disposeCount;
        public static int DisposeCount => _disposeCount;
        public static void Reset() => _disposeCount = 0;
        public void Dispose() => Interlocked.Increment(ref _disposeCount);
    }
}
