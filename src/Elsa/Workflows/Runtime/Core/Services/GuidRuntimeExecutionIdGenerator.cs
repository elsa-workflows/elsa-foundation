using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class GuidRuntimeExecutionIdGenerator : IRuntimeExecutionIdGenerator
{
    public string NewWorkflowExecutionId() => $"wfexec-{Guid.NewGuid():N}";

    public string NewWorkflowExecutionCommandId() => $"wfecmd-{Guid.NewGuid():N}";

    public string NewWorkflowExecutionCommandEnvelopeId() => $"wfeenv-{Guid.NewGuid():N}";

    public string NewActivityExecutionId() => $"actexec-{Guid.NewGuid():N}";
}
