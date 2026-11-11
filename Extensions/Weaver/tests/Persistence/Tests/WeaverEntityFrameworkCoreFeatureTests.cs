using System.Reflection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Extensions;
using Elsa.Weaver.Persistence.EntityFrameworkCore;
using Elsa.Weaver.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Weaver.Persistence.EntityFrameworkCore.Stores;
using Elsa.Weaver.Services;
using Elsa.Events;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Weaver.Persistence.Tests;

/// <summary>#3441 T028: the feature's provider selection, its replacement of the store and audit trio, and its explicit selection.</summary>
public sealed class WeaverEntityFrameworkCoreFeatureTests
{
    [Fact]
    public void The_feature_is_an_opt_in_shell_feature_over_the_weaver_and_secrets_features()
    {
        var feature = typeof(WeaverEntityFrameworkCoreFeature).GetCustomAttribute<ShellFeatureAttribute>()!;

        Assert.Equal("WeaverEntityFrameworkCore", feature.Name);
        Assert.Equal(["Weaver", "Secrets"], feature.DependsOn.Cast<string>());
        Assert.Equal("Sqlite", new WeaverEntityFrameworkCoreFeature().Provider);
    }

    [Theory]
    [InlineData("Sqlite", typeof(WeaverSqliteDbContext))]
    [InlineData("SqlServer", typeof(WeaverSqlServerDbContext))]
    [InlineData("PostgreSql", typeof(WeaverPostgreSqlDbContext))]
    [InlineData("MySql", typeof(WeaverMySqlDbContext))]
    public void Each_provider_registers_its_own_context_behind_the_module_context(string provider, Type context)
    {
        var services = Composition();
        new WeaverEntityFrameworkCoreFeature { Provider = provider }.ConfigureServices(services);

        Assert.Contains(services, x => x.ServiceType == context);
        Assert.Single(services, x => x.ServiceType == typeof(WeaverDbContext));
        Assert.DoesNotContain(services, x => x.ServiceType != context && x.ServiceType.IsSubclassOf(typeof(WeaverDbContext)));
    }

