using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;

namespace Elsa.Workflows.Design.Core.Models;

/// <summary>
/// The canonical authored document of a workflow definition. Persisted as the
/// <c>StateSource</c> shadow JSON on <c>WorkflowDefinitionVersion</c> (immutable) and
/// <c>WorkflowDefinitionDraft</c> (mutable) inside the <c>Elsa.Workflows.Design</c>
/// sub-domain.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope (Elsa constitution §E2.9).</b> State carries <b>authored content only</b>:
/// the structured representation of what the author drew, declared, and configured.
/// Today's members — <c>Variables</c>, <c>RootActivity</c>, <c>Inputs</c>,
/// <c>Outputs</c>, <c>WorkflowActivityOptions</c>, <c>StrategyOptions</c> — are clean
/// against the policy. The root activity owns any composition details such as Flowchart
/// connections or Sequence children.
/// </para>
/// <para>
/// <b>Out of State (§E2.9.2 — never add to this record):</b>
/// instance/runtime/operational state · executable/build metadata · publication/deployment
/// state · search/listing-projection types · security/ownership types · designer layout
/// metadata (owned by <c>WorkflowDefinitionVersionLayout</c> / <c>WorkflowDefinitionDraftLayout</c>
/// siblings via <c>IWorkflowDefinitionLayout</c>) · validation errors (owned by the
/// <c>WorkflowDefinitionDraftValidation</c> sibling).
/// </para>
/// <para>
/// <b>Architectural triplet (§E2.9.3).</b> <c>WorkflowDefinitionState</c> ↔ read
/// models/projections ↔ <c>WorkflowExecutable</c> — authoring, reading, executing — at
/// separate scopes; never merged. Conflation collapses §E2.2 (Design ↔ Runtime split)
/// or §E2.6 (artifact-only runtime).
/// </para>
/// <para>
/// <b>Enforcement (§E2.9.4).</b> This documentation header + PR review discipline against
/// §E2.9. A property newly proposed for State whose category is genuinely ambiguous between
/// authored content and an out-of-State category surfaces as an architecture-meeting
/// escalation; resolution is constitutional (amend §E2.9), not silent. Automated
/// compile-/build-time enforcement is deferred to a future <i>Code Analysers</i> epic.
/// </para>
/// </remarks>
public sealed record WorkflowDefinitionState(
    IEnumerable<VariableDefinition> Variables,
    ActivityNode? RootActivity,
    IEnumerable<InputDefinition> Inputs,
    IEnumerable<OutputDefinition> Outputs,
    WorkflowActivityOptions? WorkflowActivityOptions,
    WorkflowStrategyOptions? StrategyOptions
)
{
    /// <summary>
    /// The state of a version/draft whose <c>StateSource</c> is missing or failed to deserialize.
    /// </summary>
    public static readonly WorkflowDefinitionState Empty = new([], null, [], [], null, null);
}
