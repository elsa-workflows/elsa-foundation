using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Api.Services;

internal sealed class LegacyRuntimePreviewReaders(IServiceProvider services) : ILegacyRuntimePreviewReaders
{
    public ILegacyRunDetailsReader? Details => services.GetService<WorkflowInstanceDetailsService>();
    public ILegacyRuntimeHistoryReader? History => services.GetService<RuntimeHistoryInspectionService>();
    public IWorkflowInstanceDeletionService? Deletion => services.GetService<WorkflowInstanceDeletionService>();
}
