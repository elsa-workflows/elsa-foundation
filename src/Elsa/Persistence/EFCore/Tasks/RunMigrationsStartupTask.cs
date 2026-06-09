using Elsa.Persistence.EFCore.Options;
using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Elsa.Persistence.EFCore.Tasks;

/// <summary>
/// Executes EF Core migrations using the specified <see cref="ElsaDbContextBase"/> type.
/// </summary>
[UsedImplicitly]
[SingleNodeTask]
[Order(-100)]
public class RunMigrationsStartupTask<TDbContext>(IDbContextFactory<TDbContext> dbContextFactory, IOptions<MigrationOptions> options) : IStartupTask
    where TDbContext : DbContext
{
    /// <inheritdoc />
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        options.Value.RunMigrations.TryGetValue($"{typeof(TDbContext)}", out bool shouldRunMigrations);

        if (!shouldRunMigrations)
            return;

        var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}