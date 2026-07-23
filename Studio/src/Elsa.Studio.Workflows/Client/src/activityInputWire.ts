import { generateId, readArgumentType, readStringField, referenceKeyKeys } from "./workflowProperties";
import type { ActivityNode, VariableDefinition, WorkflowDefinitionState } from "./workflowTypes";
import { normalizeFlowchartStartNode } from "./flowchartStartNode";

/**
 * Bridges the Studio's in-memory model and the backend's canonical wire contract.
 *
 * Two concerns live here:
 *
 * 1. Activity input values. The editors keep them as top-level properties on the activity node, keyed
 *    by the input's catalog `referenceKey` verbatim (e.g. `activity.text = { typeName, expression:
 *    { type, value } }` for WriteLine's `referenceKey: "text"`). The backend `ActivityNode` model
 *    carries authored values in an `inputs` array of `ArgumentState { referenceKey, value, conversion?,
 *    … }`; anything else is dropped on deserialization. {@link canonicalizeStateForWire} folds the
 *    top-level wrapped properties into `inputs`; {@link expandStateFromWire} reverses it on load. Keys
 *    ride both directions untransformed — the referenceKey is an opaque contract identifier, not a
 *    naming convention. The authored conversion request and every other ArgumentState field round-trip
 *    losslessly: expand carries them onto the wrapped value (`conversion`/`argumentExtras`) and
 *    canonicalize folds them back, so a document authored elsewhere never loses fields on save.
 *
 * 2. Argument collections — the workflow-level `variables`/`inputs`/`outputs` and container-scoped
 *    variables. The typed-argument-model contract carries each argument's type as
 *    `type: { alias, collectionKind }` with a bare-alias `storageDriverType` (no `isArray`, no
 *    assembly metadata). Both directions normalize to that exact shape, tolerating legacy drafts that
 *    still hold `typeInformation`/`isArray`, so old and new documents round-trip identically.
 */

// Authored input properties share the node object with these structural fields. A referenceKey equal
// to one of them would be misread as structure; the backend treats these names as reserved.
const structuralKeys = new Set(["nodeId", "activityVersionId", "inputs", "outputs", "structure"]);

interface WrappedInputValue {
  typeName?: string;
  expression: { type: string; value: unknown };
  // Authored conversion request (the backend's AuthoredValueConversionRequest); absent means the
  // legacy default (Auto). Carried verbatim — the mode/profile semantics live in conversionSettings.
  conversion?: unknown;
  // Every other ArgumentState field (autoEvaluate, evaluatorType, storageDriverType, isSensitive,
  // and forward-compatible unknowns) rides through the editor untouched in this bag.
  argumentExtras?: Record<string, unknown>;
}

interface WireArgumentState {
  referenceKey: string;
  // `value` is the backend ArgumentValue.Value (object?). Literals serialize to a string; reference
  // expressions keep their structured identity objects so stable reference keys survive the trip.
  value: { value: unknown; expressionType: string };
  conversion?: unknown;
  [key: string]: unknown;
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
    if (isActivityNode(value)) {
      nextPayload[key] = mapActivityTree(value, transform);
      changed = true;
    } else if (Array.isArray(value) && value.length > 0 && value.every(isActivityNode)) {
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

  const mapped = changed ? { ...transformed, structure: { ...structure, payload: nextPayload } } : transformed;
  return normalizeFlowchartStartNode(mapped);
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
  // Seed from any pre-existing wire-shaped inputs (e.g. template-seeded defaults on a freshly dropped
  // node), then overlay the top-level authored properties — an edited value wins over its seeded
  // default under the same referenceKey. Keyed by referenceKey, so the transform is idempotent.
  const inputs = seedArgumentMap(node.inputs);
  // Output captures fold into `node.outputs[]` the same way inputs fold into `node.inputs[]`: seed from
  // existing wire entries (including foreign, non-capture bindings that must survive verbatim), then
  // overlay authored top-level captures under the output's referenceKey.
  const outputs = seedArgumentMap(node.outputs);

  const extras: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(node)) {
    if (structuralKeys.has(key)) continue;
    if (isWrappedInputValue(value)) {
      inputs.set(key, toWireArgumentState(key, value, inputs.get(key)));
    } else if (isOutputCaptureValue(value)) {
      outputs.set(key, toWireOutputArgumentState(key, value, outputs.get(key)));
    } else {
      extras[key] = value;
    }
  }

  return {
    ...extras,
    nodeId: node.nodeId,
    activityVersionId: node.activityVersionId,
    inputs: [...inputs.values()],
    outputs: [...outputs.values()],
    structure: node.structure
  };
}

function seedArgumentMap(records: unknown): Map<string, unknown> {
  const map = new Map<string, unknown>();
  for (const existing of Array.isArray(records) ? records : []) {
    if (isRecord(existing) && typeof existing.referenceKey === "string") map.set(existing.referenceKey, existing);
  }
  return map;
}

