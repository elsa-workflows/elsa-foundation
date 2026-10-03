using System.ComponentModel.DataAnnotations;

namespace Elsa.Samples.Nuplane.Renewals;

public sealed partial class RenewalRecord
{
    /// <summary>The optional proposed premium added by release 1.1.0 and stored as a nullable decimal.</summary>
    [ConcurrencyCheck]
    public decimal? ProposedPremium { get; set; }
}
