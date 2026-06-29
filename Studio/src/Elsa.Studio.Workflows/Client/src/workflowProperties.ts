import type { WorkflowInput, WorkflowOutput, WorkflowVariable } from "./workflowTypes";

/**
 * Shape construction for workflow Variables / Inputs / Outputs.
 *
 * The Properties editors keep entries as plain records inside `draft.state`. The backend
 * round-trips a canonical shape for each collection (see {@link WorkflowVariable} et al.);
 * the original editable prototype shipped mis-shaped objects (`{ name }` with the type written
 * to a `type` key for variables), which broke persistence. These factories emit the canonical
 * shape, and the `update*` helpers spread the existing record first so unknown fields authored
 * elsewhere (or added by future backend versions) survive an edit untouched.
 */

// Elsa's default variable type/driver for a freshly-added variable. The backend stores the
// alias the descriptor reports; "String" is the safe default when descriptors are unavailable.
export const defaultVariableTypeName = "String";
export const defaultStorageDriverTypeName = "WorkflowInstanceStorageDriver";
export const defaultArgumentTypeName = "String";
export const defaultInputUiHint = "singleline";

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
  typeName?: string;
  storageDriverTypeName?: string | null;
} ): WorkflowVariable {
  return {
    id: generateId(),
    name: options.name,
    typeName: options.typeName?.trim() || defaultVariableTypeName,
    isArray: false,
    value: null,
    storageDriverTypeName: options.storageDriverTypeName ?? defaultStorageDriverTypeName
  };
}

export function updateVariable(existing: WorkflowVariable, patch: Partial<WorkflowVariable>): WorkflowVariable {
  return { ...existing, ...patch };
}

export function createInput(options: { name: string; type?: string; storageDriverType?: string | null }): WorkflowInput {
  return {
    name: options.name,
    type: options.type?.trim() || defaultArgumentTypeName,
    displayName: options.name,
    description: "",
    category: "",
    isArray: false,
    uiHint: defaultInputUiHint,
    storageDriverType: options.storageDriverType ?? null,
    defaultValue: null,
    defaultSyntax: null,
    isReadOnly: null
  };
}

export function updateInput(existing: WorkflowInput, patch: Partial<WorkflowInput>): WorkflowInput {
  return { ...existing, ...patch };
}

export function createOutput(options: { name: string; type?: string }): WorkflowOutput {
  return {
    name: options.name,
    type: options.type?.trim() || defaultArgumentTypeName,
    displayName: options.name,
    description: "",
    category: "",
    isArray: false
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
export function friendlyTypeLabel(displayName: string | null | undefined, typeName: string): string {
  if (displayName && !displayName.includes(",") && !displayName.includes(".")) {
    return displayName;
  }
  return shortenTypeName(displayName || typeName);
}

export function friendlyDriverLabel(displayName: string | null | undefined, typeName: string): string {
  return friendlyTypeLabel(displayName, typeName).replace(/StorageDriver$/, "");
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

export function readBooleanField(record: Record<string, unknown>, keys: string[]): boolean {
  for (const key of keys) {
    if (key in record && record[key] != null) return record[key] === true || record[key] === "true";
  }
  return false;
}

// Field-name aliases for the casings the backend may use across collections.
export const variableNameKeys = ["name", "Name"];
export const variableTypeKeys = ["typeName", "TypeName"];
export const variableValueKeys = ["value", "Value"];
export const variableStorageKeys = ["storageDriverTypeName", "StorageDriverTypeName"];
export const variableIsArrayKeys = ["isArray", "IsArray"];

export const argumentNameKeys = ["name", "Name"];
export const argumentTypeKeys = ["type", "Type", "typeName", "TypeName"];
export const argumentIsArrayKeys = ["isArray", "IsArray"];
export const inputStorageKeys = ["storageDriverType", "StorageDriverType"];
export const inputDefaultValueKeys = ["defaultValue", "DefaultValue"];
