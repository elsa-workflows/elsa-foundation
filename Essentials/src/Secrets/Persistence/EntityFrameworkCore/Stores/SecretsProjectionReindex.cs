using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// The first <see cref="IEfPostMigrationAction"/> (ADR 0076 D8, FR-057): the Secrets projection reindex,
/// declared on this module's <c>[EfModule]</c> instead of hand-wired into a startup hook and a separate
/// tooling executable. It only wraps the audit/repair pair <see cref="SecretsProjectionContract"/> already
/// owns — the rule it enforces is unchanged (audit at startup, fail closed, repair only when an operator
/// asks), and what changes is that the seam is now general and the repair has one named command.
/// </summary>
public sealed class SecretsProjectionReindex : IEfPostMigrationAction
{
    public string Id => nameof(SecretsProjectionReindex);

    public string Kind => "projection-reindex";

    public string RequiredWhen => "legacy-projection-detected";

    public string Audit => $"{nameof(SecretsProjectionContract)}.{nameof(SecretsProjectionContract.HasLegacyProjectionsAsync)}";

    public Task<bool> AuditAsync(DbContext context, CancellationToken cancellationToken = default) =>
        SecretsProjectionContract.HasLegacyProjectionsAsync(Secrets(context), cancellationToken);

    public async Task RunAsync(DbContext context, CancellationToken cancellationToken = default) =>
        await SecretsProjectionContract.ReindexAsync(Secrets(context), cancellationToken);

    /// <summary>
    /// The contract hands every action a bare <see cref="DbContext"/>, and only this module's own context can
    /// answer for this module's rows. A context of any other type is a wiring mistake that must say so rather
    /// than fail as a null reference several frames deeper.
    /// </summary>
    private static SecretsDbContext Secrets(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context as SecretsDbContext ?? throw new InvalidOperationException(
            $"{nameof(SecretsProjectionReindex)} needs a {nameof(SecretsDbContext)} and was given a {context.GetType().Name}.");
    }
}
