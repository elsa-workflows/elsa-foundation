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
public sealed class WeaverShutdownTerminatorTests
{
    [Fact]
    public void The_default_composition_registers_the_lease_release_terminator_once()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new EventsFeature().ConfigureServices(services);
        services.AddWeaver();
        services.AddWeaver();

        Assert.Single(services, x => x.ServiceType == typeof(ReleaseWeaverSessionLeasesTerminator));
        var registration = Assert.Single(services.Select(x => x.ImplementationInstance).OfType<ShellTerminatorRegistration>(),
            x => x.TerminatorType == typeof(ReleaseWeaverSessionLeasesTerminator));
        Assert.Equal(LifecyclePhase.Default, registration.Phase);
    }

    [Fact]
    public async Task The_terminator_releases_every_loaded_lease_and_the_owner_then_refuses_new_work()
    {
        await using var provider = WeaverTestServices.Create();
        var sessions = provider.GetRequiredService<IWeaverSessionService>();
        var store = provider.GetRequiredService<IWeaverSessionRecordStore>();
        var first = await CreateAsync(sessions);
        var second = await CreateAsync(sessions);
        Assert.NotNull((await store.LoadAsync(first.Id))!.LeaseExpiresAt);

        await provider.GetRequiredService<ReleaseWeaverSessionLeasesTerminator>().TerminateAsync();

        Assert.Null((await store.LoadAsync(first.Id))!.LeaseExpiresAt);
        Assert.Null((await store.LoadAsync(second.Id))!.LeaseExpiresAt);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => sessions.FindAsync("not-loaded-yet"));
    }

    [Fact]
    public async Task A_replaced_session_service_owns_its_own_shutdown()
    {
        await using var provider = WeaverTestServices.Create(services =>
            services.Replace(ServiceDescriptor.Singleton<IWeaverSessionService>(new UnusedSessionService())));

        await provider.GetRequiredService<ReleaseWeaverSessionLeasesTerminator>().TerminateAsync();
    }

    private static Task<WeaverSession> CreateAsync(IWeaverSessionService sessions) => sessions.CreateAsync(new("tenant-1", "actor-1",
        "conversation-1", DeterministicWeaverProvider.Id, "explain", null, WeaverPolicy.Default, new Dictionary<string, string>()));

    private sealed class UnusedSessionService : IWeaverSessionService
    {
        private static Exception Unused() => new InvalidOperationException("A replaced session service is never shut down by the default terminator.");
        public Task<WeaverSession> CreateAsync(WeaverSessionCreateRequest request, CancellationToken cancellationToken = default) => throw Unused();
        public Task<WeaverSession?> FindAsync(string sessionId, CancellationToken cancellationToken = default) => throw Unused();
        public Task<WeaverMessage> AddMessageAsync(string sessionId, WeaverMessageCreateRequest request, CancellationToken cancellationToken = default) => throw Unused();
        public Task<IReadOnlyCollection<WeaverMessage>> ListMessagesAsync(string sessionId, CancellationToken cancellationToken = default) => throw Unused();
        public Task<WeaverMessage?> FindMessageAsync(string sessionId, string messageId, CancellationToken cancellationToken = default) => throw Unused();
        public Task<WeaverMessage?> FindLatestMessageAsync(string sessionId, WeaverRole? role = null, CancellationToken cancellationToken = default) => throw Unused();
        public Task SetMessageStatusAsync(string sessionId, string messageId, WeaverMessageStatus status, CancellationToken cancellationToken = default) => throw Unused();
        public Task AddContextAsync(string sessionId, IReadOnlyCollection<WeaverContextAttachment> attachments, CancellationToken cancellationToken = default) => throw Unused();
        public Task<IReadOnlyCollection<WeaverContextAttachment>> ListContextAsync(string sessionId, CancellationToken cancellationToken = default) => throw Unused();
    }
}
