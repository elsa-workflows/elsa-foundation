using Elsa.Persistence.EFCore;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EFCore.DbContext
{
    public sealed class WorkflowsDesignDbContext(DbContextOptions options, IServiceProvider serviceProvider)
        : ElsaDbContextBase(options, serviceProvider)
    {
        /// <summary>
        /// The workflow definitions.
        /// </summary>
        public DbSet<WorkflowDefinition> WorkflowDefinitions { get; set; } = null!;

        public DbSet<WorkflowDefinitionVersion> WorkflowDefinitionVersions { get; set; } = null!;

        public DbSet<WorkflowDefinitionDraft> WorkflowDefinitionDrafts { get; set; } = null!;

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
            base.OnModelCreating(modelBuilder);
        }
    }
}
