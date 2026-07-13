import type { WorkflowExecutionInputs, WorkflowInput } from "./workflowTypes";

export type WorkflowRunInputDrafts = Record<string, string>;
export type WorkflowRunInputValues = WorkflowExecutionInputs;

export interface WorkflowRunInputParseResult {
  values: WorkflowRunInputValues;
  errors: Record<string, string>;
}

export type WorkflowRunInputControlKind = "boolean" | "integer" | "number" | "datetime" | "text" | "json";

const exactIntegerValues = new WeakMap<object, string>();

export function getWorkflowRunInputControlKind(input: WorkflowInput): WorkflowRunInputControlKind {
  if (input.type.collectionKind !== "Single") return "json";
  const alias = canonicalAlias(input.type.alias);
  if (alias === "boolean") return "boolean";
  if (integerAliases.has(alias)) return "integer";
  if (numericAliases.has(alias)) return "number";
  if (alias === "datetime" || alias === "datetimeoffset") return "datetime";
  if (knownTextAliases.has(alias)) return "text";
  return "json";
}

export function parseWorkflowRunInputs(
  inputs: WorkflowInput[],
  drafts: WorkflowRunInputDrafts
): WorkflowRunInputParseResult {
  const values: WorkflowRunInputValues = {};
  const errors: Record<string, string> = {};

  for (const input of inputs) {
    const draft = drafts[input.referenceKey] ?? "";
    if (draft === "") {
      if (input.isRequired) errors[input.referenceKey] = `${input.displayName || input.name} is required.`;
      continue;
    }

    const parsed = input.type.collectionKind === "Single"
      ? parseScalarDraft(draft, input.type.alias)
      : parseCollectionDraft(draft, input.type.alias);
    if (parsed.error) errors[input.referenceKey] = parsed.error;
    else if (input.isRequired && parsed.value === null) errors[input.referenceKey] = `${input.displayName || input.name} is required.`;
    else values[input.name] = parsed.value;
  }

  return { values, errors };
}

function parseScalarDraft(draft: string, alias: string): { value?: unknown; error?: string } {
  const canonical = canonicalAlias(alias);
  switch (canonical) {
    case "boolean":
      return draft === "true" ? { value: true } : draft === "false" ? { value: false } : { error: "Choose true or false." };
    case "byte":
    case "sbyte":
    case "int16":
    case "uint16":
    case "int32":
    case "uint32":
    case "int64":
    case "uint64": return parseIntegerDraft(draft, canonical);
    case "single":
    case "double":
    case "decimal": {
      const value = Number(draft);
      return Number.isFinite(value) ? { value } : { error: "Enter a number." };
    }
    case "char":
      return [...draft].length === 1 ? { value: draft } : { error: "Enter one character." };
    case "datetime":
    case "datetimeoffset":
      return Number.isNaN(Date.parse(draft)) ? { error: "Enter a valid date and time." } : { value: draft };
    case "guid":
      return guidPattern.test(draft) ? { value: draft } : { error: "Enter a valid GUID." };
    case "string":
    case "timespan":
    case "uri":
      return { value: draft };
    default:
      try {
        return { value: JSON.parse(draft) as unknown };
      } catch {
        return { error: "Enter valid JSON." };
      }
  }
}

// Int64/UInt64 values outside JavaScript's safe-integer range stay opaque until the execution
// request is serialized. This keeps them out of Number entirely while still emitting JSON number
// tokens for Foundation's JsonElement input contract (never quoted decimal strings).
function parseIntegerDraft(draft: string, alias: string): { value?: unknown; error?: string } {
  const decimal = draft.trim();
  if (!integerPattern.test(decimal)) return { error: "Enter a whole number." };

  const value = BigInt(decimal);
  const range = integerRanges[alias];
  if (range && (value < range[0] || value > range[1])) {
    return { error: `Enter an integer from ${range[0]} to ${range[1]}.` };
  }
  if (alias !== "int64" && alias !== "uint64") return { value: Number(value) };

  const exact = Object.freeze(Object.create(null)) as object;
  exactIntegerValues.set(exact, decimal);
  return { value: exact };
}

/** Serializes workflow execution inputs while preserving opaque 64-bit integers as JSON numbers. */
export function serializeWorkflowExecutionPayload(payload: unknown): string {
  const rawJson = (JSON as typeof JSON & { rawJSON?: (text: string) => unknown }).rawJSON;
  const fallbacks = new Map<string, string>();
  const fallbackNonce = crypto.randomUUID();
  let fallbackIndex = 0;
  const result = JSON.stringify(payload, (_key, value: unknown) => {
    const decimal = value && typeof value === "object" ? exactIntegerValues.get(value) : undefined;
    if (decimal === undefined) return value;
    if (rawJson) return rawJson(decimal);

    const marker = `\u0000elsa-exact-integer-${fallbackNonce}-${fallbackIndex++}\u0000`;
    fallbacks.set(JSON.stringify(marker), decimal);
    return marker;
  });

  let serialized = result ?? "";
  for (const [marker, decimal] of fallbacks) serialized = serialized.replaceAll(marker, decimal);
  return serialized;
}

