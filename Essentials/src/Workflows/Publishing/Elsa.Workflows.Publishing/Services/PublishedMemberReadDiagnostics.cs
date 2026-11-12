using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Refusals for a compiled member read reaching a publication stage that does not handle one yet (#4078). Authoring
/// admission refuses member kinds before compilation; these stop a hand-built binding from passing through a stage
/// unchanged as if it read the whole source.
/// </summary>
internal static class PublishedMemberReadDiagnostics
{
    public static NotSupportedException Unsupported(string? nodeId, RuntimeInputBinding binding) => new(
        $"Input '{binding.InputName}'{(nodeId is null ? string.Empty : $" on node '{nodeId}'")} reads a member of its source " +
        $"({binding.Source}); publication does not compile member reads yet.");
}
