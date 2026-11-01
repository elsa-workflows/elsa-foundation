using System.Text.Json;
using Elsa.Agent.Specialists.Anthropic;
using Elsa.Agent.Specialists.AzureOpenAI;
using Elsa.Agent.Specialists.Connections;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Providers;

public sealed class SpecialistConnectionProviderTests
{
    [Theory]
    [InlineData("anthropic")]
    [InlineData("azure-openai")]
    public async Task Metadata_never_opens_credentials_and_runtime_uses_the_resolved_snapshot_revision(string vendor)
    {
        var connection = new Resolved(vendor);
        var resolver = new Resolver(connection);
        var provider = Provider(vendor, resolver);
        var model = Model(vendor);
        var metadata = await provider.GetCapabilitiesAsync(model);
        Assert.Equal(0, connection.CredentialReads);
        await using var lease = await provider.OpenAsync(Request(model, metadata));
        Assert.Equal(7, lease.ConnectionVersion);
        Assert.Equal(metadata.Fingerprint, lease.CapabilitiesFingerprint);
        Assert.Equal(1, connection.CredentialReads);
        Assert.Equal("tenant", resolver.Last!.TenantId);
        Assert.Equal(connection.TypeName, resolver.Last.RequiredType);
        // A null tenant still requires an explicitly selected ordinary partition.
        await using var withoutTenant = await provider.OpenAsync(Request(model, metadata) with { Invocation = Identity() with { TenantId = null } });
        Assert.Equal(7, withoutTenant.ConnectionVersion);
    }

    public static IEnumerable<object[]> Refusals() =>
        from vendor in new[] { "anthropic", "azure-openai" }
        from mode in new[] { "unknown-version", "revoked", "unknown-status", "foreign-owner", "wrong-type", "foreign-tenant", "wrong-auth", "http", "userinfo", "query", "path", "denied-host", "any-host", "unknown-model", "unsupported-json", "tokens", "wrong-partition", "wrong-tenant", "global", "privileged", "schema", "settings", "changed-profile" }
        select new object[] { vendor, mode };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Refusals_precede_credential_resolution_and_client_creation(string vendor, string mode)
    {
        var connection = new Resolved(vendor);
        var access = new Access();
        var provider = Provider(vendor, new Resolver(connection), access);
        var request = Request(Model(vendor), await provider.GetCapabilitiesAsync(Model(vendor)));
        switch (mode)
        {
            case "unknown-version": connection.Version = 0; break;
            case "revoked": connection.Status = ConnectionStatuses.Revoked; break;
            case "unknown-status": connection.Status = "FutureStatus"; break;
            case "foreign-owner": connection.Owner = "user"; break;
            case "wrong-type": connection.TypeName = "http"; break;
            case "foreign-tenant": connection.TenantId = "other"; break;
            case "wrong-auth": connection.AuthScheme = HttpAuthSchemes.OAuth2; break;
            case "http": connection.Values["endpoint"] = "http://provider.fixture.invalid"; break;
            case "userinfo": connection.Values["endpoint"] = "https://user@provider.fixture.invalid"; break;
            case "query": connection.Values["endpoint"] = "https://provider.fixture.invalid?credential=private"; break;
            case "path": connection.Values["endpoint"] = "https://provider.fixture.invalid/unqualified"; break;
            case "denied-host": connection.Hosts = AllowedHostsPolicy.Fixed("other.fixture.invalid"); break;
            case "any-host": connection.Hosts = AllowedHostsPolicy.Any; break;
            case "unknown-model": connection.Values["modelId"] = "another-model"; break;
            case "unsupported-json": connection.Values["structuredResults"] = "false"; break;
            case "tokens": request = request with { Descriptor = request.Descriptor with { Limits = new(1, 1000, 8193) } }; break;
            case "wrong-partition": request = request with { Invocation = Identity() with { PersistencePartition = "other" } }; break;
            case "wrong-tenant": request = request with { Invocation = Identity() with { TenantId = "other" } }; break;
            case "global": access.Current = PersistenceAccessContext.Global; break;
            case "privileged": access.Current = PersistenceAccessContext.PrivilegedGlobal(new("fixture-maintenance")); break;
            case "schema": request = request with { Descriptor = request.Descriptor with { ResultSchema = Json("true") } }; break;
            case "settings": request = request with { Descriptor = request.Descriptor with { Settings = Json("{\"unknown\":true}") } }; break;
            case "changed-profile": connection.Values["maximumOutputTokens"] = "8191"; break;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.OpenAsync(request));
        Assert.Equal(0, connection.CredentialReads);
    }

