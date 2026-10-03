using Elsa.Persistence.EntityFramework;
using Elsa.Samples.Nuplane.Renewals;

// Release 1.0.0: one version, no upcasters, and no document columns to upcast.
[assembly: EfSchemaFamily(RenewalsModule.Family, RenewalsModule.Name, RenewalsModule.CurrentVersion)]

namespace Elsa.Samples.Nuplane.Renewals;

public static partial class RenewalsModule
{
    public const string PackageRelease = "1.0.0";

    /// <summary>The version this build reads and writes: renewals have an id, a policy reference and a creation time.</summary>
    public const string CurrentVersion = "1.0.0";
}
