using CShells.Lifecycle;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Extensions;
using Elsa.Weaver.Services;
using Elsa.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Weaver.Tests;

/// <summary>
/// #3441 story 2.1: the default feature registers one shell terminator that gives the owner's leases back while the
/// shell's container is still usable; disposing the container afterwards finds the work done.
/// </summary>
public sealed class AgentShutdownTerminatorTests
{
    [Fact]
    public void The_default_composition_registers_the_lease_release_terminator_once()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new EventsFeature().ConfigureServices(services);
        services.AddWeaver();
        services.AddWeaver();

        Assert.Single(services, x => x.ServiceType == typeof(ReleaseAgentSessionLeasesTerminator));
        var registration = Assert.Single(services.Select(x => x.ImplementationInstance).OfType<ShellTerminatorRegistration>(),
            x => x.TerminatorType == typeof(ReleaseAgentSessionLeasesTerminator));
        Assert.Equal(LifecyclePhase.Default, registration.Phase);
    }

    [Fact]
    public async Task The_terminator_releases_every_loaded_lease_and_the_owner_then_refuses_new_work()
    {
        await using var provider = AgentTestServices.Create();
        var sessions = provider.GetRequiredService<IAgentSessionService>();
        var store = provider.GetRequiredService<IAgentSessionRecordStore>();
        var first = await CreateAsync(sessions);
        var second = await CreateAsync(sessions);
        Assert.NotNull((await store.LoadAsync(first.Id))!.LeaseExpiresAt);

        await provider.GetRequiredService<ReleaseAgentSessionLeasesTerminator>().TerminateAsync();

        Assert.Null((await store.LoadAsync(first.Id))!.LeaseExpiresAt);
        Assert.Null((await store.LoadAsync(second.Id))!.LeaseExpiresAt);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => sessions.FindAsync("not-loaded-yet"));
    }

    [Fact]
    public async Task A_replaced_session_service_owns_its_own_shutdown()
    {
        await using var provider = AgentTestServices.Create(services =>
            services.Replace(ServiceDescriptor.Singleton<IAgentSessionService>(new UnusedSessionService())));

        await provider.GetRequiredService<ReleaseAgentSessionLeasesTerminator>().TerminateAsync();
    }

    private static Task<AgentSession> CreateAsync(IAgentSessionService sessions) => sessions.CreateAsync(new("tenant-1", "actor-1",
        "conversation-1", DeterministicAgentProvider.Id, "explain", null, AgentPolicy.Default, new Dictionary<string, string>()));

    private sealed class UnusedSessionService : IAgentSessionService
    {
        private static Exception Unused() => new InvalidOperationException("A replaced session service is never shut down by the default terminator.");
        public Task<AgentSession> CreateAsync(AgentSessionCreateRequest request, CancellationToken cancellationToken = default) => throw Unused();
        public Task<AgentSession?> FindAsync(string sessionId, CancellationToken cancellationToken = default) => throw Unused();
        public Task<AgentMessage> AddMessageAsync(string sessionId, AgentMessageCreateRequest request, CancellationToken cancellationToken = default) => throw Unused();
        public Task<IReadOnlyCollection<AgentMessage>> ListMessagesAsync(string sessionId, CancellationToken cancellationToken = default) => throw Unused();
        public Task<AgentMessage?> FindMessageAsync(string sessionId, string messageId, CancellationToken cancellationToken = default) => throw Unused();
        public Task<AgentMessage?> FindLatestMessageAsync(string sessionId, AgentRole? role = null, CancellationToken cancellationToken = default) => throw Unused();
        public Task SetMessageStatusAsync(string sessionId, string messageId, AgentMessageStatus status, CancellationToken cancellationToken = default) => throw Unused();
        public Task AddContextAsync(string sessionId, IReadOnlyCollection<AgentContextAttachment> attachments, CancellationToken cancellationToken = default) => throw Unused();
        public Task<IReadOnlyCollection<AgentContextAttachment>> ListContextAsync(string sessionId, CancellationToken cancellationToken = default) => throw Unused();
    }
}
