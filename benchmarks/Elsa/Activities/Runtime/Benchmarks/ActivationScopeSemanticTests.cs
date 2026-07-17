using Xunit;

namespace Elsa.Activities.Runtime.Benchmarks;

public sealed class ActivationScopeSemanticTests
{
    public static TheoryData<ActivationStrategyKind> Strategies => new()
    {
        ActivationStrategyKind.BurstOnly,
        ActivationStrategyKind.PerAttempt,
        ActivationStrategyKind.Conditional
    };

    [Theory]
    [MemberData(nameof(Strategies))]
    public async Task Every_attempt_gets_a_fresh_activity_and_transient_dependency(ActivationStrategyKind strategy)
    {
        using var harness = new ActivationBenchmarkHarness();

        var measurement = await harness.RunRetryAsync(strategy);

        Assert.Equal(2, measurement.ActivityActivations);
        Assert.Single(measurement.InvocationIdentities.Values.Distinct());
        Assert.Equal(2, measurement.ActivityIdentities.Values.Distinct().Count());
        Assert.Equal(2, measurement.TransientServiceIdentities.Values.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Strategies))]
    public async Task Scoped_identity_and_disposal_match_the_strategy_contract(ActivationStrategyKind strategy)
    {
        using var harness = new ActivationBenchmarkHarness();

        var measurement = await harness.RunScopedAsync(strategy, 2);

        Assert.Equal(2, measurement.ActivityActivations);
        Assert.Equal(2, measurement.ActivityIdentities.Values.Distinct().Count());
        Assert.Equal(strategy == ActivationStrategyKind.BurstOnly ? 1 : 2,
            measurement.ScopedServiceIdentities.Values.Distinct().Count());
        Assert.Equal(strategy == ActivationStrategyKind.BurstOnly ? 1 : 2,
            measurement.TransitiveScopedServiceIdentities.Values.Distinct().Count());
        Assert.Equal(strategy == ActivationStrategyKind.BurstOnly ? 0 : 2, measurement.ActivityChildScopes);
        Assert.Equal(measurement.ActivityChildScopes, measurement.ActivityChildScopeDisposals);
        Assert.Equal(strategy == ActivationStrategyKind.BurstOnly ? 1 : 2, measurement.SyncDisposals);
        Assert.Equal(strategy == ActivationStrategyKind.BurstOnly ? 3 : 4, measurement.AsyncDisposals);
    }

    [Fact]
    public async Task Conditional_fast_path_is_limited_to_provably_service_free_activities()
    {
        using var harness = new ActivationBenchmarkHarness();

        var noOp = await harness.RunNoOpAsync(ActivationStrategyKind.Conditional, 3);
        var transient = await harness.RunTransientAsync(ActivationStrategyKind.Conditional, 3);
        var scoped = await harness.RunScopedAsync(ActivationStrategyKind.Conditional, 3);

        Assert.Equal(0, noOp.ActivityChildScopes);
        Assert.Equal(3, transient.ActivityChildScopes);
        Assert.Equal(3, scoped.ActivityChildScopes);
    }

    [Fact]
    public async Task Intrinsic_workload_never_activates_CLR_or_creates_activity_scope()
    {
        using var harness = new ActivationBenchmarkHarness();

        var measurement = await harness.RunIntrinsicHeavyAsync(100);

        Assert.Equal(100, measurement.IntrinsicOperations);
        Assert.Equal(0, measurement.ActivityActivations);
        Assert.Equal(0, measurement.ActivityChildScopes);
    }

    [Theory]
    [MemberData(nameof(Strategies))]
    public async Task Activation_failure_disposes_every_created_scope_and_transitive_dependency(ActivationStrategyKind strategy)
    {
        using var harness = new ActivationBenchmarkHarness();

        var measurement = await harness.RunActivationFailureAsync(strategy);

        Assert.Equal(0, measurement.ActivityActivations);
        Assert.Equal(strategy == ActivationStrategyKind.BurstOnly ? 0 : 1, measurement.ActivityChildScopes);
        Assert.Equal(measurement.ActivityChildScopes, measurement.ActivityChildScopeDisposals);
        Assert.Equal(1, measurement.SyncDisposals);
        Assert.Equal(1, measurement.AsyncDisposals);
    }

    [Theory]
    [MemberData(nameof(Strategies))]
    public async Task Resumption_uses_a_fresh_activity_and_scoped_identity_in_a_fresh_burst(ActivationStrategyKind strategy)
    {
        using var harness = new ActivationBenchmarkHarness();

        var measurement = await harness.RunResumeAsync(strategy);

        Assert.Equal(2, measurement.ActivityIdentities.Values.Distinct().Count());
        Assert.Single(measurement.InvocationIdentities.Values.Distinct());
        Assert.Equal(2, measurement.ScopedServiceIdentities.Values.Distinct().Count());
        Assert.Equal(measurement.ActivityChildScopes, measurement.ActivityChildScopeDisposals);
    }

    [Theory]
    [MemberData(nameof(Strategies))]
    public async Task Concurrent_workflows_do_not_share_scoped_identity(ActivationStrategyKind strategy)
    {
        using var harness = new ActivationBenchmarkHarness();

        var measurement = await harness.RunConcurrentDrainAsync(strategy, 4);
        var first = measurement.ScopedServiceIdentities
            .Where(x => x.Key.StartsWith("workflow-a-", StringComparison.Ordinal))
            .Select(x => x.Value)
            .ToHashSet();
        var second = measurement.ScopedServiceIdentities
            .Where(x => x.Key.StartsWith("workflow-b-", StringComparison.Ordinal))
            .Select(x => x.Value)
            .ToHashSet();

        Assert.Equal(8, measurement.ActivityActivations);
        Assert.Empty(first.Intersect(second));
    }
}
