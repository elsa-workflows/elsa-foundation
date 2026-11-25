using System.Reflection;
using CShells.DependencyInjection;
using Elsa.Studio.Backends;
using Elsa.Studio.Hosting.Nuplane;
using Elsa.Studio.Hosting.Themes;
using Microsoft.AspNetCore.Builder;

namespace Elsa.Studio.Hosting.Distribution;

/// <summary>The Studio host composition every shipped Studio app shares; each app supplies only its in-box modules.</summary>
public static class DistributionHostingExtensions
{
    // The directory feed add-on uploads go to, unless Studio:Hosting:Nuplane:DirectoryFeedName names another. The app's
    // nuplane-management.json registers it.
    private const string DirectoryFeedName = "studio-local-packages";

    /// <summary>
    /// Registers Studio hosting, the Backends authority, Nuplane add-on administration and the Theme Store, and declares
    /// <paramref name="startupAssemblies"/> as the app's in-box feature assemblies. Call once before Build.
    /// </summary>
    /// <remarks>
    /// The Studio API and Backends feature assemblies are always startup assemblies, and the Theme Store assemblies are always
    /// registered for discovery, so a bare app passes only the modules it adds. Every other module arrives from the Nuplane feed. <paramref name="configure"/> runs after
    /// <c>Studio:Hosting</c> is bound, as in <see cref="StudioHostingExtensions.AddElsaStudio"/>.
    /// </remarks>
    /// <exception cref="StudioHostingException">The shell or management configuration cannot be loaded.</exception>
    public static StudioBuilder AddElsaStudioDistribution(this WebApplicationBuilder builder, IEnumerable<Assembly> startupAssemblies, Action<StudioHostingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(startupAssemblies);
        var assemblies = startupAssemblies.Append(typeof(BackendsStudioFeature).Assembly).Distinct().ToArray();

        var studio = builder.AddElsaStudio(configure);
        builder.Services.AddStudioBackendsHostConfiguration(builder.Configuration);
        studio.AddElsaStudioNuplane(options => options.DirectoryFeedName ??= DirectoryFeedName);
        studio.AddElsaStudioThemes();
        studio.ConfigureShells(shells => shells.WithStudioStartupAssemblies(assemblies));
        return studio;
    }

    /// <summary>Installs the Studio middleware and maps the Studio and add-on administration endpoints.</summary>
    /// <remarks>Register <see cref="AddElsaStudioDistribution"/> before Build. Map app-specific endpoints after this call.</remarks>
    public static WebApplication UseElsaStudioDistribution(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseElsaStudio();
        app.MapElsaStudio();
        app.MapElsaStudioNuplane();
        return app;
    }
}
