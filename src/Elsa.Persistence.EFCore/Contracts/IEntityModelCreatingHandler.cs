using Elsa.Persistence.EFCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Persistence.EFCore.Contracts
{
    /// <summary>
    /// Represents handler for entity model creation.
    /// </summary>
    public interface IEntityModelCreatingHandler
    {
        /// <summary>
        /// Handles the entity model being created.
        /// </summary>
        void Handle(ElsaDbContextBase dbContext, ModelBuilder modelBuilder, IMutableEntityType entityType);
    }
}
