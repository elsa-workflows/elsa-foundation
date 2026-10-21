using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// A Secrets-scoped projection-row failure. Identifies the tenant/name/tombstone key without payload bytes
/// so operators can repair or delete the inconsistent row.
/// </summary>
public sealed class SecretsProjectionException : InvalidOperationException
{
    public SecretsProjectionException(
        string tenantId,
        string normalizedName,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        TenantId = tenantId;
        NormalizedName = normalizedName;
    }

    public string TenantId { get; }

    public string NormalizedName { get; }

    internal static SecretsProjectionException ForPayload(SecretRecord record, Exception innerException) =>
        new(
            record.TenantId,
            record.NormalizedName,
            Describe(record, "has a damaged or structurally invalid persisted document."),
            innerException);

    internal static SecretsProjectionException ForIdentity(SecretRecord record) =>
        new(
            record.TenantId,
            record.NormalizedName,
            Describe(record, "has a row identity that does not match its stored document."));

    internal static SecretsProjectionException ForRefusedMove(SecretRecord record, string tombstone, Exception innerException) =>
        new(
            record.TenantId,
            record.NormalizedName,
            $"Secrets row tenant '{record.TenantId}' name '{record.NormalizedName}' holds a secret that a host from " +
            $"before FreeNameOnDelete deleted in place, and the database refused the row that frees its name under " +
            $"tombstone '{tombstone}', most likely because the tenant id, name and tombstone together exceed the " +
            "provider's key length. The current bounded batch was not committed. Shorten this row's tenant id or name, " +
            $"or remove the row, by hand, then rerun the idempotent '{EfPostMigrationActions.Command}' command for " +
            "this host's provider before starting the host.",
            innerException);

    // A name can have several rows, one holding it and any number deleted, so the tombstone is what names one of them.
    private static string Describe(SecretRecord record, string reason) =>
        $"Secrets row tenant '{record.TenantId}' name '{record.NormalizedName}' tombstone '{record.Tombstone}' {reason} " +
        "The current bounded batch was not committed. Correct the inconsistent row and rerun the " +
        $"idempotent '{EfPostMigrationActions.Command}' command for this host's provider, or delete " +
        "the damaged row before starting the host.";
}
