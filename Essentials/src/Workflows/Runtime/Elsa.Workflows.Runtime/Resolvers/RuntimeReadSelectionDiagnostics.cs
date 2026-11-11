using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Resolvers;

/// <summary>Refusals for member reads, which this runtime does not interpret yet (#4078).</summary>
internal static class RuntimeReadSelectionDiagnostics
{
    public static NotSupportedException Unsupported(RuntimeInputBinding binding) => new(
        $"Input '{binding.InputName}' reads a member of its source ({binding.Source}), and this runtime does not support " +
        $"member selection: it does not provide '{RuntimeReadSelection.RequirementKey}@1'. The input is refused rather than read " +
        "as the whole source value.");
}
