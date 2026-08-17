using CShells.Features;
using CShells.Lifecycle;
using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Host.Health;
using Elsa.Foundation.Host.ModuleManagement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Nuplane.Abstractions;
using Nuplane.Admin;
using Nuplane.Operational;
using Nuplane.Reconciliation;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class FoundationHostEndpointTests
{
    [Fact]
    public async Task Foundation_host_retained_routes_publish_the_exact_manifest()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IShellRegistry>(_ => null!);
        builder.Services.AddSingleton<IRuntimeFeatureCatalog>(_ => null!);
        using var app = builder.Build();

        app.MapHostHealth();
        app.MapModuleManagementApi(new ModuleManagementOptions
        {
            Enabled = true,
            ApiKey = "expected-key"
        });

        await app.StartAsync();
        var manifest = new EndpointManifestBuilder(app.Services.GetServices<EndpointDataSource>()).Build();

        Assert.Equal(4, manifest.Entries.Count);
        var identities = manifest.Entries
            .SelectMany(entry => entry.Methods.Select(method => (entry.Route.Value, Method: method)))
            .ToHashSet();
        var expectedIdentities = new HashSet<(string Route, string Method)>
        {
            ("/health/live", "GET"),
            ("/health/ready", "GET"),
            ("/_module-management/reconcile", "POST"),
            ("/_module-management/reload", "POST")
        };
        Assert.True(expectedIdentities.SetEquals(identities));

        Assert.All(manifest.Entries, entry =>
        {
            Assert.Equal(EndpointOwnerKind.Host, entry.OwnerKind);
            Assert.Equal("Elsa.Foundation.Host", entry.Owner);
            Assert.Equal(EndpointAuthoringModels.MinimalApi, entry.AuthoringModel);
        });
        Assert.Equal(2, manifest.Entries.Count(entry => entry.SecurityDisposition?.Kind == EndpointSecurityDispositionKind.Public));
        Assert.Equal(2, manifest.Entries.Count(entry => entry.SecurityDisposition?.Kind == EndpointSecurityDispositionKind.HostCredential));
        Assert.All(manifest.Entries.Where(entry => entry.Route.Value.StartsWith("/_module-management", StringComparison.Ordinal)), entry =>
        {
            Assert.Equal(EndpointSecurityDispositionKind.HostCredential, entry.SecurityDisposition?.Kind);
            Assert.Equal(ModuleManagementOptions.ApiKeyHeader, entry.SecurityDisposition?.Value);
            Assert.Equal("Elsa.Foundation.Host", entry.SecurityDisposition?.Owner);
        });
    }

    [Fact]
    public async Task Foundation_host_module_management_credential_filter_fails_closed_and_allows_a_valid_key()
    {
        await using (var blankConfiguredKeyApp = await StartModuleManagementApp(null))
        {
            using var client = blankConfiguredKeyApp.GetTestClient();
            client.DefaultRequestHeaders.Add(ModuleManagementOptions.ApiKeyHeader, "expected-key");
            var response = await client.PostAsync("/_module-management/reconcile", content: null);
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var operations = new TestNuplaneAdminOperations();
        await using var app = await StartModuleManagementApp("expected-key", operations);
        using var missingKeyClient = app.GetTestClient();
        var missing = await missingKeyClient.PostAsync("/_module-management/reconcile", content: null);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, missing.StatusCode);

        using var invalidKeyClient = app.GetTestClient();
        invalidKeyClient.DefaultRequestHeaders.Add(ModuleManagementOptions.ApiKeyHeader, "wrong-key");
        var invalid = await invalidKeyClient.PostAsync("/_module-management/reconcile", content: null);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, invalid.StatusCode);

        using var validKeyClient = app.GetTestClient();
        validKeyClient.DefaultRequestHeaders.Add(ModuleManagementOptions.ApiKeyHeader, "expected-key");
        var valid = await validKeyClient.PostAsync("/_module-management/reconcile", content: null);
        Assert.Equal(System.Net.HttpStatusCode.OK, valid.StatusCode);
        Assert.True(operations.ReconcileCalled);
    }

    private static async Task<WebApplication> StartModuleManagementApp(string? apiKey, TestNuplaneAdminOperations? operations = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IShellRegistry>(_ => null!);
        builder.Services.AddSingleton<IRuntimeFeatureCatalog>(_ => null!);
        builder.Services.AddSingleton<INuplaneAdminOperations>(operations ?? new TestNuplaneAdminOperations());
        var app = builder.Build();
        app.MapModuleManagementApi(new ModuleManagementOptions { Enabled = true, ApiKey = apiKey });
        await app.StartAsync();
        return app;
    }

    private sealed class TestNuplaneAdminOperations : INuplaneAdminOperations
    {
        public bool ReconcileCalled { get; private set; }

        public Task<ActivePackagesSnapshot> GetPackagesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ActivePackagesSnapshot>(default!);

        public Task<OperationalStateSnapshot> GetStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult<OperationalStateSnapshot>(default!);

        public Task<ManualReconcileOutcome> TriggerReconcileAsync(CancellationToken cancellationToken)
        {
            ReconcileCalled = true;
            return Task.FromResult<ManualReconcileOutcome>(default!);
        }
    }
}
