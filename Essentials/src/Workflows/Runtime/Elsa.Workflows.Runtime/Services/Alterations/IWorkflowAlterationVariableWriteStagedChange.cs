using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>
/// A staged alteration change that wrote workflow variables (spec 1221 FR-024): its write records join the outcome of
/// the envelope at <see cref="Ordinal"/>, and the committer numbers them with the alteration's checkpoint.
/// </summary>
internal interface IWorkflowAlterationVariableWriteStagedChange
{
    int Ordinal { get; }
    IReadOnlyList<VariableWriteRecord> VariableWrites { get; }
}
