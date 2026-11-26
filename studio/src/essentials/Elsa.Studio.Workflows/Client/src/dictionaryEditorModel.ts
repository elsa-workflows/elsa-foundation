export type DictionaryKeyComparison = "ordinal" | "ordinalIgnoreCase";

export interface DictionaryRow {
  key: string;
  value: unknown;
}

export type DictionaryEntry = DictionaryRow;

export type DictionaryValidationErrorCode =
  | "invalid-json"
  | "invalid-root"
  | "blank-key"
  | "duplicate-key"
  | "invalid-value";

export interface DictionaryValidationError {
  code: DictionaryValidationErrorCode;
  message: string;
  /** Zero-based row/member occurrence, or -1 for a document-level JSON error. */
  index: number;
  key?: string;
}

export type DictionaryValidationResult =
  | { valid: true; value: Record<string, unknown> }
  | { valid: false; errors: DictionaryValidationError[] };

export function defaultDictionaryValue(_valueTypeName: string | null = null): Record<string, unknown> {
  return {};
}

/** Convert a stored plain JSON object into editable rows. Strings are deliberately not legacy-parsed. */
export function dictionaryEntries(value: unknown): DictionaryEntry[] {
  if (!isPlainObject(value)) return [];
  return Object.entries(value).map(([key, entryValue]) => ({ key, value: entryValue }));
}

export function validateDictionaryRows(
  rows: readonly DictionaryEntry[],
  comparison: DictionaryKeyComparison,
  valueTypeName: string | null
): DictionaryValidationResult {
  const errors: DictionaryValidationError[] = [];
  const occurrences = new Map<string, number[]>();

  rows.forEach((row, index) => {
    if (row.key.trim().length === 0) {
      errors.push({ code: "blank-key", message: "Key cannot be blank.", index, key: row.key });
    } else {
      const normalized = normalizeKey(row.key, comparison);
      const indices = occurrences.get(normalized) ?? [];
      indices.push(index);
      occurrences.set(normalized, indices);
    }

    const valueError = validateValue(row.value, valueTypeName);
    if (valueError) errors.push({ code: "invalid-value", message: valueError, index, key: row.key });
  });

  for (const duplicateIndices of occurrences.values()) {
    if (duplicateIndices.length < 2) continue;
    for (const index of duplicateIndices) {
      errors.push({
        code: "duplicate-key",
        message: `Key '${rows[index].key}' is duplicated.`,
        index,
        key: rows[index].key
      });
    }
  }

  if (errors.length > 0) {
    errors.sort((left, right) => (left.index ?? -1) - (right.index ?? -1));
    return { valid: false, errors };
  }
  return { valid: true, value: Object.fromEntries(rows.map(row => [row.key, row.value])) };
}

export function parseDictionaryJson(
  text: string,
  comparison: DictionaryKeyComparison,
  valueTypeName: string | null
): DictionaryValidationResult {
  let parsed: unknown;
  try {
    parsed = JSON.parse(text) as unknown;
  } catch (error) {
    return {
      valid: false,
      errors: [{
        code: "invalid-json",
        index: -1,
        message: `Invalid JSON: ${error instanceof Error ? error.message : "Unable to parse the document."}`
      }]
    };
  }

  if (!isPlainObject(parsed)) {
    return { valid: false, errors: [{ code: "invalid-root", index: -1, message: "Top-level JSON must be an object." }] };
  }

  const source = scanJsonObjects(text, comparison);
  if (source.errors.length > 0) return { valid: false, errors: source.errors };
  const rows = source.rootKeys.map(key => ({ key, value: parsed[key] }));
  return validateDictionaryRows(rows, comparison, valueTypeName);
}

export function formatDictionaryJson(value: Record<string, unknown>): string {
  return JSON.stringify(value, null, 2);
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  if (value == null || typeof value !== "object" || Array.isArray(value)) return false;
  const prototype = Object.getPrototypeOf(value);
  return prototype === Object.prototype || prototype === null;
}

function normalizeKey(key: string, comparison: DictionaryKeyComparison): string {
  return comparison === "ordinalIgnoreCase" ? key.toUpperCase() : key;
}

function validateValue(value: unknown, valueTypeName: string | null): string | null {
  const type = simpleClrTypeName(valueTypeName);
  if (!type) return null;
  if (value == null) return nonNullableValueTypes.has(type) ? `Value must be a ${displayType(type)}, not null.` : null;

  if (type === "string") return typeof value === "string" ? null : "Value must be a string.";
  if (type === "char") return typeof value === "string" && [...value].length === 1 ? null : "Value must be a single character.";
  if (type === "boolean" || type === "bool") return typeof value === "boolean" ? null : "Value must be a boolean.";
  if (integerTypes.has(type)) {
    if (typeof value !== "number" || !Number.isSafeInteger(value)) return "Value must be an integer.";
    if (unsignedIntegerTypes.has(type) && value < 0) return "Value must be a non-negative integer.";
    return null;
  }
  if (floatingPointTypes.has(type)) return typeof value === "number" && Number.isFinite(value) ? null : "Value must be a number.";
  return null;
}

