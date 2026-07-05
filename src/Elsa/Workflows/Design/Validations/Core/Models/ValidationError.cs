namespace Elsa.Workflows.Design.Validations.Core.Models;

/// <summary>
/// Validation error contributed by a validator handling <c>OnDraftValidating</c>. Carried inside
/// the <c>WorkflowDefinitionDraftValidation</c> sibling's <c>Errors</c> list. Grouping key is
/// the (<see cref="Path"/>, <see cref="Type"/>) tuple — multiple errors may share the same
/// scope (e.g. two missing required inputs on the same activity). Per Unit C FR-022 + R2 + R3.
/// </summary>
/// <remarks>
/// <para>
/// <b>Path format (R2):</b> slash-delimited path with optional input/output reference suffix.
/// Examples:
/// <list type="bullet">
/// <item><c>{NodeId}</c> — workflow-level concern bound to a node.</item>
/// <item><c>{NodeId}/inputs/{InputReferenceKey}</c> — activity input.</item>
/// <item><c>{NodeId}/outputs/{OutputReferenceKey}</c> — activity output.</item>
/// <item><c>$workflow/inputs/{InputReferenceKey}</c> — workflow-level input declaration.</item>
/// <item><c>$workflow/outputs/{OutputReferenceKey}</c> — workflow-level output declaration.</item>
/// <item><c>$workflow/variables/{VariableName}</c> — workflow variable.</item>
/// <item><c>$workflow</c> — workflow-scope concern (e.g. missing root activity).</item>
/// </list>
/// </para>
/// <para>
/// <b>Type format (R3):</b> slash-delimited category. Reserved baseline categories (as shipped,
/// incl. the FR-033 2026-07-05 amendment): <c>RootActivity/Missing</c>,
/// <c>Variables/Uniqueness</c>, <c>InputOutput/MissingRequired</c>,
/// <c>Expressions/UnresolvedVariable</c>, <c>Graph/UnknownActivityVersion</c>. External
/// validators extend by prefixing with their domain (e.g. <c>Http/AuthPolicyUnknown</c>).
/// </para>
/// </remarks>
public sealed record ValidationError(string Path, string Type, string Message);
