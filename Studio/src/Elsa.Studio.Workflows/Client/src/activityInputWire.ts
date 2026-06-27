import type { ActivityNode, WorkflowDefinitionState } from "./workflowTypes";

/**
 * Bridges the Studio's in-memory activity model and the backend's canonical wire contract.
 *
 * The editors keep input values as top-level camelCase properties on the activity node
 * (e.g. `activity.text = { typeName, expression: { type, value } }`). The backend
 * `ActivityNode` model has no place for those — it carries authored values in an `inputs`
 * array of `ArgumentState { referenceKey, value: { value, expressionType } }`. Anything else
 * is silently dropped on deserialization, which is what made WriteLine print blank lines.
 *
 * {@link canonicalizeStateForWire} folds the top-level wrapped properties into `inputs` before
 * we send state to the backend; {@link expandStateFromWire} reverses it on load so the existing
 * descriptor-aware editors keep working unchanged.
 */

const structuralKeys = new Set(["nodeId", "activityVersionId", "inputs", "outputs", "structure"]);

interface WrappedInputValue {
  typeName?: string;
  expression: { type: string; value: unknown };
}

interface WireArgumentState {
  referenceKey: string;
  value: { value: string | null; expressionType: string };
}

export function canonicalizeStateForWire(state: WorkflowDefinitionState): WorkflowDefinitionState {
  return mapStateActivities(state, canonicalizeActivityNode);
}

export function expandStateFromWire(state: WorkflowDefinitionState): WorkflowDefinitionState {
  return mapStateActivities(state, expandActivityNode);
}

function mapStateActivities(
  state: WorkflowDefinitionState,
  transform: (node: ActivityNode) => ActivityNode
): WorkflowDefinitionState {
  if (!state || !state.rootActivity) return state;
  return { ...state, rootActivity: mapActivityTree(state.rootActivity, transform) };
}

function mapActivityTree(node: ActivityNode, transform: (node: ActivityNode) => ActivityNode): ActivityNode {
  const transformed = transform(node);
  const structure = transformed.structure;
  if (!structure || !isRecord(structure.payload)) return transformed;

  let changed = false;
  const nextPayload: Record<string, unknown> = { ...structure.payload };
  for (const [key, value] of Object.entries(structure.payload)) {
    if (Array.isArray(value) && value.length > 0 && value.every(isActivityNode)) {
      nextPayload[key] = value.map(child => mapActivityTree(child, transform));
      changed = true;
    }
  }

  return changed ? { ...transformed, structure: { ...structure, payload: nextPayload } } : transformed;
}

function canonicalizeActivityNode(node: ActivityNode): ActivityNode {
  const collected: WireArgumentState[] = [];
  const extras: Record<string, unknown> = {};

  for (const [key, value] of Object.entries(node)) {
    if (structuralKeys.has(key)) continue;
    if (isWrappedInputValue(value)) {
      collected.push({
        referenceKey: pascalize(key),
        value: { value: toWireValue(value.expression.value), expressionType: value.expression.type || "Literal" }
      });
    } else {
      extras[key] = value;
    }
  }

  const existingInputs = Array.isArray(node.inputs) ? node.inputs : [];
  return {
    ...extras,
    nodeId: node.nodeId,
    activityVersionId: node.activityVersionId,
    inputs: [...existingInputs, ...collected],
    outputs: Array.isArray(node.outputs) ? node.outputs : [],
    structure: node.structure
  };
}

function expandActivityNode(node: ActivityNode): ActivityNode {
  const inputs = Array.isArray(node.inputs) ? node.inputs : [];
  const expanded: Record<string, unknown> = {};

  for (const argument of inputs) {
    if (!isRecord(argument) || typeof argument.referenceKey !== "string") continue;
    const argumentValue = isRecord(argument.value) ? argument.value : {};
    expanded[camelize(argument.referenceKey)] = {
      typeName: "",
      expression: {
        type: typeof argumentValue.expressionType === "string" ? argumentValue.expressionType : "Literal",
        value: argumentValue.value ?? ""
      }
    } satisfies WrappedInputValue;
  }

  return { ...node, ...expanded, inputs: [] };
}

// Reverses camelize ("text" -> "Text", "uRL" -> "URL"). The backend ReferenceKey is PascalCase.
function pascalize(value: string): string {
  return value ? value.charAt(0).toUpperCase() + value.slice(1) : value;
}

function camelize(value: string): string {
  return value ? value.charAt(0).toLowerCase() + value.slice(1) : value;
}

// ArgumentValue.Value is a string on the backend, so non-string literals must be serialized.
function toWireValue(value: unknown): string | null {
  if (value == null) return null;
  if (typeof value === "string") return value;
  if (typeof value === "number" || typeof value === "boolean") return String(value);
  return JSON.stringify(value);
}

function isWrappedInputValue(value: unknown): value is WrappedInputValue {
  if (!isRecord(value) || Array.isArray(value)) return false;
  const expression = value.expression;
  return isRecord(expression) && typeof expression.type === "string";
}

function isActivityNode(value: unknown): value is ActivityNode {
  return isRecord(value) &&
    typeof (value as ActivityNode).nodeId === "string" &&
    typeof (value as ActivityNode).activityVersionId === "string";
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}
