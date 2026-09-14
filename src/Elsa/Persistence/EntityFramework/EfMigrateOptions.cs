namespace Elsa.Persistence.EntityFramework;

public sealed class EfMigrateOptions
{
    public EfMigratePolicy Policy { get; set; } = EfMigratePolicy.AutoMigrate;
}
