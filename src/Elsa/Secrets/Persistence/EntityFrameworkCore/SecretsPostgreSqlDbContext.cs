using Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore;

public sealed class SecretsPostgreSqlDbContext(DbContextOptions<SecretsPostgreSqlDbContext> options)
    : SecretsDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.PostgreSql;

    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        // Npgsql installs a model-wide identity strategy even though this module has no generated
        // numeric keys. Removing the unused annotation keeps snapshots provider-package-free and
        // allows pending-model validation without referencing Npgsql from the module assembly.
        modelBuilder.Model.RemoveAnnotation("Npgsql:ValueGenerationStrategy");
        modelBuilder.Entity<SecretRecord>(entity =>
        {
            entity.Property(record => record.Payload).HasColumnType("jsonb");
            entity.Property(record => record.ConcurrencyToken).HasColumnType("bytea");
        });
    }
}
