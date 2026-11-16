using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
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

    /// <summary>
    /// Compiles a native intrinsic's inputs as workflow publication does: each input keeps its declared policy, the
    /// argument envelope's own storage and sensitivity hints do not apply, and conversion intent is forwarded only for
    /// whole result, variable and request reads.
    /// </summary>
    IReadOnlyDictionary<string, RuntimeInputBinding> CompileGraphIntrinsicInputs(
        string nodeId,
        IEnumerable<InputDefinition> inputDefinitions,
        IEnumerable<ArgumentState> inputStates);

    /// <summary>
    /// Uses the canonical variable-frame compiler for a native container inside a reusable body. Returns the declarations
    /// in Publishing's own executable-structure payload form, so a provider stores them without choosing a serializer.
    /// </summary>
    JsonElement CompileGraphVariables(
        string nodeId,
        IEnumerable<VariableDefinition> variables);

    /// <summary>Pins the normal assignment conversion using the actual declared source type.</summary>
    RuntimeInputBinding PinAssignmentSourceConversion(string nodeId, RuntimeInputBinding binding, TypeReference? sourceType);
}
