import { typeAllowsNull } from "./typeNullability";
import type {
  ArgumentType,
  ArgumentValue,
  CollectionKind,
  IncidentStrategyReference,
  PersistenceMode,
  VariableDefinition,
  VariableTypeDescriptor,
  WorkflowInput,
  WorkflowOutput,
  WorkflowStrategyOptions
} from "./workflowTypes";

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

export function readIncidentStrategyReference(strategyOptions: unknown): IncidentStrategyReference | null {
  if (!isPlainRecord(strategyOptions) || !isPlainRecord(strategyOptions.incidentStrategy)) return null;
  const { alias, version } = strategyOptions.incidentStrategy;
  return typeof alias === "string" && alias.trim().length > 0
    && typeof version === "string" && version.trim().length > 0
    ? { alias, version }
    : null;
}

export function updateIncidentStrategyReference(
  strategyOptions: unknown,
  incidentStrategy: IncidentStrategyReference | null
): WorkflowStrategyOptions {
  const options = isPlainRecord(strategyOptions) ? strategyOptions : {};
  const storedReference = isPlainRecord(options.incidentStrategy)
    ? options.incidentStrategy
    : {};
  return {
    ...options,
    incidentStrategy: incidentStrategy
      ? { ...storedReference, ...incidentStrategy }
      : null
  };
}

export const persistenceModes: readonly PersistenceMode[] = ["Durable", "Balanced", "Ephemeral"];

export function isPersistenceMode(value: unknown): value is PersistenceMode {
  return typeof value === "string" && (persistenceModes as readonly string[]).includes(value);
}

// The stored value as authored, recognized or not, so a value this Studio does not know can be shown and kept until
// the author picks another. Publishing accepts only the exact mode names.
export function readStoredPersistenceMode(strategyOptions: unknown): string | null {
  if (!isPlainRecord(strategyOptions)) return null;
  const { persistenceMode } = strategyOptions;
  return typeof persistenceMode === "string" && persistenceMode.trim().length > 0 ? persistenceMode : null;
}

// Choosing the host default removes the field rather than storing null, so the workflow carries no choice at all.
export function updatePersistenceMode(strategyOptions: unknown, mode: PersistenceMode | null): WorkflowStrategyOptions {
  const { persistenceMode: _previous, ...others } = isPlainRecord(strategyOptions) ? strategyOptions : {};
  return mode ? { ...others, persistenceMode: mode } : others;
}

export function isCollectionKind(value: unknown): value is CollectionKind {
  return value === "Single" || value === "Array" || value === "List" || value === "HashSet";
}

