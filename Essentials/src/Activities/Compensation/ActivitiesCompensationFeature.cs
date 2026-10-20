using CShells.Features;
using Elsa.Activities.Compensation.Internal;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Compensation;

/// <summary>
/// Adds the native <c>CompensationScope</c> activity: its design-side structure handler and its Draft validator.
/// Native compensation is separate from BPMN compensation (ADR 0086).
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Composition")]
[ShellFeature(
    name: "ActivitiesCompensation",
    DisplayName = "Activities Compensation",
    Description = "The compensation scope activity: runs named steps and undoes the completed ones when a step fails."
)]
public class ActivitiesCompensationFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IActivityStructureHandler, CompensationScopeStructureHandler>();
        services.AddScoped<IDraftValidator, CompensationScopeValidator>();
    }
}
