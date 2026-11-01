using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.AI;

namespace Elsa.Agent.Specialists.Connections;

/// <summary>
/// Shared operational connection boundary for SDK contributors. A connection explicitly binds one
/// model/deployment and declared support profile; this is configured metadata, not live discovery.
/// </summary>
public abstract class SpecialistConnectionModelProvider(
    IConnectionResolver resolver,
    IPersistenceAccessContextAccessor access) : ISpecialistModelClientFactory, ISpecialistModelCapabilitiesSource, IConnectionTypeProvider
{
    /// <inheritdoc />
    public abstract string ProviderKey { get; }
    /// <summary>The unique operational connection type owned by this contributor.</summary>
    protected abstract string ConnectionTypeName { get; }
    /// <summary>SDK, API and schema-profile identity. Change whenever effective support changes.</summary>
    protected abstract string ProtocolProfile { get; }
    /// <summary>Creates an owned SDK client with retries disabled and no ambient credential fallback.</summary>
    protected abstract IChatClient CreateClient(Uri endpoint, string modelId, SensitiveValue apiKey, HttpClient http);

    /// <summary>Creates the primary transport. Custom implementations require their own physical-request qualification.</summary>
    protected virtual HttpMessageHandler CreatePrimaryHandler() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false, UseCookies = false, UseProxy = false,
        Credentials = null, PreAuthenticate = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    };

    /// <inheritdoc />
    public ConnectionTypeDescriptor Descriptor => new(ConnectionTypeName, ProviderKey + " specialist model", [HttpAuthSchemes.ApiKey],
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
            var scope = access.Current.RequireScope();
            if (!StringComparer.Ordinal.Equals(scope.Value, connection.TenantId)) throw Unavailable();
            Read(connection, null);
            CheckCredential(await connection.GetSensitiveAsync(HttpConnectionSecretFields.ApiKey, cancellationToken));
            return new() { Succeeded = true, Message = "Connection configuration and credential reference are valid. Inference has not been tested." };
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new() { Succeeded = false, Message = "The model connection configuration or credential is unavailable." };
        }
    }

    /// <inheritdoc />
    public async ValueTask<SpecialistModelCapabilities> GetCapabilitiesAsync(SpecialistModelReference model, CancellationToken cancellationToken = default)
    {
        await using var connection = await ResolveAsync(model, null, cancellationToken);
        return Read(connection, model).Capabilities;
    }

    /// <inheritdoc />
    public async ValueTask<bool> SupportsResultSchemaAsync(SpecialistModelReference model, JsonElement schema, CancellationToken cancellationToken = default) =>
        (await GetCapabilitiesAsync(model, cancellationToken)).SupportsJsonSchema && SpecialistProviderSchema.Supports(schema);

    /// <inheritdoc />
    public async ValueTask<SpecialistModelClientLease> OpenAsync(SpecialistModelOpenRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var model = request.Descriptor.Model;
        await using var connection = await ResolveAsync(model, request.Invocation, cancellationToken);
        var profile = Read(connection, model);
        if (profile.Capabilities.Fingerprint != request.Descriptor.CapabilitiesFingerprint || !profile.Capabilities.SupportsJsonSchema ||
            request.Descriptor.Limits.MaximumOutputTokens is { } tokens && tokens > profile.Capabilities.MaximumOutputTokens ||
            !SpecialistProviderSchema.Supports(request.Descriptor.ResultSchema))
            throw Unavailable();
        var settings = ReadSettings(request.Descriptor.Settings);
        if (settings is not null && !profile.TemperatureSupported) throw Unavailable();
        // Resolve credential material only after every authority, metadata and schema check succeeds.
        var key = await connection.GetSensitiveAsync(HttpConnectionSecretFields.ApiKey, cancellationToken);
        CheckCredential(key);
        var version = connection.Version;
        var http = new HttpClient(new ModelRequestGuard(profile.Endpoint, connection.Hosts,
            ct => ValidateDispatchAsync(model, request.Invocation, version, profile.Capabilities.Fingerprint, ct), CreatePrimaryHandler()), disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        try
        {
            return new(CreateClient(profile.Endpoint, model.ModelId, key, http), new ChatOptions
            {
                Temperature = settings,
                MaxOutputTokens = profile.Capabilities.MaximumOutputTokens
            }, profile.Capabilities.Fingerprint, version, new OwnedHttpClient(http));
        }
        catch { http.Dispose(); throw; }
    }

    private async ValueTask ValidateDispatchAsync(SpecialistModelReference model, SpecialistInvocationIdentity invocation, int version, string fingerprint, CancellationToken cancellationToken)
    {
        await using var current = await ResolveAsync(model, invocation, cancellationToken);
        var profile = Read(current, model);
        if (current.Version != version || profile.Capabilities.Fingerprint != fingerprint) throw Unavailable();
    }

    private async ValueTask<IResolvedConnection> ResolveAsync(SpecialistModelReference model, SpecialistInvocationIdentity? invocation, CancellationToken cancellationToken)
    {
        var context = access.Current;
        var scope = context.RequireScope();
        if (invocation is not null)
        {
            context.EnsureTenantScope(invocation.TenantId);
            if (!StringComparer.Ordinal.Equals(invocation.PersistencePartition, scope.Value)) throw Unavailable();
        }
        if (model.ProviderKey != ProviderKey || string.IsNullOrWhiteSpace(model.ModelId)) throw Unavailable();
        var connection = await resolver.ResolveAsync(new() { TenantId = scope.Value, Name = model.ConnectionName, RequiredType = ConnectionTypeName }, cancellationToken);
        if (!StringComparer.Ordinal.Equals(connection.TenantId, scope.Value))
        {
            await connection.DisposeAsync();
            throw Unavailable();
        }
        return connection;
    }

    private Profile Read(IResolvedConnection connection, SpecialistModelReference? model)
    {
        if (connection.TypeName != ConnectionTypeName || connection.Owner != ConnectionOwners.Tenant || connection.Status != ConnectionStatuses.Ready ||
            connection.AuthScheme != HttpAuthSchemes.ApiKey || connection.Version < 1 ||
            !connection.Settings.TryGetValue("endpoint", out var value) || !Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 ||
            endpoint.AbsolutePath != "/" || connection.Hosts.Kind == AllowedHostsPolicy.AnyKind || !connection.Hosts.Allows(endpoint) ||
            !connection.Settings.TryGetValue("modelId", out var modelId) || string.IsNullOrWhiteSpace(modelId) || modelId.Length > 256 ||
            modelId.Any(x => !char.IsAsciiLetterOrDigit(x) && x is not ('-' or '_' or '.')) ||
            model is not null && model.ModelId != modelId ||
            !connection.Settings.TryGetValue("maximumOutputTokens", out var maximum) || !int.TryParse(maximum, NumberStyles.None, CultureInfo.InvariantCulture, out var tokens) || tokens is < 1 or > 1_000_000 ||
            !connection.Settings.TryGetValue("structuredResults", out var structured) || !bool.TryParse(structured, out var supports) ||
            !connection.Settings.TryGetValue("temperatureSupported", out var temperature) || !bool.TryParse(temperature, out var supportsTemperature))
            throw Unavailable();
        // Array serialization avoids delimiter collisions. Revision itself is operational: credential
        // rotation/display-name edits do not invalidate immutable publication with unchanged support.
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new object[]
        {
            ProviderKey, ProtocolProfile, SpecialistProviderSchema.Profile, endpoint.AbsoluteUri, modelId, tokens, supports, supportsTemperature,
            connection.Hosts.Kind, connection.Hosts.Hosts.Order(StringComparer.Ordinal).ToArray()
        }))));
        return new(endpoint, new(fingerprint, supportsTemperature ? SettingsSchema : EmptySettingsSchema, supports, tokens), supportsTemperature);
    }

    private static float? ReadSettings(JsonElement settings)
    {
        if (settings.ValueKind != JsonValueKind.Object) throw Unavailable();
        float? temperature = null;
        var count = 0;
        foreach (var property in settings.EnumerateObject())
        {
            if (++count > 1 || property.Name != "temperature" || !property.Value.TryGetSingle(out var value) || !float.IsFinite(value) || value is < 0 or > 1) throw Unavailable();
            temperature = value;
        }
        return temperature;
    }

    private static void CheckCredential(SensitiveValue key)
    {
        if (key.IsEmpty || key.Reveal().Any(x => x < ' ' || x > '~')) throw Unavailable();
    }

    private static readonly JsonElement SettingsSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object", additionalProperties = false,
        properties = new { temperature = new { type = "number", minimum = 0, maximum = 1 } }
    });
    private static readonly JsonElement EmptySettingsSchema = JsonSerializer.SerializeToElement(new { type = "object", additionalProperties = false, properties = new { } });
    private static InvalidOperationException Unavailable() => new("The model connection does not satisfy the authorized specialist profile.");
    private sealed record Profile(Uri Endpoint, SpecialistModelCapabilities Capabilities, bool TemperatureSupported);
    private sealed class OwnedHttpClient(HttpClient client) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { client.Dispose(); return ValueTask.CompletedTask; }
    }

    private sealed class ModelRequestGuard(Uri endpoint, AllowedHostsPolicy hosts, Func<CancellationToken, ValueTask> authorize, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = request.RequestUri;
            if (uri is null || uri.Scheme != endpoint.Scheme || uri.IdnHost != endpoint.IdnHost || uri.Port != endpoint.Port ||
                !hosts.Allows(uri) || uri.UserInfo.Length != 0 || request.Method != HttpMethod.Post)
                throw Unavailable();
            request.Version = HttpVersion.Version11;
            request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
            request.Headers.ExpectContinue = false;
            await authorize(cancellationToken);
            return await base.SendAsync(request, cancellationToken);
        }
    }
}
