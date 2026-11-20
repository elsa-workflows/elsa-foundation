using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Acme.Widgets.Collision;

public sealed class CollidingWidgetRecord
{
    public string Id { get; set; } = "";
}

public abstract class CollidingWidgetsDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<CollidingWidgetRecord> Widgets => Set<CollidingWidgetRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasElsaDefaultSchema(this);
        modelBuilder.Entity<CollidingWidgetRecord>(entity =>
        {
            entity.ToTable("acme_widgets_collision");
            entity.HasKey(widget => widget.Id);
            entity.Property(widget => widget.Id).HasMaxLength(64).IsRequired();
        });
    }
}

public sealed class CollidingWidgetsPostgreSqlDbContext(DbContextOptions<CollidingWidgetsPostgreSqlDbContext> options)
    : CollidingWidgetsDbContext(options);
