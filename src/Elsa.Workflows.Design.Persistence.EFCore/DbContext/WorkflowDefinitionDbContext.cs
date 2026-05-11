using Elsa.Persistence.EFCore;
using Elsa.Workflows.Design.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EFCore.DbContext
{
    public sealed class WorkflowDefinitionDbContext(DbContextOptions options, IServiceProvider serviceProvider)
        : ElsaDbContextBase(options, serviceProvider)
    {
        /// <summary>
        /// The workflow definitions.
        /// </summary>
        public DbSet<WorkflowDefinition> WorkflowDefinitions { get; set; } = null!;

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var config = new WorkflowDefinitionDbContextConfiguration();
            modelBuilder.ApplyConfiguration(config);
            base.OnModelCreating(modelBuilder);
        }
    }
}
