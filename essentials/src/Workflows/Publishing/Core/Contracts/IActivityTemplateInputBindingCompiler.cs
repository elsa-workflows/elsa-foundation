using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Contracts;

/// <summary>Compiles the authored inputs of an engine-owned node inside an activity template.</summary>
public interface IActivityTemplateInputBindingCompiler
{
    IReadOnlyDictionary<string, RuntimeInputBinding> CompileGraphInputs(
        string nodeId,
        IEnumerable<InputDefinition> inputDefinitions,
        IEnumerable<ArgumentState> inputStates);

    /// <summary>Uses the canonical variable-frame compiler for a native container inside a reusable body.</summary>
    IReadOnlyCollection<RuntimeVariableDeclaration> CompileGraphVariables(
        string nodeId,
        IEnumerable<VariableDefinition> variables);
}
