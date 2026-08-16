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
    }
}
