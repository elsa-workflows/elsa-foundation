using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Http.Connections;

/// <summary>
/// Applies a bound http connection to an outbound request. <c>ActivitiesHttp</c> registers an unbound
/// default so the activity composes without Connections; <c>ConnectionsWorkflows</c> replaces it.
/// </summary>
public interface IHttpConnectionApplicator
{
    ValueTask ApplyAsync(
        HttpRequestMessage request,
        ConnectionReference? connection,
        ActivityExecutionContext context,
        CancellationToken cancellationToken = default);
}
