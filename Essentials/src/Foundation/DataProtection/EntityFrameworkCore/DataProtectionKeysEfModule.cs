using Elsa.Persistence.EntityFramework;

namespace Elsa.Foundation.DataProtection.EntityFrameworkCore;

public static class DataProtectionKeysEfModule
{
    /// <summary>The module name operators select it by in the persistence tool (ADR 0076 D3).</summary>
    public const string Name = "DataProtection.Keys";

    public const string HistoryModuleName = "ElsaDataProtectionKeys";
    public const string TableName = "elsa_data_protection_keys";

    /// <summary>The persisted-schema version every key row is stamped with, and read against.</summary>
    public const string SchemaVersion = "1.0.0";

    /// <summary>The schema family this module's rows belong to.</summary>
    public const string SchemaFamily = "DataProtectionKeys";

    /// <summary>The family's one chain, from this assembly's declaration: what every reader of the family checks and
    /// upcasts through (spec 180, FR-010).</summary>
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(DataProtectionKeysEfModule).Assembly, SchemaFamily);

    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}
