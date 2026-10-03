namespace Elsa.Samples.Nuplane.Renewals;

public sealed partial class RenewalStore
{
    public async Task<Renewal> AddAsync(string policyReference, CancellationToken cancellationToken = default)
    {
        var record = new RenewalRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            PolicyReference = policyReference,
            CreatedAt = DateTimeOffset.UtcNow,
            SchemaVersion = WriteVersion()
        };
        context.Renewals.Add(record);
        await context.SaveChangesAsync(cancellationToken);
        return new Renewal(record.Id, record.PolicyReference, record.CreatedAt);
    }
}
