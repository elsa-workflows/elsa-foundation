using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.ProviderTests;

public sealed class ProviderRegistrationTests
{
    [Theory]
    [InlineData("Sqlite", typeof(ExecutionPlacementSqliteDbContext), ExecutionPlacementSqliteDbContext.ExpectedProviderName, "Data Source=:memory:")]
    [InlineData("SqlServer", typeof(ExecutionPlacementSqlServerDbContext), ExecutionPlacementSqlServerDbContext.ExpectedProviderName, "Server=localhost;Database=unused;User Id=sa;Password=Pass@word1;TrustServerCertificate=True")]
    [InlineData("PostgreSql", typeof(ExecutionPlacementPostgreSqlDbContext), ExecutionPlacementPostgreSqlDbContext.ExpectedProviderName, "Host=localhost;Database=unused;Username=unused;Password=unused")]
    [InlineData("MySql", typeof(ExecutionPlacementMySqlDbContext), ExecutionPlacementMySqlDbContext.ExpectedProviderName, "Server=localhost;Database=unused;User Id=unused;Password=unused")]
    public void Every_provider_registration_builds_and_resolves_its_selected_context_and_store(
        string providerName,
        Type expectedContextType,
        string expectedProviderName,
        string connectionString)
    {
        var services = new ServiceCollection();
        services.AddPersistenceCore("provider-registration-scope");
        services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(new()
        {
            Provider = providerName,
            ConnectionString = connectionString
        });

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExecutionPlacementDbContext>();

        Assert.IsType(expectedContextType, context);
        Assert.Equal(expectedProviderName, context.Database.ProviderName);
        Assert.IsType<EfExecutionPlacementStore>(scope.ServiceProvider.GetRequiredService<IExecutionPlacementStore>());
    }
}
