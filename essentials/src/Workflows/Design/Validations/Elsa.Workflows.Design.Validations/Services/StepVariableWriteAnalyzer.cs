using Elsa.Activities.Design.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;

namespace Elsa.Workflows.Design.Validations.Services;

/// <summary>
/// The default <see cref="IStepVariableWriteAnalyzer"/> (spec 1241 FR-009): the same checks workflow draft validation makes
/// (<see cref="StepVariableWriteValidation"/>), plus a script body's refused literal writes, which a workflow reports
/// through its script read validator instead.
/// </summary>
internal sealed class StepVariableWriteAnalyzer(
    IActivityStructureService structures,
    IActivityDefinitionLookup catalog,
    ScriptVariableReadResolver? scriptReads = null) : IStepVariableWriteAnalyzer
{
    public async ValueTask<StepVariableWriteAnalysis> AnalyzeAsync(
        ActivityNode root,
        IReadOnlyCollection<VariableDefinition> rootVariables,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(rootVariables);
        var (facts, errors) = await StepVariableWriteValidation.AnalyzeAsync(
            root, rootVariables, structures, catalog, cancellationToken, scriptReads, reportRefusedScriptWrites: true);
        return new StepVariableWriteAnalysis(facts, errors);
    }
}
