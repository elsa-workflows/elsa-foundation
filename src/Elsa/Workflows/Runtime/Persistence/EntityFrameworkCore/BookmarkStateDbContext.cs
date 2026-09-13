using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>Provider-neutral model for R01 bookmark state and stimulus lookup.</summary>
public abstract class BookmarkStateDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<BookmarkStateEntity> Bookmarks => Set<BookmarkStateEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new BookmarkStateEntityConfiguration());
        ConfigureProvider(modelBuilder);
    }

    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);
}
