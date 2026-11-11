using Elsa.Weaver.Api.Constants;
using Elsa.Foundation.Identity.Core.Authorization;

namespace Elsa.Weaver.Api.Authorization;

/// <summary>Contributes the Weaver API's stable, module-owned permission vocabulary.</summary>
public sealed class WeaverPermissionContributor : IPermissionContributor
{
    public const string Owner = "Elsa.Weaver.Api";

    public string OwnerId => Owner;

    public string ContributorType => typeof(WeaverPermissionContributor).FullName!;

    public IEnumerable<Permission> Contribute() =>
    [
        Permission(WeaverPermissionKeys.Use, "Use Weaver API", "Use Weaver sessions, messages, feedback, and streams."),
        Permission(WeaverPermissionKeys.Proposals, "Manage Weaver proposals", "Approve, deny, and execute Weaver action proposals."),
        Permission(WeaverPermissionKeys.Audit, "Read Weaver audit", "Read Weaver audit events.")
    ];

    private Permission Permission(string key, string displayName, string description) =>
        new(key, displayName, "Weaver", description)
        {
            OwnerId = OwnerId,
            ContributorType = ContributorType
        };
}
