namespace Elsa.Integrations.Sdk.Connections;

internal sealed class ConnectionHandle(
    string tenantId,
    string connectionName,
    string connectionType,
    string authScheme,
    IReadOnlyDictionary<string, string> settings,
    HttpClient httpClient) : IConnectionHandle
{
    public string TenantId { get; } = tenantId;
    public string ConnectionName { get; } = connectionName;
    public string ConnectionType { get; } = connectionType;
    public string AuthScheme { get; } = authScheme;
    public IReadOnlyDictionary<string, string> Settings { get; } = settings;
    public HttpClient HttpClient { get; } = httpClient;

    public override string ToString() => $"ConnectionHandle({TenantId}/{ConnectionName})";

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
