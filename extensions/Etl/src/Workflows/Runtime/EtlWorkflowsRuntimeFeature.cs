using CShells.Features;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Workflows.Runtime.Services;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Etl.Workflows.Runtime;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("ETL")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(name: "EtlWorkflowsRuntime", DisplayName = "ETL Native Workflows", Description = "Typed ETL waits and durable admission/cancellation through native workflow checkpoints.",
    DependsOn = new object[] { "EtlRuntime", "WorkflowsRuntimeResumption" })]
public class EtlWorkflowsRuntimeFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRuntimeCheckpointCommitEnricher, EtlCheckpointEnricher>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IActivityStateViewProjector, RunEtlStateViewProjector>());
        services.TryAddScoped<IEtlResultDelivery, EtlNativeResultDelivery>();
        services.TryAddScoped<EtlNativeResultExecutor>();
        var retry = RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1));
        services.AddRuntimePostCommitIntentHandler<EtlIntentHandler>(EtlNativeBoundary.StartKind, retry);
        services.AddRuntimePostCommitIntentHandler<EtlIntentHandler>(EtlNativeBoundary.CancelKind, retry);
    }
}
