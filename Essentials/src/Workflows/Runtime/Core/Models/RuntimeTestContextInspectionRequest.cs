using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Data-only setup inspection input. The execution identity namespaces returned frame data only;
/// none of these fields can admit execution or substitute for a trusted entry/context resolver.
/// </summary>
public sealed record RuntimeTestContextInspectionRequest(
    WorkflowExecutable Executable,
    string ExecutableNodeId,
    string WorkflowExecutionId,
    RuntimeTestContextSeed Seed,
    IReadOnlyDictionary<string, JsonElement> WorkflowInputs,
    IReadOnlyDictionary<string, JsonElement> RootVariableSeeds);
