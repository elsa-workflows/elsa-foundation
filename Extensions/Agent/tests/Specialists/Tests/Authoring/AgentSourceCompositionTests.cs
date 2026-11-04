using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Authoring;

public sealed class AgentSourceCompositionTests
{
    private sealed class Access : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new PersistenceScope("tenant"));
    }
    [Fact]
    public async Task Authoring_storage_works_without_installing_the_turn_runtime()
    {
        var directory = Path.Combine(Path.GetTempPath(), "elsa-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IPersistenceAccessContextAccessor, Access>();
            new AgentSourcesEntityFrameworkCoreFeature { Provider = "Sqlite", ConnectionString = "Data Source=" + Path.Combine(directory, "sources.db") + ";Pooling=False" }.ConfigureServices(services);
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SpecialistsDbContext>();
            await db.Database.MigrateAsync();
            var writer = scope.ServiceProvider.GetRequiredService<IAgentSourceWriter>();
            var content = new AgentSourceContent("Write", new("fixture", "connection", "model"), [], [], System.Text.Json.JsonSerializer.SerializeToElement(new { }), new());
            await writer.CreateAsync("writer", "draft", "Writer", content);
            Assert.Single((await scope.ServiceProvider.GetRequiredService<IAgentSourceReader>().ListAsync(null, 0, 10)).Items);
            Assert.Null(scope.ServiceProvider.GetService<ISpecialistTurnStore>());
        }
        finally { Directory.Delete(directory, true); }
    }
    private sealed class ApplicationSourceContext : DbContext;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Competing_source_contexts_are_refused_in_both_registration_orders(bool applicationFirst)
    {
        var services = new ServiceCollection();
        if (applicationFirst) services.AddAgentSourceStore<ApplicationSourceContext>();
        else services.AddSpecialistsEntityFrameworkCore(new() { Provider = "Sqlite" });
        var error = Assert.Throws<InvalidOperationException>(() =>
        {
            if (applicationFirst) services.AddSpecialistsEntityFrameworkCore(new() { Provider = "Sqlite" });
            else services.AddAgentSourceStore<ApplicationSourceContext>();
        });
        Assert.Contains("exactly one", error.Message);
        Assert.Single(services, x => x.ServiceType == typeof(IAgentSourceReader));
        Assert.Single(services, x => x.ServiceType == typeof(IAgentSourceWriter));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Startup_refuses_late_duplicate_or_substituted_source_ports(bool duplicate)
    {
        var services = new ServiceCollection();
        services.AddAgentSourceStore<ApplicationSourceContext>();
        var check = Assert.Single(services, x => x.ImplementationInstance is AgentSourceRegistrationStartupTask).ImplementationInstance as AgentSourceRegistrationStartupTask;
        if (!duplicate)
            foreach (var descriptor in services.Where(x => x.ServiceType == typeof(IAgentSourceWriter)).ToArray()) services.Remove(descriptor);
        services.AddScoped<IAgentSourceWriter>(_ => throw new InvalidOperationException("This foreign backend must never be resolved."));
        await Assert.ThrowsAsync<InvalidOperationException>(() => check!.ExecuteAsync(default));
    }

    private sealed class OtherApplicationSourceContext : DbContext;

    [Fact]
    public async Task Explicit_source_reselection_retires_the_previous_startup_guard()
    {
        var services = new ServiceCollection();
        services.AddAgentSourceStore<ApplicationSourceContext>();
        foreach (var descriptor in services.Where(x => x.ServiceType == typeof(IAgentSourceReader) || x.ServiceType == typeof(IAgentSourceWriter)).ToArray()) services.Remove(descriptor);
        services.AddScoped<OtherApplicationSourceContext>();
        services.AddAgentSourceStore<OtherApplicationSourceContext>();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        Assert.IsType<EfAgentSources<OtherApplicationSourceContext>>(scope.ServiceProvider.GetRequiredService<IAgentSourceReader>());
        Assert.IsType<EfAgentSources<OtherApplicationSourceContext>>(scope.ServiceProvider.GetRequiredService<IAgentSourceWriter>());
        var guards = services.Select(x => x.ImplementationInstance).OfType<AgentSourceRegistrationStartupTask>().ToArray();
        foreach (var guard in guards) await guard.ExecuteAsync(default);
        Assert.Single(guards);
    }

}
