using Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// A Secrets-scoped projection-row failure. Identifies the tenant/name without payload bytes
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

    private static string Describe(SecretRecord record, string reason) =>
        $"Secrets row tenant '{record.TenantId}' name '{record.NormalizedName}' {reason} " +
        "The current bounded batch was not committed. Correct the inconsistent row and rerun the " +
        "idempotent provider-specific 'bash tools/ef/dual-migrate.sh apply' command, or delete the " +
        "damaged row before starting the host.";
}
