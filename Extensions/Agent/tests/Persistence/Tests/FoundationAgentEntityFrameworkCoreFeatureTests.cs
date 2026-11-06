using System.Reflection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Extensions;
using Elsa.Agent.Persistence.EntityFrameworkCore;
using Elsa.Agent.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Agent.Persistence.EntityFrameworkCore.Stores;
using Elsa.Agent.Services;
using Elsa.Events;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Persistence.Tests;

/// <summary>#3441 T028: the feature's provider selection, its replacement of the store and audit trio, and its explicit selection.</summary>
public sealed class FoundationAgentEntityFrameworkCoreFeatureTests
{
    [Fact]
    public void The_feature_is_an_opt_in_shell_feature_over_the_agent_and_secrets_features()
    {
        var feature = typeof(FoundationAgentEntityFrameworkCoreFeature).GetCustomAttribute<ShellFeatureAttribute>()!;

        Assert.Equal("FoundationAgentEntityFrameworkCore", feature.Name);
        Assert.Equal(["FoundationAgentAbstractions", "Secrets"], feature.DependsOn.Cast<string>());
        Assert.Equal("Sqlite", new FoundationAgentEntityFrameworkCoreFeature().Provider);
    }

    [Theory]
    [InlineData("Sqlite", typeof(AgentSqliteDbContext))]
    [InlineData("SqlServer", typeof(AgentSqlServerDbContext))]
    [InlineData("PostgreSql", typeof(AgentPostgreSqlDbContext))]
    [InlineData("MySql", typeof(AgentMySqlDbContext))]
    public void Each_provider_registers_its_own_context_behind_the_module_context(string provider, Type context)
    {
        var services = Composition();
        new FoundationAgentEntityFrameworkCoreFeature { Provider = provider }.ConfigureServices(services);

        Assert.Contains(services, x => x.ServiceType == context);
        Assert.Single(services, x => x.ServiceType == typeof(AgentDbContext));
        Assert.DoesNotContain(services, x => x.ServiceType != context && x.ServiceType.IsSubclassOf(typeof(AgentDbContext)));
    }

