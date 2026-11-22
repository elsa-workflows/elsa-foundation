using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Resolvers;

/// <summary>Refusal for a member read handed to the binding resolver (#4078).</summary>
internal static class RuntimeReadSelectionDiagnostics
{
    // The input materializer and the intrinsic executor select members themselves, after resolving the whole source
    // through a whole-value binding of the same role; the (replaceable) resolver contract is whole values only.
    public static NotSupportedException Unsupported(RuntimeInputBinding binding) => new(
        $"Input '{binding.InputName}' reads a member of its source ({binding.Source}). The binding resolver resolves whole " +
        "values only; a member read is selected by the input materializer after it resolves the whole source, and is refused " +
        "here rather than read as the whole source value.");
}
