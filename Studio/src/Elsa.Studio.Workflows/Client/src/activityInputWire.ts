import { generateId, readArgumentType, readStringField, referenceKeyKeys } from "./workflowProperties";
import type { ActivityNode, VariableDefinition, WorkflowDefinitionState } from "./workflowTypes";

/**
 * Bridges the Studio's in-memory model and the backend's canonical wire contract.
 *
 * Two concerns live here:
 *
 * 1. Activity input values. The editors keep them as top-level camelCase properties on the activity
 *    node (e.g. `activity.text = { typeName, expression: { type, value } }`). The backend `ActivityNode`
 *    model carries authored values in an `inputs` array of `ArgumentState { referenceKey, value }`;
 *    anything else is dropped on deserialization. {@link canonicalizeStateForWire} folds the top-level
 *    wrapped properties into `inputs`; {@link expandStateFromWire} reverses it on load.
 *
 * 2. Argument collections — the workflow-level `variables`/`inputs`/`outputs` and container-scoped
 *    variables. The typed-argument-model contract carries each argument's type as
 *    `type: { alias, collectionKind }` with a bare-alias `storageDriverType` (no `isArray`, no
 *    assembly metadata). Both directions normalize to that exact shape, tolerating legacy drafts that
 *    still hold `typeInformation`/`isArray`, so old and new documents round-trip identically.
 */

const structuralKeys = new Set(["nodeId", "activityVersionId", "inputs", "outputs", "structure"]);

interface WrappedInputValue {
  typeName?: string;
  expression: { type: string; value: unknown };
}

interface WireArgumentState {
  referenceKey: string;
  // `value` is the backend ArgumentValue.Value (object?). Literals serialize to a string; a Variable
  // expression keeps its structured VariableReference object so the declaring scope survives the trip.
  value: { value: unknown; expressionType: string };
}

export function canonicalizeStateForWire(state: WorkflowDefinitionState): WorkflowDefinitionState {
  return mapState(state, canonicalizeActivityNode);
}

export function expandStateFromWire(state: WorkflowDefinitionState): WorkflowDefinitionState {
  return mapState(state, expandActivityNode);
}

// Argument-collection normalization is shape-identical in both directions (it reads any legacy/current
// shape and emits the canonical wire shape), so only the per-activity transform differs.
function mapState(
  state: WorkflowDefinitionState,
  transform: (node: ActivityNode) => ActivityNode
): WorkflowDefinitionState {
  if (!state) return state;
  const next: WorkflowDefinitionState = { ...state };
  if (state.rootActivity) next.rootActivity = mapActivityTree(state.rootActivity, transform);
  // Variables and inputs both carry a storage driver; outputs are minimal (produced, not consumed).
  // The normalizer is deliberately shape-loose (it repairs legacy/wire records), so its unknown[]
  // result is asserted back to the declared variables type rather than validated per-field.
  if (Array.isArray(state.variables)) next.variables = mapArgumentRecords(state.variables, normalizeStoredArgument) as VariableDefinition[];
  if (Array.isArray(state.inputs)) next.inputs = mapArgumentRecords(state.inputs, normalizeStoredArgument);
  if (Array.isArray(state.outputs)) next.outputs = mapArgumentRecords(state.outputs, record => normalizeArgumentRecord(record, false));
  return next;
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

  // Container-scoped variable declarations live on `structure.payload.variables` in the same shape as
  // workflow-scoped ones, so normalize them here too (both directions).
  if (Array.isArray(structure.payload.variables) && structure.payload.variables.length > 0) {
    nextPayload.variables = mapArgumentRecords(structure.payload.variables, normalizeStoredArgument);
    changed = true;
  }

  return changed ? { ...transformed, structure: { ...structure, payload: nextPayload } } : transformed;
}

function mapArgumentRecords(
  value: unknown[],
  transform: (record: Record<string, unknown>) => Record<string, unknown>
): unknown[] {
  return value.map(item => (isRecord(item) && !Array.isArray(item) ? transform(item) : item));
}

