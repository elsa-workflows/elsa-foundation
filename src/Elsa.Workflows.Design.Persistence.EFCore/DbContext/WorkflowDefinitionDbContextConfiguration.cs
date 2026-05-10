using Elsa.Workflows.Design.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Linq.Expressions;

namespace Elsa.Workflows.Design.Persistence.EFCore.DbContext
{
    internal sealed class WorkflowDefinitionDbContextConfiguration : IEntityTypeConfiguration<WorkflowDefinition>
    {
        private static Expression<Func<Version?, string?>> VersionToStringConverter => v => v != null ? v.ToString() : null;
        private static Expression<Func<string?, Version?>> StringToVersionConverter => v => v != null ? Version.Parse(v) : null;

        public void Configure(EntityTypeBuilder<WorkflowDefinition> builder)
        {
            builder.Ignore(x => x.Variables);
            builder.Ignore(x => x.Inputs);
            builder.Ignore(x => x.Outputs);
            builder.Ignore(x => x.Outcomes);
            builder.Ignore(x => x.CustomProperties);
            builder.Ignore(x => x.Options);
            builder.Property<string>("Data");
            builder.Property<bool?>("UsableAsActivity");
            builder.Property(x => x.ToolVersion).HasConversion(VersionToStringConverter, StringToVersionConverter);
            builder.HasIndex(x => new { x.DefinitionId, x.Version }).HasDatabaseName($"IX_{nameof(WorkflowDefinition)}_{nameof(WorkflowDefinition.DefinitionId)}_{nameof(WorkflowDefinition.Version)}").IsUnique();
            builder.HasIndex(x => x.Version).HasDatabaseName($"IX_{nameof(WorkflowDefinition)}_{nameof(WorkflowDefinition.Version)}");
            builder.HasIndex(x => x.Name).HasDatabaseName($"IX_{nameof(WorkflowDefinition)}_{nameof(WorkflowDefinition.Name)}");
            builder.HasIndex(x => x.IsLatest).HasDatabaseName($"IX_{nameof(WorkflowDefinition)}_{nameof(WorkflowDefinition.IsLatest)}");
            builder.HasIndex(x => x.IsPublished).HasDatabaseName($"IX_{nameof(WorkflowDefinition)}_{nameof(WorkflowDefinition.IsPublished)}");
            builder.HasIndex(x => x.IsSystem).HasDatabaseName($"IX_{nameof(WorkflowDefinition)}_{nameof(WorkflowDefinition.IsSystem)}");
            builder.HasIndex("UsableAsActivity").HasDatabaseName($"IX_{nameof(WorkflowDefinition)}_UsableAsActivity");
            builder.HasIndex(x => x.TenantId).HasDatabaseName($"IX_{nameof(WorkflowDefinition)}_{nameof(WorkflowDefinition.TenantId)}");
        }
    }
}
