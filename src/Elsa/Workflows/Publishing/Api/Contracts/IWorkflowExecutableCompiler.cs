using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Contracts;

public interface IWorkflowExecutableCompiler
{
    ValueTask<WorkflowExecutable> CompileAsync(
        WorkflowExecutableCompileRequest request,
        CancellationToken cancellationToken = default);
}
