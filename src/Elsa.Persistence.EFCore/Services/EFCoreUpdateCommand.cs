using Elsa.Common.Entities;
using Elsa.Persistence.Core;
using Elsa.Persistence.EFCore.Contracts;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace Elsa.Persistence.EFCore.Services
{
    public sealed class EFCoreUpdateCommand<TDbContext, TEntity>(IDbContextFactory<TDbContext> dbContextFactory)
        : IUpdateCommand<TEntity>
        where TDbContext : DbContext
        where TEntity : Entity, new()
    {
        private async Task<TDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => await dbContextFactory.CreateDbContextAsync(cancellationToken);


        /// <inheritdoc />
        public async Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContextAsync(cancellationToken);

            var set = dbContext.Set<TEntity>();
            set.Entry(entity).State = EntityState.Modified;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task UpdatePartialAsync(TEntity entity, Expression<Func<TEntity, object>>[] properties, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContextAsync(cancellationToken);
            dbContext.Attach(entity);

            foreach (var property in properties)
                dbContext.Entry(entity).Property(property).IsModified = true;

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
