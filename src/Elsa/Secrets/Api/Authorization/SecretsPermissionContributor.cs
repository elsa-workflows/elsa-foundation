using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Secrets.Core.Permissions;

namespace Elsa.Secrets.Api.Authorization;

/// <summary>Contributes the stable permission vocabulary owned by the Secrets API.</summary>
public sealed class SecretsPermissionContributor : IPermissionContributor
{
    public const string Owner = "Elsa.Secrets.Api";

    public string OwnerId => Owner;

    public string ContributorType => typeof(SecretsPermissionContributor).FullName!;

    public IEnumerable<Permission> Contribute() =>
    [
        Permission(SecretsPermissions.Read, "Read secrets", "Read secret metadata and descriptors."),
        Permission(
            SecretsPermissions.Write,
            "Write secrets",
            "Create and update secret metadata and values.",
            new HashSet<string>(StringComparer.Ordinal) { SecretsPermissions.Read }),
        Permission(SecretsPermissions.UpdateValue, "Update secret values", "Rotate secret values and configurations."),
        Permission(SecretsPermissions.Delete, "Delete secrets", "Revoke and delete secrets."),
        Permission(SecretsPermissions.Test, "Test secrets", "Test secret-store resolution without disclosing values."),
        Permission(SecretsPermissions.Use, "Use secrets", "Use secrets from runtime expressions and integrations."),
        Permission(SecretsPermissions.Import, "Import secrets", "Import safe secret references."),
        Permission(SecretsPermissions.Export, "Export secrets", "Export safe secret references.")
    ];

    private Permission Permission(string key, string displayName, string description, IReadOnlySet<string>? implies = null) =>
        new(key, displayName, "Secrets", description, implies)
        {
            OwnerId = OwnerId,
            ContributorType = ContributorType
        };
}
