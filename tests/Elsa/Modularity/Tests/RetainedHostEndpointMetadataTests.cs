using ConsoleLogStreaming.AspNetCore.DependencyInjection;
using ConsoleLogStreaming.Core.DependencyInjection;
using CShells.Management.Api;
using CShells.Lifecycle;
using Elsa.Api.AspNetCore;
using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Diagnostics.ConsoleLogStreaming;
using Elsa.Modularity.ExtensionBuilder;
using Elsa.Workbench;
using Elsa.Workbench.Readiness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class RetainedHostEndpointMetadataTests
{
    [Fact]
    public async Task Workbench_retained_host_routes_publish_a_valid_complete_manifest()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSignalR();
        builder.Services.AddAuthorization();
        builder.Services.AddConsoleLogStreamingHost(ConsoleLogStreamingSetup.ConfigureHost);
        builder.Services.AddConsoleLogStreamingAspNetCore(ConsoleLogStreamingSetup.ConfigureEndpoints);
        builder.Services.AddSingleton<IShellRegistry>(_ => null!);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ShellReadinessState>();
        builder.Services.AddOptions<ShellReadinessOptions>();
        using var app = builder.Build();

        app.MapGet("/", () => Results.Ok(new { status = "Healthy", service = "elsa-workbench" }))
            .WithHostOwner("Elsa.Workbench")
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .AllowPublic("health", "Provides the workbench process health response.");
        app.MapShellReadiness();
        app.MapElsaModuleManagementApi();
        app.MapElsaExtensionBuilderApi();
        app.MapShellManagementApi("/_admin/shells")
            .WithHostOwner("Elsa.Workbench")
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .WithSecurityDisposition(EndpointSecurityDispositionMetadata.HostCredential(
                "X-Elsa-Module-Management-Key",
                "Elsa.Workbench"))
            .WithHostCredentialEnforcement("X-Elsa-Module-Management-Key", "Elsa.Workbench");
        var consoleLogEndpoints = app.MapGroup("");
        consoleLogEndpoints.RequireAuthorization();
        consoleLogEndpoints.WithHostOwner("Elsa.Workbench")
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .WithSecurityDisposition(EndpointSecurityDispositionMetadata.NamedPolicy("Default", "Elsa.Workbench"));
        consoleLogEndpoints.MapConsoleLogStreaming();

        await app.StartAsync();
        var manifest = new EndpointManifestBuilder(app.Services.GetServices<EndpointDataSource>()).Build();

        Assert.Equal(64, manifest.Entries.Count);
        Assert.All(manifest.Entries, entry =>
        {
            Assert.Equal(EndpointOwnerKind.Host, entry.OwnerKind);
            Assert.Equal("Elsa.Workbench", entry.Owner);
            Assert.Equal(EndpointAuthoringModels.MinimalApi, entry.AuthoringModel);
            Assert.NotNull(entry.SecurityDisposition);
        });

        Assert.Equal(3, manifest.Entries.Count(entry => entry.SecurityDisposition?.Kind == EndpointSecurityDispositionKind.Public));
        Assert.Equal(57, manifest.Entries.Count(entry => entry.SecurityDisposition?.Kind == EndpointSecurityDispositionKind.HostCredential));
        Assert.Equal(4, manifest.Entries.Count(entry => entry.SecurityDisposition?.Kind == EndpointSecurityDispositionKind.NamedPolicy));

        Assert.Equal(42, manifest.Entries.Count(entry => entry.Route.Value.StartsWith("/_elsa/extension-builder", StringComparison.Ordinal)));
        AssertRouteMethods(manifest, new Dictionary<string, string[]>
        {
            ["/"] = ["GET"],
            ["/health/live"] = ["GET"],
            ["/health/ready"] = ["GET"],
            ["/_elsa/module-management/registry"] = ["GET"],
            ["/_elsa/module-management/packages/upload"] = ["POST"],
            ["/_elsa/module-management/packages/drop-folder/{param}"] = ["DELETE"],
            ["/_elsa/module-management/reconcile"] = ["POST"],
            ["/_elsa/module-management/prune"] = ["POST"],
            ["/_elsa/module-management/feeds"] = ["POST"],
            ["/_elsa/module-management/feeds/{param}"] = ["DELETE", "PUT"],
            ["/_elsa/module-management/retention-policy"] = ["PUT"],
            ["/_admin/shells"] = ["GET"],
            ["/_admin/shells/{param}"] = ["GET"],
            ["/_admin/shells/{param}/blueprint"] = ["GET"],
            ["/_admin/shells/reload/{param}"] = ["POST"],
            ["/_admin/shells/reload-all"] = ["POST"],
            ["/_admin/shells/{param}/force-drain"] = ["POST"],
            ["/_elsa/server/diagnostics/console-logs/recent"] = ["GET"],
            ["/_elsa/server/diagnostics/console-logs/sources"] = ["GET"],
            ["/_elsa/server/diagnostics/console-logs/hub"] = ["*"],
            ["/_elsa/server/diagnostics/console-logs/hub/negotiate"] = ["*"]
        });

        Assert.All(manifest.Entries.Where(entry => entry.Route.Value.StartsWith("/_elsa/module-management", StringComparison.Ordinal)), entry =>
            Assert.Equal(EndpointSecurityDispositionKind.HostCredential, entry.SecurityDisposition?.Kind));
        Assert.All(manifest.Entries.Where(entry => entry.Route.Value.StartsWith("/_admin/shells", StringComparison.Ordinal)), entry =>
            Assert.Equal(EndpointSecurityDispositionKind.HostCredential, entry.SecurityDisposition?.Kind));
        Assert.All(manifest.Entries.Where(entry => entry.Route.Value.StartsWith("/_elsa/server/diagnostics/console-logs", StringComparison.Ordinal)), entry =>
            Assert.Equal(EndpointSecurityDispositionKind.NamedPolicy, entry.SecurityDisposition?.Kind));
    }

    private static void AssertRouteMethods(EndpointManifest manifest, IReadOnlyDictionary<string, string[]> expected)
    {
        foreach (var (route, methods) in expected)
        {
            var entries = manifest.Entries.Where(entry => entry.Route.Value == route).ToArray();
            Assert.NotEmpty(entries);
            Assert.Equal(
                methods.Order(StringComparer.Ordinal),
                entries.SelectMany(entry => entry.Methods).Order(StringComparer.Ordinal));
            Assert.All(entries, entry =>
            {
                Assert.Equal(EndpointOwnerKind.Host, entry.OwnerKind);
                Assert.Equal("Elsa.Workbench", entry.Owner);
            });
        }
    }
}