    [Fact]
    public void An_unsupported_provider_or_lease_is_refused_at_registration()
    {
        Assert.ThrowsAny<Exception>(() => new FoundationAgentEntityFrameworkCoreFeature { Provider = "Oracle" }.ConfigureServices(Composition()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FoundationAgentEntityFrameworkCoreFeature { LeaseSeconds = 10 }.ConfigureServices(Composition()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FoundationAgentEntityFrameworkCoreFeature { LeaseSeconds = 86_401 }.ConfigureServices(Composition()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_feature_replaces_the_record_store_and_the_audit_trio_together_in_either_order(bool agentFirst)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new EventsFeature().ConfigureServices(services);
        if (agentFirst) services.AddFoundationAgentAbstractions();
        services.AddAgentEntityFrameworkCore(new() { ConnectionString = "Data Source=:memory:" });
        if (!agentFirst) services.AddFoundationAgentAbstractions();
        AddSecrets(services);

        Assert.Single(services, x => x.ServiceType == typeof(IAgentSessionRecordStore));
        Assert.Single(services, x => x.ServiceType == typeof(IAgentAuditSink));
        Assert.Single(services, x => x.ServiceType == typeof(IAgentAuditReader));
        Assert.Single(services, x => x.ServiceType == typeof(IAgentAuditOwnership));
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<AgentRegistrationValidator>().Validate();
        Assert.True(Assert.IsType<EfAgentSessionRecordStore>(provider.GetRequiredService<IAgentSessionRecordStore>()).SupportsFencing);
        var sink = Assert.IsType<EfAgentAuditStore>(provider.GetRequiredService<IAgentAuditSink>());
        Assert.Same(sink, provider.GetRequiredService<IAgentAuditReader>());
        Assert.Same(sink, provider.GetRequiredService<IAgentAuditOwnership>());
        using var scope = provider.CreateScope();
        // The default owner is kept and backed by the durable store, so durable recovery stays supported.
        Assert.IsType<InMemoryAgentSessionService>(scope.ServiceProvider.GetRequiredService<IAgentSessionService>());
        Assert.Equal(AgentDurableRecoverySupport.Supported, scope.ServiceProvider.GetRequiredService<AgentDurableRecovery>().Evaluate());
    }

    [Fact]
    public void A_second_selection_or_a_selection_over_an_unowned_store_throws()
    {
        var twice = Composition();
        twice.AddAgentEntityFrameworkCore(new());
        Assert.Throws<InvalidOperationException>(() => twice.AddAgentEntityFrameworkCore(new()));
        Assert.Throws<InvalidOperationException>(() => twice.SelectAgentSessionRecordStore(_ => throw new InvalidOperationException()));

        var replaced = Composition();
        replaced.Replace(ServiceDescriptor.Singleton<IAgentSessionRecordStore>(_ => throw new InvalidOperationException()));
        Assert.Throws<InvalidOperationException>(() => replaced.AddAgentEntityFrameworkCore(new()));

        var auditReplaced = Composition();
        auditReplaced.Replace(ServiceDescriptor.Singleton<IAgentAuditReader>(_ => throw new InvalidOperationException()));
        Assert.Throws<InvalidOperationException>(() => auditReplaced.AddAgentStores<AgentDbContext>());
    }

    [Fact]
    public async Task An_activated_shell_migrates_and_a_restarted_host_hydrates_the_session_and_its_audit_in_database_time()
    {
        using var database = new AgentSqliteDatabase();
        var connection = $"Data Source={Path.Combine(Path.GetDirectoryName(database.Path)!, Guid.NewGuid().ToString("N") + ".db")};Pooling=False";
        AgentSession session;
        await using (var first = await ActivateAsync(connection, database.Keys))
        {
            var sessions = first.GetRequiredService<IAgentSessionService>();
            session = await sessions.CreateAsync(new("tenant-1", "actor-1", "conversation-1", "provider", "explain", "Durable", AgentPolicy.Default,
                new Dictionary<string, string>()) { Workspace = "workspace-1" });
            await sessions.AddMessageAsync(session.Id, new(AgentRole.User, "hello", AgentMessageStatus.Pending, null, [], []));
            // A graceful shutdown gives back the leases this host holds, while its store can still reach the database.
            await ((IAsyncDisposable)sessions).DisposeAsync();
        }

        await using var second = await ActivateAsync(connection, database.Keys);
        var restored = second.GetRequiredService<IAgentSessionService>();
        Assert.Equal(session.Id, (await restored.FindAsync(session.Id))!.Id);
        Assert.Equal(["hello"], (await restored.ListMessagesAsync(session.Id)).Select(x => x.Content));
        var listed = await restored.ListOwnedAsync(new("actor-1", "tenant-1", "workspace-1"));
        Assert.Equal([session.Id], listed.Page!.Items.Select(x => x.Id));
        var audit = await second.GetRequiredService<IAgentAuditReader>().ListOwnedAsync(new("actor-1", "tenant-1", session.Id, 10));
        Assert.Contains(audit.Events, x => x.Kind == AgentAuditEventKind.SessionCreated);
    }

    private static async Task<ServiceProvider> ActivateAsync(string connection, ISecretKeyRing keys)
    {
        var services = Composition();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(keys);
        services.AddSingleton<ISecretValueProtector, DefaultSecretValueProtector>();
        new FoundationAgentEntityFrameworkCoreFeature { ConnectionString = connection }.ConfigureServices(services);
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
        services.AddFoundationAgentAbstractions();
        return services;
    }

    private static void AddSecrets(IServiceCollection services)
    {
        services.AddSingleton<ISecretKeyRing>(new TestKeyRing());
        services.AddSingleton<ISecretValueProtector, DefaultSecretValueProtector>();
    }
}
