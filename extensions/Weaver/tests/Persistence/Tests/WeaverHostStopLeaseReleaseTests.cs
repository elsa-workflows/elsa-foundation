using CShells.DependencyInjection;
using CShells.Lifecycle;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Persistence.EntityFrameworkCore;
using Elsa.Weaver.Persistence.EntityFrameworkCore.Entities;
using Elsa.Events;
using Elsa.Secrets.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elsa.Weaver.Persistence.Tests;

/// <summary>
/// #3441 story 2.1: a graceful host stop hands the durable sessions a shell's owner holds back at once, on a real CShells
/// host whose shell runs the packaged EF store. Disposing the shell's provider is too late for that: a disposed provider
/// opens no scope, so the store could no longer reach its database and every lease would have to lapse instead.
/// </summary>
public sealed class WeaverHostStopLeaseReleaseTests
{
    private const string ShellName = "weaver";

    [Fact]
    public async Task A_graceful_host_stop_releases_the_leases_of_the_sessions_its_shell_owns()
    {
        using var database = new WeaverSqliteDatabase();
        using var host = BuildHost(database.ConnectionString);
        await host.StartAsync();
        var shell = await host.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        string sessionId;
        await using (var scope = shell.ServiceProvider.CreateAsyncScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<IWeaverSessionService>();
            var session = await sessions.CreateAsync(new("tenant-1", "actor-1", "conversation-1", "provider", "explain", "Durable",
                WeaverPolicy.Default, new Dictionary<string, string>()));
            await sessions.AddMessageAsync(session.Id, new(WeaverRole.User, "hello", WeaverMessageStatus.Pending, null, [], []));
            sessionId = session.Id;
        }
        Assert.NotNull(await LeaseAsync(database, sessionId));

        await host.StopAsync();

        Assert.Null(await LeaseAsync(database, sessionId));
    }

    [Fact]
    public async Task A_shell_reload_hands_the_old_generations_sessions_to_the_new_generation_at_once()
    {
        using var database = new WeaverSqliteDatabase();
        using var host = BuildHost(database.ConnectionString);
        await host.StartAsync();
        var shells = host.Services.GetRequiredService<IShellRegistry>();
        var first = await shells.GetOrActivateAsync(ShellName);
        string sessionId;
        await using (var scope = first.ServiceProvider.CreateAsyncScope())
            sessionId = (await scope.ServiceProvider.GetRequiredService<IWeaverSessionService>().CreateAsync(new("tenant-1", "actor-1",
                "conversation-1", "provider", "explain", "Durable", WeaverPolicy.Default, new Dictionary<string, string>()))).Id;

        var reload = await shells.ReloadAsync(ShellName);
        Assert.Null(reload.Error);
        await reload.Drain!.WaitAsync();

        Assert.Null(await LeaseAsync(database, sessionId));
        await using (var scope = shells.GetActive(ShellName)!.ServiceProvider.CreateAsyncScope())
            Assert.Equal(sessionId, (await scope.ServiceProvider.GetRequiredService<IWeaverSessionService>().FindAsync(sessionId))!.Id);
        await host.StopAsync();
    }

    private static IHost BuildHost(string connectionString)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddCShells(shells => shells
            .WithAssemblies(typeof(EventsFeature).Assembly, typeof(SecretsFeature).Assembly, typeof(WeaverFeature).Assembly,
                typeof(WeaverEntityFrameworkCoreFeature).Assembly)
            .AddShell(ShellName, shell => shell
                .WithFeature<EventsFeature>()
                .WithFeature<SecretsFeature>(feature => feature.EncryptionKey = "weaver-host-stop-test-key")
                .WithFeature<WeaverFeature>()
                .WithFeature<WeaverEntityFrameworkCoreFeature>(feature => feature.ConnectionString = connectionString)));
        return builder.Build();
    }

    private static async Task<long?> LeaseAsync(WeaverSqliteDatabase database, string sessionId)
    {
        await using var db = database.Open();
        return await db.Set<WeaverSessionRow>().Where(x => x.SessionId == sessionId).Select(x => x.LeaseExpiresAtUtcTicks).SingleAsync();
    }
}
