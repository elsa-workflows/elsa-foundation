using Microsoft.EntityFrameworkCore;

namespace Elsa.Samples.Nuplane.Renewals;

/// <summary>A renewal as the premium feature exposes it.</summary>
public sealed record RenewalWithPremium(string Id, string PolicyReference, DateTimeOffset CreatedAt, decimal? ProposedPremium);

public sealed partial class RenewalStore
{
    public async Task<IReadOnlyList<RenewalWithPremium>> ListWithPremiumAsync(CancellationToken cancellationToken = default)
    {
        var rows = await context.Renewals.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var row in rows)
            RenewalsModule.Chain.EnsureReadable(row.SchemaVersion);
        return [.. rows.OrderBy(row => row.CreatedAt).Select(ToRenewalWithPremium)];
    }

    private static RenewalWithPremium ToRenewalWithPremium(RenewalRecord row) =>
        new(row.Id, row.PolicyReference, row.CreatedAt, row.ProposedPremium);
}
