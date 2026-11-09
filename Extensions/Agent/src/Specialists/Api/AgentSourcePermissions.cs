using Elsa.Foundation.Identity.Core.Authorization;

namespace Elsa.Agent.Specialists.Api;

internal sealed class AgentSourcePermissions : IPermissionContributor
{
    internal const string Read = "agent/definitions:read";
    internal const string Manage = "agent/definitions:manage";
    public string OwnerId => SpecialistsApi.OwnerId;
    public string ContributorType => typeof(AgentSourcePermissions).FullName!;
    public IEnumerable<Permission> Contribute() => new[]
    {
        new Permission(Read, "Read Agent definitions", "Agent", "Read Agent sources, drafts and history.") { OwnerId = OwnerId, ContributorType = ContributorType },
        new Permission(Manage, "Author Agent definitions", "Agent", "Create, edit, capture and discard Agent source.") { OwnerId = OwnerId, ContributorType = ContributorType }
    };
}
