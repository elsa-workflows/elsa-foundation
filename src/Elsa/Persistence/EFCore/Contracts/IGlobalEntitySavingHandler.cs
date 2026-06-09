using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Elsa.Persistence.EFCore.Contracts;

public interface IGlobalEntitySavingHandler
{
    ValueTask Handle(DbContext dbContext, EntityEntry entity, CancellationToken cancellationToken);
}