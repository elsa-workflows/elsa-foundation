using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Core.Contracts;

/// <summary>
/// Checks the step writes of an activity tree (spec 1231 FR-024, FR-026, FR-028, FR-029) for a body that is not a workflow
/// draft: a reusable (graph) activity's body (spec 1241 FR-009). It finds what workflow draft validation finds: variable
/// references that are unbound, undeclared, not a variable or of another type; a step write that overlaps the node's own
/// capture; a script body's literal write to a name it cannot write, and its write by a computed name. It also returns
/// the step writers the
/// concurrent-write rule counts (<see cref="IUnorderedVariableWriteAnalyzer"/>).
/// </summary>
/// <remarks>
/// A single-implementation replacement contract, registered scoped by the workflow design validations feature (it reads the
/// scoped activity catalog). A consumer that cannot resolve it keeps counting captures only.
/// </remarks>
public interface IStepVariableWriteAnalyzer
{
    /// <summary>
    /// Analyses the tree under <paramref name="root"/>, whose root scope declares <paramref name="rootVariables"/> (a graph
    /// body's own variables, addressed as the <c>workflow</c> scope inside the body). A real host always composes exactly
    /// one; a consumer built without one checks no step writes.
    /// </summary>
    ValueTask<StepVariableWriteAnalysis> AnalyzeAsync(
        ActivityNode root,
        IReadOnlyCollection<VariableDefinition> rootVariables,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The result of <see cref="IStepVariableWriteAnalyzer.AnalyzeAsync"/>: the step writers by node, and the findings, each
/// with the rule type workflow draft validation reports it under. The step-write findings use
/// <see cref="StepVariableWriteRules"/>; the default analyzer also returns the workflow-state findings
/// (<c>VF-STATE-001</c>, <c>VF-STATE-002</c>) workflow validation makes on the same walk. A consumer may leave those to
/// its own workflow-state checks: a graph body's are refused when a workflow places the body.
/// </summary>
public sealed record StepVariableWriteAnalysis(StepVariableWriteFacts Facts, IReadOnlyList<ValidationError> Findings)
{
    public static StepVariableWriteAnalysis None { get; } = new(StepVariableWriteFacts.None, []);
}

/// <summary>The rule types of <see cref="StepVariableWriteAnalysis.Findings"/>.</summary>
public static class StepVariableWriteRules
{
    /// <summary>A variable-reference input that is unbound, undeclared, not a variable, or of another type. An error.</summary>
    public const string VariableReference = "ValueFlow/VariableReference";

    /// <summary>A node's step may write a variable its own output capture also writes. A warning.</summary>
    public const string StepWriteOverlapsCapture = "ValueFlow/StepWriteOverlapsCapture";

    /// <summary>A script body's literal write to an undeclared name or a loop item or index. An error.</summary>
    public const string RefusedScriptWrite = "Scripts/RefusedVariableWrite";

    /// <summary>A script body that writes by a computed name, so it may write every visible variable. A warning.</summary>
    public const string ComputedScriptWrite = "Scripts/ComputedVariableWrite";
}
