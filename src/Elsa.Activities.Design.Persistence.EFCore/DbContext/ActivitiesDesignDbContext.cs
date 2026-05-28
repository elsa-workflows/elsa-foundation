using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Persistence.EFCore;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Activities.Design.Persistence.EFCore.DbContext
{
    public sealed class ActivitiesDesignDbContext(DbContextOptions<ActivitiesDesignDbContext> options, IServiceProvider serviceProvider)
        : ElsaDbContextBase(options, serviceProvider)
    {
        public DbSet<ActivityDefinition> ActivityDefinitions { get; set; } = null!;

        public DbSet<ActivityDefinitionVersion> ActivityDefinitionVersions { get; set; } = null!;

        public DbSet<ActivityDefinitionReconciliationState> ActivityDefinitionReconciliationStates { get; set; } = null!;

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
            base.OnModelCreating(modelBuilder);
        }
    }
}