// The wrapped value is authoritative for the expression and the conversion request (its absence on an
// expanded value means the author cleared it); the seeded wire record only fills in when the value was
// never expanded — a template-seeded default written straight by the palette.
function toWireArgumentState(
  referenceKey: string,
  wrapped: WrappedInputValue,
  seeded: unknown
): WireArgumentState {
  const seededRecord = isRecord(seeded) ? seeded : {};
  const conversion = "conversion" in wrapped ? wrapped.conversion : seededRecord.conversion;
  return {
    ...(wrapped.argumentExtras ?? omitKeys(seededRecord, argumentStateOwnKeys)),
    referenceKey,
    value: toWireArgument(wrapped.expression),
    ...(conversion != null ? { conversion } : {})
  };
}

// The authored in-memory shape for an output capture (see activityOutputCapture.ts): a variable target
// plus optional conversion/extras. The target rides the wire as a Variable-expression ArgumentValue.
interface OutputCaptureValue {
  target: Record<string, unknown>;
  conversion?: unknown;
  argumentExtras?: Record<string, unknown>;
}

// The capture value is authoritative for the target and the conversion request; a seeded wire record only
// fills in the passthrough ArgumentState extras when the capture was never expanded.
function toWireOutputArgumentState(
  referenceKey: string,
  capture: OutputCaptureValue,
  seeded: unknown
): WireArgumentState {
  const seededRecord = isRecord(seeded) ? seeded : {};
  const conversion = "conversion" in capture ? capture.conversion : seededRecord.conversion;
  return {
    ...(capture.argumentExtras ?? omitKeys(seededRecord, argumentStateOwnKeys)),
    referenceKey,
    value: { value: capture.target, expressionType: "Variable" },
    ...(conversion != null ? { conversion } : {})
  };
}

// The ArgumentState fields the editor models directly; everything else is a passthrough extra.
const argumentStateOwnKeys = ["referenceKey", "value", "conversion"];

function expandActivityNode(node: ActivityNode): ActivityNode {
  const inputs = Array.isArray(node.inputs) ? node.inputs : [];
  const expanded: Record<string, unknown> = {};

  for (const argument of inputs) {
    if (!isRecord(argument) || typeof argument.referenceKey !== "string") continue;
    const argumentValue = isRecord(argument.value) ? argument.value : {};
    const argumentExtras = omitKeys(argument, argumentStateOwnKeys);
    expanded[argument.referenceKey] = {
      typeName: "",
      expression: {
        type: typeof argumentValue.expressionType === "string" ? argumentValue.expressionType : "Literal",
        value: argumentValue.value ?? ""
      },
      ...(argument.conversion != null ? { conversion: argument.conversion } : {}),
      ...(Object.keys(argumentExtras).length > 0 ? { argumentExtras } : {})
    } satisfies WrappedInputValue;
  }

  // Expand only Variable-expression output bindings into the editable in-memory capture model; any other
  // output ArgumentState (a foreign non-variable binding) rides through the outputs array untouched so a
  // document authored elsewhere never loses fields on load.
  const remainingOutputs: unknown[] = [];
  for (const argument of Array.isArray(node.outputs) ? node.outputs : []) {
    const capture = toOutputCaptureValue(argument);
    if (capture) expanded[(argument as Record<string, unknown>).referenceKey as string] = capture;
    else remainingOutputs.push(argument);
  }

  return { ...node, ...expanded, inputs: [], outputs: remainingOutputs };
}

function toOutputCaptureValue(argument: unknown): OutputCaptureValue | null {
  if (!isRecord(argument) || typeof argument.referenceKey !== "string") return null;
  const argumentValue = isRecord(argument.value) ? argument.value : null;
  if (!argumentValue || argumentValue.expressionType !== "Variable" || !isRecord(argumentValue.value)) return null;
  const argumentExtras = omitKeys(argument, argumentStateOwnKeys);
  return {
    target: argumentValue.value,
    ...(argument.conversion != null ? { conversion: argument.conversion } : {}),
    ...(Object.keys(argumentExtras).length > 0 ? { argumentExtras } : {})
  };
}

// Resolves an expression to the backend's ArgumentValue { value, expressionType }. Most expressions keep
// their authored syntax and serialize the value to a string. Two exceptions:
//  - Variable and Input expressions keep their structured reference objects (the backend's
//    VariableReference.TryParse reads `{ referenceKey, declaringScopeId }` from an ArgumentValue.Value
//    object) so stable identities and declaring scope survive the trip.
//  - A Literal whose value is structured (an array or object, e.g. a collection repeater's list) is sent
//    as an "Object" expression. The backend's literal converter only handles scalars, so it can't turn
//    the JSON-string form of a list into ICollection<T>; the Object handler JSON-deserializes it into the
//    target type. isRecord covers both arrays and plain objects.
function toWireArgument(expression: { type: string; value: unknown }): { value: unknown; expressionType: string } {
  const expressionType = expression.type || "Literal";
  if ((expressionType === "Variable" || expressionType === "Input") && isRecord(expression.value)) {
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

// An authored output capture carries a variable `target` reference (never an `expression`), so it is
// distinguishable from a wrapped input value among the node's top-level authored properties.
function isOutputCaptureValue(value: unknown): value is OutputCaptureValue {
  if (!isRecord(value) || Array.isArray(value)) return false;
  const target = value.target;
  return isRecord(target) && typeof target.referenceKey === "string";
}

function isActivityNode(value: unknown): value is ActivityNode {
  return isRecord(value) &&
    typeof (value as ActivityNode).nodeId === "string" &&
    typeof (value as ActivityNode).activityVersionId === "string";
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}
