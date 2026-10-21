using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime;

namespace Elsa.Etl.Runtime.Bindings;

/// <summary>Resolves scoped HTTP revisions and rechecks retained pins against live nonsecret connection metadata.</summary>
internal sealed class HostHttpBindings
{
    private const int MaximumNameLength = 128;
    private const int MaximumUriLength = 4096;
    private const int MaximumAllowedPathCount = 1024;
    private const int MaximumShards = 1024;
    private const int MaximumConnectionSettings = 256;
    private const int MaximumConnectionSettingValueLength = 16 * 1024;
    private const int MaximumAllowedHosts = 256;

    private readonly IReadOnlyList<HostHttpBinding> _bindings;
    private readonly IConnectionResolver? _connectionResolver;
    private readonly IConnectionTypeRegistry? _connectionTypes;
    private readonly IAuthenticationSchemeRegistry? _authenticationSchemes;
    private readonly IReadOnlyList<IHttpRequestAuthenticator> _authenticators;

    public HostHttpBindings(IReadOnlyList<EtlHostHttpBindingOptions> bindings)
    {
        _bindings = Snapshot(bindings);
        _authenticators = Array.Empty<IHttpRequestAuthenticator>();
    }

    private HostHttpBindings(
        IReadOnlyList<HostHttpBinding> bindings,
        IConnectionResolver? connectionResolver,
        IConnectionTypeRegistry? connectionTypes,
        IAuthenticationSchemeRegistry? authenticationSchemes,
        IReadOnlyList<IHttpRequestAuthenticator> authenticators)
    {
        _bindings = bindings;
        _connectionResolver = connectionResolver;
        _connectionTypes = connectionTypes;
        _authenticationSchemes = authenticationSchemes;
        _authenticators = authenticators;
    }

    public HostHttpBindings WithConnections(
        IConnectionResolver? connectionResolver,
        IConnectionTypeRegistry? connectionTypes,
        IAuthenticationSchemeRegistry? authenticationSchemes,
        IEnumerable<IHttpRequestAuthenticator>? authenticators) =>
        new(_bindings, connectionResolver, connectionTypes, authenticationSchemes,
            Array.AsReadOnly((authenticators ?? []).Where(x => x is not null).ToArray()));

