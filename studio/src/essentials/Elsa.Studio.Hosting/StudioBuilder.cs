using CShells.DependencyInjection;

namespace Elsa.Studio.Hosting;

/// <summary>Composes Studio modules and optional hosting extensions before the application is built.</summary>
public sealed class StudioBuilder
{
    private readonly CShellsBuilder _shells;

    internal StudioBuilder(WebApplicationBuilder builder, CShellsBuilder shells, StudioConfigurationMutationGate gate)
    {
        Services = builder.Services;
        Configuration = builder.Configuration;
        Environment = builder.Environment;
        ConfigurationMutationGate = gate;
        _shells = shells;
    }

    public IServiceCollection Services { get; }
    public IConfiguration Configuration { get; }
    public IWebHostEnvironment Environment { get; }
    public StudioConfigurationMutationGate ConfigurationMutationGate { get; }

    /// <summary>Customizes the existing CShells composition after Studio defaults.</summary>
    public StudioBuilder ConfigureShells(Action<CShellsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (Services.IsReadOnly)
            throw new InvalidOperationException("Studio composition must finish before Build.");
        configure(_shells);
        return this;
    }
}
