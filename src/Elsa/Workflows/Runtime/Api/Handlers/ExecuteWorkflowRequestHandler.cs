using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Workflows.Runtime.Api.Handlers;

public sealed class ExecuteWorkflowRequestHandler(
    IWorkflowExecutionStartDispatcher startDispatcher,
    IWorkflowExecutableStore executableStore)
    : IRequestHandler<ExecuteWorkflow, WorkflowExecutionStartDispatchView>
{
    private const string RequestedBy = "runtime-api";
    private static readonly RuntimeVariableScopeFactory ScopeFactory = new();

    public async Task<WorkflowExecutionStartDispatchView> Handle(ExecuteWorkflow request, CancellationToken cancellationToken)
    {
        // Seed authored workflow variable defaults so `variables.*` input expressions resolve to their
        // declared values in production (Seam C, #254). The defaults are read from the compiled executable's
        // root structure, not the design document, keeping authored content separate from the runtime
        // artifact (§E2.9). A missing executable is left to the dispatcher to reject with its own diagnostics.
        var executable = await executableStore.FindAsync(request.ArtifactId, cancellationToken);
        var variables = executable is null
            ? EmptyVariables
            : ScopeFactory.ProjectDeclaredVariableDefaultsByName(executable.RootActivity);

        var result = await startDispatcher.DispatchAsync(
            new WorkflowExecutionStartDispatchRequest(request.ArtifactId, RequestedBy, variables: variables),
            cancellationToken);
        return WorkflowExecutionStartDispatchView.From(result);
    }

    private static readonly IReadOnlyDictionary<string, object?> EmptyVariables = new Dictionary<string, object?>(StringComparer.Ordinal);
}
