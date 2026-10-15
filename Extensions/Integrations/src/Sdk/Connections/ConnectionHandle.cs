using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;

namespace Elsa.Integrations.Sdk.Connections;

internal sealed class ConnectionHandle : IConnectionHandle
{
    private readonly ConnectionSnapshot _snapshot;
    private readonly HttpClient _httpClient;
    private readonly ISecretValueResolver _secrets;
    private IDisposable? _scope;
    private bool _disposed;

    public ConnectionHandle(
        ConnectionSnapshot snapshot,
        HttpClient httpClient,
        ISecretValueResolver secrets)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(secrets);

        _snapshot = snapshot;
        _httpClient = httpClient;
        _secrets = secrets;
    }

    public string TenantId => _snapshot.TenantId;

    public string ConnectionName => _snapshot.Name;

    public string ConnectionType => _snapshot.TypeName;

    public string AuthScheme => _snapshot.AuthScheme;

    public IReadOnlyDictionary<string, string> Settings => _snapshot.Settings;

    public IReadOnlyList<string> EgressAllowlist => _snapshot.EgressAllowlist;

    public IReadOnlyDictionary<string, SecretReference> SecretFields => _snapshot.SecretFields;

    /// <summary>
    /// Pushes this handle into <see cref="ConnectionExecutionScope"/> on the caller. An async
    /// <c>ResolveAsync</c> cannot publish <see cref="AsyncLocal{T}"/> to its caller: the async
    /// machinery restores the previous execution context when it returns.
    /// </summary>
    public HttpClient HttpClient
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _scope ??= ConnectionExecutionScope.Push(this);
            return _httpClient;
        }
    }

    public async ValueTask<SensitiveValue> GetSensitiveAsync(string fieldName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_snapshot.SecretFields.TryGetValue(fieldName, out var reference))
        {
            throw new ConnectionUnavailableException(
                $"Connection '{_snapshot.Name}' has no secret field '{fieldName}'.",
                _snapshot.Name);
        }

        var resolved = await _secrets.ResolveAsync(_snapshot.TenantId, reference, cancellationToken);
        if (!resolved.Succeeded || resolved.Value is null)
        {
            throw new ConnectionUnavailableException(
                $"Secret '{reference.Name}' for connection '{_snapshot.Name}' could not be resolved ({resolved.FailureCode}).",
                _snapshot.Name);
        }

        return new SensitiveValue(resolved.Value);
    }

    public override string ToString() => $"ConnectionHandle({TenantId}/{ConnectionName})";

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _disposed = true;
        _scope?.Dispose();
        return ValueTask.CompletedTask;
    }
}
