namespace Elsa.Samples.Nuplane.Renewals;

public sealed partial class RenewalStore
{
    /// <summary>Preserves the original registration API and its response contract.</summary>
    public async Task<Renewal> AddAsync(string policyReference, CancellationToken cancellationToken = default)
    {
        var renewal = await AddAsync(policyReference, null, cancellationToken);
        return new Renewal(renewal.Id, renewal.PolicyReference, renewal.CreatedAt);
    }

    /// <summary>Registers one renewal and persists its optional premium in the same save.</summary>
    public async Task<RenewalWithPremium> AddAsync(string policyReference, decimal? proposedPremium, CancellationToken cancellationToken = default)
    {
        var record = new RenewalRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            PolicyReference = policyReference,
            ProposedPremium = proposedPremium,
            CreatedAt = DateTimeOffset.UtcNow,
            SchemaVersion = WriteVersion()
        };
        context.Renewals.Add(record);
        await context.SaveChangesAsync(cancellationToken);
        return new RenewalWithPremium(record.Id, record.PolicyReference, record.CreatedAt, record.ProposedPremium);
    }
}
