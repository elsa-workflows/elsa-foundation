import type { ArgumentType, ArgumentValue, CollectionKind, VariableDefinition, VariableTypeDescriptor, WorkflowInput, WorkflowOutput } from "./workflowTypes";

/**
 * Shape construction for workflow Variables / Inputs / Outputs.
 *
 * All three converge on one argument shape: a `type: { alias, collectionKind }` (typed-argument-model
 * wire contract) plus per-collection extras — variables/inputs also carry a default and a storage
 * driver; outputs are minimal. The Properties editors keep entries as plain records inside
 * `draft.state`; these factories emit the canonical shape and the `update*` helpers spread the existing
 * record first so unknown fields authored elsewhere (or added by future backend versions) survive edits.
 */

// Elsa's default element-type alias for a freshly-added variable/input/output. The backend stores the
// alias the descriptor reports; "String" is the safe default when descriptors are unavailable.
export const defaultArgumentTypeName = "String";
export const defaultInputUiHint = "singleline";

export function isCollectionKind(value: unknown): value is CollectionKind {
  return value === "Single" || value === "Array" || value === "List" || value === "HashSet";
}

// Build an ArgumentType from a picker alias; collection kind defaults to a scalar.
export function makeArgumentType(alias: string | null | undefined, collectionKind: CollectionKind = "Single"): ArgumentType {
  return { alias: (alias ?? "").trim() || defaultArgumentTypeName, collectionKind };
}

// Reads a stored argument type from any shape: the current `{ alias, collectionKind }`, a legacy variable
// `typeInformation` object, or a legacy input/output bare-string type paired with an `isArray` flag. This
// is the single tolerant reader shared by the editor (in-memory, already normalized) and the wire layer
// (canonicalize/expand, which must convert legacy drafts).
export function readArgumentType(record: Record<string, unknown>): ArgumentType {
  const raw = record.type ?? record.Type;
  if (isPlainRecord(raw)) {
    const alias = typeof raw.alias === "string" ? raw.alias : typeof raw.typeName === "string" ? raw.typeName : "";
    return { alias, collectionKind: isCollectionKind(raw.collectionKind) ? raw.collectionKind : "Single" };
  }
  const legacy = record.typeInformation ?? record.TypeInformation;
  if (isPlainRecord(legacy)) {
    const alias = typeof legacy.typeName === "string" ? legacy.typeName : "";
    return { alias, collectionKind: readLegacyIsArray(record) ? "Array" : "Single" };
  }
  return { alias: typeof raw === "string" ? raw : "", collectionKind: readLegacyIsArray(record) ? "Array" : "Single" };
}

function readLegacyIsArray(record: Record<string, unknown>): boolean {
  const value = record.isArray ?? record.IsArray;
  return value === true || value === "true";
}

// The alias a descriptor advertises: the new `alias`, falling back to a legacy `typeName`.
export function descriptorAlias(descriptor: VariableTypeDescriptor): string {
  return (descriptor.alias ?? descriptor.typeName ?? "").trim();
}

// The default-value editor hint for a selected alias. Unknown/unlisted aliases fall back to "text".
export function defaultEditorFor(alias: string, descriptors: VariableTypeDescriptor[] | null | undefined): string {
  const match = (descriptors ?? []).find(descriptor => descriptorAlias(descriptor) === alias);
  const editor = match?.defaultEditor?.trim();
  return editor && editor.length > 0 ? editor : "text";
}

export function generateId(): string {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID();
  }
  // Deterministic-enough fallback for environments without the Web Crypto API.
  return `id-${Math.random().toString(36).slice(2)}${Date.now().toString(36)}`;
}

export function generateUniqueName(prefix: string, existingNames: Iterable<string>): string {
  const taken = new Set(existingNames);
  let index = 1;
  let candidate = `${prefix}${index}`;
  while (taken.has(candidate)) {
    index += 1;
    candidate = `${prefix}${index}`;
  }
  return candidate;
}

export function createVariable(options: {
  name: string;
  alias?: string;
  storageDriver?: string | null;
}): VariableDefinition {
  return {
    referenceKey: generateId(),
    name: options.name,
    type: makeArgumentType(options.alias),
    storageDriverType: options.storageDriver?.trim() || null,
    default: null
  };
}

export function updateVariable(existing: VariableDefinition, patch: Partial<VariableDefinition>): VariableDefinition {
  return { ...existing, ...patch };
}

// Wrap a literal default value as the backend's ArgumentValue, or null when cleared.
export function literalDefault(value: string): ArgumentValue | null {
  return value === "" ? null : { value, expressionType: "Literal" };
}

// Read a variable's literal default as a string for the editor (non-literal defaults show their raw value).
export function readVariableDefault(value: ArgumentValue | null | undefined): string {
  if (value == null || value.value == null) return "";
  return typeof value.value === "object" ? JSON.stringify(value.value) : String(value.value);
}

export function createInput(options: { name: string; alias?: string; storageDriver?: string | null }): WorkflowInput {
  return {
    referenceKey: generateId(),
    name: options.name,
    type: makeArgumentType(options.alias),
    displayName: options.name,
    description: "",
    category: "",
    uiHint: defaultInputUiHint,
    storageDriverType: options.storageDriver ?? null,
    isRequired: false
  };
}

export function updateInput(existing: WorkflowInput, patch: Partial<WorkflowInput>): WorkflowInput {
  return { ...existing, ...patch };
}

export function createOutput(options: { name: string; alias?: string }): WorkflowOutput {
  return {
    referenceKey: generateId(),
    name: options.name,
    type: makeArgumentType(options.alias),
    displayName: options.name,
    description: "",
    category: ""
  };
}

export function updateOutput(existing: WorkflowOutput, patch: Partial<WorkflowOutput>): WorkflowOutput {
  return { ...existing, ...patch };
}

// --- display helpers -------------------------------------------------------

/** Drop assembly suffix, namespace, and generic-arity backtick from a .NET type name. */
export function shortenTypeName(name: string): string {
  const noAssembly = name.split(",")[0].trim();
  const noNamespace = noAssembly.split(".").pop() ?? noAssembly;
  return noNamespace.split("`")[0];
}

/**
 * Most user-readable label for a type. A `displayName` with no namespace dots or assembly
 * commas is already friendly and used as-is; otherwise we shorten whichever field reads best.
 */
export function friendlyTypeLabel(displayName: string | null | undefined, alias: string): string {
  if (displayName && !displayName.includes(",") && !displayName.includes(".")) {
    return displayName;
  }
  return shortenTypeName(displayName || alias);
}

// --- field reading ---------------------------------------------------------

export function isPlainRecord(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === "object" && !Array.isArray(value);
}

/** Reads a record field as a string, trying each casing/alias key in order. */
export function readStringField(record: Record<string, unknown>, keys: string[]): string {
  for (const key of keys) {
    const value = record[key];
    if (value != null && typeof value !== "object") return String(value);
  }
  return "";
}

// Field-name aliases for the casings the backend may use across collections. Type is a nested
// `{ alias, collectionKind }` object read via readArgumentType, so only scalar fields are keyed here.
export const variableNameKeys = ["name", "Name"];
export const argumentNameKeys = ["name", "Name"];
export const storageDriverKeys = ["storageDriverType", "StorageDriverType"];
export const referenceKeyKeys = ["referenceKey", "ReferenceKey"];
