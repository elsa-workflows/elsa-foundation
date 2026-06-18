using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Agent.Abstractions.Contracts;
using Elsa.Foundation.Agent.Abstractions.Models;
using Elsa.Foundation.Agent.Api.Constants;
using Elsa.Foundation.Agent.Api.Models;

namespace Elsa.Foundation.Agent.Api.Endpoints;

internal sealed class Audit(IAgentAuditReader audit)
    : ElsaEndpoint<AgentAuditQueryRequest, AgentApiResponse<IReadOnlyCollection<AgentAuditEvent>>>
{
    public override void Configure()
    {
        Get(AgentRouteConstants.GetRoute("audit"));
        ConfigurePermissions(AgentPermissionKeys.Audit);
    }

    public override async Task HandleAsync(AgentAuditQueryRequest req, CancellationToken ct)
    {
        var events = await audit.ListAsync(req.SessionId, req.Take, ct);
        await Send.OkAsync(AgentApiResponse<IReadOnlyCollection<AgentAuditEvent>>.Success(events), ct);
    }
}
