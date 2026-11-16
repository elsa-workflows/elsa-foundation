using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Elsa.Persistence.EntityFrameworkCore.MySql.FeasibilityTests;

internal sealed class MySqlTestModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        (context.GetType(), (context as SecretsMySqlDbContext)?.IncludePendingModel ?? false, designTime);
}
