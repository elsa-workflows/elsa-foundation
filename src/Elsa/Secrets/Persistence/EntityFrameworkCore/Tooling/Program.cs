using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Tooling;

if (args is not ["reindex", var contextName])
{
    Console.Error.WriteLine(
        "Usage: Elsa.Secrets.Persistence.EntityFrameworkCore.Tooling reindex " +
        "<SecretsSqliteDbContext|SecretsSqlServerDbContext|SecretsPostgreSqlDbContext>");
    return 2;
}

await using SecretsDbContext? context = contextName switch
{
    nameof(SecretsSqliteDbContext) => new SecretsSqliteDesignTimeFactory().CreateDbContext([]),
    nameof(SecretsSqlServerDbContext) => new SecretsSqlServerDesignTimeFactory().CreateDbContext([]),
    nameof(SecretsPostgreSqlDbContext) => new SecretsPostgreSqlDesignTimeFactory().CreateDbContext([]),
    _ => null
};

if (context is null)
{
    Console.Error.WriteLine($"Unknown Secrets EF context '{contextName}'.");
    return 2;
}

var updated = await SecretsProjectionContract.ReindexAsync(context);
Console.WriteLine(
    $"Secrets EF: reindexed {updated} row(s) to {SecretsSearchKeys.UnicodeOrdinalIgnoreCaseAlgorithmId}.");
return 0;
