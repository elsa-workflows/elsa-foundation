using System.Diagnostics;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Benchmarks;

[MemoryDiagnoser]
[AllStatisticsColumn]
public class ActivationScopeBenchmarks
{
    private ActivationBenchmarkHarness _harness = null!;

    [ParamsAllValues]
    public ActivationStrategyKind Strategy { get; set; }

    [GlobalSetup]
    public void Setup() => _harness = new ActivationBenchmarkHarness();

    [GlobalCleanup]
    public void Cleanup() => _harness.Dispose();

    [Benchmark(Baseline = true)]
    public async Task<long> NoOp() => (await _harness.RunNoOpAsync(Strategy, 32)).Score;

    [Benchmark]
    public async Task<long> TransientDependency() => (await _harness.RunTransientAsync(Strategy, 32)).Score;

    [Benchmark]
    public async Task<long> ScopedDisposableTransitive() => (await _harness.RunScopedAsync(Strategy, 32)).Score;

    [Benchmark]
    public async Task<long> IntrinsicHeavy() => (await _harness.RunIntrinsicHeavyAsync(256)).Score;

    [Benchmark]
    public async Task<long> MixedIntrinsicAndActivities() => (await _harness.RunMixedAsync(Strategy, 128)).Score;

    [Benchmark]
    public async Task<long> IoBound() => (await _harness.RunIoAsync(Strategy, 4)).Score;

    [Benchmark]
    public async Task<long> Retry() => (await _harness.RunRetryAsync(Strategy)).Score;

    [Benchmark]
    public async Task<long> ResumeInFreshBurst() => (await _harness.RunResumeAsync(Strategy)).Score;

    [Benchmark]
    public async Task<long> ConcurrentDrain() => (await _harness.RunConcurrentDrainAsync(Strategy, 16)).Score;
}

internal sealed class ActivationBenchmarkHarness : IDisposable
{
    private static readonly ValueTypeDescriptor UnitType = new("Unit");
    private readonly ServiceProvider _rootServices;
    private readonly BenchmarkActivityCatalog _catalog;
    private readonly IReadOnlyDictionary<Type, ActivityContract> _contracts;
    private readonly ActivationScopeCounters _counters = new();

    public ActivationBenchmarkHarness()
    {
        _rootServices = new ServiceCollection()
            .AddSingleton(_counters)
            .AddTransient<TransientDependency>()
            .AddTransient<IoDependency>()
            .AddScoped<ScopedDisposableDependency>()
            .AddScoped<TransitiveScopedDependency>()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var registrations = new[]
        {
            Registration<NoOpActivity>(allowsConditionalFastPath: true),
            Registration<TransientDependencyActivity>(),
            Registration<ScopedDisposableActivity>(),
            Registration<IoActivity>(),
            Registration<FailingActivity>()
        };
        _catalog = new BenchmarkActivityCatalog(registrations);
        _contracts = registrations.ToDictionary(x => x.ActivityType, CreateContract);
    }

    public async Task<ActivationMeasurement> RunNoOpAsync(ActivationStrategyKind strategy, int attempts)
    {
        _counters.Reset();
        await RunBurstAsync(strategy, "noop", async activator =>
        {
            for (var i = 0; i < attempts; i++)
                await ActivateAndRunAsync<NoOpActivity>(activator, $"noop-{i}", i + 1, ActivityAttemptReason.Initial);
        });
        return _counters.Snapshot();
    }

    public async Task<ActivationMeasurement> RunTransientAsync(ActivationStrategyKind strategy, int attempts)
    {
        _counters.Reset();
        await RunBurstAsync(strategy, "transient", async activator =>
        {
            for (var i = 0; i < attempts; i++)
                await ActivateAndRunAsync<TransientDependencyActivity>(activator, $"transient-{i}", i + 1, ActivityAttemptReason.Initial);
        });
        return _counters.Snapshot();
    }

    public async Task<ActivationMeasurement> RunScopedAsync(ActivationStrategyKind strategy, int attempts)
    {
        _counters.Reset();
        await RunBurstAsync(strategy, "scoped", async activator =>
        {
            for (var i = 0; i < attempts; i++)
                await ActivateAndRunAsync<ScopedDisposableActivity>(activator, $"scoped-{i}", i + 1, ActivityAttemptReason.Initial);
        });
        return _counters.Snapshot();
    }

    public Task<ActivationMeasurement> RunIntrinsicHeavyAsync(int operations)
    {
        _counters.Reset();
        for (var i = 0; i < operations; i++)
            _counters.RecordIntrinsicOperation();
        return Task.FromResult(_counters.Snapshot());
    }

