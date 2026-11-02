using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class PumpConcurrencyContextPostgreSqlTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableTheory]
    [InlineData("timer")]
    [InlineData("recurring")]
    [InlineData("recovery")]
    public Task PostgreSql_pump_dispatch_and_renewal_have_independent_contexts(string kind)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Native provider unavailable.");
        return PumpConcurrencyContextContract.RunAsync(kind, "PostgreSql", fixture.ConnectionString,
            (services, interceptor) => services.ConfigureDbContext<RuntimePostgreSqlDbContext>(o => o.AddInterceptors(interceptor)));
    }
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class PumpConcurrencyContextSqlServerTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableTheory]
    [InlineData("timer")]
    [InlineData("recurring")]
    [InlineData("recovery")]
    public Task SqlServer_pump_dispatch_and_renewal_have_independent_contexts(string kind)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Native provider unavailable.");
        return PumpConcurrencyContextContract.RunAsync(kind, "SqlServer", fixture.ConnectionString,
            (services, interceptor) => services.ConfigureDbContext<RuntimeSqlServerDbContext>(o => o.AddInterceptors(interceptor)));
    }
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class PumpConcurrencyContextMySqlTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableTheory]
    [InlineData("timer")]
    [InlineData("recurring")]
    [InlineData("recovery")]
    public Task MySql_pump_dispatch_and_renewal_have_independent_contexts(string kind)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Native provider unavailable.");
        return PumpConcurrencyContextContract.RunAsync(kind, "MySql", fixture.ConnectionString,
            (services, interceptor) => services.ConfigureDbContext<RuntimeMySqlDbContext>(o => o.AddInterceptors(interceptor)));
    }
}
