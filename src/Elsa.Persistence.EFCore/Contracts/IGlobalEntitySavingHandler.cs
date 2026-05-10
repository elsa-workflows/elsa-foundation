using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Persistence.EFCore.Contracts
{
    public interface IGlobalEntitySavingHandler
    {
        ValueTask HandleAsync(DbContext dbContext, EntityEntry entity, CancellationToken cancellationToken);
    }
}