    [Theory]
    [InlineData("anthropic")]
    [InlineData("azure-openai")]
    public async Task Capability_changes_are_fingerprinted_while_operational_revision_changes_remain_compatible(string vendor)
    {
        var connection = new Resolved(vendor);
        var provider = Provider(vendor, new Resolver(connection));
        var first = await provider.GetCapabilitiesAsync(Model(vendor));
        connection.Version++;
        Assert.Equal(first.Fingerprint, (await provider.GetCapabilitiesAsync(Model(vendor))).Fingerprint);
        connection.Values["maximumOutputTokens"] = "8191";
        Assert.NotEqual(first.Fingerprint, (await provider.GetCapabilitiesAsync(Model(vendor))).Fingerprint);
    }

    [Theory]
    [InlineData("{\"type\":\"object\",\"properties\":{\"headline\":{\"type\":\"string\"}},\"required\":[\"headline\"],\"additionalProperties\":false}", true)]
    [InlineData("{\"type\":\"object\",\"properties\":{\"score\":{\"type\":\"number\",\"minimum\":0,\"maximum\":100}},\"required\":[\"score\"],\"additionalProperties\":false}", true)]
    [InlineData("{\"type\":\"object\",\"properties\":{\"headline\":{\"type\":[\"string\",\"null\"]}},\"required\":[\"headline\"],\"additionalProperties\":false}", true)]
    [InlineData("{\"type\":\"object\",\"properties\":{\"headline\":{\"type\":\"string\"}},\"required\":[],\"additionalProperties\":false}", false)]
    [InlineData("{\"type\":\"object\",\"properties\":{},\"required\":[],\"additionalProperties\":true}", false)]
    [InlineData("{\"type\":\"object\",\"properties\":{\"headline\":{\"type\":\"string\",\"const\":\"fixed\"}},\"required\":[\"headline\"],\"additionalProperties\":false}", false)]
    [InlineData("{\"type\":\"object\",\"properties\":{},\"required\":[],\"additionalProperties\":false,\"$ref\":\"#\"}", false)]
    public void The_explicit_provider_schema_subset_refuses_unqualified_request_shapes(string raw, bool supported) =>
        Assert.Equal(supported, SpecialistProviderSchema.Supports(Json(raw)));

    internal static SpecialistConnectionModelProvider Provider(string vendor, IConnectionResolver resolver, Access? access = null) => vendor == "anthropic"
        ? new AnthropicSpecialistModelProvider(resolver, access ?? new()) : new AzureOpenAISpecialistModelProvider(resolver, access ?? new());
    internal static SpecialistModelReference Model(string vendor) => new(vendor, "model-connection", "fixture-model");
    internal static SpecialistInvocationIdentity Identity() => new("tenant", "tenant", "workflow", "activity", "operation", "artifact", "artifact-hash", "version", "template-hash");
    internal static SpecialistModelOpenRequest Request(SpecialistModelReference model, SpecialistModelCapabilities metadata) => new(
        new("Write a headline.", model, Json("{\"temperature\":0.2}"), Json("{\"type\":\"object\"}"), Schema, metadata.Fingerprint,
            SpecialistRuntimeIds.SchemaProfile, new(1, 30_000, 64), false), Identity());
    internal static JsonElement Schema => Json("""{"type":"object","properties":{"headline":{"type":"string"}},"required":["headline"],"additionalProperties":false}""");
    internal static JsonElement Json(string raw) { using var document = JsonDocument.Parse(raw); return document.RootElement.Clone(); }

    internal sealed class Access : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; set; } = PersistenceAccessContext.Scoped(new("tenant"));
    }
    internal sealed class Resolver(Resolved connection) : IConnectionResolver
    {
        public ConnectionResolveRequest? Last { get; private set; }
        public ValueTask<IResolvedConnection> ResolveAsync(ConnectionResolveRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Last = request; return ValueTask.FromResult<IResolvedConnection>(connection);
        }
    }
    internal sealed class Resolved(string vendor) : IResolvedConnection
    {
        public string TenantId { get; set; } = "tenant";
        public string Name => "model-connection";
        public string TypeName { get; set; } = "specialist-" + vendor;
        public string AuthScheme { get; set; } = HttpAuthSchemes.ApiKey;
        public string Owner { get; set; } = ConnectionOwners.Tenant;
        public string Status { get; set; } = ConnectionStatuses.Ready;
        public int Version { get; set; } = 7;
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal)
        {
            ["endpoint"] = "https://provider.fixture.invalid", ["allowedHosts"] = "provider.fixture.invalid",
            ["modelId"] = "fixture-model", ["maximumOutputTokens"] = "8192", ["structuredResults"] = "true", ["temperatureSupported"] = "true"
        };
        public IReadOnlyDictionary<string, string> Settings => Values;
        public AllowedHostsPolicy Hosts { get; set; } = AllowedHostsPolicy.Fixed("provider.fixture.invalid");
        public int CredentialReads { get; private set; }
        public ValueTask<SensitiveValue> GetSensitiveAsync(string fieldName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Assert.Equal(HttpConnectionSecretFields.ApiKey, fieldName);
            CredentialReads++; return ValueTask.FromResult(new SensitiveValue("fixture-not-a-real-credential"));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
