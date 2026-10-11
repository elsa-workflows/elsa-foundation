using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Contracts;

public interface IWorkflowExecutableCompiler
{
    ValueTask<WorkflowExecutable> CompileAsync(
        WorkflowExecutableCompileRequest request,
        CancellationToken cancellationToken = default);
}
