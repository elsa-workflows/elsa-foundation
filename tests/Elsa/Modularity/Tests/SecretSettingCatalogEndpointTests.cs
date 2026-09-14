using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using CShells.Features;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork;
using Elsa.Modularity.Api.Authorization;
using Elsa.Modularity.Api.Endpoints;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Nuplane.Extensions;
using Elsa.Modularity.Nuplane.Services;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workbench;
using Elsa.Workflows.Runtime.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nuplane.Abstractions;
using Nuplane.Admin;
using Xunit;

namespace Elsa.Modularity.Tests;

/// <summary>
/// Drives both feature-catalog surfaces over HTTP: the shell's <c>GET /modularity/features</c> and the Workbench host's
/// <c>GET /_elsa/module-management/registry</c>. Secrets are bound to real features' secret settings (read through
/// their <c>[ManifestSetting]</c> attributes) and to a secret declared in a Nuplane package's manifest.
/// </summary>
public sealed class SecretSettingCatalogEndpointTests : IAsyncDisposable
{
    private const string ManagementKey = "test-management-key";
    private const string PermissionHeader = "X-Test-Permission";
    private const string PackagedFeatureId = "PackagedFeature";

    private const string RecoveryKey = "recovery-signing-key-that-must-not-leak";
    private const string PayloadProtectionKey = "payload-protection-key-that-must-not-leak";
    private const string SeedAdminPassword = "seed-admin-password-that-must-not-leak";
    private const string PackageToken = "package-token-that-must-not-leak";

    private static readonly (string FeatureId, string Setting)[] SecretSettings =
    [
        ("GroundworkWorkflowRuntime", "RecoveryContinuationSigningKey"),
        ("WorkflowsRuntimeApi", "WorkflowAlterationPayloadProtectionKeys"),
        ("FoundationIdentityAspNetCoreIdentityGroundwork", "SeedAdminPassword"),
        (PackagedFeatureId, "ApiToken")
    ];

    private readonly string _contentRoot = Directory.CreateTempSubdirectory("elsa-secret-catalog-").FullName;
    private readonly FakeShellStore _store = new();
    private readonly FakeNuplaneAdminOperations _nuplane = new();
    private WebApplication? _app;

    public SecretSettingCatalogEndpointTests()
    {
        _store.Features["GroundworkWorkflowRuntime"] = Json($$"""{"RecoveryContinuationSigningKey":"{{RecoveryKey}}"}""");
        _store.Features["WorkflowsRuntimeApi"] = Json($$$"""{"WorkflowAlterationPayloadProtectionActiveKeyId":"active","WorkflowAlterationPayloadProtectionKeys":{"active":"{{{PayloadProtectionKey}}}"}}""");
        _store.Features["FoundationIdentityAspNetCoreIdentityGroundwork"] = Json($$"""{"SeedAdminPassword":"{{SeedAdminPassword}}"}""");
        _store.Features[PackagedFeatureId] = Json($$"""{"ApiToken":"{{PackageToken}}"}""");
        _nuplane.Packages.Add(CreatePackage());
    }

    [Fact]
    public async Task Shell_feature_catalog_masks_every_secret_setting()
    {
        using var client = await StartAsync();
        client.DefaultRequestHeaders.Add(PermissionHeader, ModuleManagementPermissionKeys.Read);

        AssertSecretsMasked(
            await GetOkBodyAsync(client, "/modularity/features"),
            root => root.GetProperty("features").EnumerateArray());
    }

    [Fact]
    public async Task Host_module_registry_masks_every_secret_setting()
    {
        using var client = await StartAsync();
        client.DefaultRequestHeaders.Add(ManagementApiKeyAuthentication.HeaderName, ManagementKey);

        AssertSecretsMasked(
            await GetOkBodyAsync(client, "/_elsa/module-management/registry"),
            root => root.GetProperty("modules").EnumerateArray().SelectMany(module => module.GetProperty("features").EnumerateArray()));
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();

        Directory.Delete(_contentRoot, recursive: true);
    }

