import { flowchartStructureKind, getChildSlots, readStructureDesignFacet, sequenceStructureKind, type ActivityCatalogLookup } from "./workflowAdapter";
import { isPlainRecord } from "./workflowProperties";
import type { ActivityCatalogItem, ActivityNode, ScopedVariableShadowingWarning, VariableDefinition, VariableReference, WorkflowDefinitionState } from "./workflowTypes";

/**
 * Scoped-variable helpers (ADR-0027). Container-scoped variable declarations live on a container
 * activity node's `structure.payload.variables` in the same {@link VariableDefinition} shape as
 * workflow-scoped declarations, so the same editor authors both and they round-trip through the
 * state wire layer unchanged. A scoped reference from an activity input pairs the variable's stable
 * `referenceKey` with the declaring scope id (a container node id, or the workflow-scope sentinel).
 */

// Workflow-scope sentinel (Elsa.Expressions.Core.Models.VariableReference.WorkflowScopeId).
export const WORKFLOW_SCOPE_ID = "workflow";

// Container kinds that own scoped variables, mirroring the backend's
// IActivityStructureService.SupportsScopedVariables (Sequence and Flowchart).
const scopedVariableContainerKinds = new Set([sequenceStructureKind, flowchartStructureKind]);

export function supportsScopedVariables(node: ActivityNode | null | undefined, catalogItem?: ActivityCatalogItem | null): boolean {
  const kind = node?.structure?.kind;
  if (!kind) return false;
  if (scopedVariableContainerKinds.has(kind)) return true;

  const structureFacet = catalogItem?.activityVersionId === node?.activityVersionId
    ? readStructureDesignFacet(catalogItem)
    : null;
  return structureFacet?.kind === kind && structureFacet.payload.supportsScopedVariables;
}

export function readContainerVariables(node: ActivityNode | null | undefined): VariableDefinition[] {
  const value = node?.structure?.payload?.variables;
  return Array.isArray(value) ? (value.filter(isPlainRecord) as VariableDefinition[]) : [];
}

// Returns a copy of the node with its container-scoped variables replaced. No-op for nodes without
// an owned structure (a non-container activity can't declare scoped variables).
export function writeContainerVariables(node: ActivityNode, variables: VariableDefinition[]): ActivityNode {
  if (!node.structure) return node;
  return {
    ...node,
    structure: {
      ...node.structure,
      payload: { ...node.structure.payload, variables }
    }
  };
}

export function isWorkflowScopeRef(reference: Pick<VariableReference, "declaringScopeId">): boolean {
  return !reference.declaringScopeId || reference.declaringScopeId === WORKFLOW_SCOPE_ID;
}

// Builds a structured reference; workflow-scoped references carry the explicit sentinel so the
// authored scope is always recorded (the backend treats an omitted scope as the workflow scope too).
export function makeVariableReference(referenceKey: string, declaringScopeId: string | null | undefined): VariableReference {
  return {
    referenceKey,
    declaringScopeId: declaringScopeId && declaringScopeId !== WORKFLOW_SCOPE_ID ? declaringScopeId : WORKFLOW_SCOPE_ID
  };
}

// Reads a possibly-stringified variable reference from an activity input value into a structured form.
// Tolerates the structured object, a JSON object string, and a bare reference-key string (legacy).
export function readVariableReference(value: unknown): VariableReference | null {
  if (value && typeof value === "object") {
    const record = value as Record<string, unknown>;
    return typeof record.referenceKey === "string" && record.referenceKey
      ? { referenceKey: record.referenceKey, declaringScopeId: typeof record.declaringScopeId === "string" ? record.declaringScopeId : null }
      : null;
  }
  if (typeof value === "string") {
    const trimmed = value.trim();
    if (trimmed.startsWith("{")) {
      try {
        return readVariableReference(JSON.parse(trimmed));
      } catch {
        // fall through to treating the string as a bare reference key
      }
    }
    return trimmed ? { referenceKey: trimmed, declaringScopeId: null } : null;
  }
  return null;
}

// A cheap signature of everything that affects scoped-variable visibility/shadowing: workflow-scoped
// declarations, each container's declarations, and the parent→child tree shape. Used to key the design
// analysis query so it re-runs on relevant edits but not on every unrelated keystroke.
export function scopedVariableSignature(state: WorkflowDefinitionState | null | undefined, catalog?: ActivityCatalogLookup): string {
  if (!state) return "";
  const parts: string[] = [`workflow:${declarationSignature(state.variables)}`];

  const visit = (node: ActivityNode) => {
    const slots = getChildSlots(node, catalog);
    const childIds = slots.flatMap(slot => slot.activities.map(activity => activity.nodeId));
    parts.push(`${node.nodeId}:${declarationSignature(readContainerVariables(node))}>${childIds.join(",")}`);
    slots.forEach(slot => slot.activities.forEach(visit));
  };
  if (state.rootActivity) visit(state.rootActivity);

  return parts.join(";");
}

function declarationSignature(variables: VariableDefinition[] | null | undefined): string {
  return (variables ?? []).map(variable => `${variable.referenceKey}=${variable.name}`).join(",");
}

// Builds a per-declaration advisory map (referenceKey → message) for the variables declared in
// `scopeId` that shadow a visible ancestor declaration. Shadowing is allowed; these are advisory only.
export function shadowingWarningMap(
  warnings: ScopedVariableShadowingWarning[] | null | undefined,
  scopeId: string
): Map<string, string> {
  const map = new Map<string, string>();
  for (const warning of warnings ?? []) {
    if (warning.scopeId === scopeId) {
      map.set(warning.referenceKey, `Shadows "${warning.name}" declared in an outer scope.`);
    }
  }
  return map;
}
