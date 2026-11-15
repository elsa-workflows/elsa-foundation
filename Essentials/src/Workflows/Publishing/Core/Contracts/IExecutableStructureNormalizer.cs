using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Contracts;

/// <summary>Normalizes an activity-owned semantic structure through the canonical variable compiler.</summary>
public interface IExecutableStructureNormalizer
{
    /// <exception cref="Elsa.Workflows.Publishing.Core.Models.WorkflowExecutableCompilationException">Malformed semantic JSON retains its parser cause and node identity.</exception>
    ExecutableActivityStructure? Normalize(string nodeId, ActivityNodeStructure? structure);
}
