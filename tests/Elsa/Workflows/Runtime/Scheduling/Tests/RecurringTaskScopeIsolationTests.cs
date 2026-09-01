using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Runtime.Scheduling.Tests;

public sealed class RecurringTaskScopeIsolationTests
{
    [Fact]
    public async Task DurableTimerPumpUsesAndDisposesAFreshWorkerScopePerTick()
    {
        var observations = new ScopeObservations();
        var services = Services();
        services.AddSingleton<IPersistenceScopeSource>(new TestPersistenceScopeSource("tenant-a", "tenant-b"));
        new WorkflowsRuntimeSchedulingFeature().ConfigureServices(services);
        services.RemoveAll<IDurableTimerStore>();
        services.AddScoped<IDurableTimerStore>(sp => new TimerStoreProbe(observations, CurrentScope(sp)));
        services.AddScoped<IBookmarkResumeDispatcher>(_ => new BookmarkDispatcherProbe(observations));

        await AssertTwoFreshTicksAsync<DurableTimerPumpTask>(services, observations, expectedWorkersPerTick: 4);
    }

    [Fact]
    public async Task RecurringTriggerPumpUsesAndDisposesAFreshWorkerScopePerTick()
    {
        var observations = new ScopeObservations();
        var services = Services();
        services.AddSingleton<IPersistenceScopeSource>(new TestPersistenceScopeSource("tenant-a", "tenant-b"));
        new WorkflowsRuntimeRecurringTriggersFeature().ConfigureServices(services);
        services.RemoveAll<IRecurringTriggerScheduleStore>();
        services.AddScoped<IRecurringTriggerScheduleStore>(sp => new RecurringScheduleStoreProbe(observations, CurrentScope(sp)));
        services.AddScoped<IStimulusRouter>(_ => new StimulusRouterProbe(observations));
        // In real composition the WorkflowsRuntimeTriggers dependency registers the binding store the pump's
        // owner-scoped dispatch resolves per worker scope.
        services.AddScoped<IWorkflowTriggerBindingStore>(_ => new TriggerBindingStoreProbe(observations));

        await AssertTwoFreshTicksAsync<RecurringTriggerPumpTask>(services, observations, expectedWorkersPerTick: 6);
    }

