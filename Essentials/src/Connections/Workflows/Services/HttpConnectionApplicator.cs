using Elsa.Activities.Http.Connections;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Connections.Workflows.Services;

/// <summary>
/// Resolves the bound http connection for the execution partition and applies its authenticator.
/// Revealed credentials are registered with <see cref="IRuntimeSecretMask"/>.
/// </summary>
internal sealed class HttpConnectionApplicator(
    IConnectionResolver resolver,
    IEnumerable<IHttpRequestAuthenticator> authenticators,
    IWorkflowExecutionPartitionAccessor partitionAccessor,
    IRuntimeSecretMask secretMask,
    IConnectionPermissionGate permissionGate) : IHttpConnectionApplicator
{
    private readonly IReadOnlyList<IHttpRequestAuthenticator> _authenticators = authenticators.ToArray();

    public async ValueTask ApplyAsync(
        HttpRequestMessage request,
        ConnectionReference? connection,
        ActivityExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (connection is null || string.IsNullOrWhiteSpace(connection.Name))
            return;

        var tenantId = partitionAccessor.Current.Value;
        await permissionGate.EnsureUseAsync(tenantId, cancellationToken);
        await using var resolved = await resolver.ResolveAsync(new ConnectionResolveRequest
        {
            TenantId = tenantId,
            Name = connection.Name,
            RequiredType = connection.Type ?? ConnectionTypeNames.Http,
            Owner = ConnectionOwners.Tenant
        }, cancellationToken);

        var authenticator = _authenticators.FirstOrDefault(candidate =>
            string.Equals(candidate.SchemeName, resolved.AuthScheme, StringComparison.OrdinalIgnoreCase));
        if (authenticator is null)
        {
            throw new ConnectionUnavailableException(
                resolved.Name,
                ConnectionFaultCodes.Unavailable,
                $"No HTTP authenticator is registered for scheme '{resolved.AuthScheme}'.");
        }

        var masked = new MaskingResolvedConnection(resolved, secretMask, context.InvocationId);
        await authenticator.ApplyAsync(request, masked, cancellationToken);
        RegisterAppliedValues(request, secretMask, context.InvocationId, resolved.Name);
    }

    private static void RegisterAppliedValues(HttpRequestMessage request, IRuntimeSecretMask mask, string activityExecutionId, string connectionName)
    {
        if (request.Headers.TryGetValues("Authorization", out var authorization))
        {
            foreach (var value in authorization)
                mask.Register(activityExecutionId, $"{connectionName}:authorization", value);
        }

        if (request.RequestUri?.Query is { Length: > 1 } query)
        {
            foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = part.IndexOf('=');
                if (separator < 0)
                    continue;
                var value = Uri.UnescapeDataString(part[(separator + 1)..]);
                if (value.Length > 0)
                    mask.Register(activityExecutionId, $"{connectionName}:query", value);
            }
        }
    }

    private sealed class MaskingResolvedConnection(
        IResolvedConnection inner,
        IRuntimeSecretMask mask,
        string activityExecutionId) : IResolvedConnection
    {
        public string TenantId => inner.TenantId;
        public string Name => inner.Name;
        public string TypeName => inner.TypeName;
        public string AuthScheme => inner.AuthScheme;
        public string Owner => inner.Owner;
        public string Status => inner.Status;
        public IReadOnlyDictionary<string, string> Settings => inner.Settings;
        public AllowedHostsPolicy Hosts => inner.Hosts;

        public async ValueTask<SensitiveValue> GetSensitiveAsync(string fieldName, CancellationToken cancellationToken = default)
        {
            var value = await inner.GetSensitiveAsync(fieldName, cancellationToken);
            mask.Register(activityExecutionId, $"{Name}:{fieldName}", value.Reveal());
            return value;
        }

        public override string ToString() => inner.ToString() ?? SensitiveValue.Redacted;

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