function simpleClrTypeName(typeName: string | null): string {
  if (!typeName) return "";
  const withoutAssembly = typeName.split(",", 1)[0].trim();
  return (withoutAssembly.split(".").at(-1) ?? withoutAssembly).toLowerCase();
}

function displayType(type: string): string {
  return type === "boolean" || type === "bool" ? "boolean" : integerTypes.has(type) ? "integer" : "number";
}

const integerTypes = new Set(["byte", "sbyte", "int16", "uint16", "int32", "uint32", "int64", "uint64"]);
const unsignedIntegerTypes = new Set(["byte", "uint16", "uint32", "uint64"]);
const floatingPointTypes = new Set(["single", "double", "decimal"]);
const nonNullableValueTypes = new Set(["char", "boolean", "bool", ...integerTypes, ...floatingPointTypes]);

// JSON.parse intentionally drops duplicate object members. Walk the already syntax-validated source as
// well, retaining every occurrence at every object depth before the parsed object becomes authoritative.
function scanJsonObjects(text: string, comparison: DictionaryKeyComparison) {
  const rootKeys: string[] = [];
  const errors: DictionaryValidationError[] = [];

  const scanValue = (start: number, rootIndex: number, path: string[]): number => {
    const cursor = skipWhitespace(text, start);
    if (text[cursor] === "{") return scanObject(cursor, rootIndex, path);
    if (text[cursor] === "[") return scanArray(cursor, rootIndex, path);
    if (text[cursor] === '"') return readStringToken(text, cursor).end;
    let end = cursor;
    while (end < text.length && !/[\s,}\]]/.test(text[end])) end++;
    return end;
  };

  const scanObject = (start: number, inheritedRootIndex: number, path: string[]): number => {
    const occurrences = new Map<string, Array<{ key: string; index: number; path: string }>>();
    let cursor = skipWhitespace(text, start + 1);
    let memberIndex = 0;
    while (text[cursor] !== "}") {
      const token = readStringToken(text, cursor);
      const key = JSON.parse(token.text) as string;
      const rootIndex = inheritedRootIndex < 0 ? memberIndex : inheritedRootIndex;
      if (path.length === 0) rootKeys.push(key);
      const occurrence = { key, index: rootIndex, path: [...path, key].join(".") };
      const normalized = normalizeKey(key, comparison);
      occurrences.set(normalized, [...(occurrences.get(normalized) ?? []), occurrence]);

      cursor = skipWhitespace(text, token.end);
      cursor = scanValue(skipWhitespace(text, cursor + 1), rootIndex, [...path, key]);
      cursor = skipWhitespace(text, cursor);
      if (text[cursor] === ",") cursor = skipWhitespace(text, cursor + 1);
      memberIndex++;
    }

    for (const duplicates of occurrences.values()) {
      if (duplicates.length < 2) continue;
      for (const duplicate of duplicates) {
        errors.push({
          code: "duplicate-key",
          message: `Key '${duplicate.path}' is duplicated.`,
          index: duplicate.index,
          key: duplicate.path
        });
      }
    }
    return cursor + 1;
  };

  const scanArray = (start: number, rootIndex: number, path: string[]): number => {
    let cursor = skipWhitespace(text, start + 1);
    let itemIndex = 0;
    while (text[cursor] !== "]") {
      cursor = scanValue(cursor, rootIndex, [...path, String(itemIndex)]);
      cursor = skipWhitespace(text, cursor);
      if (text[cursor] === ",") cursor = skipWhitespace(text, cursor + 1);
      itemIndex++;
    }
    return cursor + 1;
  };

  scanValue(0, -1, []);
  errors.sort((left, right) => left.index - right.index);
  return { rootKeys, errors };
}

function readStringToken(text: string, start: number): { text: string; end: number } {
  let escaped = false;
  for (let cursor = start + 1; cursor < text.length; cursor++) {
    const character = text[cursor];
    if (escaped) escaped = false;
    else if (character === "\\") escaped = true;
    else if (character === '"') return { text: text.slice(start, cursor + 1), end: cursor + 1 };
  }
  return { text: text.slice(start), end: text.length };
}

function skipWhitespace(text: string, start: number): number {
  let cursor = start;
  while (/\s/.test(text[cursor] ?? "")) cursor++;
  return cursor;
}
