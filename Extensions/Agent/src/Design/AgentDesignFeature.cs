using CShells.Features;
using Elsa.Agent.JsonSchema;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Agent.Design.Services;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Publishing.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Design;

/// <summary>Contributes agents to the ordinary versioned activity publication pipeline.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ManifestFeatureCategory("Design")]
[ShellFeature(name: "AgentDesign", DisplayName = "Agents (Design)",
    Description = "Publishes agent agents as native versioned activities.",
    DependsOn = new object[] { "ActivitiesDesignApi", "WorkflowsPublishing" })]
public class AgentDesignFeature : IShellFeature
{
    /// <inheritdoc />
    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<AgentSchemaValidator>();
        services.AddScoped<AgentSchemaCompiler>();
        services.AddScoped<AgentActivityProvider>();
        services.AddScoped<IActivityProvider>(sp => sp.GetRequiredService<AgentActivityProvider>());
        services.AddScoped<IActivityTemplateProviderCompiler>(sp => sp.GetRequiredService<AgentActivityProvider>());
        services.AddScoped<IActivityTemplateDependencyDiscoverer>(sp => sp.GetRequiredService<AgentActivityProvider>());
    }
}
