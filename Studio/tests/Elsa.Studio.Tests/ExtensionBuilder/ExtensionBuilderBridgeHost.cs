using Elsa.Studio.ExtensionBuilder;
using Elsa.Studio.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Tests.ExtensionBuilder;

/// <summary>
/// The slim TestServer host the Extension Builder bridge tests run against: <see cref="BridgeTestHost"/>'s bridge
/// infrastructure plus the extension's own registration and endpoint mapping — exactly what the
/// <see cref="ExtensionBuilderStudioFeature"/> contributes, minus the CShells shell around it. Every Studio→backend
/// client (capabilities, relay, bearer introspection) is routed through the supplied <see cref="RecordingBackend"/> so
/// the tests assert real HTTP behaviour at the wire.
/// </summary>
internal static class ExtensionBuilderBridgeHost
{
    public const string ExtensionBuilderReadBearer = "user-extension-builder-read";
    public const string ExtensionBuilderManageBearer = "user-extension-builder-manage";
    public const string ModuleReadBearer = "user-module-read";

    /// <summary>The host-control permissions the stub backend's session endpoint reports for each test bearer.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> BearerPermissions = new Dictionary<string, string[]>
    {
        [ExtensionBuilderReadBearer] = [ExtensionBuilderPermissions.Read],
        [ExtensionBuilderManageBearer] = [ExtensionBuilderPermissions.Manage],
        [ModuleReadBearer] = [StudioBridgeAuth.ModuleManagementReadPermission]
    };

    public static async Task<WebApplication> StartAsync(
        RecordingBackend backend,
        string? backendBaseUrl = BridgeTestHost.BackendBaseUrl,
        string? managementKey = BridgeTestHost.ManagementKey,
        bool authEnabled = false,
        TimeProvider? timeProvider = null,
        string? pathBase = null)
    {
        var builder = BridgeTestHost.CreateBuilder(backendBaseUrl, managementKey, authEnabled);
        builder.Services.AddStudioExtensionBuilderBridge();
        if (timeProvider is not null)
            builder.Services.AddSingleton(timeProvider);

        builder.Services.RouteBackendClientsThrough(backend);

        var app = builder.Build();
        if (pathBase is not null)
            app.UsePathBase(pathBase);
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapStudioExtensionBuilderBridge();

        await app.StartAsync();
        return app;
    }

    /// <summary>
    /// Routes the extension's typed clients and the named bearer-introspection client through the recording backend
    /// stub so the tests assert real HTTP behaviour (methods, paths, headers, bodies, outbound-call counts) at the wire.
    /// </summary>
    public static IServiceCollection RouteBackendClientsThrough(this IServiceCollection services, RecordingBackend backend)
    {
        foreach (var clientName in new[] { nameof(StudioExtensionBuilderRelayClient), nameof(StudioExtensionBuilderCapabilitiesClient), StudioBridgeAuthHandler.HttpClientName })
            services.AddHttpClient(clientName).ConfigurePrimaryHttpMessageHandler(() => backend);

        return services;
    }
}
