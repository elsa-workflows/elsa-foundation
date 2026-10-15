using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Http.Connections;

/// <summary>
/// Default applicator when Connections is not composed: an unbound request is sent as authored; a
/// bound connection is refused rather than sending credentials the host cannot resolve.
/// </summary>
public sealed class UnboundHttpConnectionApplicator : IHttpConnectionApplicator
{
    public ValueTask ApplyAsync(
        HttpRequestMessage request,
        ConnectionReference? connection,
        ActivityExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (connection is null || string.IsNullOrWhiteSpace(connection.Name))
            return ValueTask.CompletedTask;

        throw new InvalidOperationException(
            $"SendHttpRequest is bound to connection '{connection.Name}' but no connection applicator is composed. Enable the ConnectionsWorkflows feature.");
    }
}
