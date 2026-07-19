using System.Collections.Concurrent;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Benchmarks;

public enum ActivationStrategyKind
{
    BurstOnly,
    PerAttempt,
    Conditional
}

internal sealed class BenchmarkActivityCatalog
{
    private readonly IReadOnlyDictionary<string, BenchmarkActivityRegistration> _registrations;

    public BenchmarkActivityCatalog(IEnumerable<BenchmarkActivityRegistration> registrations)
    {
        _registrations = registrations.ToDictionary(x => x.ActivityTypeKey, StringComparer.Ordinal);
    }

    public BenchmarkActivityRegistration Resolve(string activityTypeKey) =>
        _registrations.TryGetValue(activityTypeKey, out var registration)
            ? registration
            : throw new InvalidOperationException($"No benchmark activity is registered for '{activityTypeKey}'.");
}

internal sealed record BenchmarkActivityRegistration(
    string ActivityTypeKey,
    Type ActivityType,
    bool AllowsConditionalFastPath = false)
{
    public bool IsProvablyFastPathEligible
    {
        get
        {
            if (!AllowsConditionalFastPath || typeof(IDisposable).IsAssignableFrom(ActivityType) || typeof(IAsyncDisposable).IsAssignableFrom(ActivityType))
                return false;

            var constructors = ActivityType.GetConstructors();
            return constructors.Length == 1 && constructors[0].GetParameters().Length == 0;
        }
    }
}

internal sealed class ActivationScopeCounters
{
    private readonly ConcurrentDictionary<string, Guid> _activityIdentities = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _invocationIdentities = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Guid> _scopedServiceIdentities = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Guid> _transitiveScopedServiceIdentities = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Guid> _transientServiceIdentities = new(StringComparer.Ordinal);
    private long _activityActivations;
    private long _activityChildScopes;
    private long _activityChildScopeDisposals;
    private long _syncDisposals;
    private long _asyncDisposals;
    private long _intrinsicOperations;

    public void Reset()
    {
        Interlocked.Exchange(ref _activityActivations, 0);
        Interlocked.Exchange(ref _activityChildScopes, 0);
        Interlocked.Exchange(ref _activityChildScopeDisposals, 0);
        Interlocked.Exchange(ref _syncDisposals, 0);
        Interlocked.Exchange(ref _asyncDisposals, 0);
        Interlocked.Exchange(ref _intrinsicOperations, 0);
        _activityIdentities.Clear();
        _invocationIdentities.Clear();
        _scopedServiceIdentities.Clear();
        _transitiveScopedServiceIdentities.Clear();
        _transientServiceIdentities.Clear();
    }

    public void RecordActivation(ActivityActivationRequest request, IActivity activity)
    {
        Interlocked.Increment(ref _activityActivations);
        _invocationIdentities[request.Attempt.AttemptId] = request.Attempt.InvocationId;
        if (activity is IBenchmarkActivity benchmarkActivity)
            _activityIdentities[request.Attempt.AttemptId] = benchmarkActivity.InstanceId;
    }

    public void RecordChildScopeCreated() => Interlocked.Increment(ref _activityChildScopes);
    public void RecordChildScopeDisposed() => Interlocked.Increment(ref _activityChildScopeDisposals);
    public void RecordSyncDisposal() => Interlocked.Increment(ref _syncDisposals);
    public void RecordAsyncDisposal() => Interlocked.Increment(ref _asyncDisposals);
    public void RecordIntrinsicOperation() => Interlocked.Increment(ref _intrinsicOperations);
    public void RecordScopedService(string attemptId, Guid identity) => _scopedServiceIdentities[attemptId] = identity;
    public void RecordTransitiveScopedService(string attemptId, Guid identity) => _transitiveScopedServiceIdentities[attemptId] = identity;
    public void RecordTransientService(string attemptId, Guid identity) => _transientServiceIdentities[attemptId] = identity;

    public ActivationMeasurement Snapshot() => new(
        Interlocked.Read(ref _activityActivations),
        Interlocked.Read(ref _activityChildScopes),
        Interlocked.Read(ref _activityChildScopeDisposals),
        Interlocked.Read(ref _syncDisposals),
        Interlocked.Read(ref _asyncDisposals),
        Interlocked.Read(ref _intrinsicOperations),
        new Dictionary<string, Guid>(_activityIdentities, StringComparer.Ordinal),
        new Dictionary<string, string>(_invocationIdentities, StringComparer.Ordinal),
        new Dictionary<string, Guid>(_scopedServiceIdentities, StringComparer.Ordinal),
        new Dictionary<string, Guid>(_transitiveScopedServiceIdentities, StringComparer.Ordinal),
        new Dictionary<string, Guid>(_transientServiceIdentities, StringComparer.Ordinal));
}

