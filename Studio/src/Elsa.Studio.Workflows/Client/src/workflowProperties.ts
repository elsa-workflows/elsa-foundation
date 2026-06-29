import type { ArgumentValue, TypeInformation, VariableDefinition, WorkflowInput, WorkflowOutput } from "./workflowTypes";

/**
 * Shape construction for workflow Variables / Inputs / Outputs.
 *
 * The Properties editors keep entries as plain records inside `draft.state`. The backend
 * round-trips a canonical shape for each collection; variables use the scoped-variable
 * {@link VariableDefinition} contract (ADR-0027). These factories emit that exact shape, and the
 * `update*` helpers spread the existing record first so unknown fields authored elsewhere (or added
 * by future backend versions) survive an edit untouched.
 */

// Elsa's default argument type for a freshly-added input/output. The backend stores the alias the
// descriptor reports; "String" is the safe default when descriptors are unavailable.
export const defaultArgumentTypeName = "String";
export const defaultInputUiHint = "singleline";

function typeInfo(typeName: string, namespace: string, assemblyName = ""): TypeInformation {
  return { typeName, namespace, assemblyName, assemblyVersion: "" };
}

// Curated well-known variable types. The backend resolves a variable's CLR type by `namespace.typeName`
// (System.* types resolve without assembly metadata), so these are sufficient until a backend variable
// type descriptor endpoint exists (see elsa-foundation-studio#154); unknown types fall back to free text.
export const wellKnownVariableTypes: TypeInformation[] = [
  typeInfo("String", "System", "System.Private.CoreLib"),
  typeInfo("Boolean", "System", "System.Private.CoreLib"),
  typeInfo("Int32", "System", "System.Private.CoreLib"),
  typeInfo("Int64", "System", "System.Private.CoreLib"),
  typeInfo("Double", "System", "System.Private.CoreLib"),
  typeInfo("Decimal", "System", "System.Private.CoreLib"),
  typeInfo("DateTimeOffset", "System", "System.Private.CoreLib"),
  typeInfo("DateTime", "System", "System.Private.CoreLib"),
  typeInfo("TimeSpan", "System", "System.Private.CoreLib"),
  typeInfo("Guid", "System", "System.Private.CoreLib"),
  typeInfo("Object", "System", "System.Private.CoreLib")
];

export const defaultVariableType = wellKnownVariableTypes[0]; // String

// Canonical key for a type ("Namespace.TypeName"), matching the backend's GetTypeFullName() so the
// same string round-trips through the type picker.
export function typeInformationKey(type: TypeInformation | null | undefined): string {
  if (!type || !type.typeName) return "";
  return type.namespace ? `${type.namespace}.${type.typeName}` : type.typeName;
}

// Build a TypeInformation from a picker/free-text key. A curated well-known type wins (matched by full
// name or bare alias); otherwise the key is split into namespace + type name with assembly fields left
// blank for the backend's name-based resolution.
export function resolveTypeInformation(key: string): TypeInformation {
  const trimmed = key.trim();
  if (!trimmed) return { ...defaultVariableType };
  const curated = wellKnownVariableTypes.find(type => typeInformationKey(type) === trimmed || type.typeName === trimmed);
  if (curated) return { ...curated };
  const lastDot = trimmed.lastIndexOf(".");
  return lastDot > 0 ? typeInfo(trimmed.slice(lastDot + 1), trimmed.slice(0, lastDot)) : typeInfo(trimmed, "");
}

// Storage drivers have no curated table; split a free-text/descriptor key, or null for "backend default".
export function resolveStorageDriverType(key: string | null | undefined): TypeInformation | null {
  const trimmed = (key ?? "").trim();
  if (!trimmed) return null;
  const lastDot = trimmed.lastIndexOf(".");
  return lastDot > 0 ? typeInfo(trimmed.slice(lastDot + 1), trimmed.slice(0, lastDot)) : typeInfo(trimmed, "");
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
  typeKey?: string;
  storageDriverKey?: string | null;
}): VariableDefinition {
  return {
    referenceKey: generateId(),
    name: options.name,
    typeInformation: options.typeKey ? resolveTypeInformation(options.typeKey) : { ...defaultVariableType },
    storageDriverType: resolveStorageDriverType(options.storageDriverKey),
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

// Field-name aliases for the casings the backend may use across collections. Variable type/default/
// storage are nested objects on {@link VariableDefinition}, so only the name is read positionally.
export const variableNameKeys = ["name", "Name"];

export const argumentNameKeys = ["name", "Name"];
export const argumentTypeKeys = ["type", "Type", "typeName", "TypeName"];
export const argumentIsArrayKeys = ["isArray", "IsArray"];
export const inputStorageKeys = ["storageDriverType", "StorageDriverType"];
export const inputDefaultValueKeys = ["defaultValue", "DefaultValue"];
