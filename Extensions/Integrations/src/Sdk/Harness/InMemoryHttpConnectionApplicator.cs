using System.Net.Http.Headers;
using Elsa.Activities.Http.Connections;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Integrations.Sdk.Harness;

/// <summary>
/// In-memory <see cref="IHttpConnectionApplicator"/> for connector tests. Register a name to an
/// authorization header, then assert what <see cref="ApplyAsync"/> attached.
/// </summary>
public sealed class InMemoryHttpConnectionApplicator : IHttpConnectionApplicator
{
    private readonly Dictionary<string, AuthenticationHeaderValue> _headers = new(StringComparer.Ordinal);
    private readonly List<AppliedConnection> _applied = [];

    public void Add(string name, AuthenticationHeaderValue authorization)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(authorization);
        lock (_headers)
            _headers[name.Trim()] = authorization;
    }

    public void Add(string name, string scheme, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameter);
        Add(name, new AuthenticationHeaderValue(scheme, parameter));
    }

    public IReadOnlyList<AppliedConnection> Applied
    {
        get
        {
            lock (_applied)
                return [.._applied];
        }
    }

    public AppliedConnection? LastApplied
    {
        get
        {
            lock (_applied)
                return _applied.Count == 0 ? null : _applied[^1];
        }
    }

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
                "A bound connection reference has no name.");
        }

        AuthenticationHeaderValue authorization;
        lock (_headers)
        {
            if (!_headers.TryGetValue(connection.Name, out var header))
            {
                throw new InvalidOperationException(
                    $"No in-memory connection named '{connection.Name}' is registered.");
            }

            authorization = header;
        }

        request.Headers.Authorization = authorization;
        var applied = new AppliedConnection(connection, authorization);
        lock (_applied)
            _applied.Add(applied);
        return ValueTask.CompletedTask;
    }

    public sealed record AppliedConnection(ConnectionReference Connection, AuthenticationHeaderValue Authorization);
}