internal sealed record ActivationMeasurement(
    long ActivityActivations,
    long ActivityChildScopes,
    long ActivityChildScopeDisposals,
    long SyncDisposals,
    long AsyncDisposals,
    long IntrinsicOperations,
    IReadOnlyDictionary<string, Guid> ActivityIdentities,
    IReadOnlyDictionary<string, string> InvocationIdentities,
    IReadOnlyDictionary<string, Guid> ScopedServiceIdentities,
    IReadOnlyDictionary<string, Guid> TransitiveScopedServiceIdentities,
    IReadOnlyDictionary<string, Guid> TransientServiceIdentities)
{
    public long Score => ActivityActivations + ActivityChildScopes + ActivityChildScopeDisposals +
                         SyncDisposals + AsyncDisposals + IntrinsicOperations;
}

internal static class ActivationStrategyFactory
{
    public static IActivityActivator Create(
        ActivationStrategyKind strategy,
        IServiceProvider burstServices,
        BenchmarkActivityCatalog catalog,
        ActivationScopeCounters counters) => strategy switch
    {
        ActivationStrategyKind.BurstOnly => new BurstOnlyActivityActivator(burstServices, catalog, counters),
        ActivationStrategyKind.PerAttempt => new PerAttemptActivityActivator(
            burstServices.GetRequiredService<IServiceScopeFactory>(), catalog, counters),
        ActivationStrategyKind.Conditional => new ConditionalActivityActivator(
            burstServices, burstServices.GetRequiredService<IServiceScopeFactory>(), catalog, counters),
        _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, null)
    };
}

internal abstract class BenchmarkActivityActivator(
    BenchmarkActivityCatalog catalog,
    ActivationScopeCounters counters) : IActivityActivator
{
    protected ActivationScopeCounters Counters { get; } = counters;

    public abstract ValueTask<ActivityActivationLease> ActivateAsync(
        ActivityActivationRequest request,
        CancellationToken cancellationToken = default);

    protected IActivity CreateActivity(ActivityActivationRequest request, IServiceProvider services)
    {
        var registration = catalog.Resolve(request.Contract.ActivityTypeKey);
        var activity = (IActivity)ActivatorUtilities.CreateInstance(services, registration.ActivityType);
        Counters.RecordActivation(request, activity);
        return activity;
    }

    protected BenchmarkActivityRegistration Resolve(ActivityActivationRequest request) =>
        catalog.Resolve(request.Contract.ActivityTypeKey);

    protected async ValueTask<ActivityActivationLease> ActivateInChildScopeAsync(
        IServiceScopeFactory scopeFactory,
        ActivityActivationRequest request)
    {
        var scope = scopeFactory.CreateAsyncScope();
        Counters.RecordChildScopeCreated();
        try
        {
            var activity = CreateActivity(request, scope.ServiceProvider);
            return new ActivityActivationLease(activity, new CountedAsyncScope(scope, Counters));
        }
        catch
        {
            await scope.DisposeAsync();
            Counters.RecordChildScopeDisposed();
            throw;
        }
    }
}

internal sealed class BurstOnlyActivityActivator(
    IServiceProvider burstServices,
    BenchmarkActivityCatalog catalog,
    ActivationScopeCounters counters) : BenchmarkActivityActivator(catalog, counters)
{
    public override ValueTask<ActivityActivationLease> ActivateAsync(
        ActivityActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ActivityActivationLease(CreateActivity(request, burstServices)));
    }
}

internal sealed class PerAttemptActivityActivator(
    IServiceScopeFactory scopeFactory,
    BenchmarkActivityCatalog catalog,
    ActivationScopeCounters counters) : BenchmarkActivityActivator(catalog, counters)
{
    public override async ValueTask<ActivityActivationLease> ActivateAsync(
        ActivityActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await ActivateInChildScopeAsync(scopeFactory, request);
    }
}

internal sealed class ConditionalActivityActivator(
    IServiceProvider burstServices,
    IServiceScopeFactory scopeFactory,
    BenchmarkActivityCatalog catalog,
    ActivationScopeCounters counters) : BenchmarkActivityActivator(catalog, counters)
{
    public override async ValueTask<ActivityActivationLease> ActivateAsync(
        ActivityActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var registration = Resolve(request);
        if (registration.IsProvablyFastPathEligible)
            return new ActivityActivationLease(CreateActivity(request, burstServices));

        return await ActivateInChildScopeAsync(scopeFactory, request);
    }
}

internal sealed class CountedAsyncScope(AsyncServiceScope scope, ActivationScopeCounters counters) : IAsyncDisposable
{
    private int _disposed;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await scope.DisposeAsync();
        counters.RecordChildScopeDisposed();
    }
}