export function createWorkflowExecutionRequestInit(payload: unknown): RequestInit {
  return {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: serializeWorkflowExecutionPayload(payload)
  };
}

function parseCollectionDraft(draft: string, alias: string): { value?: unknown[]; error?: string } {
  let value: unknown;
  try {
    value = JSON.parse(draft) as unknown;
  } catch {
    return { error: "Enter a valid JSON array." };
  }
  if (!Array.isArray(value)) return { error: "Enter a JSON array." };

  const canonical = canonicalAlias(alias);
  if (canonical === "int64" || canonical === "uint64") {
    const tokens = readJsonArrayItems(draft);
    if (!tokens || tokens.length !== value.length) return { error: "Enter a valid JSON array." };
    const exactValues: unknown[] = [];
    for (let index = 0; index < tokens.length; index++) {
      const parsed = parseIntegerDraft(tokens[index], canonical);
      if (parsed.error) return { error: `Item ${index + 1}: ${parsed.error}` };
      exactValues.push(parsed.value);
    }
    return { value: exactValues };
  }

  for (let index = 0; index < value.length; index++) {
    const error = validateJsonScalar(value[index], alias);
    if (error) return { error: `Item ${index + 1} ${error}` };
  }
  return { value };
}

// JSON.parse validates the document first; this scanner only retains each top-level array item's
// original token so a 64-bit number is never rounded while being projected into the input value.
function readJsonArrayItems(draft: string): string[] | null {
  const text = draft.trim();
  if (!text.startsWith("[") || !text.endsWith("]")) return null;
  const content = text.slice(1, -1);
  if (content.trim() === "") return [];

  const items: string[] = [];
  let start = 0;
  let depth = 0;
  let quoted = false;
  let escaped = false;
  for (let index = 0; index < content.length; index++) {
    const character = content[index];
    if (quoted) {
      if (escaped) escaped = false;
      else if (character === "\\") escaped = true;
      else if (character === '"') quoted = false;
      continue;
    }
    if (character === '"') quoted = true;
    else if (character === "[" || character === "{") depth += 1;
    else if (character === "]" || character === "}") depth -= 1;
    else if (character === "," && depth === 0) {
      items.push(content.slice(start, index).trim());
      start = index + 1;
    }
  }
  items.push(content.slice(start).trim());
  return items;
}

function validateJsonScalar(value: unknown, alias: string): string | undefined {
  const canonical = canonicalAlias(alias);
  switch (canonical) {
    case "boolean": return typeof value === "boolean" ? undefined : "must be true or false.";
    case "byte":
    case "sbyte":
    case "int16":
    case "uint16":
    case "int32":
    case "uint32":
    case "int64":
    case "uint64":
      return typeof value === "number" && Number.isSafeInteger(value) && isIntegerInRange(value, canonical)
        ? undefined
        : "must be a whole number.";
    case "single":
    case "double":
    case "decimal": return typeof value === "number" && Number.isFinite(value) ? undefined : "must be a number.";
    case "char": return typeof value === "string" && [...value].length === 1 ? undefined : "must be one character.";
    case "datetime":
    case "datetimeoffset": return typeof value === "string" && !Number.isNaN(Date.parse(value)) ? undefined : "must be a valid date and time.";
    case "guid": return typeof value === "string" && guidPattern.test(value) ? undefined : "must be a valid GUID.";
    case "string":
    case "timespan":
    case "uri": return typeof value === "string" ? undefined : "must be text.";
    default: return undefined;
  }
}

function isIntegerInRange(value: number, alias: string) {
  const range = integerRanges[alias];
  return !range || (BigInt(value) >= range[0] && BigInt(value) <= range[1]);
}

const integerRanges: Record<string, readonly [bigint, bigint]> = {
  byte: [0n, 255n],
  sbyte: [-128n, 127n],
  int16: [-32768n, 32767n],
  uint16: [0n, 65535n],
  int32: [-2147483648n, 2147483647n],
  uint32: [0n, 4294967295n],
  int64: [-9223372036854775808n, 9223372036854775807n],
  uint64: [0n, 18446744073709551615n]
};

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const integerPattern = /^-?(?:0|[1-9]\d*)$/;
const integerAliases = new Set(Object.keys(integerRanges));
const numericAliases = new Set(["single", "double", "decimal"]);
const knownTextAliases = new Set(["char", "string", "datetime", "datetimeoffset", "timespan", "guid", "uri"]);

function canonicalAlias(alias: string) {
  const normalized = alias.trim().toLowerCase();
  return normalized.startsWith("system.") ? normalized.slice("system.".length) : normalized;
}
