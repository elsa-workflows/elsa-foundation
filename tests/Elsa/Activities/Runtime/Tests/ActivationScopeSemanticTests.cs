using Elsa.Activities.Primitives.Activation;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Activation-scope semantics of the production CLR activation path, which ADR 0045 fixed as one fresh activity in one
/// owned child DI scope per invocation attempt. Moved from the retired activation benchmark (#1668, ADR 0073), whose
/// semantic tests asserted these invariants against its own strategy models; these run against
/// <see cref="ActivityActivator"/> over <see cref="ClrActivityActivator"/>. The benchmark's burst-only and conditional
/// fast-path candidates were rejected by ADR 0045 and never shipped, so their assertions are not carried over.
/// </summary>
public sealed class ActivationScopeSemanticTests : IAsyncDisposable
{
    private const string InvocationId = "invocation-1";

    private readonly DependencyLog _log = new();
    private readonly ServiceProvider _root;
    private readonly JsonPayloadSerializer _serializer = new(new JsonPayloadConverterRegistry());
    private readonly IActivityActivator _activator;

    public ActivationScopeSemanticTests()
    {
        _root = new ServiceCollection()
            .AddSingleton(_log)
            .AddTransient<TransientDependency>()
            .AddScoped<ScopedDisposableDependency>()
            .AddScoped<TransitiveScopedDependency>()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var registry = new WellKnownTypeRegistry();
        foreach (var activityType in new[] { typeof(TransientDependencyActivity), typeof(ScopedDisposableActivity), typeof(FailingActivity) })
            registry.RegisterType(activityType, activityType.FullName!);
        _activator = new ActivityActivator(
            [new ClrActivityActivator(_root.GetRequiredService<IServiceScopeFactory>(), registry, _serializer)],
            new ActivityInputHydrator());
    }

    public ValueTask DisposeAsync() => _root.DisposeAsync();

    [Theory]
    [InlineData(ActivityAttemptReason.Retry)]
    [InlineData(ActivityAttemptReason.Resume)]
    public async Task Every_attempt_of_one_invocation_gets_a_fresh_activity_and_transient_dependency(ActivityAttemptReason reason)
    {
        var first = await ActivateAndReleaseAsync<TransientDependencyActivity>("attempt-1", 1, ActivityAttemptReason.Initial);
        var second = await ActivateAndReleaseAsync<TransientDependencyActivity>("attempt-2", 2, reason);

        Assert.NotSame(first, second);
        Assert.NotSame(first.Dependency, second.Dependency);
    }

    [Fact]
    public async Task Every_attempt_owns_its_scoped_dependencies_until_its_own_lease_is_disposed()
    {
        await using var first = await ActivateAsync<ScopedDisposableActivity>("attempt-1", 1, ActivityAttemptReason.Initial);
        await using var second = await ActivateAsync<ScopedDisposableActivity>("attempt-2", 2, ActivityAttemptReason.Retry);
        var firstActivity = (ScopedDisposableActivity)first.Activity;
        var secondActivity = (ScopedDisposableActivity)second.Activity;

        Assert.NotSame(firstActivity.Dependency, secondActivity.Dependency);
        Assert.NotSame(firstActivity.Dependency.Transitive, secondActivity.Dependency.Transitive);
        Assert.All(firstActivity.OwnedDisposables, owned => Assert.False(owned.Disposed));

        await first.DisposeAsync();

        Assert.All(firstActivity.OwnedDisposables, owned => Assert.True(owned.Disposed));
        Assert.All(secondActivity.OwnedDisposables, owned => Assert.False(owned.Disposed));
    }

