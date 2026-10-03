using Elsa.Persistence.EntityFramework;
using Elsa.Samples.Nuplane.Renewals;

// Release 1.1.0: the family is at 2.0.0 and its chain reaches back to 1.0.0 through one upcaster, so this build reads rows
// of both versions and a host still on release 1.0.0 reads only the older. Its rewriter brings the rows of 1.0.0 up to 2.0.0
// in the background once that version is finalized, so the family can be recorded complete at it.
[assembly: EfSchemaFamily(RenewalsModule.Family, RenewalsModule.Name, RenewalsModule.CurrentVersion, Upcasters = [typeof(RenewalsOneToTwo)], Rewriter = typeof(RenewalsRewriter))]

namespace Elsa.Samples.Nuplane.Renewals;

public static partial class RenewalsModule
{
    public const string PackageRelease = "1.1.0";

    /// <summary>The version release 1.1.0 adds: renewals may carry an optional proposed premium.</summary>
    public const string ProposedPremiumVersion = "2.0.0";

    /// <summary>The version this build reads and, once every host sharing the database can read it, writes.</summary>
    public const string CurrentVersion = ProposedPremiumVersion;

    /// <summary>The feature that serves the premium-aware registration and list endpoints.</summary>
    public const string ProposedPremiumFeature = "RenewalsPremium";
}
