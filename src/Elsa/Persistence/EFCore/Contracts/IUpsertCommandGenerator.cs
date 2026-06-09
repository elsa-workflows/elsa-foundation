using Elsa.Primitives.Entities;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Elsa.Persistence.EFCore.Contracts;

public interface IUpsertCommandGenerator
{
    GeneratedCommand Generate<TDbContext, TEntity>(TDbContext dbContext, IList<TEntity> entities, Expression<Func<TEntity, string>> keySelector)
        where TDbContext : DbContext
        where TEntity : Entity;
}

public sealed record GeneratedCommand(string Command, object[] Parameters);