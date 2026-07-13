import type { WorkflowExecutionInputs, WorkflowInput } from "./workflowTypes";

export type WorkflowRunInputDrafts = Record<string, string>;
export type WorkflowRunInputValues = WorkflowExecutionInputs;

export interface WorkflowRunInputParseResult {
  values: WorkflowRunInputValues;
  errors: Record<string, string>;
}

export type WorkflowRunInputControlKind = "boolean" | "integer" | "number" | "datetime" | "text" | "json";

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
    case "uint64": {
      const value = Number(draft);
      return Number.isSafeInteger(value) && isIntegerInRange(value, canonical)
        ? { value }
        : { error: "Enter a whole number." };
    }
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

function parseCollectionDraft(draft: string, alias: string): { value?: unknown[]; error?: string } {
  let value: unknown;
  try {
    value = JSON.parse(draft) as unknown;
  } catch {
    return { error: "Enter a valid JSON array." };
  }
  if (!Array.isArray(value)) return { error: "Enter a JSON array." };
  for (let index = 0; index < value.length; index++) {
    const error = validateJsonScalar(value[index], alias);
    if (error) return { error: `Item ${index + 1} ${error}` };
  }
  return { value };
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
  return !range || (value >= range[0] && value <= range[1]);
}

const integerRanges: Record<string, readonly [number, number]> = {
  byte: [0, 255],
  sbyte: [-128, 127],
  int16: [-32768, 32767],
  uint16: [0, 65535],
  int32: [-2147483648, 2147483647],
  uint32: [0, 4294967295],
  int64: [Number.MIN_SAFE_INTEGER, Number.MAX_SAFE_INTEGER],
  uint64: [0, Number.MAX_SAFE_INTEGER]
};

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const integerAliases = new Set(Object.keys(integerRanges));
const numericAliases = new Set(["single", "double", "decimal"]);
const knownTextAliases = new Set(["char", "string", "datetime", "datetimeoffset", "timespan", "guid", "uri"]);

function canonicalAlias(alias: string) {
  const normalized = alias.trim().toLowerCase();
  return normalized.startsWith("system.") ? normalized.slice("system.".length) : normalized;
}
