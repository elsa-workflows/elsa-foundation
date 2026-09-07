using CShells.Features;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Resumption;
using Elsa.Workflows.Runtime.Resumption.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Resumption.Tests;

public sealed class WorkflowsRuntimeResumptionFeatureTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RegistersResumptionServiceAndPumpTask()
    {
        var services = new ServiceCollection();

        new WorkflowsRuntimeResumptionFeature().ConfigureServices(services);

        // TS-1 (§2.23.1): single-implementation service asserted by registration presence, not implementation-type
        // or lifetime pinning. The resumption pump task is a named participant in the multi-implementation
        // IRecurringTask collection, preserved as a composition contract.
        Assert.Contains(services, d =>
            d.ServiceType == typeof(IRuntimeResumptionService) &&
            d.Lifetime == ServiceLifetime.Scoped);

        Assert.Contains(services, d =>
            d.ServiceType == typeof(IRecurringTask) &&
            d.ImplementationFactory is not null &&
            d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void ContributesResumptionDurabilityEvidence()
    {
        var services = new ServiceCollection();

        new WorkflowsRuntimeResumptionFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();

        // Not redundant with the readiness report: since #1320 this contribution steers a runtime WRITE decision.
        // WorkflowSchedulerCommandRouter parks a shed command's work item only when some injected evidence names the
        // resumption component, so deleting this registration silently flips every host composing this feature from
        // "park, the sweep re-drives it" to "refuse, write nothing" — with no other test going red. Asserted through a
        // built provider rather than a descriptor because the router reads Component off the resolved instance, and
        // the contributing type is private to the feature.
        var evidence = Assert.Single(
            provider.GetServices<IWorkflowDispatchDurabilityEvidence>(),
            candidate => candidate.Component == WorkflowDispatchDurabilityComponents.Resumption);
        Assert.Equal(WorkflowDispatchDurabilityLevel.Durable, evidence.Level);
    }

    [Fact]
    public void MapsSettingsOntoOptions()
    {
        var services = new ServiceCollection();

        new WorkflowsRuntimeResumptionFeature
        {
            SweepIntervalSeconds = 3,
            MaxBackoffIntervalMinutes = 2,
            OutboxBatchSize = 11,
            BacklogBatchSize = 12,
            RecoveryScanBatchSize = 13,
            MaxExecutionsPerSweep = 14,
            LeaseTimeoutMinutes = 7,
            HeartbeatTimeoutMinutes = 8
        }.ConfigureServices(services);

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<RuntimeResumptionOptions>>().Value;

        Assert.Equal(TimeSpan.FromSeconds(3), options.SweepInterval);
        Assert.Equal(TimeSpan.FromMinutes(2), options.MaxBackoffInterval);
        Assert.Equal(11, options.OutboxBatchSize);
        Assert.Equal(12, options.BacklogBatchSize);
        Assert.Equal(13, options.RecoveryScanBatchSize);
        Assert.Equal(14, options.MaxExecutionsPerSweep);
        Assert.Equal(TimeSpan.FromMinutes(7), options.LeaseTimeout);
        Assert.Equal(TimeSpan.FromMinutes(8), options.HeartbeatTimeout);
    }

    [Fact]
    public void DeclaresTasksDependencyAndServerRuntime()
    {
        var attribute = Assert.Single(
            typeof(WorkflowsRuntimeResumptionFeature).GetCustomAttributes(typeof(ShellFeatureAttribute), inherit: false)
                .Cast<ShellFeatureAttribute>());

        Assert.Equal("WorkflowsRuntimeResumption", attribute.Name);
        Assert.Contains("Tasks", attribute.DependsOn.Select(d => d?.ToString()));
    }

    [Fact]
    public async Task PumpUsesAndDisposesAFreshResumptionScopePerTick()
    {
        var observations = new ScopeObservations();
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        new WorkflowsRuntimeResumptionFeature().ConfigureServices(services);
        services.RemoveAll<IRuntimeResumptionService>();
        services.AddScoped<IRuntimeResumptionService>(sp => new ResumptionServiceProbe(observations, CurrentScope(sp)));

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var pump = Assert.Single(provider.GetServices<IRecurringTask>().OfType<RuntimeResumptionPumpTask>());

        await pump.ExecuteAsync(CancellationToken.None);
        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Equal(2, observations.Created.Count);
        Assert.Equal(2, observations.Created.Distinct().Count());
        Assert.Equal(observations.Created.Order(), observations.Disposed.Order());
        Assert.Equal([PersistenceScope.DefaultValue, PersistenceScope.DefaultValue], observations.Scopes);
    }

    [Fact]
    public async Task PumpSweepsEveryConfiguredPersistenceScope()
    {
        var observations = new ScopeObservations();
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IPersistenceScopeSource>(new TestPersistenceScopeSource("tenant-a", "tenant-b"));
        new WorkflowsRuntimeResumptionFeature().ConfigureServices(services);
        services.RemoveAll<IRuntimeResumptionService>();
        services.AddScoped<IRuntimeResumptionService>(sp => new ResumptionServiceProbe(observations, CurrentScope(sp)));

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var pump = Assert.Single(provider.GetServices<IRecurringTask>().OfType<RuntimeResumptionPumpTask>());

        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Equal(["tenant-a", "tenant-b"], observations.Scopes);
        Assert.Equal(observations.Created.Order(), observations.Disposed.Order());
    }

    [Fact]
    public async Task Pump_sweeps_test_scopes_before_runtime_resumption_in_every_persistence_scope()
    {
        var events = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(ObservedAt));
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IPersistenceScopeSource>(new TestPersistenceScopeSource("tenant-a", "tenant-b"));
        new WorkflowsRuntimeResumptionFeature().ConfigureServices(services);
        services.RemoveAll<IRuntimeResumptionService>();
        services.AddScoped<IRuntimeResumptionService>(sp => new OrderedResumptionProbe(events, CurrentScope(sp)));
        services.AddScoped<IWorkflowTestScopeCleaner>(sp => new OrderedScopeCleanerProbe(events, CurrentScope(sp)));

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var pump = Assert.Single(provider.GetServices<IRecurringTask>().OfType<RuntimeResumptionPumpTask>());

        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Equal(
            [
                $"cleanup:tenant-a:{ObservedAt:O}",
                "resumption:tenant-a",
                $"cleanup:tenant-b:{ObservedAt:O}",
                "resumption:tenant-b"
            ],
            events);
    }

    [Fact]
    public async Task PerExecutionBackoffDoesNotLeakAcrossPersistenceScopes()
    {
        var observations = new BackoffObservations();
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IPersistenceScopeSource>(new TestPersistenceScopeSource("tenant-a", "tenant-b"));
        new WorkflowsRuntimeResumptionFeature().ConfigureServices(services);
        services.RemoveAll<IRuntimeResumptionService>();
        services.AddScoped<IRuntimeResumptionService>(sp => new BackoffResumptionServiceProbe(observations, CurrentScope(sp)));

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var pump = Assert.Single(provider.GetServices<IRecurringTask>().OfType<RuntimeResumptionPumpTask>());

        await pump.ExecuteAsync(CancellationToken.None);
        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Contains("same-execution", observations.ExcludedByScope["tenant-a"][1]);
        Assert.DoesNotContain("same-execution", observations.ExcludedByScope["tenant-b"][1]);
    }

    private sealed class ScopeObservations
    {
        public List<Guid> Created { get; } = [];
        public List<Guid> Disposed { get; } = [];
        public List<string> Scopes { get; } = [];
    }

    private sealed class ResumptionServiceProbe : IRuntimeResumptionService, IAsyncDisposable
    {
        private readonly ScopeObservations _observations;
        private readonly Guid _id = Guid.NewGuid();

        public ResumptionServiceProbe(ScopeObservations observations, string persistenceScope)
        {
            _observations = observations;
            observations.Created.Add(_id);
            observations.Scopes.Add(persistenceScope);
        }

        public ValueTask<RuntimeResumptionSweepResult> SweepAsync(
            RuntimeResumptionSweepRequest request,
            CancellationToken cancellationToken = default) =>
            new(new RuntimeResumptionSweepResult(0, 0, 0, []));

        public ValueTask DisposeAsync()
        {
            _observations.Disposed.Add(_id);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BackoffObservations
    {
        public Dictionary<string, List<IReadOnlySet<string>>> ExcludedByScope { get; } = new(StringComparer.Ordinal);
    }

    private sealed class BackoffResumptionServiceProbe(
        BackoffObservations observations,
        string persistenceScope) : IRuntimeResumptionService
    {
        public ValueTask<RuntimeResumptionSweepResult> SweepAsync(
            RuntimeResumptionSweepRequest request,
            CancellationToken cancellationToken = default)
        {
            if (!observations.ExcludedByScope.TryGetValue(persistenceScope, out var requests))
                observations.ExcludedByScope[persistenceScope] = requests = [];
            requests.Add(request.ExcludedWorkflowExecutionIds);

            var firstSweep = requests.Count == 1;
            var outcome = persistenceScope == "tenant-a"
                ? RuntimeResumptionDispatchOutcome.Faulted
                : RuntimeResumptionDispatchOutcome.Accepted;
            IReadOnlyCollection<RuntimeResumptionDispatch> dispatches = firstSweep
                ? [new RuntimeResumptionDispatch("same-execution", outcome, null, outcome == RuntimeResumptionDispatchOutcome.Faulted ? "boom" : null)]
                : [];
            return new(new RuntimeResumptionSweepResult(0, 0, 0, dispatches));
        }
    }

    private sealed class OrderedResumptionProbe(ICollection<string> events, string persistenceScope)
        : IRuntimeResumptionService
    {
        public ValueTask<RuntimeResumptionSweepResult> SweepAsync(
            RuntimeResumptionSweepRequest request,
            CancellationToken cancellationToken = default)
        {
            events.Add($"resumption:{persistenceScope}");
            return new(new RuntimeResumptionSweepResult(0, 0, 0, []));
        }
    }

    private sealed class OrderedScopeCleanerProbe(ICollection<string> events, string persistenceScope)
        : IWorkflowTestScopeCleaner
    {
        public ValueTask<WorkflowTestScopeCleanupResult> CloseAsync(
            string scopeId,
            WorkflowTestScopeCloseReason reason,
            DateTimeOffset requestedAt,
            CancellationToken cancellationToken = default) =>
            new(new WorkflowTestScopeCleanupResult(0, 0, 0, 0, 0));

        public ValueTask<int> SweepAsync(
            DateTimeOffset observedAt,
            CancellationToken cancellationToken = default)
        {
            events.Add($"cleanup:{persistenceScope}:{observedAt:O}");
            return ValueTask.FromResult(0);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static string CurrentScope(IServiceProvider serviceProvider) => serviceProvider
        .GetRequiredService<IPersistenceAccessContextAccessor>()
        .Current.Scope!.Value;

    private sealed class TestPersistenceScopeSource(params string[] scopes) : IPersistenceScopeSource
    {
        private readonly IReadOnlyList<PersistenceScope> _scopes = scopes.Select(x => new PersistenceScope(x)).ToArray();

        public ValueTask<IReadOnlyList<PersistenceScope>> GetScopesAsync(CancellationToken cancellationToken = default) =>
            new(_scopes);
    }
}
