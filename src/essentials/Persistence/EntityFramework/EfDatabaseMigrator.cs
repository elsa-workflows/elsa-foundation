using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Applies or validates pending migrations after the provider guard.
/// <see cref="DatabaseFacade.MigrateAsync"/> already takes
/// <c>IHistoryRepository.AcquireDatabaseLockAsync</c> (EF 9+). Hosts that call
/// <c>IMigrator.Migrate</c> or apply pending migrations themselves must take that same lock;
/// wrapping <see cref="DatabaseFacade.MigrateAsync"/> in a second lock is not required and races.
/// </summary>
/// <remarks>
/// <para>
/// Under both policies a pending contracting migration whose schema family is not yet finalized at the version its
/// opt-out names refuses the context's whole pending batch before anything runs, with
/// <see cref="EfContractingMigrationRefusedException"/> (spec 185, FR-024): <see cref="DatabaseFacade.MigrateAsync"/>
/// cannot apply some of a context's pending migrations and withhold others. Every host's module migrator and the
/// persistence tool's <c>apply</c> and <c>validate</c> come through here, so none of them can skip the check.
/// </para>
/// <para>
/// On a database no host has admitted the module in, <see cref="EfMigratePolicy.AutoMigrate"/> seeds first (#2136):
/// it applies the pending migrations before the first contracting one, creates each contracted family's finalization
/// record at the version the contracting migration names, and applies the contracting migrations only once a fresh
/// read shows every such record there. <see cref="EfContractingMigrationCheck"/> states why that holds.
/// <see cref="EfMigratePolicy.Validate"/> applies nothing, so it creates nothing either.
/// </para>
/// </remarks>
public static class EfDatabaseMigrator
{
    public static Task ApplyAsync(
        DbContext context,
        string expectedProviderName,
        EfMigratePolicy policy = EfMigratePolicy.AutoMigrate,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(context, expectedProviderName, policy, host: null, cancellationToken);

    /// <param name="context">The module context whose migrations to apply or validate.</param>
    /// <param name="expectedProviderName">The provider the context must be bound to.</param>
    /// <param name="policy">Whether to apply pending migrations or refuse them.</param>
    /// <param name="host">
    /// This host's member in the fleet: a finalization record created before a contracting migration names it, as
    /// <c>migrator:&lt;host id&gt;</c>. Null where there is none, such as the persistence tool, when the machine name stands in.
    /// </param>
    /// <param name="cancellationToken">Cancels the work.</param>
    public static async Task ApplyAsync(
        DbContext context,
        string expectedProviderName,
        EfMigratePolicy policy,
        SchemaFinalizationMember? host,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        EfProviderGuard.Ensure(context, expectedProviderName);

        switch (policy)
        {
            case EfMigratePolicy.AutoMigrate:
                await EfContractingMigrationCheck.MigrateAsync(context, host, cancellationToken);
                return;
            case EfMigratePolicy.Validate:
                var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
                if (pending.Length == 0)
                    return;
                // A pending batch that could not be applied either is reported as such, naming the family and version it
                // waits for, rather than pointing the operator at an `apply` that would refuse it too.
                if (await EfContractingMigrationCheck.FindRefusalAsync(context, pending, cancellationToken) is { } withheld)
                    throw withheld;
                throw PendingMigrations(context, expectedProviderName, pending);
            default:
                throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unknown EF migrate policy.");
        }
    }

    /// <summary>
    /// The refusal for <paramref name="pending"/> under <see cref="EfMigratePolicy.Validate"/>. It names the EF module that
    /// declares <paramref name="context"/> and the exact command that applies its migrations: the operator who reads it
    /// is at a host's log or a reload's answer, not at a DbContext type.
    /// </summary>
    private static EfPendingMigrationsException PendingMigrations(DbContext context, string expectedProviderName, string[] pending)
    {
        var contextType = context.GetType();
        var module = DeclaringModule(contextType);
        var policy = $"{EfMigrateOptions.SectionName}:{nameof(EfMigrateOptions.Policy)} to {nameof(EfMigratePolicy.AutoMigrate)}";
        if (module is null)
            return new EfPendingMigrationsException(
                contextType.Name,
                pending,
                $"{contextType.Name} has pending migrations: {string.Join(", ", pending)}. " +
                $"Apply them out of process (dotnet elsa persistence apply) or set {policy}.");

        var command = EfPersistenceCommand.Apply(host: null, module, ProviderOf(expectedProviderName));
        return new EfPendingMigrationsException(
            module,
            pending,
            $"EF module '{module}' has pending migrations: {string.Join(", ", pending)}. " +
            $"Apply them out of process with `{command}`, or set {policy}.",
            command);
    }

    /// <summary>The EF module whose <c>[EfModule]</c> declares <paramref name="contextType"/> or a type it derives from, if one does.</summary>
    private static string? DeclaringModule(Type contextType)
    {
        var declared = EfModuleCatalog.Discover([contextType.Assembly]);
        for (var type = contextType; type is not null; type = type.BaseType)
            if (declared.FirstOrDefault(module => module.ContextType == type || module.Sqlite == type || module.SqlServer == type || module.PostgreSql == type || module.MySql == type) is { } match)
                return match.Name;

        return null;
    }

    /// <summary>The provider name the tool's <c>--provider</c> takes for <paramref name="expectedProviderName"/>.</summary>
    private static string ProviderOf(string expectedProviderName) =>
        EfRelationalProviderBinding.Select(expectedProviderName, "relational", "Sqlite", "SqlServer", "PostgreSql", "MySql");
}