    [Fact]
    public async Task DurableTimerBackoffDoesNotLeakAcrossPersistenceScopes()
    {
        var dispatchCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var services = Services();
        services.AddSingleton<IPersistenceScopeSource>(new TestPersistenceScopeSource("tenant-a", "tenant-b"));
        new WorkflowsRuntimeSchedulingFeature().ConfigureServices(services);
        services.RemoveAll<IDurableTimerStore>();
        services.AddScoped<IDurableTimerStore, BackoffTimerStore>();
        services.AddScoped<IBookmarkResumeDispatcher>(sp => new BackoffBookmarkDispatcher(dispatchCounts, CurrentScope(sp)));

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var pump = Assert.Single(provider.GetServices<IRecurringTask>().OfType<DurableTimerPumpTask>());

        await pump.ExecuteAsync(CancellationToken.None);
        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, dispatchCounts["tenant-a"]);
        Assert.Equal(2, dispatchCounts["tenant-b"]);
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        return services;
    }

    private static async Task AssertTwoFreshTicksAsync<TPump>(
        ServiceCollection services,
        ScopeObservations observations,
        int expectedWorkersPerTick)
        where TPump : class, IRecurringTask
    {
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var pump = Assert.Single(provider.GetServices<IRecurringTask>().OfType<TPump>());

        await pump.ExecuteAsync(CancellationToken.None);
        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Equal(expectedWorkersPerTick * 2, observations.Created.Count);
        Assert.Equal(observations.Created.Count, observations.Created.Distinct().Count());
        Assert.Equal(4, observations.Disposed.Count);
        Assert.All(observations.Disposed, id => Assert.Contains(id, observations.Created));
        Assert.Equal(["tenant-a", "tenant-b", "tenant-a", "tenant-b"], observations.Scopes);
    }

    private sealed class ScopeObservations
    {
        public List<Guid> Created { get; } = [];
        public List<Guid> Disposed { get; } = [];
        public List<string> Scopes { get; } = [];
    }

    private sealed class TimerStoreProbe : IDurableTimerStore, IAsyncDisposable
    {
        private readonly ScopeObservations _observations;
        private readonly Guid _id = Guid.NewGuid();

        public TimerStoreProbe(ScopeObservations observations, string persistenceScope)
        {
            _observations = observations;
            observations.Created.Add(_id);
            observations.Scopes.Add(persistenceScope);
        }

        public ValueTask<DurableTimer> SaveAsync(DurableTimer timer, CancellationToken cancellationToken = default) => new(timer);
        public ValueTask<IReadOnlyCollection<DurableTimer>> ListDueAsync(DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default) => new((IReadOnlyCollection<DurableTimer>)[]);
        public ValueTask<DurableTimer?> FindAsync(string workflowExecutionId, string timerId, CancellationToken cancellationToken = default) => new((DurableTimer?)null);
        public ValueTask DeleteAsync(string workflowExecutionId, string timerId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync()
        {
            _observations.Disposed.Add(_id);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BookmarkDispatcherProbe(ScopeObservations observations) : IBookmarkResumeDispatcher
    {
        private readonly Guid _id = Record(observations);

        public ValueTask<BookmarkResumeDispatchResult> DispatchAsync(
            BookmarkResumeDispatchRequest request,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecurringScheduleStoreProbe : IRecurringTriggerScheduleStore, IAsyncDisposable
    {
        private readonly ScopeObservations _observations;
        private readonly Guid _id = Guid.NewGuid();

        public RecurringScheduleStoreProbe(ScopeObservations observations, string persistenceScope)
        {
            _observations = observations;
            observations.Created.Add(_id);
            observations.Scopes.Add(persistenceScope);
        }

        public ValueTask<RecurringTriggerSchedule> SaveAsync(RecurringTriggerSchedule schedule, CancellationToken cancellationToken = default) => new(schedule);
        public ValueTask<IReadOnlyCollection<RecurringTriggerSchedule>> ListDueAsync(DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default) => new((IReadOnlyCollection<RecurringTriggerSchedule>)[]);
        public ValueTask<RecurringTriggerSchedule?> FindAsync(string scheduleId, CancellationToken cancellationToken = default) => new((RecurringTriggerSchedule?)null);
        public ValueTask<bool> TryAdvanceAsync(string scheduleId, DateTimeOffset expectedNextOccurrence, DateTimeOffset newNextOccurrence, CancellationToken cancellationToken = default) => new(false);
        public ValueTask DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask DeleteAsync(string scheduleId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync()
        {
            _observations.Disposed.Add(_id);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TriggerBindingStoreProbe(ScopeObservations observations) : IWorkflowTriggerBindingStore
    {
        private readonly Guid _id = Record(observations);

        public ValueTask<WorkflowTriggerBinding> SaveAsync(WorkflowTriggerBinding binding, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<int> DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowTriggerBindingPage> ListByStimulusAsync(WorkflowTriggerBindingPageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowTriggerBindingPage> ListByArtifactAsync(WorkflowTriggerBindingArtifactPageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowTriggerBindingPage> ListByStimulusTypeAsync(WorkflowTriggerBindingTypePageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StimulusRouterProbe(ScopeObservations observations) : IStimulusRouter
    {
        private readonly Guid _id = Record(observations);

        public ValueTask<StimulusRoutingResult> RouteAsync(StimulusDispatchRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class BackoffTimerStore : IDurableTimerStore
    {
        private static readonly DurableTimer Timer = new(
            "same-timer",
            "same-execution",
            "Timer",
            "same-stimulus",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);

        public ValueTask<DurableTimer> SaveAsync(DurableTimer timer, CancellationToken cancellationToken = default) => new(timer);
        public ValueTask<IReadOnlyCollection<DurableTimer>> ListDueAsync(DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default) =>
            new((IReadOnlyCollection<DurableTimer>)[Timer]);
        public ValueTask<DurableTimer?> FindAsync(string workflowExecutionId, string timerId, CancellationToken cancellationToken = default) => new(Timer);
        public ValueTask DeleteAsync(string workflowExecutionId, string timerId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class BackoffBookmarkDispatcher(
        Dictionary<string, int> dispatchCounts,
        string persistenceScope) : IBookmarkResumeDispatcher
    {
        public ValueTask<BookmarkResumeDispatchResult> DispatchAsync(
            BookmarkResumeDispatchRequest request,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
            CancellationToken cancellationToken = default)
        {
            dispatchCounts[persistenceScope] = dispatchCounts.GetValueOrDefault(persistenceScope) + 1;
            var status = persistenceScope == "tenant-a"
                ? BookmarkResumeDispatchStatus.Rejected
                : BookmarkResumeDispatchStatus.Duplicate;
            return new(new BookmarkResumeDispatchResult(status, request.WorkflowExecutionId, reason: "test"));
        }
    }

    private static Guid Record(ScopeObservations observations)
    {
        var id = Guid.NewGuid();
        observations.Created.Add(id);
        return id;
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
