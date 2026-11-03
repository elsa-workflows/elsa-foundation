using CShells.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Studio.Hosting.Themes;

/// <summary>Public composition for the optional Studio Theme Store.</summary>
public static class ThemeHostingExtensions
{
    /// <summary>Registers theme storage and discovers its features without enabling them. Call once before Build.</summary>
    public static StudioBuilder AddElsaStudioThemes(this StudioBuilder studio, Action<StudioThemeStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(studio);
        if (studio.Services.IsReadOnly)
            throw new InvalidOperationException("Theme composition must finish before Build.");
        if (studio.Services.Any(service => service.ServiceType == typeof(ThemeStoreSettings)))
            throw new InvalidOperationException("AddElsaStudioThemes may only be called once per application.");
        var contentRoot = studio.Environment.ContentRootPath;
        studio.Services.AddOptions<StudioThemeStoreOptions>()
            .Configure(options => ThemeStoreSettings.BindConfiguration(options, studio.Configuration.GetSection("Themes")))
            .Configure(options => configure?.Invoke(options))
            .ValidateOnStart();
        studio.Services.AddSingleton<IValidateOptions<StudioThemeStoreOptions>>(new ThemeOptionsValidator(contentRoot));
        studio.Services.AddSingleton(provider => ThemeStoreSettings.Create(provider.GetRequiredService<IOptions<StudioThemeStoreOptions>>().Value, contentRoot));
        studio.Services.AddSingleton<ThemeDirectorySource>();
        studio.Services.AddHostedService<ThemeStoreStartup>();
        studio.ConfigureShells(shells => shells.WithAssemblies(typeof(ThemeStoreCoreStudioFeature).Assembly)
            .ShareSingletonWithShells<ThemeStoreSettings>()
            .ShareSingletonWithShells<ThemeDirectorySource>());
        return studio;
    }
}

internal sealed class ThemeStoreStartup(ThemeStoreSettings settings) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Materialize copied applied choices during startup, before a caller can mutate a retained option object.
        _ = settings.StoreFile;
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
