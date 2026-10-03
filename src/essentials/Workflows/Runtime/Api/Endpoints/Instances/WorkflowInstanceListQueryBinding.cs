using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Http;
using NativeEndpoints;

namespace Elsa.Workflows.Runtime.Api.Endpoints.Instances;

/// <summary>
/// Binds the additive health filter while keeping it out of the legacy positional request constructor.
/// NativeEndpoints binds positional contracts from their constructor parameters only.
/// </summary>
internal static class WorkflowInstanceListQueryBinding
{
    public static EndpointParameterMetadata IncidentHealthParameter { get; } = new(
        nameof(ListWorkflowInstances.IncidentHealth), EndpointBindingSource.Query, typeof(IncidentHealth), Required: false);

    public static ListWorkflowInstances WithIncidentHealthQuery(this ListWorkflowInstances request, IQueryCollection query)
    {
        if (!query.TryGetValue(nameof(ListWorkflowInstances.IncidentHealth), out var values))
            return request;

        return request with { IncidentHealth = values.Count == 0 ? null : values[0] };
    }
}