    public async Task<ActivationMeasurement> RunMixedAsync(ActivationStrategyKind strategy, int operations)
    {
        _counters.Reset();
        await RunBurstAsync(strategy, "mixed", async activator =>
        {
            var attempt = 0;
            for (var i = 0; i < operations; i++)
            {
                if (i % 8 == 0)
                {
                    attempt++;
                    await ActivateAndRunAsync<NoOpActivity>(activator, $"mixed-{attempt}", attempt, ActivityAttemptReason.Initial);
                }
                else
                    _counters.RecordIntrinsicOperation();
            }
        });
        return _counters.Snapshot();
    }

    public async Task<ActivationMeasurement> RunIoAsync(ActivationStrategyKind strategy, int attempts)
    {
        _counters.Reset();
        await RunBurstAsync(strategy, "io", async activator =>
        {
            for (var i = 0; i < attempts; i++)
                await ActivateAndRunAsync<IoActivity>(activator, $"io-{i}", i + 1, ActivityAttemptReason.Initial);
        });
        return _counters.Snapshot();
    }

    public async Task<ActivationMeasurement> RunRetryAsync(ActivationStrategyKind strategy)
    {
        _counters.Reset();
        await RunBurstAsync(strategy, "retry", async activator =>
        {
            await ActivateAndRunAsync<TransientDependencyActivity>(
                activator, "retry-initial", 1, ActivityAttemptReason.Initial, "retry-invocation");
            await ActivateAndRunAsync<TransientDependencyActivity>(
                activator, "retry-second", 2, ActivityAttemptReason.Retry, "retry-invocation");
        });
        return _counters.Snapshot();
    }

    public async Task<ActivationMeasurement> RunResumeAsync(ActivationStrategyKind strategy)
    {
        _counters.Reset();
        await RunBurstAsync(strategy, "resume-initial", activator =>
            ActivateAndRunAsync<ScopedDisposableActivity>(activator, "resume-initial", 1, ActivityAttemptReason.Initial));
        await RunBurstAsync(strategy, "resume-fresh-burst", activator =>
            ActivateAndRunAsync<ScopedDisposableActivity>(activator, "resume-second", 2, ActivityAttemptReason.Resume));
        return _counters.Snapshot();
    }

    public async Task<ActivationMeasurement> RunConcurrentDrainAsync(ActivationStrategyKind strategy, int attemptsPerWorkflow)
    {
        _counters.Reset();
        await Task.WhenAll(
            RunScopedWorkflowAsync(strategy, "workflow-a", attemptsPerWorkflow),
            RunScopedWorkflowAsync(strategy, "workflow-b", attemptsPerWorkflow));
        return _counters.Snapshot();
    }

    public async Task<ActivationMeasurement> RunActivationFailureAsync(ActivationStrategyKind strategy)
    {
        _counters.Reset();
        await RunBurstAsync(strategy, "activation-failure", async activator =>
        {
            try
            {
                await using var activation = await activator.ActivateAsync(
                    CreateRequest<FailingActivity>("activation-failure", 1, ActivityAttemptReason.Initial));
            }
            catch (Exception exception) when (exception.ToString().Contains("benchmark activation failure", StringComparison.Ordinal))
            {
                return;
            }

            throw new InvalidOperationException("The activation-failure workload unexpectedly activated its fixture.");
        });
        return _counters.Snapshot();
    }

    private async Task RunScopedWorkflowAsync(ActivationStrategyKind strategy, string workflow, int attempts)
    {
        await RunBurstAsync(strategy, workflow, async activator =>
        {
            for (var i = 0; i < attempts; i++)
                await ActivateAndRunAsync<ScopedDisposableActivity>(activator, $"{workflow}-{i}", i + 1, ActivityAttemptReason.Initial);
        });
    }

    private async Task RunBurstAsync(
        ActivationStrategyKind strategy,
        string burstId,
        Func<IActivityActivator, Task> execute)
    {
        await using var burstScope = _rootServices.CreateAsyncScope();
        var activator = ActivationStrategyFactory.Create(strategy, burstScope.ServiceProvider, _catalog, _counters);
        await execute(activator);
        GC.KeepAlive(burstId);
    }

    private async Task ActivateAndRunAsync<TActivity>(
        IActivityActivator activator,
        string attemptId,
        int ordinal,
        ActivityAttemptReason reason,
        string? invocationId = null)
        where TActivity : IBenchmarkActivity
    {
        await using var activation = await activator.ActivateAsync(
            CreateRequest<TActivity>(attemptId, ordinal, reason, invocationId));
        await ((IBenchmarkActivity)activation.Activity).ExecuteBenchmarkAsync(attemptId, _counters);
    }

