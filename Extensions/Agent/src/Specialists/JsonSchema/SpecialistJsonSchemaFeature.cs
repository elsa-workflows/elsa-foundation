using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Specialists.JsonSchema;

/// <summary>Registers the shared bounded schema profile used by specialist authoring and execution.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ShellFeature(name: "SpecialistJsonSchema", DisplayName = "Specialist JSON Schema",
    Description = "Validates specialist JSON contracts without authoring dependencies or remote schema resolution.")]
public class SpecialistJsonSchemaFeature : IShellFeature
{
    /// <inheritdoc />
    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<SpecialistSchemaValidator>();
    }
}
