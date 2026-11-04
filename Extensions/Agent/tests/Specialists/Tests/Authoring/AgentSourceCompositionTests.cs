using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.DependencyInjection;
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
}
