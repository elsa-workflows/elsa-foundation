using Microsoft.AspNetCore.Hosting;

namespace Elsa.Modularity.Api.ModuleManagement;

public sealed class ModuleManagementApiOptions
{
    public string? HostOwner { get; set; }
    public string HostId { get; set; } = "server";
    public string HostDisplayName { get; set; } = "Server";
    public string ConfigurationFileName { get; set; } = "appsettings.json";

    internal static string ResolveHostOwner(ModuleManagementApiOptions options, IWebHostEnvironment environment) =>
        string.IsNullOrWhiteSpace(options.HostOwner) ? environment.ApplicationName : options.HostOwner;
}