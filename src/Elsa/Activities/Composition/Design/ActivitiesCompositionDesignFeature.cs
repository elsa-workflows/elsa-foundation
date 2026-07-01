using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Activities.Composition.Design.Reconciliation;
using Elsa.Activities.Design.Reconciliation.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Composition.Design;

/// <summary>
/// Design-side of the Workflow activity kind: contributes the
/// <see cref="WorkflowActivityReconciliationSource"/> that turns workflow definition versions marked
/// usable-as-activity into activity catalog rows carrying the <c>WorkflowIdentity</c> descriptor. This is
/// a standalone source feature (§2.6.1) — it does not derive from the reconciliation feature; it only
/// registers an <see cref="IActivityReconciliationSource"/> that the reconciliation feature's universal
/// handler discovers from DI. Pair it with <c>ActivitiesCompositionRuntime</c> for construction and with
/// a reconciliation feature to persist the rows.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Design")]
[ManifestFeatureCategory("Composition")]
[ShellFeature(
    name: "ActivitiesCompositionDesign",
    DisplayName = "Activities Composition (Design)",
    Description = "Reconciliation source that catalogs workflow definitions marked usable-as-activity as activities (the Workflow kind)."
)]
public class ActivitiesCompositionDesignFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        // The adapter is the only piece that reaches into Workflows Design; the reconciliation source
        // depends only on the port, so it stays a pure mapper (§2.7).
        services.AddScoped<IUsableAsActivityWorkflowSource, WorkflowDefinitionUsableAsActivitySource>();
        services.AddScoped<IActivityReconciliationSource, WorkflowActivityReconciliationSource>();
    }
}
