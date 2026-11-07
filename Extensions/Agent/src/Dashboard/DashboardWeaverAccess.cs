using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Studio.Dashboard.Core.Models;
using Elsa.Studio.Dashboard.Core.Resources;
using Microsoft.Extensions.Hosting;

namespace Elsa.Agent.Dashboard;

internal sealed class DashboardWeaverAccess(IEnumerable<IDashboardActorContext> contexts, IAgentSessionService sessions,
    IHostEnvironment environment)
{
    internal async ValueTask<DashboardActor> CurrentAsync(CancellationToken cancellationToken)
    {
        var installed = contexts.Take(2).ToArray();
        if (installed.Length != 1)
            throw new DashboardResourceException("unavailable", "Current Dashboard identity cannot be established.");
        var actor = await installed[0].GetAsync(cancellationToken);
        if (!DashboardWeaverProtocol.Identity(actor.TenantId, 256) || !DashboardWeaverProtocol.Identity(actor.UserId, 256) ||
            !DashboardWeaverProtocol.Identity(actor.AuthenticationSubjectId, 256))
            throw new DashboardResourceException("denied", "A canonical Dashboard user and authenticated conversation subject are required.");
        return actor;
    }

    internal DashboardRequestScope Scope(DashboardActor actor) => new(actor.TenantId, actor.UserId, environment.ApplicationName);

    internal async ValueTask<DashboardActor> RequireActorAsync(string actorId, string tenantId, CancellationToken cancellationToken)
    {
        var actor = await CurrentAsync(cancellationToken);
        if (!StringComparer.Ordinal.Equals(actor.AuthenticationSubjectId, actorId) || !StringComparer.Ordinal.Equals(actor.TenantId, tenantId))
            throw new DashboardResourceException("denied", "The authoring conversation does not belong to the current user and tenant.");
        return actor;
    }

    internal async ValueTask<DashboardActor> RequireSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        var actor = await CurrentAsync(cancellationToken);
        var owner = await sessions.FindOwnerAsync(sessionId, cancellationToken);
        if (owner is null || !StringComparer.Ordinal.Equals(owner.ActorId, actor.AuthenticationSubjectId) ||
            !StringComparer.Ordinal.Equals(owner.TenantId, actor.TenantId))
            throw new DashboardResourceException("notFound", "The authoring conversation is unavailable.");
        return actor;
    }

    internal async ValueTask<AgentPolicyDecision> AuthorizeTargetAsync(string actorId, string tenantId, AgentResourceTarget? target,
        IDashboardResourceService resources, CancellationToken cancellationToken)
    {
        try
        {
            var actor = await RequireActorAsync(actorId, tenantId, cancellationToken);
            if (target is null) return new(true, []);
            if (target.ModuleId != DashboardWeaverProtocol.ModuleId || target.ResourceType is not ("question" or "question-draft") ||
                !DashboardWeaverProtocol.Identity(target.ResourceId, 256) || !DashboardWeaverProtocol.Identity(target.Revision, 128))
                return new(false, [new("dashboards.target.invalid", "The tool requires the originating Dashboard question draft.")]);
            if (target.ResourceType == "question")
            {
                var current = await resources.GetAsync(actor, DashboardResourceKinds.Question, target.ResourceId!, cancellationToken);
                if (current.Revision != target.Revision || current.DefinitionState != "available")
                    return new(false, [new("dashboards.target.changed", "Reload the current question before authoring.")]);
            }
            return new(true, []);
        }
        catch (DashboardResourceException error)
        {
            return new(false, [new("dashboards.authority." + error.Code, "Current Dashboard authoring authority could not be established.")
                { OwnerUnavailable = error.Code is "unavailable" or "schemaIncompatible" }]);
        }
    }
}
