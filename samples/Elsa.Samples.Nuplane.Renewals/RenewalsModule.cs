using Elsa.Persistence.EntityFramework;

namespace Elsa.Samples.Nuplane.Renewals;

/// <summary>The names the module, its schema family and its features are declared and composed by.</summary>
public static partial class RenewalsModule
{
    /// <summary>The EF module, as its <c>[EfModule]</c> names it.</summary>
    public const string Name = "Samples.Renewals";

    /// <summary>The module's migrations-history name, which also names its finalization tables.</summary>
    public const string HistoryModuleName = "ElsaSamplesRenewals";

    /// <summary>The one table: a renewal per row.</summary>
    public const string TableName = "elsa_samples_renewals";

    /// <summary>The module's one schema family, as its <c>[EfSchemaFamily]</c> names it: the tables whose rows stamp one persisted-schema version.</summary>
    public const string Family = "SamplesRenewals";

    /// <summary>The feature that binds the module's context and has its migrator admit it through its finalization gate.</summary>
    public const string EntityFrameworkCoreFeature = "RenewalsEntityFrameworkCore";

    /// <summary>The feature that registers and lists renewals.</summary>
    public const string RenewalsFeature = "Renewals";

    /// <summary>Where renewals are registered (POST) and listed (GET).</summary>
    public const string RenewalsPath = "/demo/renewals";

    /// <summary>The release metadata endpoint used by the demo cockpit to prove which package is serving.</summary>
    public const string ReleasePath = "/demo/renewals/release";

    /// <summary>The family's one chain handle, which every read checks and upcasts through.</summary>
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(RenewalsModule).Assembly, Family);
}
