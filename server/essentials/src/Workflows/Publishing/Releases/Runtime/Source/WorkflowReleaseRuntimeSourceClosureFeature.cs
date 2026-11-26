using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Publishing.Releases.Capture.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Publishing.Releases.Runtime.Source;

/// <summary>Optional exact source material/template expansion. No compiler, target lookup, import or activation.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ShellFeature(name: "WorkflowReleaseRuntimeSourceClosure", DisplayName = "Workflow release Runtime source closure",
    Description = "Exports explicitly authorized, bounded native/model/template dependencies under exact source retention.", DependsOn = new object[] { "WorkflowReleaseRuntime" })]
public class WorkflowReleaseRuntimeSourceClosureFeature : IShellFeature, IPostConfigureShellServices
{
    public virtual void ConfigureServices(IServiceCollection services) =>
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReleaseSourceMemberContributor, RuntimeSourceMemberContributor>());
    public virtual void PostConfigureServices(IServiceCollection services)
    {
        foreach (var contract in new[] { typeof(IReleaseRuntimeSourceContextAccessor), typeof(IReleaseRuntimeSourceAuthorization) })
            if (services.Count(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == contract) != 1)
                throw new InvalidOperationException("Runtime source closure requires one explicit source context and one source export authorization policy.");
    }
}
