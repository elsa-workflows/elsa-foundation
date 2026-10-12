namespace Elsa.Workflows.Runtime.Core.Models;

public sealed record RuntimePipelineSlotDefinition(
    string Name,
    int SortOrder);

public sealed record RuntimePipelineMiddlewareRegistration(
    RuntimePipelineKind PipelineKind,
    Type MiddlewareType,
    string Name,
    string SlotName,
    int Order,
    int RegistrationIndex,
    bool IsBuiltIn);

public sealed record RuntimePipelinePlan(
    RuntimePipelineKind PipelineKind,
    IReadOnlyList<RuntimePipelinePlanStep> Steps);

public sealed record RuntimePipelinePlanStep(
    RuntimePipelineKind PipelineKind,
    Type MiddlewareType,
    string Name,
    RuntimePipelineSlotDefinition Slot,
    int Order,
    int RegistrationIndex,
    bool IsBuiltIn);

public enum RuntimePipelineKind
{
    Workflow,
    Activity
}
