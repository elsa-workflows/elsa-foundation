using CShells.Features;
using Elsa.Persistence.Core;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.ReferenceGarbageCollection;
using Elsa.Workflows.Runtime.ReferenceGarbageCollection.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowsRuntimeReferenceGarbageCollectionFeatureTests
{
    [Fact]
    public void RegistersCollectorAndRecurringPump()
    {
        var services = new ServiceCollection();

        new WorkflowsRuntimeReferenceGarbageCollectionFeature().ConfigureServices(services);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowExecutableReferenceGarbageCollector) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRecurringTask) &&
            descriptor.ImplementationFactory is not null &&
            descriptor.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void MapsCadenceAndSafetySettingsOntoTheirOwningOptions()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeReferenceGarbageCollectionFeature
        {
            SweepIntervalMinutes = 2,
            MaxBackoffIntervalMinutes = 7,
            ArtifactCreationGracePeriodMinutes = 11
        }.ConfigureServices(services);

        var provider = services.BuildServiceProvider();
        var pump = provider.GetRequiredService<IOptions<WorkflowExecutableReferenceGarbageCollectionOptions>>().Value;
        var collector = provider.GetRequiredService<IOptions<WorkflowExecutableGarbageCollectionOptions>>().Value;

        Assert.Equal(TimeSpan.FromMinutes(2), pump.SweepInterval);
        Assert.Equal(TimeSpan.FromMinutes(7), pump.MaxBackoffInterval);
        Assert.Equal(TimeSpan.FromMinutes(11), pump.ArtifactCreationGracePeriod);
        Assert.Equal(TimeSpan.FromMinutes(11), collector.ArtifactCreationGracePeriod);
    }

    [Fact]
    public void DeclaresTasksDependency()
    {
        var feature = Assert.Single(
            typeof(WorkflowsRuntimeReferenceGarbageCollectionFeature)
                .GetCustomAttributes(typeof(ShellFeatureAttribute), inherit: false)
                .Cast<ShellFeatureAttribute>());

        Assert.Equal("WorkflowsRuntimeReferenceGarbageCollection", feature.Name);
        Assert.Contains("Tasks", feature.DependsOn.Select(dependency => dependency?.ToString()));
    }

    [Fact]
    public async Task PumpUsesAndDisposesAFreshCollectorScopePerTick()
    {
        var observations = new ScopeObservations();
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        new WorkflowsRuntimeReferenceGarbageCollectionFeature().ConfigureServices(services);
        services.RemoveAll<IWorkflowExecutableReferenceGarbageCollector>();
        services.AddScoped<IWorkflowExecutableReferenceGarbageCollector>(sp => new CollectorProbe(observations, CurrentScope(sp)));

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var pump = Assert.Single(provider.GetServices<IRecurringTask>().OfType<WorkflowExecutableReferenceGarbageCollectionPumpTask>());

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
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IPersistenceScopeSource>(new TestPersistenceScopeSource("tenant-a", "tenant-b"));
        new WorkflowsRuntimeReferenceGarbageCollectionFeature().ConfigureServices(services);
        services.RemoveAll<IWorkflowExecutableReferenceGarbageCollector>();
        services.AddScoped<IWorkflowExecutableReferenceGarbageCollector>(sp => new CollectorProbe(observations, CurrentScope(sp)));

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var pump = Assert.Single(provider.GetServices<IRecurringTask>().OfType<WorkflowExecutableReferenceGarbageCollectionPumpTask>());

        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Equal(["tenant-a", "tenant-b"], observations.Scopes);
        Assert.Equal(observations.Created.Order(), observations.Disposed.Order());
    }

    private sealed class ScopeObservations
    {
        public List<Guid> Created { get; } = [];
        public List<Guid> Disposed { get; } = [];
        public List<string> Scopes { get; } = [];
    }

    private sealed class CollectorProbe : IWorkflowExecutableReferenceGarbageCollector, IAsyncDisposable
    {
        private readonly ScopeObservations _observations;
        private readonly Guid _id = Guid.NewGuid();

        public CollectorProbe(ScopeObservations observations, string persistenceScope)
        {
            _observations = observations;
            observations.Created.Add(_id);
            observations.Scopes.Add(persistenceScope);
        }

        public ValueTask<WorkflowExecutableReferenceSweepResult> SweepAsync(CancellationToken cancellationToken = default) =>
            new(new WorkflowExecutableReferenceSweepResult(0, 0));

        public ValueTask<WorkflowExecutableReferenceSweepResult> SweepAsync(DateTimeOffset now, CancellationToken cancellationToken = default) =>
            SweepAsync(cancellationToken);

        public ValueTask DisposeAsync()
        {
            _observations.Disposed.Add(_id);
            return ValueTask.CompletedTask;
        }
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
