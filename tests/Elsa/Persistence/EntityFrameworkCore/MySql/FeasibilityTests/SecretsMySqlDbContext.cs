using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using MySql.EntityFrameworkCore.Extensions;

namespace Elsa.Persistence.EntityFrameworkCore.MySql.FeasibilityTests;

/// <summary>
/// Test-owned MySQL context for the provider spike. It deliberately derives from the hard Secrets
/// model so the proof exercises a representative JSON payload, composite identity, projections,
/// DateTimeOffset and an explicit concurrency token without adding a shipped MySQL registration.
/// </summary>
public class SecretsMySqlDbContext : SecretsDbContext
{
    private readonly bool includePendingModel;

    public SecretsMySqlDbContext(
        DbContextOptions<SecretsMySqlDbContext> options,
        bool includePendingModel = false) : base(options)
    {
        this.includePendingModel = includePendingModel;
    }

    public const string ExpectedProviderName = "MySql.EntityFrameworkCore";
    public const string CharacterSet = "utf8mb4";
    public const string Collation = "utf8mb4_0900_bin";
    public const string HistoryTableName = "__EFMigrationsHistory_ElsaSecretsMySqlSpike";
    public const string MigrationId = "20260912081709_Initial";
    public bool IncludePendingModel => includePendingModel;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        if (includePendingModel)
            modelBuilder.Entity<SecretRecord>().Property<string>("IntentionalPendingModelProbe");
    }

    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.HasCharSet(CharacterSet);
        MySQLModelBuilderExtensions.UseCollation(modelBuilder, Collation);

        modelBuilder.Entity<SecretRecord>(entity =>
        {
            entity.ForMySQLHasCollation(Collation);

            // Every identity and lookup projection is compared using ordinal binary semantics.
            entity.Property(record => record.TenantId).ForMySQLHasCollation(Collation);
            entity.Property(record => record.NormalizedName).ForMySQLHasCollation(Collation);
            entity.Property(record => record.NameSearchKey).ForMySQLHasCollation(Collation);
            entity.Property(record => record.DisplayNameSearchKey).ForMySQLHasCollation(Collation);
            entity.Property(record => record.TypeNameLookupKey).ForMySQLHasCollation(Collation);
            entity.Property(record => record.StoreNameLookupKey).ForMySQLHasCollation(Collation);
            entity.Property(record => record.ScopeLookupKey).ForMySQLHasCollation(Collation);
            entity.Property(record => record.Payload).HasColumnType("json");
            entity.Property(record => record.ConcurrencyToken).HasColumnType("varbinary(16)");
            // MySQL datetime(6) drops DateTimeOffset ticks in the Oracle provider. Persist UTC ticks
            // explicitly so the persisted projection remains full-fidelity and offset-free.
            entity.Property(record => record.MaxActiveVersionExpiresAt)
                .HasConversion(
                    value => value.HasValue ? value.Value.UtcTicks : (long?)null,
                    value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null)
                .HasColumnType("bigint");
        });
    }
}
