using Elsa.Weaver.Api.Constants;
using Elsa.Foundation.Identity.Core.Authorization;

namespace Elsa.Weaver.Api.Authorization;

/// <summary>Contributes the Agent API's stable, module-owned permission vocabulary.</summary>
public sealed class WeaverPermissionContributor : IPermissionContributor
{
    public const string Owner = "Elsa.Weaver.Api";

    public string OwnerId => Owner;

    public string ContributorType => typeof(WeaverPermissionContributor).FullName!;

    public IEnumerable<Permission> Contribute() =>
    [
        Permission(WeaverPermissionKeys.Use, "Use agent API", "Use agent sessions, messages, feedback, and streams."),
        Permission(WeaverPermissionKeys.Proposals, "Manage agent proposals", "Approve, deny, and execute agent action proposals."),
        Permission(WeaverPermissionKeys.Audit, "Read agent audit", "Read agent audit events.")
    ];

    private Permission Permission(string key, string displayName, string description) =>
        new(key, displayName, "Weaver", description)
        {
            OwnerId = OwnerId,
            ContributorType = ContributorType
        };
}
