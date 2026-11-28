using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.KernelApi;

/// <summary>Access to the old Runtime readers for the mixed kernel preview.</summary>
internal sealed class KernelPreviewRunReads(IServiceProvider services)
{
    internal IWorkflowExecutionStateStore? Runs => services.GetService<IWorkflowExecutionStateStore>();
    internal ActivityExecutionBrowseSource? ActivityBrowse => services.GetService<ActivityExecutionBrowseSource>();
    internal ActivityExecutionInspectionService? ActivityInspection => services.GetService<ActivityExecutionInspectionService>();
    internal WorkflowInstanceDetailsService? Details => services.GetService<WorkflowInstanceDetailsService>();
    internal RuntimeHistoryInspectionService? History => services.GetService<RuntimeHistoryInspectionService>();
    internal LegacyWorkflowExecutableRetentionReader? ExecutableRetention => services.GetService<LegacyWorkflowExecutableRetentionReader>();
    internal WorkflowInstanceDeletionService? Deletion => services.GetService<WorkflowInstanceDeletionService>();
}
