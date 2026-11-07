using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Testing.Core.Contracts;
using Elsa.Workflows.Testing.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Testing;

/// <summary>Composes deterministic assertion/report services; execution profiles are admitted separately.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ShellFeature(name: "WorkflowsTesting", DisplayName = "Workflow Tests", Description = "Shared workflow and activity regression assertions and reports.")]
public class WorkflowsTestingFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.TryAddScoped<IWorkflowTestAssertionEvaluator, WorkflowTestAssertionEvaluator>();
        services.TryAddScoped<IWorkflowTestReportPolicy, WorkflowTestReportPolicy>();
        services.TryAddScoped<IWorkflowTestReportWriter, WorkflowTestReportWriter>();
        WorkflowTestingCompositionValidator.Register(services);
    }
}