    public bool ContainsBinding(EtlScope scope, string alias, string role)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(alias);
        ArgumentNullException.ThrowIfNull(role);
        return _bindings.Any(item => item.IsCurrent && item.Scope == scope &&
            item.Alias == alias && item.Role == role);
    }

    public IReadOnlyList<EtlBindingSummary> ListBindings(EtlScope scope, string? afterAlias, string? afterRole, int take)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(take, 1000);
        if ((afterAlias is null) != (afterRole is null))
            throw new ArgumentException("A binding page boundary requires both alias and role.");

        return _bindings.Where(item => item.IsCurrent && item.Scope == scope)
            .Select(item => new EtlBindingSummary(item.Alias, item.Role))
            .Where(item => afterAlias is null || StringComparer.Ordinal.Compare(item.Alias, afterAlias) > 0 ||
                StringComparer.Ordinal.Equals(item.Alias, afterAlias) && StringComparer.Ordinal.Compare(item.Role, afterRole) > 0)
            .OrderBy(item => item.Alias, StringComparer.Ordinal).ThenBy(item => item.Role, StringComparer.Ordinal)
            .Take(take).ToArray();
    }

    public async ValueTask<HostHttpResolvedBinding> ResolveCurrentAsync(
        EtlScope scope,
        string alias,
        string role,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(scope);
        if (!ValidName(alias) || !ValidRole(role))
            throw Unavailable();

        var binding = _bindings.SingleOrDefault(item => item.IsCurrent && item.Scope == scope &&
            item.Alias == alias && item.Role == role);
        if (binding is null)
            throw Unavailable();

        return await ResolveAsync(binding, cancellationToken);
    }

    public async ValueTask<HostHttpResolvedBinding> ResolveExactAsync(
        EtlScope scope,
        string alias,
        string role,
        string revision,
        string handle,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(scope);
        if (!ValidName(alias) || !ValidRole(role) || !ValidName(revision) || !ValidHash(handle))
            throw Unavailable();

        var binding = _bindings.SingleOrDefault(item => item.Scope == scope && item.Alias == alias &&
            item.Role == role && item.Revision == revision);
        if (binding is null)
            throw Unavailable();

        var resolved = await ResolveAsync(binding, cancellationToken);
        if (string.Equals(resolved.Handle, handle, StringComparison.Ordinal))
            return resolved;

        await DisposeSafelyAsync(resolved);
        throw Unavailable();
    }

    private async ValueTask<HostHttpResolvedBinding> ResolveAsync(HostHttpBinding binding, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IResolvedConnection? connection = null;
        try
        {
            var connectionMetadata = (ConnectionMetadataSnapshot?)null;
            IHttpRequestAuthenticator? authenticator = null;
            if (binding.ConnectionName is not null)
            {
                if (binding.Scope.TenantId is null || _connectionResolver is null || _connectionTypes is null ||
                    _authenticationSchemes is null || _authenticators.Count == 0)
                    throw Unavailable();

                connection = await _connectionResolver.ResolveAsync(new ConnectionResolveRequest
                {
                    TenantId = binding.Scope.TenantId,
                    Name = binding.ConnectionName,
                    RequiredType = ConnectionTypeNames.Http,
                    Owner = ConnectionOwners.Tenant
                }, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (connection is null)
                    throw Unavailable();

                (connectionMetadata, authenticator) = ValidateConnection(binding, connection);
            }

            var handle = ComputeHandle(binding, connectionMetadata);
            return new HostHttpResolvedBinding(binding.Alias, binding.Role, binding.Revision, handle,
                binding.IdempotencyHeader, binding.EndpointPolicy, binding.Shards, connection, authenticator);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await DisposeSafelyAsync(connection);
            throw;
        }
        catch (EtlBindingException)
        {
            await DisposeSafelyAsync(connection);
            throw;
        }
        catch
        {
            await DisposeSafelyAsync(connection);
            throw Unavailable();
        }
    }

    private (ConnectionMetadataSnapshot Metadata, IHttpRequestAuthenticator Authenticator) ValidateConnection(
        HostHttpBinding binding,
        IResolvedConnection connection)
    {
        if (connection is null || !string.Equals(connection.TenantId, binding.Scope.TenantId, StringComparison.Ordinal) ||
            !string.Equals(connection.Name, binding.ConnectionName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(connection.TypeName, ConnectionTypeNames.Http, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(connection.Owner, ConnectionOwners.Tenant, StringComparison.Ordinal) ||
            !string.Equals(connection.Status, ConnectionStatuses.Ready, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(connection.AuthScheme) || connection.Settings is null || connection.Hosts is null)
            throw Unavailable();

        var type = _connectionTypes!.Find(connection.TypeName);
        if (type is null || !string.Equals(type.Descriptor.Name, ConnectionTypeNames.Http, StringComparison.OrdinalIgnoreCase) ||
            type.Descriptor.SupportedSchemes is null ||
            !type.Descriptor.SupportedSchemes.Contains(connection.AuthScheme, StringComparer.OrdinalIgnoreCase))
            throw Unavailable();

        var scheme = _authenticationSchemes!.Find(connection.AuthScheme);
        if (scheme is null || !string.Equals(scheme.Descriptor.Name, connection.AuthScheme, StringComparison.OrdinalIgnoreCase))
            throw Unavailable();

        var matches = _authenticators.Where(item => string.Equals(item.SchemeName, connection.AuthScheme, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1)
            throw Unavailable();

        var hosts = connection.Hosts;
        if (string.IsNullOrWhiteSpace(hosts.Kind) || hosts.Hosts is null || hosts.Hosts.Count > MaximumAllowedHosts ||
            hosts.Hosts.Any(host => string.IsNullOrWhiteSpace(host) || host.Length > 512 || host != host.Trim() || host.Any(char.IsControl)))
            throw Unavailable();

        var settings = connection.Settings;
        if (settings.Count > MaximumConnectionSettings || settings.Any(item => item.Key is null || item.Key.Length > MaximumNameLength ||
                item.Value is null || item.Value.Length > MaximumConnectionSettingValueLength))
            throw Unavailable();

        var sensitiveFieldNames = new HashSet<string>(scheme.Descriptor.Fields.Where(item => item.Sensitive).Select(item => item.Name),
            StringComparer.OrdinalIgnoreCase);
        if (settings.Keys.Any(sensitiveFieldNames.Contains))
            throw Unavailable();

        foreach (var uri in binding.RequestUris)
        {
            if (!hosts.Allows(uri))
                throw Unavailable();
            if (uri.Scheme == Uri.UriSchemeHttp && !AllowsInsecureHttp(settings))
                throw Unavailable();
        }

        EnsureIdempotencyHeaderDoesNotConflict(binding, connection);
        var publicSettings = settings
            .Select(item => new KeyValuePair<string, string>(item.Key.ToLowerInvariant(), item.Value))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ThenBy(item => item.Value, StringComparer.Ordinal)
            .ToArray();
        if (publicSettings.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count() != publicSettings.Length)
            throw Unavailable();

        var normalizedHosts = hosts.Hosts.Select(host => host.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var metadata = new ConnectionMetadataSnapshot(
            connection.TenantId,
            connection.Name.ToLowerInvariant(),
            connection.TypeName.ToLowerInvariant(),
            connection.AuthScheme.ToLowerInvariant(),
            connection.Owner,
            connection.Status,
            Array.AsReadOnly(publicSettings),
            hosts.Kind.ToLowerInvariant(),
            Array.AsReadOnly(normalizedHosts));
        return (metadata, matches[0]);
    }

    private static void EnsureIdempotencyHeaderDoesNotConflict(HostHttpBinding binding, IResolvedConnection connection)
    {
        if (binding.Role == "Source")
            return;

        string? authenticationHeader = null;
        if (string.Equals(connection.AuthScheme, HttpAuthSchemes.Basic, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(connection.AuthScheme, HttpAuthSchemes.Bearer, StringComparison.OrdinalIgnoreCase))
            authenticationHeader = "Authorization";
        else if (string.Equals(connection.AuthScheme, HttpAuthSchemes.ApiKey, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(ReadSetting(connection.Settings, HttpConnectionSettingKeys.ApiKeyPlacement, ApiKeyPlacements.Header),
                ApiKeyPlacements.Query, StringComparison.OrdinalIgnoreCase))
            authenticationHeader = ReadSetting(connection.Settings, HttpConnectionSettingKeys.ApiKeyHeaderName, "Authorization");

        if (authenticationHeader is not null && string.Equals(authenticationHeader, binding.IdempotencyHeader, StringComparison.OrdinalIgnoreCase))
            throw Unavailable();
    }

    private static bool AllowsInsecureHttp(IReadOnlyDictionary<string, string> settings) =>
        TryGetSetting(settings, HttpConnectionSettingKeys.AllowInsecureHttp, out var value) &&
        bool.TryParse(value, out var allowHttp) && allowHttp;

    private static string ReadSetting(IReadOnlyDictionary<string, string> settings, string name, string fallback) =>
        TryGetSetting(settings, name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static bool TryGetSetting(IReadOnlyDictionary<string, string> settings, string name, out string value)
    {
        if (settings.TryGetValue(name, out value!))
            return true;
        foreach (var pair in settings)
        {
            if (!string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                continue;
            value = pair.Value;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static IReadOnlyList<HostHttpBinding> Snapshot(IReadOnlyList<EtlHostHttpBindingOptions> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        var snapshots = new List<HostHttpBinding>(bindings.Count);
        var revisions = new HashSet<(EtlScope Scope, string Alias, string Role, string Revision)>();
        var current = new HashSet<(EtlScope Scope, string Alias, string Role)>();

        foreach (var options in bindings)
        {
            if (options is null || !ValidName(options.Alias) || !ValidRole(options.Role) || !ValidName(options.Revision) ||
                string.IsNullOrWhiteSpace(options.Endpoint) || options.Endpoint.Length > MaximumUriLength ||
                options.AllowedPaths is null || options.AllowedPaths.Count > MaximumAllowedPathCount ||
                options.Shards is null || options.Shards.Count > MaximumShards ||
                options.ConnectionName is not null && !ValidName(options.ConnectionName) ||
                !EtlHttpHeaderPolicy.ValidHeaderName(options.IdempotencyHeader) || EtlHttpHeaderPolicy.IsReservedHeader(options.IdempotencyHeader))
                throw InvalidOptions();

            EtlScope scope;
            try
            {
                scope = new EtlScope(options.TenantId, options.PersistencePartition);
            }
            catch (ArgumentException)
            {
                throw InvalidOptions();
            }

            var isSource = options.Role == "Source";
            if (isSource != (options.Shards.Count > 0))
                throw InvalidOptions();

            var shards = new List<HostHttpShard>(options.Shards.Count);
            var shardIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var shard in options.Shards)
            {
                if (shard is null || !ValidName(shard.Id) || !shardIds.Add(shard.Id) ||
                    string.IsNullOrWhiteSpace(shard.SeedUri) || shard.SeedUri.Length > MaximumUriLength ||
                    !Uri.TryCreate(shard.SeedUri, UriKind.Absolute, out var uri))
                    throw InvalidOptions();
                shards.Add(new HostHttpShard(shard.Id, uri));
            }

            var paths = new List<string>(options.AllowedPaths.Count + shards.Count);
            foreach (var path in options.AllowedPaths)
            {
                if (string.IsNullOrWhiteSpace(path) || path.Length > MaximumUriLength)
                    throw InvalidOptions();
                paths.Add(path);
            }

            if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint))
                throw InvalidOptions();
            if (paths.Count == 0)
            {
                if (isSource)
                    paths.AddRange(shards.Select(shard => shard.SeedUri.AbsolutePath));
                else
                    paths.Add(endpoint.AbsolutePath);
            }

            HttpEndpointPolicy policy;
            try
            {
                policy = new HttpEndpointPolicy(options.Endpoint, paths, options.AllowHttp);
                foreach (var shard in shards)
                    policy.Validate(shard.SeedUri);
            }
            catch (EtlBindingException)
            {
                throw;
            }

            var identity = new HostHttpBinding(scope, options.Alias, options.Role, options.Revision,
                policy, options.AllowHttp, options.IsCurrent, options.ConnectionName,
                options.IdempotencyHeader, Array.AsReadOnly(shards.ToArray()));

            if (!revisions.Add((scope, identity.Alias, identity.Role, identity.Revision)) ||
                identity.IsCurrent && !current.Add((scope, identity.Alias, identity.Role)))
                throw new EtlBindingException("etl.binding-ambiguous", "An ETL host HTTP binding has ambiguous revisions.");

            snapshots.Add(identity);
        }

        return Array.AsReadOnly(snapshots.ToArray());
    }

    private static string ComputeHandle(HostHttpBinding binding, ConnectionMetadataSnapshot? connection) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            FormatVersion = 1,
            ScopeIdentity = EtlRuntimeIdentity.ScopeKey(binding.Scope),
            binding.Alias,
            binding.Role,
            binding.Revision,
            Endpoint = binding.EndpointPolicy.Endpoint.AbsoluteUri,
            binding.AllowHttp,
            AllowedPaths = binding.EndpointPolicy.AllowedPaths,
            binding.IdempotencyHeader,
            ConnectionAlias = binding.ConnectionName?.ToLowerInvariant(),
            Shards = binding.Shards.Select(shard => new { shard.Id, SeedUri = shard.SeedUri.AbsoluteUri }).ToArray(),
            Connection = connection
        }))).ToLowerInvariant();

    private static bool ValidName(string? value) => value is { Length: > 0 and <= MaximumNameLength } &&
        char.IsAsciiLetterOrDigit(value[0]) && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    private static bool ValidRole(string? role) => role is "Source" or "MainOutput" or "ErrorOutput";

    private static bool ValidHash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);

    private static EtlBindingException InvalidOptions() =>
        new("etl.binding-invalid", "An ETL host HTTP binding is invalid.");

    private static EtlBindingException Unavailable() =>
        new("etl.binding-unavailable", "The scoped ETL HTTP binding revision is unavailable.");

    private static async ValueTask DisposeSafelyAsync(IAsyncDisposable? resource)
    {
        if (resource is null)
            return;
        try
        {
            await resource.DisposeAsync();
        }
        catch
        {
            // Provider cleanup failures can contain connection data; resolution already failed safely.
        }
    }

    private sealed record HostHttpBinding(
        EtlScope Scope,
        string Alias,
        string Role,
        string Revision,
        HttpEndpointPolicy EndpointPolicy,
        bool AllowHttp,
        bool IsCurrent,
        string? ConnectionName,
        string IdempotencyHeader,
        IReadOnlyList<HostHttpShard> Shards)
    {
        public IEnumerable<Uri> RequestUris => new[] { EndpointPolicy.Endpoint }.Concat(Shards.Select(shard => shard.SeedUri));
    }

    private sealed record ConnectionMetadataSnapshot(
        string TenantId,
        string Name,
        string TypeName,
        string AuthScheme,
        string Owner,
        string Status,
        IReadOnlyList<KeyValuePair<string, string>> Settings,
        string AllowedHostsKind,
        IReadOnlyList<string> AllowedHosts);
}

internal sealed record HostHttpShard(string Id, Uri SeedUri)
{
    public override string ToString() => nameof(HostHttpShard);
}

/// <summary>A pinned HTTP endpoint and safely owned resolved authentication connection for one attempt.</summary>
internal sealed class HostHttpResolvedBinding(
    string alias,
    string role,
    string revision,
    string handle,
    string idempotencyHeader,
    HttpEndpointPolicy endpointPolicy,
    IReadOnlyList<HostHttpShard> shards,
    IResolvedConnection? connection,
    IHttpRequestAuthenticator? authenticator) : IAsyncDisposable
{
    private IResolvedConnection? _connection = connection;

    public string Alias { get; } = alias;
    public string Role { get; } = role;
    public string Revision { get; } = revision;
    public string Handle { get; } = handle;
    public string IdempotencyHeader { get; } = idempotencyHeader;
    public HttpEndpointPolicy EndpointPolicy { get; } = endpointPolicy;
    public IReadOnlyList<HostHttpShard> Shards { get; } = shards;
    public IResolvedConnection? Connection => _connection;
    public IHttpRequestAuthenticator? Authenticator { get; } = authenticator;
    public string? ConnectionTypeName => _connection?.TypeName;

    public async ValueTask DisposeAsync()
    {
        var connectionToDispose = Interlocked.Exchange(ref _connection, null);
        if (connectionToDispose is not null)
        {
            try
            {
                await connectionToDispose.DisposeAsync();
            }
            catch
            {
                throw Unavailable();
            }
        }
    }

    public override string ToString() => nameof(HostHttpResolvedBinding);

    private static EtlBindingException Unavailable() =>
        new("etl.binding-unavailable", "The scoped ETL HTTP binding revision is unavailable.");
}
