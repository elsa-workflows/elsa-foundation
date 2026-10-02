using CShells.Features;
using Elsa.Secrets.Workflows.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Secrets.Workflows.Features;

/// <summary>
/// Resolves workflow activity inputs bound to a secret reference through the Secrets module, each time the activity is
/// activated, for the execution partition read at activation (on the background drain path, the host's persistence
/// scope; see spec 188 research R2).
/// </summary>
/// <remarks>
/// Depends on <c>Secrets</c>, whose <c>ISecretValueResolver</c> the resolver reads through, and on
/// <c>ActivitiesRuntime</c>, whose activator is the only caller of the runtime's secret resolver. A shell without this
/// feature cannot resolve secrets at all: a secret-bound activity then waits with an activation-failure incident until
/// the feature is composed, instead of faulting. The bridge has no settings: the tenant comes from the execution, never
/// from configuration.
/// </remarks>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Secrets")]
[ManifestFeatureCategory("Workflows")]
[ShellFeature(
    name: "SecretsWorkflows",
    DisplayName = "Secrets Workflow Resolution",
    Description = "Resolves workflow activity inputs bound to a secret reference through the Secrets module when the activity runs.",
    DependsOn = new object[] { "Secrets", "ActivitiesRuntime" }
)]
public class SecretsWorkflowsFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services) => services.AddSecretsWorkflows();
}