export function normalizeCollectionKind(value: unknown): CollectionKind {
  if (typeof value !== "string") return "Single";
  const normalized = value.toLowerCase();
  const kinds: readonly CollectionKind[] = ["Single", "Array", "List", "HashSet"];
  return kinds.find(kind => kind.toLowerCase() === normalized) ?? "Single";
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
    return { alias, collectionKind: normalizeCollectionKind(raw.collectionKind) };
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

export function readWorkflowInputDefault(value: unknown): string {
  if (value === null) return "null";
  if (typeof value === "object") return JSON.stringify(value) ?? "";
  return value === undefined ? "" : String(value);
}

export function parseWorkflowInputDefault(input: WorkflowInput, draft: string, editor: string): { value?: unknown; error?: string } {
  if (editor === "checkbox") return draft === "true" ? { value: true } : draft === "false" ? { value: false } : { error: "Choose true or false." };
  if (editor === "date") return parseDateDefault(draft);
  if (editor === "number") return parseNumericDefault(input.type.alias, draft);
  if (editor === "json" || input.type.collectionKind !== "Single") {
    if (!hasSafeJsonNumbers(draft)) return { error: "Unsafe JSON number." };
    try {
      const value = JSON.parse(draft) as unknown;
      if (value !== null && input.type.collectionKind !== "Single" && !Array.isArray(value)) return { error: "Enter an array." };
      if (value === null && !typeAllowsNull(input.type)) return { error: "This type does not accept null." };
      return { value };
    } catch {
      return { error: "Invalid JSON." };
    }
  }
  return { value: draft };
}

function parseDateDefault(draft: string): { value?: unknown; error?: string } {
  return /^\d{4}-\d{2}-\d{2}$/.test(draft) && !Number.isNaN(Date.parse(`${draft}T00:00:00Z`))
    ? { value: draft }
    : { error: "Invalid date." };
}

function parseNumericDefault(alias: string, draft: string): { value?: unknown; error?: string } {
  const canonicalAlias = alias.trim().split(".").pop()?.toLowerCase() ?? "";
  const decimal = draft.trim();
  const integerRanges: Record<string, [bigint, bigint]> = {
    byte: [0n, 255n], sbyte: [-128n, 127n],
    int16: [-32768n, 32767n], short: [-32768n, 32767n],
    uint16: [0n, 65535n], ushort: [0n, 65535n],
    int32: [-2147483648n, 2147483647n], int: [-2147483648n, 2147483647n],
    uint32: [0n, 4294967295n], uint: [0n, 4294967295n],
    int64: [-9223372036854775808n, 9223372036854775807n], long: [-9223372036854775808n, 9223372036854775807n],
    uint64: [0n, 18446744073709551615n], ulong: [0n, 18446744073709551615n]
  };
  const range = integerRanges[canonicalAlias];
  if (range) {
    if (!/^-?\d+$/.test(decimal)) return { error: "Integer required." };
    const value = BigInt(decimal);
    if (value < range[0] || value > range[1]) return { error: "Value out of range." };
    const number = Number(value);
    return Number.isSafeInteger(number) ? { value: number } : { error: "Integer exceeds safe range." };
  }

  const isDecimal = canonicalAlias === "decimal";
  const pattern = isDecimal ? /^-?(?:0|[1-9]\d*)(?:\.\d+)?$/ : /^-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?$/;
  const number = Number(decimal);
  if (!pattern.test(decimal) || decimal === "" || !Number.isFinite(number)) return { error: "Enter a number." };
  if (!hasSafeNumericPrecision(decimal)) return { error: "Number exceeds safe precision." };
  return { value: number };
}

function hasSafeJsonNumbers(source: string): boolean {
  return [...source.matchAll(/"(?:\\.|[^"\\])*"|(-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?)/gi)]
    .every(match => match[1] === undefined || isSafeJsonNumber(match[1]));
}

function isSafeJsonNumber(token: string): boolean {
  const number = Number(token);
  if (!Number.isFinite(number)) return false;
  if (!/[.eE]/.test(token)) return Number.isSafeInteger(number);
  return hasSafeNumericPrecision(token);
}

function hasSafeNumericPrecision(value: string): boolean {
  const mantissa = value.split(/[eE]/)[0].replace(/^-/, "").replace(".", "");
  const significantDigits = mantissa.replace(/^0+/, "").replace(/0+$/, "");
  return significantDigits.length <= 15;
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
    isRequired: false,
  };
}

export function updateInput(existing: WorkflowInput, patch: Partial<WorkflowInput>): WorkflowInput {
  const displayName = patch.name !== undefined
    && (existing.displayName == null || existing.displayName === "" || existing.displayName === existing.name)
    ? { displayName: patch.name }
    : {};
  const updated = { ...existing, ...displayName, ...patch };
  if (Object.prototype.hasOwnProperty.call(patch, "defaultValue") && patch.defaultValue === undefined) delete updated.defaultValue;
  if (Object.prototype.hasOwnProperty.call(patch, "defaultSyntax") && patch.defaultSyntax === undefined) delete updated.defaultSyntax;
  return updated;
}

export function createOutput(options: { name: string; alias?: string }): WorkflowOutput {
  return {
    referenceKey: generateId(),
    name: options.name,
    type: makeArgumentType(options.alias),
    displayName: options.name,
    description: "",
    category: "",
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
