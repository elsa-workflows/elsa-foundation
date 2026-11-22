using System.Globalization;
using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Http.Contracts;

namespace Elsa.Workflows.Runtime.Http.Services;

internal sealed class StandaloneTriggerRouteObserver(IHttpEndpointRouteTableSynchronizer synchronizer)
    : IEventHandler<StandaloneTriggerResourceCommitted>
{
    public async Task Handle(StandaloneTriggerResourceCommitted notification, CancellationToken cancellationToken)
    {
        if (notification.TypeKey != "http") return;
        try
        {
            await synchronizer.ConvergeAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            throw new TriggerOperationException("unavailable",
                "The resource change committed, but its local HTTP routes could not refresh. Retry the same management request to repair the projection.",
                new Dictionary<string, string>
                {
                    ["scope"] = notification.Scope.Persistence.Value,
                    ["tenantId"] = notification.Scope.TenantId ?? "",
                    ["triggerId"] = notification.TriggerId,
                    ["revision"] = notification.Revision.ToString(CultureInfo.InvariantCulture),
                    ["commitState"] = "Committed"
                }, exception);
        }
    }
}
