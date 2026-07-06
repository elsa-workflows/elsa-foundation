using Elsa.Events.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Events;
using Elsa.Primitives.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.EFCore.Services;

public sealed class EFCoreBulkUpsert<TDbContext, TEntity>(IDbContextFactory<TDbContext> dbContextFactory, IServiceProvider serviceProvider, IUpsertCommandGenerator upsertCommandGenerator)
    : IBulkUpsertCommand<TEntity>

    where TDbContext : DbContext
    where TEntity : Entity
{
    private async Task<TDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => await dbContextFactory.CreateDbContextAsync(cancellationToken);

    /// <inheritdoc />
    public async Task BulkUpsertAsync(
        IList<TEntity> entities,
        int batchSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (entities.Count == 0)
            return;

        await using var dbContext = await CreateDbContextAsync(cancellationToken);

        try
        {
            // Loop through batched entities
            foreach (var batch in entities.Chunk(batchSize))
            {
                // This command bypasses DbContext.SaveChanges (which is overridden in
                // ElsaDbContextBase to publish OnEntitySaving), so we publish the event here
                // ourselves — the single ApplyEntitySavingHandlers aggregator then runs every
                // registered IEntitySavingHandler<,> so source columns are populated before the
                // raw upsert SQL is generated.
                await PublishEntitySavingEvents(dbContext, batch, cancellationToken);

                // Generate SQL and parameters
                var generatedCommand = upsertCommandGenerator.Generate(dbContext, batch, e => e.Id);

                await dbContext.Database.ExecuteSqlRawAsync(generatedCommand.Command, generatedCommand.Parameters, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            var handler = serviceProvider.GetService<IDbExceptionHandler>();

            if (handler != null)
            {
                await handler.HandleAsync(ex, cancellationToken);
            }

            throw;
        }
    }

    private async Task PublishEntitySavingEvents(TDbContext dbContext, IEnumerable<TEntity> entities, CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var eventPublisher = scope.ServiceProvider.GetService<IInlineEventPublisher>();
        if (eventPublisher is null)
            return;

        // dbContext.Entry(entity) materialises the EntityEntry the event carries; it begins
        // tracking the entity as Unchanged, which is inert here since this path never calls
        // SaveChanges (it executes raw upsert SQL instead).
        foreach (var entity in entities)
            await eventPublisher.Publish(new OnEntitySaving(dbContext, dbContext.Entry(entity)), cancellationToken);
    }
}