    [Fact]
    public void An_unsupported_provider_or_lease_is_refused_at_registration()
    {
        Assert.ThrowsAny<Exception>(() => new WeaverEntityFrameworkCoreFeature { Provider = "Oracle" }.ConfigureServices(Composition()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WeaverEntityFrameworkCoreFeature { LeaseSeconds = 10 }.ConfigureServices(Composition()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WeaverEntityFrameworkCoreFeature { LeaseSeconds = 86_401 }.ConfigureServices(Composition()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_feature_replaces_the_record_store_and_the_audit_trio_together_in_either_order(bool weaverFirst)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new EventsFeature().ConfigureServices(services);
        if (weaverFirst) services.AddWeaver();
        services.AddWeaverEntityFrameworkCore(new() { ConnectionString = "Data Source=:memory:" });
        if (!weaverFirst) services.AddWeaver();
        AddSecrets(services);

        Assert.Single(services, x => x.ServiceType == typeof(IWeaverSessionRecordStore));
        Assert.Single(services, x => x.ServiceType == typeof(IWeaverAuditSink));
        Assert.Single(services, x => x.ServiceType == typeof(IWeaverAuditReader));
        Assert.Single(services, x => x.ServiceType == typeof(IWeaverAuditOwnership));
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<WeaverRegistrationValidator>().Validate();
        Assert.True(Assert.IsType<EfWeaverSessionRecordStore>(provider.GetRequiredService<IWeaverSessionRecordStore>()).SupportsFencing);
        var sink = Assert.IsType<EfWeaverAuditStore>(provider.GetRequiredService<IWeaverAuditSink>());
        Assert.Same(sink, provider.GetRequiredService<IWeaverAuditReader>());
        Assert.Same(sink, provider.GetRequiredService<IWeaverAuditOwnership>());
        using var scope = provider.CreateScope();
        // The default owner is kept and backed by the durable store, so durable recovery stays supported.
        Assert.IsType<InMemoryWeaverSessionService>(scope.ServiceProvider.GetRequiredService<IWeaverSessionService>());
        Assert.Equal(WeaverDurableRecoverySupport.Supported, scope.ServiceProvider.GetRequiredService<WeaverDurableRecovery>().Evaluate());
    }

    [Fact]
    public void A_second_selection_or_a_selection_over_an_unowned_store_throws()
    {
        var twice = Composition();
        twice.AddWeaverEntityFrameworkCore(new());
        Assert.Throws<InvalidOperationException>(() => twice.AddWeaverEntityFrameworkCore(new()));
        Assert.Throws<InvalidOperationException>(() => twice.SelectWeaverSessionRecordStore(_ => throw new InvalidOperationException()));

        var replaced = Composition();
        replaced.Replace(ServiceDescriptor.Singleton<IWeaverSessionRecordStore>(_ => throw new InvalidOperationException()));
        Assert.Throws<InvalidOperationException>(() => replaced.AddWeaverEntityFrameworkCore(new()));

        var auditReplaced = Composition();
        auditReplaced.Replace(ServiceDescriptor.Singleton<IWeaverAuditReader>(_ => throw new InvalidOperationException()));
        Assert.Throws<InvalidOperationException>(() => auditReplaced.AddWeaverStores<WeaverDbContext>());
    }

    [Fact]
    public async Task An_activated_shell_migrates_and_a_restarted_host_hydrates_the_session_and_its_audit_in_database_time()
    {
        using var database = new WeaverSqliteDatabase();
        var connection = $"Data Source={Path.Combine(Path.GetDirectoryName(database.Path)!, Guid.NewGuid().ToString("N") + ".db")};Pooling=False";
        WeaverSession session;
        await using (var first = await ActivateAsync(connection, database.Keys))
        {
            var sessions = first.GetRequiredService<IWeaverSessionService>();
            session = await sessions.CreateAsync(new("tenant-1", "actor-1", "conversation-1", "provider", "explain", "Durable", WeaverPolicy.Default,
                new Dictionary<string, string>()) { Workspace = "workspace-1" });
            await sessions.AddMessageAsync(session.Id, new(WeaverRole.User, "hello", WeaverMessageStatus.Pending, null, [], []));
            // A graceful shutdown gives back the leases this host holds, while its store can still reach the database.
            await ((IAsyncDisposable)sessions).DisposeAsync();
        }

        await using var second = await ActivateAsync(connection, database.Keys);
        var restored = second.GetRequiredService<IWeaverSessionService>();
        Assert.Equal(session.Id, (await restored.FindAsync(session.Id))!.Id);
        Assert.Equal(["hello"], (await restored.ListMessagesAsync(session.Id)).Select(x => x.Content));
        var listed = await restored.ListOwnedAsync(new("actor-1", "tenant-1", "workspace-1"));
        Assert.Equal([session.Id], listed.Page!.Items.Select(x => x.Id));
        var audit = await second.GetRequiredService<IWeaverAuditReader>().ListOwnedAsync(new("actor-1", "tenant-1", session.Id, 10));
        Assert.Contains(audit.Events, x => x.Kind == WeaverAuditEventKind.SessionCreated);
    }

    private static async Task<ServiceProvider> ActivateAsync(string connection, ISecretKeyRing keys)
    {
        var services = Composition();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(keys);
        services.AddSingleton<ISecretValueProtector, DefaultSecretValueProtector>();
        new WeaverEntityFrameworkCoreFeature { ConnectionString = connection }.ConfigureServices(services);
        var provider = services.BuildServiceProvider();
        foreach (var initializer in provider.GetServices<IShellInitializer>())
            await initializer.InitializeAsync(CancellationToken.None);
        return provider;
    }

    private static ServiceCollection Composition()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new EventsFeature().ConfigureServices(services);
        services.AddWeaver();
        return services;
    }

    private static void AddSecrets(IServiceCollection services)
    {
        services.AddSingleton<ISecretKeyRing>(new TestKeyRing());
        services.AddSingleton<ISecretValueProtector, DefaultSecretValueProtector>();
    }
}