// Variables and inputs share one normalizer (both keep a storage driver); outputs pass keepStorage=false.
function normalizeStoredArgument(record: Record<string, unknown>): Record<string, unknown> {
  return normalizeArgumentRecord(record, true);
}

// Emits the canonical argument shape: a stable `referenceKey`, `type: { alias, collectionKind }` (via
// the shared {@link readArgumentType}), plus a bare-alias `storageDriverType` for variables/inputs.
// Legacy `typeInformation`/`isArray`/casing variants and assembly metadata are dropped, as are the
// Elsa-3 input-default leftovers (`defaultValue`/`defaultSyntax`/`isReadOnly`) — the backend Input/Output
// records have no home for them. Every other field (name, displayName, default, …) is preserved
// untouched. Idempotent, so it is safe to run on either wire or in-memory records.
const droppedArgumentKeys = [
  "type", "Type", "typeInformation", "TypeInformation", "isArray", "IsArray",
  "storageDriverType", "StorageDriverType",
  "defaultValue", "DefaultValue", "defaultSyntax", "DefaultSyntax", "isReadOnly", "IsReadOnly"
];

function normalizeArgumentRecord(record: Record<string, unknown>, keepStorage: boolean): Record<string, unknown> {
  const next = omitKeys(record, droppedArgumentKeys);
  // Backfill a stable referenceKey when a legacy/foreign record lacks one (inputs/outputs authored before
  // the typed-argument-model didn't carry it). Only-when-missing keeps this idempotent and lossless.
  if (!readStringField(record, referenceKeyKeys).trim()) {
    next.referenceKey = generateId();
  }
  next.type = readArgumentType(record);
  if (keepStorage) next.storageDriverType = readWireStorageDriver(record.storageDriverType ?? record.StorageDriverType);
  return next;
}

function omitKeys(record: Record<string, unknown>, keys: string[]): Record<string, unknown> {
  const dropped = new Set(keys);
  const out: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(record)) {
    if (!dropped.has(key)) out[key] = value;
  }
  return out;
}

// Reads a storage driver as a bare alias: a string passes through; a legacy TypeInformation object is
// collapsed to `Namespace.TypeName` (or bare TypeName). Empty/absent ⇒ null (backend default).
function readWireStorageDriver(value: unknown): string | null {
  if (typeof value === "string") return value.trim() ? value : null;
  if (isRecord(value)) {
    const typeName = typeof value.typeName === "string" ? value.typeName : "";
    if (!typeName) return null;
    const namespace = typeof value.namespace === "string" ? value.namespace : "";
    return namespace ? `${namespace}.${typeName}` : typeName;
  }
  return null;
}

function canonicalizeActivityNode(node: ActivityNode): ActivityNode {
  const collected: WireArgumentState[] = [];
  const extras: Record<string, unknown> = {};

  for (const [key, value] of Object.entries(node)) {
    if (structuralKeys.has(key)) continue;
    if (isWrappedInputValue(value)) {
      collected.push({
        referenceKey: pascalize(key),
        value: toWireArgument(value.expression)
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

// Resolves an expression to the backend's ArgumentValue { value, expressionType }. Most expressions keep
// their authored syntax and serialize the value to a string. Two exceptions:
//  - A Variable expression keeps its structured VariableReference object (the backend's
//    VariableReference.TryParse reads `{ referenceKey, declaringScopeId }` from an ArgumentValue.Value
//    object) so the declaring scope survives the trip.
//  - A Literal whose value is structured (an array or object, e.g. a collection repeater's list) is sent
//    as an "Object" expression. The backend's literal converter only handles scalars, so it can't turn
//    the JSON-string form of a list into ICollection<T>; the Object handler JSON-deserializes it into the
//    target type. isRecord covers both arrays and plain objects.
function toWireArgument(expression: { type: string; value: unknown }): { value: unknown; expressionType: string } {
  const expressionType = expression.type || "Literal";
  if (expressionType === "Variable" && isRecord(expression.value)) {
    return { value: expression.value, expressionType };
  }
  if (expressionType === "Literal" && isRecord(expression.value)) {
    return { value: toWireValue(expression.value), expressionType: "Object" };
  }
  return { value: toWireValue(expression.value), expressionType };
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
