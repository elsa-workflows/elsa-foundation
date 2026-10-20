using CShells.Features;
using Elsa.Activities.Compensation.Internal;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Compensation;

/// <summary>
/// Adds the native <c>CompensationScope</c> activity with <c>Confirm</c> and <c>Compensate</c>: the scope's design-side
/// structure handler, the Draft validators for scopes, for Confirm and Compensate targets, and for the variables a
/// handed-over compensation handler uses, and the journal projector the runtime's activity state view serves.
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
        services.AddScoped<IDraftValidator, CompensationTargetValidator>();
        services.AddScoped<IDraftValidator, CompensationHandlerVariableValidator>();
        // The read-only journal operators read through the runtime's activity state view (spec 260 R-6).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IActivityStateViewProjector, CompensationJournalProjector>());
    }
}
