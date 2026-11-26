using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Acme.Widgets;

/// <summary>One stored widget. Deliberately plain: the fixture is about discovery and scripting, not modelling.</summary>
public sealed class WidgetRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Label { get; set; }
}

/// <summary>The module's shared model; each provider-derived context below owns its own migration set.</summary>
public abstract class WidgetsDbContext(DbContextOptions options) : DbContext(options)
{
    public const string TableName = "acme_widgets";

    public DbSet<WidgetRecord> Widgets => Set<WidgetRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasElsaDefaultSchema(this);
        modelBuilder.Entity<WidgetRecord>(entity =>
        {
            entity.ToTable(TableName);
            entity.HasKey(widget => widget.Id);
            entity.Property(widget => widget.Id).HasMaxLength(64).IsRequired();
            entity.Property(widget => widget.Name).HasMaxLength(128).IsRequired();
            entity.Property(widget => widget.Label).HasMaxLength(128);
        });
    }
}

public sealed class WidgetsPostgreSqlDbContext(DbContextOptions<WidgetsPostgreSqlDbContext> options) : WidgetsDbContext(options);

public sealed class WidgetsSqlServerDbContext(DbContextOptions<WidgetsSqlServerDbContext> options) : WidgetsDbContext(options);
