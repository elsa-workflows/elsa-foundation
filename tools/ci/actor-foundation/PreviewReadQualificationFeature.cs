using CShells.Features;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Inspection.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workbench.Qualification;

[ShellFeature(name: "PreviewReadQualification", DependsOn = new object[] { "WorkflowsRuntimeApi" })]
public sealed class PreviewReadQualificationFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IActivityInspectionContextAsync>();
        services.AddScoped<IActivityInspectionContextAsync, RestrictedInspectionContext>();
        services.EnsureReplacementContract<IActivityInspectionContextAsync, RestrictedInspectionContext>();
    }

    private sealed class RestrictedInspectionContext(HttpContextActivityExecutionInspectionAuthorizationContext inner) : IActivityInspectionContextAsync
    {
        public string TenantScope => inner.TenantScope;
        public string AuditSubject => inner.AuditSubject;
        public string RequestCorrelationId => inner.RequestCorrelationId;
        public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default) =>
            inner.GetAuthorizationProfileAsync(cancellationToken);
        public ValueTask<bool> CanInspectStructureAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);
        public ValueTask<bool> CanInspectSensitiveValuesAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);
        public ValueTask<bool> CanResolveSensitiveValuePayloadsAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);
    }
}
