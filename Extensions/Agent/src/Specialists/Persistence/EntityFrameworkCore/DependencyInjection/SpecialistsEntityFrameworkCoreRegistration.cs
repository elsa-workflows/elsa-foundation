using Elsa.Agent.Specialists.Core.DependencyInjection;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Tasks.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.DependencyInjection;

public static class SpecialistsEntityFrameworkCoreRegistration
{
    private static readonly EfModuleBinding Binding = EfModuleBinding.For(typeof(SpecialistsDbContext));
    public static IServiceCollection AddSpecialistsEntityFrameworkCore(this IServiceCollection services, SpecialistsEntityFrameworkCoreOptions options)
    {
        services.AddAgentSourcesEntityFrameworkCore(options);
        services.AddSpecialistTurnStore<SpecialistsDbContext>();
        return services;
    }

    /// <summary>Source authoring persistence without installing the model-turn runtime.</summary>
    public static IServiceCollection AddAgentSourcesEntityFrameworkCore(this IServiceCollection services, SpecialistsEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var add = Binding.Select<Action<IServiceCollection, SpecialistsEntityFrameworkCoreOptions>>(options.Provider,
            AddContext<SpecialistsSqliteDbContext>, AddContext<SpecialistsSqlServerDbContext>, AddContext<SpecialistsPostgreSqlDbContext>, AddContext<SpecialistsMySqlDbContext>);
        services.AddAgentSourceStore<SpecialistsDbContext>();
        services.AddSingleton(options);
        add(services, options);
        services.AddEfModuleMigrations<SpecialistsDbContext>(options.Provider);
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
    public static IServiceCollection AddSpecialistTurnStore<TContext>(this IServiceCollection services) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.ReplaceSpecialistTurnStore(provider => new EfSpecialistTurnStore(provider.GetRequiredService<TContext>(),
            provider.GetRequiredService<IPersistenceAccessContextAccessor>()));
        services.AddPersistenceCore();
        return services;
    }
    private static void AddContext<TContext>(IServiceCollection services, SpecialistsEntityFrameworkCoreOptions options) where TContext : SpecialistsDbContext
    {
        Binding.AddContext<TContext>(services, options.Pooling, (provider, builder) => Binding.Apply(builder, provider,
            options.Provider, options.ConnectionString, options.ConnectionName, options.Schema));
        services.AddScoped<SpecialistsDbContext>(provider => provider.GetRequiredService<TContext>());
    }
}

public sealed class SpecialistsEntityFrameworkCoreOptions
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
