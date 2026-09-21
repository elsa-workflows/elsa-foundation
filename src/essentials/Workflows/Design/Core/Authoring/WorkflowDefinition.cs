using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Authoring;

/// <summary>
/// IDE-guided code-first compiler entry point. Instances produce authored state only; they are not
/// workflow instances and are never retained by Runtime.
/// </summary>
public abstract class WorkflowDefinition<TRequest, TResult>
{
    private readonly object _sync = new();
    private readonly Dictionary<string, WorkflowDefinitionState> _compiledVersions = new(StringComparer.Ordinal);

    protected abstract void Build(IWorkflowBuilder<TRequest, TResult> workflow);

    public WorkflowDefinitionState Compile(string definitionVersionId = "default")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionVersionId);
        lock (_sync)
        {
            if (_compiledVersions.TryGetValue(definitionVersionId, out var state))
                return state;

            var builder = new WorkflowBuilder<TRequest, TResult>();
            Build(builder);
            state = builder.BuildState();
            _compiledVersions.Add(definitionVersionId, state);
            return state;
        }
    }
}
