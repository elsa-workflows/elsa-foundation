using Elsa.Agent.Api.Constants;
using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Agent.Api.Authorization;

/// <summary>Contributes the Agent API's stable, module-owned permission vocabulary.</summary>
public sealed class AgentPermissionContributor : IPermissionContributor
{
    public const string Owner = "Elsa.Agent.Api";

    public string OwnerId => Owner;

    public string ContributorType => typeof(AgentPermissionContributor).FullName!;

    public IEnumerable<Permission> Contribute() =>
    [
        Permission(AgentPermissionKeys.Use, "Use agent API", "Use agent sessions, messages, feedback, and streams."),
        Permission(AgentPermissionKeys.Proposals, "Manage agent proposals", "Approve, deny, and execute agent action proposals.",
            new HashSet<string>(StringComparer.Ordinal) { AgentPermissionKeys.Use }),
        Permission(AgentPermissionKeys.Audit, "Read agent audit", "Read agent audit events.")
    ];

    private Permission Permission(string key, string displayName, string description, IReadOnlySet<string>? implies = null) =>
        new(key, displayName, "Agent", description, implies)
        {
            OwnerId = OwnerId,
            ContributorType = ContributorType
        };
}
