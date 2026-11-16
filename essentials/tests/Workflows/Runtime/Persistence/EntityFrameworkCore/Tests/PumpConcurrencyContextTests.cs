using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class PumpConcurrencyContextTests
{
    [Theory]
    [InlineData("timer")]
    [InlineData("recurring")]
    [InlineData("recovery")]
    public async Task Pump_UsesIndependentEfContexts_WhileTwoDispatchQueriesAreBlocked(string kind)
    {
        await using var keeper = new SqliteConnection($"Data Source=file:pump-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False");
        await keeper.OpenAsync();
        await PumpConcurrencyContextContract.RunAsync(kind, "Sqlite", keeper.ConnectionString,
            (services, interceptor) => services.ConfigureDbContext<RuntimeSqliteDbContext>(o => o.AddInterceptors(interceptor)));
    }
}
