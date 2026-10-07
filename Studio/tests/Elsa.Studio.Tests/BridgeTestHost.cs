using Elsa.Studio.Api.Options;
using Elsa.Studio.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Studio.Tests;

/// <summary>
/// The slim TestServer builder every Studio management-bridge test host starts from: the host's bridge infrastructure
/// (<see cref="StudioBridgeAuth"/> gate + <see cref="StudioBackendManagementBridgeServiceCollectionExtensions"/>
/// options) configured with the given backend coordinates, auth switch, and any extra configuration (e.g. CShells shell
/// settings). Callers add their own client routing and endpoint mapping.
/// </summary>
internal static class BridgeTestHost
{
    public const string BackendBaseUrl = "https://backend.example";
    public const string ManagementKey = "s3cr3t-management-key";

    public static WebApplicationBuilder CreateBuilder(
        string? backendBaseUrl = BackendBaseUrl,
        string? managementKey = ManagementKey,
        bool authEnabled = false,
        string? backendServerBaseUrl = null,
        IDictionary<string, string?>? extraSettings = null)
    {
        var settings = new Dictionary<string, string?>(extraSettings ?? new Dictionary<string, string?>())
        {
            [StudioBackendManagementOptions.BackendBaseUrlConfigurationKey] = backendBaseUrl,
            [StudioBackendManagementOptions.BackendServerBaseUrlConfigurationKey] = backendServerBaseUrl,
            [StudioBackendManagementOptions.ManagementApiKeyConfigurationKey] = managementKey,
            ["Studio:Auth:Enabled"] = authEnabled ? "true" : "false"
        };

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddStudioBridgeAuth(builder.Configuration);
        builder.Services.AddStudioBackendManagementBridge(builder.Configuration);
        return builder;
    }
}
