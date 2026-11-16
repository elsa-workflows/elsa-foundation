using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Core.Contracts;

/// <summary>
/// Finds declared writes to one variable whose order the definition does not fix (spec 266). Workflow draft validation
/// and graph activity validation and compilation report its findings as warnings; nothing blocks on them.
/// </summary>
/// <remarks>
/// All ordering knowledge comes from the structures' own declarations
/// (<see cref="IActivityStructureHandler.RelateChildren"/> and <see cref="IActivityStructureHandler.CompletesAfterItsChildren"/>);
/// the analyzer knows no structure kind. A declared write is an output capture into a variable and, when
/// <paramref name="includeIntrinsics"/> is set on <see cref="Analyze"/>, a Set, Merge or Reduce intrinsic.
/// </remarks>
public interface IUnorderedVariableWriteAnalyzer
{
    /// <summary>Analyses the tree under <paramref name="root"/>, projecting children through <paramref name="structures"/>.</summary>
    UnorderedVariableWriteAnalysis Analyze(
        ActivityNode root,
        IActivityStructureService structures,
        bool includeIntrinsics,
        CancellationToken cancellationToken = default);
}
