using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Samples.Nuplane.Renewals;

/// <summary>A renewal as the base endpoints show it.</summary>
public sealed record Renewal(string Id, string PolicyReference, DateTimeOffset CreatedAt);

/// <summary>
/// Registers and lists renewals. Every row it writes is stamped with the version this host may write, and every row it reads
/// has its stamp checked against what this build reads, so a row of a version no host here understands is refused
/// rather than misread.
/// </summary>
public sealed partial class RenewalStore(RenewalsDbContext context, IServiceProvider services)
{
    public async Task<IReadOnlyList<Renewal>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await context.Renewals.AsNoTracking()
            .Select(renewal => new { renewal.Id, renewal.PolicyReference, renewal.CreatedAt, renewal.SchemaVersion })
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
            RenewalsModule.Chain.EnsureReadable(row.SchemaVersion);
        return [.. rows.OrderBy(row => row.CreatedAt).Select(row => new Renewal(row.Id, row.PolicyReference, row.CreatedAt))];
    }

    /// <summary>
    /// The version this host writes: the finalized version its finalization gate last observed. While a newer release
    /// is running beside an older one, that is still the older version, so rows this host writes stay readable by every
    /// host that shares the database. Before the gate has admitted the module, the oldest version this build reads.
    /// </summary>
    private string WriteVersion() => AdmittedWriteVersion() ?? RenewalsModule.Chain.ReadableVersions[0];

    /// <summary>The write version the finalization gate keeps for the family, or null before the gate has admitted the module.</summary>
    private string? AdmittedWriteVersion() =>
        services.GetService<EfSchemaFinalizationGates>()?.FindModuleGate(typeof(RenewalsDbContext))?.StateOf(RenewalsModule.Family)?.WriteVersion;
}
