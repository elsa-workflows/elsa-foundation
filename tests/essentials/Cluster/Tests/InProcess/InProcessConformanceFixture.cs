using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Options;
using Elsa.Cluster.InProcess;
using Elsa.Cluster.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Elsa.Cluster.Tests.InProcess;

/// <summary>
/// The in-process provider as the conformance suite sees it: a cluster of one, composed the way every consumer composes
/// it. Operations that need a second member or a store are not supported.
/// </summary>
public sealed class InProcessConformanceFixture : IClusterMembershipConformanceFixture
{
    private const string ClusterOfOne = "The in-process provider is a cluster of one.";
    private readonly FakeTimeProvider _clock = new();
    private ServiceProvider? _services;

    public string ProviderName => InProcessClusterMembershipServiceCollectionExtensions.ProviderName;

    public ClusterProviderKind ProviderKind => ClusterProviderKind.InProcess;

    public bool SupportsMultipleMembers => false;

    public ConformanceTimings Timings { get; } = Defaults();

    public TimeSpan? CleanupPeriod => null;

    public void ComposeProvider(IServiceCollection services) => services.TryAddInProcessClusterMembership();

    public ValueTask<int> CountStoredEntriesAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(0);

    public ValueTask<IConformanceMember> StartMemberAsync(ConformanceMemberSetup setup, CancellationToken cancellationToken = default)
    {
        if (_services is not null)
            throw new NotSupportedException(ClusterOfOne);

        var readability = setup.Readability is null ? null : new ConformanceReadabilitySource(setup.Readability);
        var services = new ServiceCollection()
            .AddSingleton<TimeProvider>(_clock)
            .Configure<ClusterMembershipOptions>(options => options.HostId = setup.HostId);
        if (readability is not null)
            services.AddSingleton<IMemberReportSource<ReadabilitySection>>(readability);
        ComposeProvider(services);

        _services = services.BuildServiceProvider();
        return ValueTask.FromResult<IConformanceMember>(new Member(_services.GetRequiredService<IClusterMembership>(), _clock, readability));
    }

    public ValueTask<IConformanceMember> JoinMemberAsync(ConformanceMemberSetup setup, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(ClusterOfOne);

    public ValueTask AdvanceAsync(TimeSpan delta, CancellationToken cancellationToken = default)
    {
        _clock.Advance(delta);
        return ValueTask.CompletedTask;
    }

    public ValueTask PlantUninterpretableEntryAsync(string hostId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(ClusterOfOne);

    public ValueTask RunCleanupAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The in-process provider stores nothing.");

    public async ValueTask DisposeAsync()
    {
        if (_services is not null)
            await _services.DisposeAsync();
    }

    private static ConformanceTimings Defaults()
    {
        var defaults = new ClusterMembershipOptions();
        return new ConformanceTimings(defaults.HeartbeatInterval, defaults.ExpiryPeriod, defaults.SkewAllowance);
    }

    private sealed class Member(IClusterMembership membership, TimeProvider clock, ConformanceReadabilitySource? readability) : IConformanceMember
    {
        public IClusterMembership Membership => membership;

        public TimeProvider Clock => clock;

        public void SetReadability(params ReadabilityEntry[] entries) =>
            (readability ?? throw new InvalidOperationException("This member has no readability source.")).Set(entries);

        public ValueTask ActivateAsync() => throw new NotSupportedException(ClusterOfOne);

        public ValueTask BeginStopAsync() => throw new NotSupportedException(ClusterOfOne);

        public ValueTask StopAsync() => throw new NotSupportedException(ClusterOfOne);

        public ValueTask KillAsync() => throw new NotSupportedException(ClusterOfOne);

        public ValueTask IsolateAsync() => throw new NotSupportedException(ClusterOfOne);
    }
}
