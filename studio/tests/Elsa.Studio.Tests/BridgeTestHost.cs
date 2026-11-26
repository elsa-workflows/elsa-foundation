using Elsa.Studio.Api.Options;
using Elsa.Studio.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Studio.Tests;

/// <summary>
/// The slim TestServer builder every Studio management-bridge test host starts from: the host's bridge infrastructure
/// (public Hosting registration and Api backend options) configured with the given backend coordinates, auth switch, and any extra configuration (e.g. CShells shell
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
        IDictionary<string, string?>? extraSettings = null,
        Action<StudioBuilder>? configureStudio = null)
    {
        var settings = new Dictionary<string, string?>(extraSettings ?? new Dictionary<string, string?>())
        {
            [StudioBackendManagementOptions.BackendBaseUrlConfigurationKey] = backendBaseUrl,
            [StudioBackendManagementOptions.BackendServerBaseUrlConfigurationKey] = backendServerBaseUrl,
            [StudioBackendManagementOptions.ManagementApiKeyConfigurationKey] = managementKey,
            ["Studio:Auth:Enabled"] = authEnabled ? "true" : "false",
            ["CShells:Shells:Default:Features:StudioApi"] = "true"
        };

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);
        var studio = builder.AddElsaStudio(options => options.ShellsFile = null);
        configureStudio?.Invoke(studio);
        return builder;
    }
}
