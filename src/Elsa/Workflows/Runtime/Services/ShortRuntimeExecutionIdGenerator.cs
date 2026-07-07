using Elsa.Primitives.Identity;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class ShortRuntimeExecutionIdGenerator(TimeProvider timeProvider) : IRuntimeExecutionIdGenerator
{
    public ShortRuntimeExecutionIdGenerator() : this(TimeProvider.System)
    {
    }

    public string NewWorkflowExecutionId() => NewId();

    public string NewWorkflowExecutionCommandId() => NewId();

    public string NewWorkflowExecutionCommandEnvelopeId() => NewId();

    public string NewActivityExecutionId() => NewId();

    private string NewId() => ShortIdentityGenerator.Generate(timeProvider.GetUtcNow());
}
