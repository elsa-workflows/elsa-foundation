using CShells.Nuplane;
using CShells.Features;
using CShells.AspNetCore.Features;
using Elsa.Studio.Api.Features;
using Elsa.Studio.Core.Attributes;
using Elsa.Modularity.Nuplane.Extensions;
using Elsa.Modularity.Core.Contracts;
using Microsoft.Extensions.Options;
using global::Nuplane;
using global::Nuplane.Abstractions;
using global::Nuplane.Admin;
using global::Nuplane.Builder;
using global::Nuplane.Feeds.Configuration;
using global::Nuplane.Feeds.Registration;
using global::Nuplane.Loading.Hosting.Builder;
using global::Nuplane.Reconciliation.LockFile;
using global::Nuplane.Sources.Directory.Builder;
using global::Nuplane.Sources.Directory.Configuration;
using global::Nuplane.Store.State;

namespace Elsa.Studio.Hosting.Nuplane;

/// <summary>Optional package/feature administration composed over the existing Nuplane infrastructure.</summary>
public static class NuplaneHostingExtensions
{
    /// <summary>Registers optional Studio administration before Build. Callbacks override bound configuration.</summary>
    /// <exception cref="StudioHostingException">The management configuration cannot be loaded.</exception>
    public static StudioBuilder AddElsaStudioNuplane(this StudioBuilder studio, Action<StudioNuplaneOptions>? configure = null, Action<NuplaneBuilder>? configureNuplane = null)
    {
        ArgumentNullException.ThrowIfNull(studio);
        if (studio.Services.IsReadOnly)
            throw new InvalidOperationException("Studio composition must finish before Build.");
        if (studio.Services.Any(service => service.ServiceType == typeof(StudioNuplaneSettings)))
            throw new InvalidOperationException("AddElsaStudioNuplane may only be called once per application.");
        var options = new StudioNuplaneOptions();
        studio.Configuration.GetSection("Studio:Hosting:Nuplane").Bind(options);
        configure?.Invoke(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ManagementFile);
        if (options.DirectoryFeedName is not null && string.IsNullOrWhiteSpace(options.DirectoryFeedName))
            throw new ArgumentException("DirectoryFeedName must be a non-blank registered directory feed name.", nameof(configure));
        var contentRoot = studio.Environment.ContentRootPath;
        var managementFile = Path.GetFullPath(options.ManagementFile, contentRoot);
        try
        {
            ((IConfigurationBuilder)studio.Configuration).AddJsonFile(managementFile, optional: true, reloadOnChange: true);
        }
        catch (Exception error) when (error is IOException or FormatException or UnauthorizedAccessException)
        {
            throw new StudioHostingException($"Could not load Studio management configuration '{managementFile}'.", error);
        }
        var feedName = options.DirectoryFeedName?.Trim();
        studio.Services.AddSingleton(provider => StudioNuplaneSettings.Create(managementFile, feedName, provider.GetRequiredService<IOptions<FeedResolutionOptions>>().Value));
        // Validate the installed selection before Nuplane's reconciliation hosted services start.
        studio.Services.AddHostedService<NuplaneHostingStartup>();
        studio.Services.AddNuplaneAdmin();
        studio.Services.AddNuplanePackageStoreMaintenance();
        studio.Services.AddSingleton<PackageContentReader>();
        var configuration = studio.Configuration.GetSection("Nuplane");
        studio.Services.AddNuplane(configuration, nuplane =>
        {
            nuplane.UseBasePath(contentRoot);
            nuplane.AddDirectoryFeedsFromConfiguration(configuration);
            nuplane.AutoloadPackages(configuration.GetSection("Loading"), loading =>
            {
                // Studio features must use the host's contract identities instead of private package copies.
                foreach (var assembly in new[] { typeof(IShellFeature).Assembly, typeof(IWebShellFeature).Assembly,
                             typeof(StudioModuleAttribute).Assembly, typeof(StudioApiFeature).Assembly, typeof(IServiceCollection).Assembly })
                {
                    var name = assembly.GetName();
                    var token = name.GetPublicKeyToken();
                    loading.SharedAssembly(name.Name!, token is { Length: > 0 } ? Convert.ToHexString(token).ToLowerInvariant() : null, name.Version!.Major);
                }
            });
            configureNuplane?.Invoke(nuplane);
            RegisterFallback(studio, nuplane);
        });
        studio.Services.PostConfigure<FeedResolutionOptions>(value =>
        {
            value.PackageInstallRoot = Resolve(contentRoot, value.PackageInstallRoot ?? ".nuplane/packages");
            if (value.Feeds.Any(feed => feed.Name != StudioNuplaneSettings.FallbackFeedName && feed.ServiceIndex.IsFile))
                value.Feeds.RemoveAll(feed => feed.Name == StudioNuplaneSettings.FallbackFeedName);
        });
        studio.Services.PostConfigure<StoreRegistryOptions>(value =>
        {
            if (!value.UseInMemoryStore)
                value.StateFilePath = Resolve(contentRoot, value.StateFilePath ?? ".nuplane/store-state.json");
        });
        studio.Services.PostConfigure<LockFileOptions>(value => value.Path = Resolve(contentRoot, value.Path));
        studio.Services.AddNuplaneFeatureCatalog();
        studio.Services.AddSingleton<IShellFeatureConfigurationStore, ShellFeatureStore>();
        studio.Services.AddScoped<IShellReloader, SelectedShellReloader>();
        studio.Services.AddScoped<IFeatureActivationGuard, StudioFeatureGuard>();
        studio.Services.AddOptions<NuplaneIntegrationOptions>().Configure<ILoggerFactory>((integration, factory) =>
        {
            var logger = factory.CreateLogger("Elsa.Studio.Hosting.Nuplane.PackageReload");
            integration.OnReloadResults = (results, _) =>
            {
                foreach (var result in results.Where(result => result.Error is not null))
                    logger.LogWarning(result.Error, "Studio shell {ShellName} failed to reload after package reconciliation; its previous generation remains active.", result.Name);
                logger.LogInformation("Reloaded {ReloadedShellCount} active Studio shell(s) after package reconciliation.", results.Count(result => result.Error is null));
                return ValueTask.CompletedTask;
            };
        });
        studio.ConfigureShells(shells => shells.WithNuplaneFeatureDiscovery(integration =>
        {
            integration.RefreshTrigger = NuplaneRefreshTrigger.ChangedOrPending;
            integration.AutoReload = true;
        }).ShareSingletonWithShells<StudioNuplaneSettings>()
          .ShareSingletonWithShells<PackageContentReader>()
          .ShareSingletonWithShells<IShellFeatureConfigurationStore>());
        return studio;
    }

