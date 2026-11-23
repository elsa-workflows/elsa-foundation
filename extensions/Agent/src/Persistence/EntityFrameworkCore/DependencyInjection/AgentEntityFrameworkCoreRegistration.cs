using Elsa.Agent.Core.DependencyInjection;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Tasks.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Persistence.EntityFrameworkCore.DependencyInjection;

public static class AgentEntityFrameworkCoreRegistration
{
    private static readonly EfModuleBinding Binding = EfModuleBinding.For(typeof(AgentDbContext));
    public static IServiceCollection AddAgentEntityFrameworkCore(this IServiceCollection services, AgentEntityFrameworkCoreOptions options)
    {
        services.AddAgentSourcesEntityFrameworkCore(options);
        services.AddAgentTurnStore<AgentDbContext>();
        return services;
    }

    /// <summary>Source authoring persistence without installing the model-turn runtime.</summary>
    public static IServiceCollection AddAgentSourcesEntityFrameworkCore(this IServiceCollection services, AgentEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var add = Binding.Select<Action<IServiceCollection, AgentEntityFrameworkCoreOptions>>(options.Provider,
            AddContext<AgentSqliteDbContext>, AddContext<AgentSqlServerDbContext>, AddContext<AgentPostgreSqlDbContext>, AddContext<AgentMySqlDbContext>);
        services.AddAgentSourceStore<AgentDbContext>();
        services.AddSingleton(options);
        add(services, options);
        services.AddEfModuleMigrations<AgentDbContext>(options.Provider);
        return services;
    }

    /// <summary>Installs typed source ports in an ordinary consumer-owned context mapping AddSourceRevisions.</summary>
    public static IServiceCollection AddAgentSourceStore<TContext>(this IServiceCollection services) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(x => x.ServiceType == typeof(IAgentSourceReader) || x.ServiceType == typeof(IAgentSourceWriter)))
            throw new InvalidOperationException("Agent source persistence requires exactly one explicitly selected reader/writer backend per shell. Remove the previous selection explicitly before choosing another.");
        // Re-entering this explicit selection boundary after both ports were removed retires its old guard.
        foreach (var previous in services.Where(x => x.ImplementationInstance is AgentSourceRegistrationStartupTask).ToArray()) services.Remove(previous);
        // The ports share the scoped context; an extra concrete registration would outlive explicit reselection.
        EfAgentSources<TContext> Create(IServiceProvider provider) => new(provider.GetRequiredService<TContext>(), provider.GetRequiredService<IPersistenceAccessContextAccessor>());
        var reader = ServiceDescriptor.Scoped<IAgentSourceReader>(Create);
        var writer = ServiceDescriptor.Scoped<IAgentSourceWriter>(Create);
        services.Add(reader); services.Add(writer);
        services.AddSingleton<IStartupTask>(new AgentSourceRegistrationStartupTask(services, reader, writer));
        services.AddPersistenceCore();
        return services;
    }
    /// <summary>Uses fresh owned write contexts; the caller's tracked work and transaction are not enlisted.</summary>
    public static IServiceCollection AddAgentTurnStore<TContext>(this IServiceCollection services) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.ReplaceAgentTurnStore(provider => new EfAgentTurnStore(provider.GetRequiredService<TContext>(),
            provider.GetRequiredService<IPersistenceAccessContextAccessor>()));
        services.AddPersistenceCore();
        return services;
    }
    private static void AddContext<TContext>(IServiceCollection services, AgentEntityFrameworkCoreOptions options) where TContext : AgentDbContext
    {
        Binding.AddContext<TContext>(services, options.Pooling, (provider, builder) => Binding.Apply(builder, provider,
            options.Provider, options.ConnectionString, options.ConnectionName, options.Schema));
        services.AddScoped<AgentDbContext>(provider => provider.GetRequiredService<TContext>());
    }
}

public sealed class AgentEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
    public string? Schema { get; set; }
    public bool Pooling { get; set; }
}

internal sealed class AgentSourceRegistrationStartupTask(IServiceCollection services, ServiceDescriptor reader, ServiceDescriptor writer) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var readers = services.Where(x => x.ServiceType == typeof(IAgentSourceReader)).ToArray();
        var writers = services.Where(x => x.ServiceType == typeof(IAgentSourceWriter)).ToArray();
        if (readers.Length != 1 || writers.Length != 1 || !ReferenceEquals(readers[0], reader) || !ReferenceEquals(writers[0], writer))
            throw new InvalidOperationException("Agent source persistence requires exactly one selected reader/writer backend; registrations changed outside its selection boundary.");
        return Task.CompletedTask;
    }
}
