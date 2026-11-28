using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Inspection.Api;
using Elsa.Agent.Journal;
using Elsa.Agent.Persistence.EntityFrameworkCore;
using Elsa.Agent.Persistence.EntityFrameworkCore.Stores;
using Elsa.Locking.FileSystem;
using Elsa.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Tests.Runtime;

public sealed class AgentJournalFeatureCompositionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Journal_selectors_activate_without_installing_classic_agent_execution(bool useEntityFramework)
    {
        var assemblies = new[]
        {
            typeof(AgentRuntimeFeature).Assembly,
            typeof(AgentInspectionApiFeature).Assembly,
            typeof(AgentEntityFrameworkCoreFeature).Assembly,
            typeof(TasksFeature).Assembly
        };
        var featureNames = FeatureDiscovery.DiscoverFeatures(assemblies).Select(feature => feature.Id).ToArray();
        Assert.DoesNotContain("AgentRuntime", featureNames);
        Assert.DoesNotContain("AgentInspectionApi", featureNames);
        Assert.Contains("AgentInMemoryJournal", featureNames);
        Assert.Contains("AgentEntityFrameworkCore", featureNames);

        const string shellName = "agent-journal";
        var directory = Directory.CreateTempSubdirectory("elsa-agent-journal-");
        var database = Path.Combine(directory.FullName, "journal.db");
        string feature = useEntityFramework ? "AgentEntityFrameworkCore" : "AgentInMemoryJournal";
        try
        {
            var settings = new Dictionary<string, string?> { [$"CShells:Shells:{shellName}:Features:{feature}"] = null };
            if (useEntityFramework)
                settings[$"CShells:Shells:{shellName}:Features:{feature}:ConnectionString"] = $"Data Source={database};Pooling=False";
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            var services = new ServiceCollection().AddLogging();
            if (useEntityFramework)
            {
                var locks = directory.CreateSubdirectory("locks");
                new FileSystemLockingFeature { LocksFolderPath = locks.FullName }.ConfigureServices(services);
            }
            services.AddSingleton<IConfiguration>(configuration);
            services.AddCShells(shells => shells.WithAssemblies(assemblies).WithConfigurationProvider(configuration));
            await using var host = services.BuildServiceProvider();

            var shell = await host.GetRequiredService<IShellRegistry>().GetOrActivateAsync(shellName);
            await using var scope = shell.BeginScope();
            var selected = scope.ServiceProvider;
            Assert.Null(selected.GetService<IAgentInvocationInspector>());
            if (useEntityFramework)
            {
                Assert.IsType<EfAgentTurnStore>(selected.GetRequiredService<IAgentTurnStore>());
                Assert.NotNull(selected.GetRequiredService<IAgentSourceReader>());
                Assert.NotNull(selected.GetRequiredService<IAgentSourceWriter>());
                var context = selected.GetRequiredService<AgentDbContext>();
                Assert.Equal(database, context.Database.GetDbConnection().DataSource);
                Assert.NotEmpty(await context.Database.GetAppliedMigrationsAsync());
                Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            }
            else
            {
                Assert.IsType<InMemoryAgentTurnStore>(selected.GetRequiredService<IAgentTurnStore>());
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
