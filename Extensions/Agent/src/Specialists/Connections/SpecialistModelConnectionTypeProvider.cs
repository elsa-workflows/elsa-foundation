using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;

namespace Elsa.Agent.Specialists.Connections;

/// <summary>
/// Immutable operational connection-type contribution. Register as a singleton in the existing type
/// registry; it holds no scoped model factory, resolver or persistence access context.
/// </summary>
public sealed class SpecialistModelConnectionTypeProvider(string providerKey, string connectionTypeName) : IConnectionTypeProvider
{
    /// <inheritdoc />
    public ConnectionTypeDescriptor Descriptor { get; } = new(connectionTypeName, providerKey + " specialist model", [HttpAuthSchemes.ApiKey],
    [
        new("endpoint", "HTTPS endpoint", "url", true),
        new("allowedHosts", "Allowed hosts", "text", true),
        new("modelId", "Model or deployment", "text", true),
        new("maximumOutputTokens", "Maximum output tokens", "number", true),
        new("temperatureSupported", "Temperature setting supported", "checkbox", true),
        new("structuredResults", "Structured JSON results supported", "checkbox", true)
    ], AllowedHostsPolicy.UserDeclared);

    /// <inheritdoc />
    public AllowedHostsPolicy ResolveHosts(IReadOnlyDictionary<string, string> settings) => new(AllowedHostsPolicy.UserDeclaredKind,
        settings.TryGetValue("allowedHosts", out var hosts)
            ? hosts.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : []);

    /// <inheritdoc />
    public async Task<ConnectionTestResult> TestAsync(IResolvedConnection connection, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // The manager passes its authorized resolved connection; the singleton definition never
            // captures the manager's scope or resolves a connection through the registry it belongs to.
            _ = SpecialistConnectionProfile.Read(connection, Descriptor.Name, null);
            var key = await connection.GetSensitiveAsync(HttpConnectionSecretFields.ApiKey, cancellationToken);
            if (key.IsEmpty || key.Reveal().Any(x => x < ' ' || x > '~')) throw new InvalidOperationException("The model credential is invalid.");
            return new() { Succeeded = true, Message = "Connection configuration and credential reference are valid. Inference has not been tested." };
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new() { Succeeded = false, Message = "The model connection configuration or credential is unavailable." };
        }
    }
}
