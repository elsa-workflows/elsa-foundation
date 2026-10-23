import type {
  ActivityAuthoringIntrinsic,
  ActivityCatalogItem,
  ActivityNode,
  AuthoredWorkflowIntrinsic,
  VariableReference
} from "./workflowTypes";

// Workflow-scope sentinel (Elsa.Expressions.Core.Models.VariableReference.WorkflowScopeId). Inlined rather
// than imported from ./scopedVariables to keep this module free of runtime imports — ./workflowAdapter
// imports it, and ./scopedVariables imports ./workflowAdapter, so an import here would form an eval cycle.
const WORKFLOW_SCOPE_ID = "workflow";

/**
 * Engine-intrinsic authoring (Set Variable / Set Output — foundation #929, backend PR #942).
 *
 * The runtime assigns workflow variables and outputs through engine intrinsics, never CLR activities
 * (ADR 0045). The authoring catalog therefore surfaces "Set Variable" and "Set Output" as built-in
 * descriptors carrying an additive {@link ActivityAuthoringIntrinsic} block. When such a descriptor is
 * placed, the client must materialize an intrinsic {@link ActivityNode} — one carrying an
 * {@link AuthoredWorkflowIntrinsic} rather than referencing an activity catalog version — mirroring the
 * node shape the code-first `WorkflowBuilder.Set`/`SetOutput` emit and `ExecutableNodeCompiler` compiles.
 *
 * Wire shape emitted (Set Variable):
 *   { nodeId, activityVersionId: "elsa.intrinsic.set@1", inputs: [{ referenceKey: "value", value }],
 *     outputs: [], intrinsic: { kind: "Set", valueType: { alias: "Elsa.Any", collectionKind: "Single" },
 *     variable: { referenceKey, declaringScopeId } } }
 *
 * Wire shape emitted (Set Output):
 *   { nodeId, activityVersionId: "elsa.intrinsic.set-output@1",
 *     inputs: [{ referenceKey: "name", value }, { referenceKey: "value", value }], outputs: [],
 *     intrinsic: { kind: "SetOutput", valueType: { alias: "Elsa.Any", collectionKind: "Single" } } }
 *
 * The variable/value/name inputs ride the standard `inputs` folding in {@link ./activityInputWire}; the
 * `intrinsic` block round-trips verbatim as a non-input node member.
 */

// The portable value type pinned on every authored intrinsic. "Elsa.Any" is the runtime's canonical
// dynamic-value contract (WorkflowIntrinsicExecutor treats it as convert-to-target), so the value binds
// losslessly whatever the declared target is. The authoring surface never asks the user for a value type,
// so a single stable dynamic type is the faithful choice — it matches the descriptor's value input type.
export const INTRINSIC_ANY_VALUE_TYPE = { alias: "Elsa.Any", collectionKind: "Single" } as const;

// Reads the additive intrinsic descriptor block from a catalog item, tolerating older backends that omit
// it (returns null). A well-formed block requires a `kind` and a `valueInputKey`.
export function readIntrinsicDescriptor(item: ActivityCatalogItem | null | undefined): ActivityAuthoringIntrinsic | null {
  const intrinsic = item?.intrinsic;
  if (!intrinsic || typeof intrinsic !== "object") return null;
  if (typeof intrinsic.kind !== "string" || !intrinsic.kind.trim()) return null;
  if (typeof intrinsic.valueInputKey !== "string" || !intrinsic.valueInputKey.trim()) return null;
  return intrinsic;
}

export function isIntrinsicCatalogItem(item: ActivityCatalogItem | null | undefined): boolean {
  return readIntrinsicDescriptor(item) !== null;
}

// True once a placed node carries an authored intrinsic block — independent of catalog availability, so a
// loaded intrinsic node is still recognized when its catalog descriptor is missing.
export function readNodeIntrinsic(node: ActivityNode | null | undefined): AuthoredWorkflowIntrinsic | null {
  const intrinsic = node?.intrinsic;
  return intrinsic && typeof intrinsic === "object" && typeof intrinsic.kind === "string" ? intrinsic : null;
}

// Builds the seed intrinsic block for a freshly placed node. Variable-writing kinds (those whose
// descriptor names a variable input) must carry a non-null variable target for the backend's
// AuthoredWorkflowIntrinsic constructor to accept the draft on save, so an unset target is seeded as an
// empty workflow-scoped reference the inspector then fills in.
export function buildIntrinsicWireBlock(descriptor: ActivityAuthoringIntrinsic): AuthoredWorkflowIntrinsic {
  const block: AuthoredWorkflowIntrinsic = {
    kind: descriptor.kind,
    valueType: { ...INTRINSIC_ANY_VALUE_TYPE }
  };
  if (descriptor.variableInputKey) {
    block.variable = { referenceKey: "", declaringScopeId: WORKFLOW_SCOPE_ID };
  }
  return block;
}

// Returns a copy of the node with its intrinsic variable target replaced (Set Variable authoring). No-op
// for a node without an intrinsic block.
export function writeIntrinsicVariable(node: ActivityNode, variable: VariableReference | null): ActivityNode {
  const intrinsic = readNodeIntrinsic(node);
  if (!intrinsic) return node;
  return {
    ...node,
    intrinsic: { ...intrinsic, variable: variable ?? undefined }
  };
}

// The variable target currently authored on an intrinsic node, or null when none is selected yet (a seed
// with an empty referenceKey reads as "unset").
export function readIntrinsicVariable(node: ActivityNode | null | undefined): VariableReference | null {
  const variable = readNodeIntrinsic(node)?.variable;
  return variable && typeof variable.referenceKey === "string" && variable.referenceKey.trim() ? variable : null;
}
