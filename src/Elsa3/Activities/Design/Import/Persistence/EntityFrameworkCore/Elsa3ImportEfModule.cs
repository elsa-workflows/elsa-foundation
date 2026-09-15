using Elsa.Persistence.EntityFramework;

namespace Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore;

/// <summary>Module identity, physical names, and provider-safe sizes of the Elsa 3 import ledger (L01-L03).</summary>
public static class Elsa3ImportEfModule
{
    public const string HistoryModuleName = "Elsa3ReusableActivityImport";
    public const string DefaultConnectionName = "ElsaElsa3Import";
    public const string DefaultSqliteConnectionString = "Data Source=elsa-elsa3-import.db";

    public const string CollectionTable = "elsa3_reusable_import_collections";
    public const string ReceiptTable = "elsa3_reusable_import_receipts";
    public const string DefinitionBindingTable = "elsa3_reusable_import_definition_bindings";

    public const string SchemaVersion = "1.0.0";

    /// <summary>Opaque identities are bounded like the other Elsa relational identity projections.</summary>
    public const int IdentityMaximumLength = 450;

    /// <summary>
    /// Residual columns hold <see cref="EfRelationalIdentity.Encode"/>'s lossless Base64 projection of UTF-16
    /// code units, which survives NUL and lone surrogates that some providers reject in text.
    /// </summary>
    public const int EncodedIdentityMaximumLength = ((IdentityMaximumLength * sizeof(char) + 2) / 3) * 4;

    public const int HashLength = 64;
    public const int TenantKeyLength = 66;
    public const int KindMaximumLength = 64;
    public const int SchemaVersionMaximumLength = 32;
    public const int CommitAttemptIdLength = 32;

    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}