    private static async Task<string> GetOkBodyAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {path} returned {(int)response.StatusCode}: {body}");
        return body;
    }

    private static void AssertSecretsMasked(string body, Func<JsonElement, IEnumerable<JsonElement>> selectFeatures)
    {
        foreach (var secret in new[] { RecoveryKey, PayloadProtectionKey, SeedAdminPassword, PackageToken })
            Assert.DoesNotContain(secret, body);

        using var document = JsonDocument.Parse(body);
        var configurationById = selectFeatures(document.RootElement).ToDictionary(
            feature => feature.GetProperty("id").GetString()!,
            feature => feature.GetProperty("configuration"));

        foreach (var (featureId, setting) in SecretSettings)
            Assert.Equal(SecretSettingMask.Placeholder, configurationById[featureId].GetProperty(setting).GetString());

        // A non-secret setting beside a secret one still comes through verbatim.
        Assert.Equal("active", configurationById["WorkflowsRuntimeApi"].GetProperty("WorkflowAlterationPayloadProtectionActiveKeyId").GetString());
    }

    private async Task<HttpClient> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = _contentRoot });
        builder.WebHost.UseTestServer();
        builder.Configuration[ManagementApiKeyAuthentication.ConfigurationKey] = ManagementKey;

        builder.Services.AddElsaEndpoints();
        builder.Services.AddAuthentication(TestAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddFoundationIdentityAbstractions(options =>
            options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { TestAuthenticationHandler.SchemeName });
        builder.Services.AddPermissionContributor<ModuleManagementPermissionContributor>();

        // The production catalog composition, fed by the real runtime and package contributors.
        builder.Services.AddNuplaneFeatureCatalog();
        builder.Services.AddSingleton<IRuntimeFeatureCatalog>(new FakeRuntimeFeatureCatalog(
            Descriptor<GroundworkWorkflowRuntimeFeature>("GroundworkWorkflowRuntime"),
            Descriptor<WorkflowsRuntimeApiFeature>("WorkflowsRuntimeApi"),
            Descriptor<AspNetCoreIdentityGroundworkFeature>("FoundationIdentityAspNetCoreIdentityGroundwork")));
        builder.Services.AddSingleton<INuplaneAdminOperations>(_nuplane);
        builder.Services.AddSingleton<IShellFeatureConfigurationStore>(_store);
        builder.Services.AddSingleton<IShellReloader, FakeShellReloader>();
        builder.Services.AddScoped<IModuleRegistryService, ModuleRegistryService>();

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        ModularityApi.MapModularityApi(_app);
        _app.MapElsaModuleManagementApi();
        await _app.StartAsync();
        return _app.GetTestClient();
    }

    private static ShellFeatureDescriptor Descriptor<TFeature>(string id) => new(id) { StartupType = typeof(TFeature) };

    private ActivePackage CreatePackage()
    {
        var installPath = Directory.CreateDirectory(Path.Combine(_contentRoot, "Elsa.SecretPackage")).FullName;
        File.WriteAllText(Path.Combine(installPath, "elsa-package.json"), $$"""
        {
          "package": { "id": "Elsa.SecretPackage", "version": "1.0.0" },
          "features": [
            {
              "id": "{{PackagedFeatureId}}",
              "settings": [ { "name": "ApiToken", "jsonType": "string", "secret": true } ]
            }
          ]
        }
        """);

        return new("Elsa.SecretPackage", "1.0.0", "local", "drop", installPath, DateTimeOffset.UtcNow, "corr", "graph", "gen", default, [], [], true);
    }

    private static JsonElement Json(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "SecretSettingCatalogTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(PermissionHeader, out var permission) || string.IsNullOrWhiteSpace(permission))
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "operator-1"),
                    new Claim(IdentityClaimTypes.Permission, permission.ToString()),
                    new Claim(IdentityClaimTypes.Normalized, "v1")
                ],
                SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
