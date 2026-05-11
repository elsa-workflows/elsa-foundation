using Elsa.Primitives.Entities;
using Elsa.Persistence.Core;
using Elsa.Persistence.EFCore.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.EFCore.Services
{
    public sealed class EFCoreBulkUpsert<TDbContext, TEntity>(IDbContextFactory<TDbContext> dbContextFactory, IServiceProvider serviceProvider, IUpsertCommandGenerator upsertCommandGenerator)
        : IBulkUpsertCommand<TEntity>

        where TDbContext : DbContext
        where TEntity : Entity, new()
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
                    // The reason why we manually need to execute SavingHandlers here is because this command bypasses the DbContext.SaveChanges,
                    // which is overridden in ElsaDbContextBase to execute the handlers
                    await HandleOnBeforeExecuting(dbContext, batch, cancellationToken);

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

        async Task HandleOnBeforeExecuting(TDbContext dbContext, IEnumerable<TEntity> entities, CancellationToken cancellationToken)
        {
            var entitySaveHandlers = serviceProvider.GetServices<IEntitySavingHandler<TDbContext, TEntity>>();

            foreach(var entity in entities)
            {
                foreach (var handler in entitySaveHandlers)
                    await handler.Handle(dbContext, entity, cancellationToken);
            }
        }
    }
}
