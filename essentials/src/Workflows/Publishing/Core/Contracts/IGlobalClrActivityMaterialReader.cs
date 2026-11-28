using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Contracts;

/// <summary>Resolves exact Runtime material owned by a visible, reconciled global CLR activity.</summary>
public interface IGlobalClrActivityMaterialReader
{
    /// <summary>Reads the selected version and its canonical source reference without changing either partition.</summary>
    ValueTask<(ExecutableActivityTemplate Template, WorkflowExecutableSourceReference Reference)?> FindAsync(
        ActivityDefinitionVersionPublication publication, CancellationToken cancellationToken = default);
}
