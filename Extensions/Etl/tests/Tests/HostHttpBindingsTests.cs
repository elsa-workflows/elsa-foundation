using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;
using Elsa.Etl.Core;
using Elsa.Etl.Runtime;
using Elsa.Etl.Runtime.Bindings;

namespace Elsa.Etl.Tests;

public sealed class HostHttpBindingsTests
{
    private static readonly EtlScope Scope = new("tenant-a", "partition-a");

    [Fact]
    public async Task Registry_snapshots_mutable_options_and_source_shards()
    {
        var options = Source();
        var registry = new HostHttpBindings([options]);
        options.Alias = "retargeted";
        options.AllowedPaths.Add("/mutated");
        options.Shards[0].Id = "retargeted-shard";
        options.Shards[0].SeedUri = "https://other.example.test/changed";

        await using var binding = await registry.ResolveCurrentAsync(Scope, "source", "Source");

        var shard = Assert.Single(binding.Shards);
        Assert.Equal("north", shard.Id);
        Assert.Equal(new Uri("https://api.example.test/v1/items?cursor=first"), shard.SeedUri);
        Assert.Contains("/v1/items", binding.EndpointPolicy.AllowedPaths);
        Assert.DoesNotContain("/mutated", binding.EndpointPolicy.AllowedPaths);
    }

