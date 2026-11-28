using System.Net.Http.Headers;
using Elsa.Activities.Http.Connections;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Elsa.Connections.Http;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Connections.Workflows.Services;

/// <summary>
/// Resolves the bound http connection for the execution partition and applies its authenticator.
/// Revealed credentials are registered with <see cref="IRuntimeSecretMask"/>.
/// </summary>
internal sealed class HttpConnectionApplicator(
    IConnectionResolver resolver,
    IEnumerable<IHttpRequestAuthenticator> authenticators,
    IAuthenticationSchemeRegistry schemes,
    IWorkflowExecutionPartitionAccessor partitionAccessor,
    IRuntimeSecretMask secretMask) : IHttpConnectionApplicator
{
    private readonly BoundHttpConnection _connection = new(resolver, authenticators, schemes);

    public async ValueTask ApplyAsync(
        HttpRequestMessage request,
        ConnectionReference? connection,
        ActivityExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (connection is null)
            return;
        if (string.IsNullOrWhiteSpace(connection.Name))
        {
            throw new ConnectionUnavailableException(
                "connection",
                ConnectionFaultCodes.Unavailable,
                "A bound connection reference has no name.");
        }

        await _connection.ApplyAsync(request, connection.Name, connection.Type ?? ConnectionTypeNames.Http,
            partitionAccessor.Current.Value, secretMask, context.InvocationId, actorJob: false, cancellationToken);
    }
}

internal sealed class ActorHttpConnectionApplicator(
    IConnectionResolver resolver,
    IEnumerable<IHttpRequestAuthenticator> authenticators,
    IAuthenticationSchemeRegistry schemes) : IWorkflowHttpConnectionApplier
{
    private readonly BoundHttpConnection _connection = new(resolver, authenticators, schemes);

    public ValueTask ApplyAsync(HttpRequestMessage request, string name, string tenantId, IRuntimeSecretMask mask,
        string executionId, CancellationToken cancellationToken) =>
        _connection.ApplyAsync(request, name, ConnectionTypeNames.Http, tenantId, mask, executionId, actorJob: true, cancellationToken);
}

internal sealed class BoundHttpConnection(
    IConnectionResolver resolver,
    IEnumerable<IHttpRequestAuthenticator> authenticators,
    IAuthenticationSchemeRegistry schemes)
{
    private readonly IReadOnlyList<IHttpRequestAuthenticator> _authenticators = authenticators.ToArray();

    internal async ValueTask ApplyAsync(HttpRequestMessage request, string name, string type, string tenantId,
        IRuntimeSecretMask secretMask, string executionId, bool actorJob, CancellationToken cancellationToken)
    {
        await using var resolved = await resolver.ResolveAsync(new ConnectionResolveRequest
        {
            TenantId = tenantId,
            Name = name,
            RequiredType = type,
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

        var scheme = schemes.Find(resolved.AuthScheme)?.Descriptor;
        var originalDestination = request.RequestUri;
        if (actorJob)
        {
            if (!string.Equals(resolved.TenantId, tenantId, StringComparison.Ordinal) ||
                !string.Equals(resolved.Owner, ConnectionOwners.Tenant, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(resolved.TypeName, ConnectionTypeNames.Http, StringComparison.OrdinalIgnoreCase))
                throw new ConnectionUnavailableException(name, ConnectionFaultCodes.Unavailable,
                    "The resolved HTTP Connection does not match the admitted tenant, owner and type.");
            if (scheme is null || scheme.HasRefreshableState)
                throw new ConnectionUnavailableException(resolved.Name, ConnectionFaultCodes.Unavailable,
                    "The Actor HTTP job requires a registered authentication scheme with no refreshable state.");
            if (request.Headers.Contains("Host"))
                throw new InvalidOperationException("A bound Actor HTTP request cannot override its destination host.");
            HttpConnectionCredentials.EnsureHostAllowed(request, resolved);
        }
        else if (scheme?.HasRefreshableState == true)
        {
            request.Options.Set(HttpConnectionApplyOptions.HasRefreshableState, true);
        }

        var masked = new MaskingResolvedConnection(resolved, secretMask, executionId);
        await authenticator.ApplyAsync(request, masked, cancellationToken);
        if (actorJob)
        {
            if (request.Headers.Contains("Host") ||
                request.RequestUri?.GetLeftPart(UriPartial.Authority) != originalDestination?.GetLeftPart(UriPartial.Authority))
                throw new InvalidOperationException("The HTTP authenticator changed the bound request destination.");
            HttpConnectionCredentials.EnsureHostAllowed(request, resolved);
        }
        RegisterAppliedValues(request, secretMask, executionId, resolved.Name, maskAuthorizationParameter: actorJob);
    }

    private static void RegisterAppliedValues(HttpRequestMessage request, IRuntimeSecretMask mask, string activityExecutionId,
        string connectionName, bool maskAuthorizationParameter)
    {
        if (request.Headers.TryGetValues("Authorization", out var authorization))
        {
            foreach (var value in authorization)
            {
                mask.Register(activityExecutionId, $"{connectionName}:authorization", value);
                if (maskAuthorizationParameter && AuthenticationHeaderValue.TryParse(value, out var header) &&
                    header.Parameter is { Length: > 0 } parameter)
                    mask.Register(activityExecutionId, $"{connectionName}:authorization", parameter);
            }
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

        foreach (var (name, value) in HttpConnectionApplyOptions.GetMaskedSecrets(request))
            mask.Register(activityExecutionId, name, value);
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
        public int Version => inner.Version;
        public string? IncarnationId => inner.IncarnationId;
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
