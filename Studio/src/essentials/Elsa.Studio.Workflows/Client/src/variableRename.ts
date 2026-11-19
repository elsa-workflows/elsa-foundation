import type { ActivityNode, VariableDefinition, WorkflowDefinitionState } from "./workflowTypes";
import { updateActivity, type ActivityCatalogLookup } from "./workflowAdapter";
import { readContainerVariables, writeContainerVariables } from "./scopedVariables";

/** The workflow-scope sentinel a variable rename plan is keyed by (Elsa.Expressions.Core.Models.VariableReference). */
export const workflowVariableScopeId = "workflow";

/** One script edit that keeps a read on the renamed variable (backend ScriptVariableRenameEdit, spec 1222 FR-021, FR-022). */
export interface VariableRenameEdit {
  nodeId: string;
  inputKey: string;
  oldSource: string;
  newSource: string;
}

/** A reference the rename cannot update safely, for manual repair (backend ScriptVariableRenameFlag, FR-022). */
export interface VariableRenameFlag {
  nodeId: string;
  inputKey: string;
  reason: string;
  message: string;
}

export interface VariableRenamePlan {
  oldName: string;
  newName: string;
  edits: VariableRenameEdit[];
  flags: VariableRenameFlag[];
}

/**
 * Returns the state with one variable's name set, leaving everything else as it is. The planner finds the variable by
 * scope and reference key and plans from its current name, so Studio asks it with the name as it was before the edit.
 */
export function withVariableName(
  state: WorkflowDefinitionState,
  scopeId: string,
  referenceKey: string,
  name: string,
  catalog?: ActivityCatalogLookup
): WorkflowDefinitionState {
  // The variable editors accept the legacy `ReferenceKey` spelling too (WorkflowPropertiesView referenceKeyKeys).
  const rename = (variables: VariableDefinition[]) =>
    variables.map(variable => ((variable.referenceKey ?? variable.ReferenceKey) === referenceKey ? { ...variable, name } : variable));
  if (scopeId === workflowVariableScopeId)
    return { ...state, variables: rename((state.variables ?? []) as VariableDefinition[]) };
  if (!state.rootActivity) return state;
  return {
    ...state,
    rootActivity: updateActivity(state.rootActivity, scopeId, node => writeContainerVariables(node, rename(readContainerVariables(node))), catalog)
  };
}

/**
 * Applies a rename plan's script edits to the draft. An edit applies only while its input still holds the source the
 * plan was made from; one whose script changed in the meantime is returned as skipped, for the author to repair.
 */
export function applyVariableRenameEdits(
  state: WorkflowDefinitionState,
  edits: VariableRenameEdit[],
  catalog?: ActivityCatalogLookup
): { state: WorkflowDefinitionState; applied: VariableRenameEdit[]; skipped: VariableRenameEdit[] } {
  const applied: VariableRenameEdit[] = [];
  const skipped: VariableRenameEdit[] = [];
  let root = state.rootActivity;
  for (const edit of edits) {
    let changed = false;
    if (root) {
      root = updateActivity(root, edit.nodeId, node => {
        const next = replaceInputSource(node, edit);
        changed = next !== node;
        return next;
      }, catalog);
    }
    (changed ? applied : skipped).push(edit);
  }
  return { state: applied.length > 0 ? { ...state, rootActivity: root } : state, applied, skipped };
}

// An authored input is held in memory as a top-level wrapped value (`node[key].expression.value`, see
// activityInputWire), which wins over a seeded wire entry in `node.inputs` under the same key.
function replaceInputSource(node: ActivityNode, edit: VariableRenameEdit): ActivityNode {
  const wrapped = node[edit.inputKey];
  if (isRecord(wrapped) && isRecord(wrapped.expression)) {
    const source = replacedSource(wrapped.expression.value, edit);
    return source === undefined ? node : { ...node, [edit.inputKey]: { ...wrapped, expression: { ...wrapped.expression, value: source } } };
  }
  let replaced = false;
  const inputs = node.inputs.map(input => {
    if (replaced || !isRecord(input) || input.referenceKey !== edit.inputKey || !isRecord(input.value)) return input;
    const source = replacedSource(input.value.value, edit);
    if (source === undefined) return input;
    replaced = true;
    return { ...input, value: { ...input.value, value: source } };
  });
  return replaced ? { ...node, inputs } : node;
}

/** The value with the plan's source replaced: a plain source string, or a definition object's `source`. */
function replacedSource(value: unknown, edit: VariableRenameEdit): unknown {
  if (value === edit.oldSource) return edit.newSource;
  if (isRecord(value) && value.source === edit.oldSource) return { ...value, source: edit.newSource };
  return undefined;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
