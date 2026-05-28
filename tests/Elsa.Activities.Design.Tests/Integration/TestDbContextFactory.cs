using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Elsa.Activities.Design.Tests.Unit;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Activities.Design.Tests.Integration;

/// <summary>
/// Bridges <see cref="ActivitiesDesignTestHost"/> to <see cref="IDbContextFactory{TContext}"/>
/// — the lookup expects the EF Core factory abstraction; the test host exposes a manual
/// <c>CreateContext</c> against the in-memory SQLite connection.
/// </summary>
internal sealed class TestDbContextFactory(ActivitiesDesignTestHost host) : IDbContextFactory<ActivitiesDesignDbContext>
{
    public ActivitiesDesignDbContext CreateDbContext() => host.CreateContext();

    public Task<ActivitiesDesignDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(host.CreateContext());
}