    [Fact]
    public void Source_seed_must_match_the_pinned_origin_and_exact_path_policy()
    {
        var options = Source();
        options.Shards[0].SeedUri = "https://other.example.test/v1/items";

        var error = Assert.Throws<EtlBindingException>(() => new HostHttpBindings([options]));

        Assert.Equal("etl.http-endpoint-unavailable", error.Code);
        Assert.DoesNotContain("other.example.test", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_path_allowlist_does_not_expand_to_include_endpoint_or_source_seed()
    {
        var endpointExcluded = Source();
        endpointExcluded.AllowedPaths.Add("/v1/other");
        var seedExcluded = Source();
        seedExcluded.AllowedPaths.Add("/v1/items");
        seedExcluded.Shards[0].SeedUri = "https://api.example.test/v1/other";

        var endpointError = Assert.Throws<EtlBindingException>(() => new HostHttpBindings([endpointExcluded]));
        var seedError = Assert.Throws<EtlBindingException>(() => new HostHttpBindings([seedExcluded]));

        Assert.Equal("etl.http-endpoint-unavailable", endpointError.Code);
        Assert.Equal("etl.http-endpoint-unavailable", seedError.Code);
        Assert.DoesNotContain("/v1/other", endpointError.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("/v1/other", seedError.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Retained_revision_resolves_exactly_after_current_alias_moves()
    {
        var original = Source(revision: "v1");
        var oldHandle = await GetCurrentHandleAsync(new HostHttpBindings([original]));
        var registry = new HostHttpBindings([Source(revision: "v1", isCurrent: false), Source(revision: "v2")]);

        await using var current = await registry.ResolveCurrentAsync(Scope, "source", "Source");
        await using var retained = await registry.ResolveExactAsync(Scope, "source", "Source", "v1", oldHandle);

        Assert.Equal("v2", current.Revision);
        Assert.Equal("v1", retained.Revision);
        Assert.Equal(oldHandle, retained.Handle);
    }

    [Fact]
    public void Duplicate_current_revisions_and_shard_role_mismatches_are_refused()
    {
        var duplicate = Assert.Throws<EtlBindingException>(() => new HostHttpBindings([Source(), Source(revision: "v2")]));
        var outputWithShards = Output();
        outputWithShards.Shards.Add(new EtlHostHttpShardOptions { Id = "north", SeedUri = "https://api.example.test/v1/items" });
        var invalidRole = Assert.Throws<EtlBindingException>(() => new HostHttpBindings([outputWithShards]));

        Assert.Equal("etl.binding-ambiguous", duplicate.Code);
        Assert.Equal("etl.binding-invalid", invalidRole.Code);
    }

    [Fact]
    public async Task Authenticated_binding_requires_an_explicit_tenant_before_resolving_connection()
    {
        var options = Source();
        options.TenantId = null;
        options.ConnectionName = "orders";
        var resolver = new FakeResolver(Connection());
        var registry = AuthenticatedRegistry(options, resolver);

        var error = await Assert.ThrowsAsync<EtlBindingException>(async () =>
            await registry.ResolveCurrentAsync(new EtlScope(null, "partition-a"), "source", "Source"));

        Assert.Equal("etl.binding-unavailable", error.Code);
        Assert.Empty(resolver.Requests);
    }

    [Fact]
    public async Task Exact_pin_rejects_changed_nonsecret_connection_settings_and_disposes_resolution()
    {
        var options = Source();
        options.ConnectionName = "orders";
        var originalConnection = Connection();
        var changedConnection = Connection();
        changedConnection.Settings = Settings("https://changed.example.test");
        var resolver = new FakeResolver(originalConnection, changedConnection);
        var registry = AuthenticatedRegistry(options, resolver);
        string handle;
        await using (var current = await registry.ResolveCurrentAsync(Scope, "source", "Source"))
            handle = current.Handle;

        var error = await Assert.ThrowsAsync<EtlBindingException>(async () =>
            await registry.ResolveExactAsync(Scope, "source", "Source", "v1", handle));

        Assert.Equal("etl.binding-unavailable", error.Code);
        Assert.True(originalConnection.Disposed);
        Assert.True(changedConnection.Disposed);
        Assert.Equal(0, originalConnection.SensitiveReads);
        Assert.Equal(0, changedConnection.SensitiveReads);
        Assert.DoesNotContain("changed.example.test", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exact_pin_rejects_drift_in_additional_public_connection_settings()
    {
        var options = Source();
        options.ConnectionName = "orders";
        var original = Connection();
        var changed = Connection();
        original.Settings = SettingsWithMode("first");
        changed.Settings = SettingsWithMode("second");
        var registry = AuthenticatedRegistry(options, new FakeResolver(original, changed));
        string handle;
        await using (var current = await registry.ResolveCurrentAsync(Scope, "source", "Source"))
            handle = current.Handle;

        var error = await Assert.ThrowsAsync<EtlBindingException>(async () =>
            await registry.ResolveExactAsync(Scope, "source", "Source", "v1", handle));

        Assert.Equal("etl.binding-unavailable", error.Code);
        Assert.True(changed.Disposed);
    }

    [Fact]
    public async Task Sensitive_scheme_fields_cannot_be_smuggled_into_public_settings()
    {
        var options = Source();
        options.ConnectionName = "orders";
        var connection = Connection();
        var settings = connection.Settings.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
        settings[HttpConnectionSecretFields.Token] = "private-secret";
        connection.Settings = settings;
        var registry = AuthenticatedRegistry(options, new FakeResolver(connection));

        var error = await Assert.ThrowsAsync<EtlBindingException>(async () =>
            await registry.ResolveCurrentAsync(Scope, "source", "Source"));

        Assert.Equal("etl.binding-unavailable", error.Code);
        Assert.True(connection.Disposed);
        Assert.DoesNotContain("private-secret", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Secret_rotation_does_not_change_pin_or_read_secret_during_resolution()
    {
        var options = Source();
        options.ConnectionName = "orders";
        var first = Connection(secret: "first-secret");
        var second = Connection(secret: "second-secret");
        var registry = AuthenticatedRegistry(options, new FakeResolver(first, second));

        await using var firstBinding = await registry.ResolveCurrentAsync(Scope, "source", "Source");
        await using var secondBinding = await registry.ResolveCurrentAsync(Scope, "source", "Source");

        Assert.Equal(firstBinding.Handle, secondBinding.Handle);
        Assert.NotNull(firstBinding.Connection);
        Assert.NotNull(firstBinding.Authenticator);
        Assert.Equal(ConnectionTypeNames.Http, firstBinding.ConnectionTypeName);
        Assert.Equal(0, first.SensitiveReads);
        Assert.Equal(0, second.SensitiveReads);
        await firstBinding.DisposeAsync();
        await secondBinding.DisposeAsync();
        Assert.True(first.Disposed);
        Assert.True(second.Disposed);
    }

    [Theory]
    [InlineData("wrong-tenant")]
    [InlineData("wrong-type")]
    [InlineData("wrong-owner")]
    [InlineData("not-ready")]
    [InlineData("unsupported-scheme")]
    [InlineData("host-not-allowed")]
    public async Task Unsafe_resolved_connection_metadata_is_refused_and_disposed(string failure)
    {
        var options = Source();
        options.ConnectionName = "orders";
        var connection = ConnectionForFailure(failure);
        var registry = AuthenticatedRegistry(options, new FakeResolver(connection));

        var error = await Assert.ThrowsAsync<EtlBindingException>(async () =>
            await registry.ResolveCurrentAsync(Scope, "source", "Source"));

        Assert.Equal("etl.binding-unavailable", error.Code);
        Assert.True(connection.Disposed);
        Assert.DoesNotContain("secret", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Authentication_scheme_must_have_exactly_one_registered_authenticator()
    {
        var options = Source();
        options.ConnectionName = "orders";
        var connection = Connection();
        var registry = AuthenticatedRegistry(options, new FakeResolver(connection), [new FakeAuthenticator(), new FakeAuthenticator()]);

        var error = await Assert.ThrowsAsync<EtlBindingException>(async () =>
            await registry.ResolveCurrentAsync(Scope, "source", "Source"));

        Assert.Equal("etl.binding-unavailable", error.Code);
        Assert.True(connection.Disposed);
    }

    [Fact]
    public async Task Connection_disposal_errors_are_redacted()
    {
        var options = Source();
        options.ConnectionName = "orders";
        var connection = Connection();
        connection.ThrowOnDispose = true;
        var registry = AuthenticatedRegistry(options, new FakeResolver(connection));
        var binding = await registry.ResolveCurrentAsync(Scope, "source", "Source");

        var error = await Assert.ThrowsAsync<EtlBindingException>(async () => await binding.DisposeAsync());

        Assert.Equal("etl.binding-unavailable", error.Code);
        Assert.DoesNotContain("private-secret", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolver_cancellation_is_only_propagated_when_attempt_token_was_cancelled()
    {
        var options = Source();
        options.ConnectionName = "orders";
        var registry = AuthenticatedRegistry(options, new ThrowingResolver(new OperationCanceledException("private-secret")));

        var error = await Assert.ThrowsAsync<EtlBindingException>(async () =>
            await registry.ResolveCurrentAsync(Scope, "source", "Source"));

        Assert.Equal("etl.binding-unavailable", error.Code);
        Assert.DoesNotContain("private-secret", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Already_cancelled_attempt_cannot_resolve_anonymous_binding()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var registry = new HostHttpBindings([Source()]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await registry.ResolveCurrentAsync(Scope, "source", "Source", cancellation.Token));
    }

    [Fact]
    public async Task Cancellation_after_a_resolver_ignores_the_token_disposes_its_connection()
    {
        using var cancellation = new CancellationTokenSource();
        var options = Source();
        options.ConnectionName = "orders";
        var connection = Connection();
        var registry = AuthenticatedRegistry(options, new CancellingResolver(connection, cancellation));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await registry.ResolveCurrentAsync(Scope, "source", "Source", cancellation.Token));

        Assert.True(connection.Disposed);
    }

    [Fact]
    public async Task Output_idempotency_header_cannot_replace_authentication_header()
    {
        var options = Output();
        options.ConnectionName = "orders";
        options.IdempotencyHeader = "X-Request-Key";
        var connection = ApiKeyConnection();
        var registry = AuthenticatedRegistry(options, new FakeResolver(connection), [new FakeAuthenticator(HttpAuthSchemes.ApiKey)]);

        var error = await Assert.ThrowsAsync<EtlBindingException>(async () =>
            await registry.ResolveCurrentAsync(Scope, "output", "MainOutput"));

        Assert.Equal("etl.binding-unavailable", error.Code);
        Assert.True(connection.Disposed);
    }

    [Theory]
    [InlineData("Keep-Alive")]
    [InlineData("TE")]
    [InlineData("Trailer")]
    [InlineData("Upgrade")]
    [InlineData("host")]
    [InlineData("cOnTeNt-LeNgTh")]
    public void Idempotency_header_cannot_control_transport(string header)
    {
        var options = Output();
        options.IdempotencyHeader = header;
        Assert.Equal("etl.binding-invalid", Assert.Throws<EtlBindingException>(() => new HostHttpBindings([options])).Code);
    }

    private static async Task<string> GetCurrentHandleAsync(HostHttpBindings registry)
    {
        await using var binding = await registry.ResolveCurrentAsync(Scope, "source", "Source");
        return binding.Handle;
    }

    private static HostHttpBindings AuthenticatedRegistry(
        EtlHostHttpBindingOptions options,
        IConnectionResolver resolver,
        IReadOnlyList<IHttpRequestAuthenticator>? authenticators = null) =>
        new HostHttpBindings([options]).WithConnections(resolver, new FakeTypeRegistry(), new FakeSchemeRegistry(),
            authenticators ?? [new FakeAuthenticator()]);

    private static EtlHostHttpBindingOptions Source(string revision = "v1", bool isCurrent = true) => new()
    {
        TenantId = Scope.TenantId,
        PersistencePartition = Scope.PersistencePartition,
        Alias = "source",
        Role = "Source",
        Revision = revision,
        Endpoint = "https://api.example.test/v1/items",
        IsCurrent = isCurrent,
        Shards = [new EtlHostHttpShardOptions { Id = "north", SeedUri = "https://api.example.test/v1/items?cursor=first" }]
    };

    private static EtlHostHttpBindingOptions Output() => new()
    {
        TenantId = Scope.TenantId,
        PersistencePartition = Scope.PersistencePartition,
        Alias = "output",
        Role = "MainOutput",
        Revision = "v1",
        Endpoint = "https://api.example.test/v1/items"
    };

    private static FakeResolvedConnection Connection(string secret = "test-secret") => new()
    {
        TenantId = Scope.TenantId!,
        Name = "orders",
        TypeName = ConnectionTypeNames.Http,
        AuthScheme = HttpAuthSchemes.Bearer,
        Owner = ConnectionOwners.Tenant,
        Status = ConnectionStatuses.Ready,
        Settings = Settings("https://api.example.test"),
        Hosts = AllowedHostsPolicy.Fixed("api.example.test"),
        Secret = secret
    };

    private static FakeResolvedConnection ApiKeyConnection()
    {
        var settings = Settings("https://api.example.test").ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
        settings[HttpConnectionSettingKeys.ApiKeyPlacement] = ApiKeyPlacements.Header;
        settings[HttpConnectionSettingKeys.ApiKeyHeaderName] = "X-Request-Key";
        return new FakeResolvedConnection
        {
            TenantId = Scope.TenantId!,
            Name = "orders",
            TypeName = ConnectionTypeNames.Http,
            AuthScheme = HttpAuthSchemes.ApiKey,
            Owner = ConnectionOwners.Tenant,
            Status = ConnectionStatuses.Ready,
            Settings = settings,
            Hosts = AllowedHostsPolicy.Fixed("api.example.test")
        };
    }

    private static IReadOnlyDictionary<string, string> Settings(string baseUrl) => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [HttpConnectionSettingKeys.BaseUrl] = baseUrl,
        [HttpConnectionSettingKeys.AllowedHosts] = "api.example.test",
        [HttpConnectionSettingKeys.AllowInsecureHttp] = "false"
    };

    private static IReadOnlyDictionary<string, string> SettingsWithMode(string mode)
    {
        var settings = Settings("https://api.example.test").ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
        settings["transportMode"] = mode;
        return settings;
    }

    private static FakeResolvedConnection ConnectionForFailure(string failure)
    {
        var connection = Connection();
        switch (failure)
        {
            case "wrong-tenant": connection.TenantId = "tenant-b"; break;
            case "wrong-type": connection.TypeName = "smtp"; break;
            case "wrong-owner": connection.Owner = "user"; break;
            case "not-ready": connection.Status = ConnectionStatuses.Revoked; break;
            case "unsupported-scheme": connection.AuthScheme = "unknown"; break;
            case "host-not-allowed": connection.Hosts = AllowedHostsPolicy.Fixed("other.example.test"); break;
            default: throw new ArgumentOutOfRangeException(nameof(failure));
        }

        return connection;
    }

    private sealed class FakeResolver(params FakeResolvedConnection[] connections) : IConnectionResolver
    {
        private readonly Queue<FakeResolvedConnection> _connections = new(connections);
        public List<ConnectionResolveRequest> Requests { get; } = [];

        public ValueTask<IResolvedConnection> ResolveAsync(ConnectionResolveRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return ValueTask.FromResult<IResolvedConnection>(_connections.Dequeue());
        }
    }

    private sealed class ThrowingResolver(Exception exception) : IConnectionResolver
    {
        public ValueTask<IResolvedConnection> ResolveAsync(ConnectionResolveRequest request, CancellationToken cancellationToken = default) =>
            throw exception;
    }

    private sealed class CancellingResolver(FakeResolvedConnection connection, CancellationTokenSource cancellation) : IConnectionResolver
    {
        public ValueTask<IResolvedConnection> ResolveAsync(ConnectionResolveRequest request, CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            return ValueTask.FromResult<IResolvedConnection>(connection);
        }
    }

    private sealed class FakeResolvedConnection : IResolvedConnection
    {
        public required string TenantId { get; set; }
        public required string Name { get; set; }
        public required string TypeName { get; set; }
        public required string AuthScheme { get; set; }
        public required string Owner { get; set; }
        public required string Status { get; set; }
        public required IReadOnlyDictionary<string, string> Settings { get; set; }
        public required AllowedHostsPolicy Hosts { get; set; }
        public string Secret { get; init; } = string.Empty;
        public int SensitiveReads { get; private set; }
        public bool Disposed { get; private set; }
        public bool ThrowOnDispose { get; set; }

        public ValueTask<SensitiveValue> GetSensitiveAsync(string fieldName, CancellationToken cancellationToken = default)
        {
            SensitiveReads++;
            return ValueTask.FromResult(new SensitiveValue(Secret));
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            if (ThrowOnDispose)
                return new ValueTask(Task.FromException(new InvalidOperationException("private-secret")));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeTypeRegistry : IConnectionTypeRegistry
    {
        private readonly IConnectionTypeProvider _provider = new FakeTypeProvider();
        public IConnectionTypeProvider? Find(string name) => string.Equals(name, ConnectionTypeNames.Http, StringComparison.OrdinalIgnoreCase) ? _provider : null;
        public IReadOnlyList<IConnectionTypeProvider> List() => [_provider];
    }

    private sealed class FakeTypeProvider : IConnectionTypeProvider
    {
        public ConnectionTypeDescriptor Descriptor { get; } = new(ConnectionTypeNames.Http, "HTTP", [HttpAuthSchemes.Bearer, HttpAuthSchemes.ApiKey],
            [new ConnectionSettingField(HttpConnectionSettingKeys.BaseUrl, "Base URL", "url"),
                new ConnectionSettingField(HttpConnectionSettingKeys.AllowedHosts, "Allowed hosts", "text"),
                new ConnectionSettingField(HttpConnectionSettingKeys.AllowInsecureHttp, "Allow insecure HTTP", "checkbox")],
            AllowedHostsPolicy.UserDeclared);
        public Task<ConnectionTestResult> TestAsync(IResolvedConnection connection, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionTestResult { Succeeded = true });
    }

    private sealed class FakeSchemeRegistry : IAuthenticationSchemeRegistry
    {
        private readonly IReadOnlyList<IAuthenticationSchemeProvider> _providers = [new FakeSchemeProvider(), new FakeApiKeySchemeProvider()];
        public IAuthenticationSchemeProvider? Find(string name) => _providers.SingleOrDefault(provider =>
            string.Equals(provider.Descriptor.Name, name, StringComparison.OrdinalIgnoreCase));
        public IReadOnlyList<IAuthenticationSchemeProvider> List() => _providers;
    }

    private sealed class FakeSchemeProvider : IAuthenticationSchemeProvider
    {
        public AuthSchemeDescriptor Descriptor { get; } = new(HttpAuthSchemes.Bearer, "Bearer",
            [new AuthSchemeField(HttpConnectionSecretFields.Token, "Token", Sensitive: true, "password")],
            HasInteractiveAuthorization: false, HasRefreshableState: false, HasRevocation: false, RequiresWritableStore: false);
    }

    private sealed class FakeApiKeySchemeProvider : IAuthenticationSchemeProvider
    {
        public AuthSchemeDescriptor Descriptor { get; } = new(HttpAuthSchemes.ApiKey, "API key",
            [new AuthSchemeField(HttpConnectionSettingKeys.ApiKeyHeaderName, "Header", Sensitive: false, "text", Required: false),
                new AuthSchemeField(HttpConnectionSettingKeys.ApiKeyPlacement, "Placement", Sensitive: false, "text", Required: false),
                new AuthSchemeField(HttpConnectionSecretFields.ApiKey, "API key", Sensitive: true, "password")],
            HasInteractiveAuthorization: false, HasRefreshableState: false, HasRevocation: false, RequiresWritableStore: false);
    }

    private sealed class FakeAuthenticator(string schemeName = HttpAuthSchemes.Bearer) : IHttpRequestAuthenticator
    {
        public string SchemeName { get; } = schemeName;
        public ValueTask ApplyAsync(HttpRequestMessage request, IResolvedConnection connection, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