    /// <summary>Maps optional Studio administration. Register AddElsaStudioNuplane before Build.</summary>
    public static IEndpointRouteBuilder MapElsaStudioNuplane(this IEndpointRouteBuilder endpoints)
    {
        _ = endpoints.ServiceProvider.GetRequiredService<StudioNuplaneSettings>();
        FeatureManagementEndpoints.Map(endpoints);
        PackageAssetEndpoints.Map(endpoints);
        return endpoints;
    }

    private static string Resolve(string contentRoot, string path) => Path.GetFullPath(path, contentRoot);

    private static void RegisterFallback(StudioBuilder studio, NuplaneBuilder nuplane)
    {
        // Only final options know whether callback-created feeds include a directory. The fallback source delegates
        // to Nuplane's native-aware source only when that final selection uses it; no second discovery algorithm.
        if (NuplaneFeedRegistrationServices.HasRegisteredFeed(studio.Services, StudioNuplaneSettings.FallbackFeedName))
            throw new ArgumentException($"Feed name '{StudioNuplaneSettings.FallbackFeedName}' is reserved for Studio's fallback.");
        var before = studio.Services.ToHashSet();
        nuplane.AddDirectoryFeed(StudioNuplaneSettings.FallbackFeedName, Path.Combine(studio.Environment.ContentRootPath, "packages"), directory =>
        {
            directory.Watch = false; // Upload already reconciles; direct fallback-folder copies use manual reconciliation.
            directory.IncludeAll();
        });
        var source = studio.Services.Single(service => !before.Contains(service) && service.ServiceType == typeof(IDesiredPackageSource));
        var index = studio.Services.IndexOf(source);
        studio.Services[index] = ServiceDescriptor.Singleton<IDesiredPackageSource>(provider => new FallbackPackageSource(
            (IDesiredPackageSource)source.ImplementationFactory!(provider),
            () => provider.GetRequiredService<StudioNuplaneSettings>().UsesFallback));
    }
}
