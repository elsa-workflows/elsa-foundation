using CShells.Features;
using Elsa.Agent.Specialists.JsonSchema;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Agent.Specialists.Design.Services;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Publishing.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Specialists.Design;

/// <summary>Contributes specialists to the ordinary versioned activity publication pipeline.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ManifestFeatureCategory("Design")]
[ShellFeature(name: "SpecialistsDesign", DisplayName = "Specialists (Design)",
    Description = "Publishes specialist agents as native versioned activities.",
    DependsOn = new object[] { "ActivitiesDesignApi", "WorkflowsPublishing" })]
public class SpecialistsDesignFeature : IShellFeature
{
    /// <inheritdoc />
    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<SpecialistSchemaValidator>();
        services.AddScoped<SpecialistSchemaCompiler>();
        services.AddScoped<SpecialistActivityProvider>();
        services.AddScoped<IActivityProvider>(sp => sp.GetRequiredService<SpecialistActivityProvider>());
        services.AddScoped<IActivityTemplateProviderCompiler>(sp => sp.GetRequiredService<SpecialistActivityProvider>());
        services.AddScoped<IActivityTemplateDependencyDiscoverer>(sp => sp.GetRequiredService<SpecialistActivityProvider>());
    }
}