    private ActivityActivationRequest CreateRequest<TActivity>(
        string attemptId,
        int ordinal,
        ActivityAttemptReason reason,
        string? invocationId = null)
        where TActivity : IBenchmarkActivity
    {
        var contract = _contracts[typeof(TActivity)];
        invocationId ??= attemptId.StartsWith("resume-", StringComparison.Ordinal) ? "resume" : attemptId;
        var snapshot = new ActivityInputSnapshot(
            invocationId,
            contract.SchemaFingerprint,
            "benchmark-bindings",
            new Dictionary<string, ValueEnvelope>(),
            DateTimeOffset.UnixEpoch);
        var attempt = new ActivityAttempt(
            attemptId,
            invocationId,
            ordinal,
            reason,
            DateTimeOffset.UnixEpoch);
        return new ActivityActivationRequest(contract, snapshot, attempt);
    }

    private static BenchmarkActivityRegistration Registration<TActivity>(bool allowsConditionalFastPath = false) =>
        new(typeof(TActivity).FullName!, typeof(TActivity), allowsConditionalFastPath);

    private static ActivityContract CreateContract(BenchmarkActivityRegistration registration) =>
        new(
            registration.ActivityTypeKey,
            "1.0.0",
            "benchmark-clr",
            JsonSerializer.SerializeToElement(new { type = registration.ActivityTypeKey }),
            [],
            new ActivityResultContract(UnitType, true, ActivityValuePolicy.Default, []),
            ["Done"],
            new ActivityActivationRequirement("benchmark-clr", registration.ActivityTypeKey));

    public void Dispose() => _rootServices.Dispose();
}

internal interface IBenchmarkActivity
{
    Guid InstanceId { get; }
    ValueTask ExecuteBenchmarkAsync(string attemptId, ActivationScopeCounters counters);
}

internal abstract class BenchmarkActivity : Activity<ActivityUnit>, IBenchmarkActivity
{
    public Guid InstanceId { get; } = Guid.NewGuid();

    public abstract ValueTask ExecuteBenchmarkAsync(string attemptId, ActivationScopeCounters counters);

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
}

internal sealed class NoOpActivity : BenchmarkActivity
{
    public override ValueTask ExecuteBenchmarkAsync(string attemptId, ActivationScopeCounters counters) => ValueTask.CompletedTask;
}

internal sealed class TransientDependencyActivity(TransientDependency dependency) : BenchmarkActivity
{
    public override ValueTask ExecuteBenchmarkAsync(string attemptId, ActivationScopeCounters counters)
    {
        counters.RecordTransientService(attemptId, dependency.Identity);
        return ValueTask.CompletedTask;
    }
}

internal sealed class ScopedDisposableActivity(ScopedDisposableDependency dependency) : BenchmarkActivity, IAsyncDisposable
{
    public override ValueTask ExecuteBenchmarkAsync(string attemptId, ActivationScopeCounters counters)
    {
        counters.RecordScopedService(attemptId, dependency.Identity);
        counters.RecordTransitiveScopedService(attemptId, dependency.TransitiveIdentity);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        dependency.Counters.RecordAsyncDisposal();
        return ValueTask.CompletedTask;
    }
}

internal sealed class IoActivity(IoDependency dependency) : BenchmarkActivity
{
    public override async ValueTask ExecuteBenchmarkAsync(string attemptId, ActivationScopeCounters counters) =>
        await dependency.WaitAsync();
}

internal sealed class FailingActivity : BenchmarkActivity
{
    public FailingActivity(ScopedDisposableDependency dependency) =>
        throw new InvalidOperationException("benchmark activation failure");

    public override ValueTask ExecuteBenchmarkAsync(string attemptId, ActivationScopeCounters counters) =>
        throw new UnreachableException();
}

internal sealed class TransientDependency
{
    public Guid Identity { get; } = Guid.NewGuid();
}

internal sealed class ScopedDisposableDependency(TransitiveScopedDependency transitive) : IDisposable
{
    public Guid Identity { get; } = Guid.NewGuid();
    public Guid TransitiveIdentity => transitive.Identity;
    public ActivationScopeCounters Counters => transitive.Counters;

    public void Dispose() => Counters.RecordSyncDisposal();
}

internal sealed class TransitiveScopedDependency(ActivationScopeCounters counters) : IAsyncDisposable
{
    public Guid Identity { get; } = Guid.NewGuid();
    public ActivationScopeCounters Counters { get; } = counters;

    public ValueTask DisposeAsync()
    {
        Counters.RecordAsyncDisposal();
        return ValueTask.CompletedTask;
    }
}

internal sealed class IoDependency
{
    public async ValueTask WaitAsync() => await Task.Delay(1);
}