    [Fact]
    public async Task Constructor_failure_disposes_the_attempt_scope_and_every_dependency_it_created()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ActivateAsync<FailingActivity>("attempt-1", 1, ActivityAttemptReason.Initial));

        Assert.Equal(FailingActivity.FailureMessage, exception.Message);
        var dependency = Assert.Single(_log.ScopedDependencies);
        Assert.True(dependency.Disposed);
        Assert.True(dependency.Transitive.Disposed);
    }

    [Fact]
    public async Task Concurrent_attempts_never_share_scoped_identity()
    {
        var leases = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Task.Run(() =>
            ActivateAsync<ScopedDisposableActivity>($"attempt-{index}", 1, ActivityAttemptReason.Initial, $"invocation-{index}"))));
        try
        {
            var activities = leases.Select(lease => (ScopedDisposableActivity)lease.Activity).ToArray();

            Assert.Equal(leases.Length, activities.Select(activity => activity.Dependency).Distinct().Count());
            Assert.Equal(leases.Length, activities.Select(activity => activity.Dependency.Transitive).Distinct().Count());
        }
        finally
        {
            foreach (var lease in leases)
                await lease.DisposeAsync();
        }
    }

    private async Task<TActivity> ActivateAndReleaseAsync<TActivity>(string attemptId, int ordinal, ActivityAttemptReason reason)
    {
        await using var lease = await ActivateAsync<TActivity>(attemptId, ordinal, reason);
        return (TActivity)lease.Activity;
    }

    private async Task<ActivityActivationLease> ActivateAsync<TActivity>(
        string attemptId,
        int ordinal,
        ActivityAttemptReason reason,
        string invocationId = InvocationId)
    {
        var alias = typeof(TActivity).FullName!;
        var contract = new ActivityContract(
            alias,
            "1.0.0",
            typeof(ClrActivityDescriptor).FullName!,
            _serializer.SerializeToElement(new ClrActivityDescriptor(alias)),
            [],
            new ActivityResultContract(new ValueTypeDescriptor("Unit"), false, ActivityValuePolicy.Default, []),
            ["Done"],
            new ActivityActivationRequirement(typeof(ClrActivityDescriptor).FullName!, "constructor-injection"));
        return await _activator.ActivateAsync(new ActivityActivationRequest(
            contract,
            new ActivityInputSnapshot(invocationId, contract.SchemaFingerprint, "bindings", new Dictionary<string, ValueEnvelope>(), DateTimeOffset.UnixEpoch),
            new ActivityAttempt(attemptId, invocationId, ordinal, reason, DateTimeOffset.UnixEpoch),
            Descriptor: new RuntimeActivityDescriptor(
                WellKnownRuntimeActivityConsumers.ClrActivity,
                RuntimeActivityDescriptor.InitialSchemaVersion,
                contract.DescriptorPayload)));
    }

    private interface IOwnedDisposable
    {
        bool Disposed { get; }
    }

    private sealed class DependencyLog
    {
        private readonly List<ScopedDisposableDependency> _scopedDependencies = [];

        public IReadOnlyList<ScopedDisposableDependency> ScopedDependencies
        {
            get
            {
                lock (_scopedDependencies)
                    return _scopedDependencies.ToArray();
            }
        }

        public void Created(ScopedDisposableDependency dependency)
        {
            lock (_scopedDependencies)
                _scopedDependencies.Add(dependency);
        }
    }

    private sealed class TransientDependency;

    private sealed class TransitiveScopedDependency : IAsyncDisposable, IOwnedDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ScopedDisposableDependency : IDisposable, IOwnedDisposable
    {
        public ScopedDisposableDependency(TransitiveScopedDependency transitive, DependencyLog log)
        {
            Transitive = transitive;
            log.Created(this);
        }

        public TransitiveScopedDependency Transitive { get; }
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private sealed class TransientDependencyActivity(TransientDependency dependency) : Activity
    {
        public TransientDependency Dependency { get; } = dependency;

        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }

    private sealed class ScopedDisposableActivity(ScopedDisposableDependency dependency) : Activity, IAsyncDisposable, IOwnedDisposable
    {
        public ScopedDisposableDependency Dependency { get; } = dependency;
        public bool Disposed { get; private set; }
        public IOwnedDisposable[] OwnedDisposables => [this, Dependency, Dependency.Transitive];

        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingActivity : Activity
    {
        public const string FailureMessage = "activation-scope constructor failure";

        public FailingActivity(ScopedDisposableDependency dependency) => throw new InvalidOperationException(FailureMessage);

        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }
}
