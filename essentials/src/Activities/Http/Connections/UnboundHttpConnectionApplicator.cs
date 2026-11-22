using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Http.Connections;

/// <summary>
/// Default applicator when Connections is not composed: an unbound request is sent as authored; a
/// bound connection is refused rather than sending credentials the host cannot resolve.
/// Throws <see cref="InvalidOperationException"/> rather than the typed connection fault because
/// this package cannot reference Elsa.Connections.
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
        if (connection is null)
            return ValueTask.CompletedTask;
        if (string.IsNullOrWhiteSpace(connection.Name))
        {
            throw new InvalidOperationException(
                "SendHttpRequest is bound to a connection reference that has no name.");
        }

        throw new InvalidOperationException(
            $"SendHttpRequest is bound to connection '{connection.Name}' but no connection applicator is composed. Enable the ConnectionsWorkflows feature.");
    }
}
