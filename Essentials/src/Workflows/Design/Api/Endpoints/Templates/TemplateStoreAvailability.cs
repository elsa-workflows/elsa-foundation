using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Api.Endpoints.Templates;

internal static class TemplateStoreAvailability
{
    public static IWorkflowTemplateStore Require(IWorkflowTemplateStore? store) =>
        store ?? throw new WorkflowTemplatesUnavailableException();
}

internal sealed class WorkflowTemplatesUnavailableException() : Exception("Saved workflow templates are not supported by this server.");
