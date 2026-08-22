using Elsa.Activities.Design.Api;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Mediator.Core.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Reflection;

namespace Elsa.Activities.Design.Tests.Api.Support;

/// <summary>
/// Real TestServer host for the post-migration owner. The mapper is resolved by reflection so this
/// scaffold compiles before T019 adds the production mapper and fails with an explicit missing-owner
/// diagnostic instead of introducing a test-only production dependency.
/// </summary>
public sealed class ActivitiesDesignMinimalApiHost(IHost host) : IAsyncDisposable
{
    public HttpClient Client { get; } = host.GetTestClient();
    public IHost Host { get; } = host;
    internal CaptureRequestSender RequestSender { get; } = host.Services.GetRequiredService<CaptureRequestSender>();
    internal CaptureCommandSender CommandSender { get; } = host.Services.GetRequiredService<CaptureCommandSender>();

    public static async Task<ActivitiesDesignMinimalApiHost> StartAsync()
    {
        var host = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.UseSetting(WebHostDefaults.ApplicationKey, "elsa-activities-design-minimal-api");
                webHost.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddHttpContextAccessor();
                    services.AddAuthentication(CaptureAuthenticationHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, CaptureAuthenticationHandler>(CaptureAuthenticationHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddFoundationIdentityAbstractions(options =>
                        options.NormalizedAuthenticationTypes = new HashSet<string>([CaptureAuthenticationHandler.SchemeName], StringComparer.Ordinal));
                    services.AddOpenApi();
                    new ActivitiesDesignApiFeature().ConfigureServices(services);
                    services.AddSingleton<CaptureRequestSender>();
                    services.AddSingleton<IRequestSender>(provider => provider.GetRequiredService<CaptureRequestSender>());
                    services.AddSingleton<CaptureCommandSender>();
                    services.AddSingleton<ICommandSender>(provider => provider.GetRequiredService<CaptureCommandSender>());
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        MapActivitiesDesignApi(endpoints);
                        endpoints.MapOpenApi();
                    });
                });
            })
            .Build();

        await host.StartAsync();
        return new ActivitiesDesignMinimalApiHost(host);
    }

    public async Task<string> GetOpenApiAsync()
    {
        using var response = await Client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Host.StopAsync();
        Host.Dispose();
    }

    private static void MapActivitiesDesignApi(IEndpointRouteBuilder endpoints)
    {
        var assembly = typeof(ActivitiesDesignApiFeature).Assembly;
        var mapperType = assembly.GetType("Elsa.Activities.Design.Api.ActivitiesDesignApi", throwOnError: false);
        if (mapperType is null)
            throw new InvalidOperationException(
                "Activities Design Minimal API mapper is absent. T016-T018 require Elsa.Activities.Design.Api.ActivitiesDesignApi before the post-migration host can start.");

        var mapper = mapperType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method => method.Name is "MapActivitiesDesignApi" or "Map")
            .SingleOrDefault(method =>
            {
                var parameters = method.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(endpoints.GetType());
            });
        if (mapper is null)
            throw new InvalidOperationException(
                $"Activities Design Minimal API mapper type '{mapperType.FullName}' does not expose a one-argument MapActivitiesDesignApi/Map method.");

        try
        {
            mapper.Invoke(null, [endpoints]);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw exception.InnerException;
        }
    }
}
