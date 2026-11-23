using CShells.Features;
using CShells.Lifecycle;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Oidc;

public static class ManagedOidcRegistration
{
    public const string HttpClientName = "Elsa.ManagedOidc";
    public static IServiceCollection AddFoundationExternalOidc(this IServiceCollection services, Action<ManagedOidcOptions>? configure = null)
    {
        services.AddFoundationExternalAuthentication();
        services.AddOptions<ManagedOidcOptions>();
        if (configure is not null) services.Configure(configure);
        services.AddHttpClient(HttpClientName).RedactLoggedHeaders(_ => true).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseCookies = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IExternalAuthenticationProtocol, ManagedOidcProtocol>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ManagedOidcActivation>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IShellInitializer, ManagedOidcActivation>());
        return services;
    }
}

internal sealed class ManagedOidcActivation(IOptions<ManagedOidcOptions> options, IServiceProvider services) : IHostedService, IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (options.Value.BackchannelTimeout <= TimeSpan.Zero || options.Value.BackchannelTimeout > TimeSpan.FromSeconds(30))
            throw new InvalidOperationException("Managed OIDC BackchannelTimeout must be between zero and thirty seconds.");
        if (options.Value.AllowLoopbackHttpForDevelopment && services.GetService<IHostEnvironment>()?.IsDevelopment() != true)
            throw new InvalidOperationException("Managed OIDC loopback HTTP requires a confirmed Development host.");
        return Task.CompletedTask;
    }
    public Task StartAsync(CancellationToken cancellationToken) => InitializeAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Identity")]
[ShellFeature(name: "FoundationIdentityExternalOidc", DisplayName = "Managed OpenID Connect",
    Description = "Authorization-code/PKCE protocol for managed external identity connections.")]
public class ManagedOidcFeature : IShellFeature
{
    public bool AllowLoopbackHttpForDevelopment { get; set; }
    public virtual void ConfigureServices(IServiceCollection services) => services.AddFoundationExternalOidc(options =>
        options.AllowLoopbackHttpForDevelopment = AllowLoopbackHttpForDevelopment);
}
