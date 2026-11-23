import { serializeWorkflowExecutionPayload } from "../workflowRunInputs";
import type { JsonValue } from "./types";

/** Exact tokens stay inside the client and are serialized back as JSON numbers. */
class ExactJsonNumber { constructor(readonly text: string) {} }

/** Preserve number tokens before JSON.parse can round a declared-input literal. Strings remain untouched. */
export function parseLiteralJson(text: string): JsonValue {
  JSON.parse(text); // Validate original syntax; discard values before retaining exact number tokens.
  const numbers = new Map<string, string>();
  const nonce = crypto.randomUUID();
  let protectedText = "";
  for (let index = 0; index < text.length;) {
    const char = text[index];
    if (char === '"') {
      const start = index++;
      while (index < text.length) { if (text[index] === "\\") { index += 2; continue; } if (text[index++] === '"') break; }
      protectedText += text.slice(start, index);
    } else if (char === "-" || /\d/.test(char)) {
      const number = /^-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?/.exec(text.slice(index));
      if (!number) { protectedText += char; index++; continue; }
      const marker = `\u0000trigger-number-${nonce}-${numbers.size}\u0000`;
      numbers.set(marker, number[0]); protectedText += JSON.stringify(marker); index += number[0].length;
    } else { protectedText += char; index++; }
  }
  return JSON.parse(protectedText, (_key, value: unknown) => typeof value === "string" && numbers.has(value) ? new ExactJsonNumber(numbers.get(value)!) : value) as JsonValue;
}

export function serializeTriggerPayload(value: unknown): string {
  const replacements = new Map<string, string>();
  const nonce = crypto.randomUUID();
  function prepare(item: unknown): unknown {
    if (item instanceof ExactJsonNumber) { const marker = `\u0000trigger-raw-${nonce}-${replacements.size}\u0000`; replacements.set(JSON.stringify(marker), item.text); return marker; }
    if (Array.isArray(item)) return item.map(prepare);
    // Preserve the existing run-input parser's opaque exact-number objects for its own serializer.
    if (item && typeof item === "object" && Object.keys(item).length) return Object.fromEntries(Object.entries(item).map(([key, field]) => [key, prepare(field)]));
    return item;
  }
  let result = serializeWorkflowExecutionPayload(prepare(value));
  for (const [marker, raw] of replacements) result = result.replaceAll(marker, raw);
  return result;
}

/** Structural DTO counters remain numbers; authored/preview JSON values retain their exact tokens. */
export function parseTriggerResponse<T>(text: string): T {
  const parsed = parseLiteralJson(text);
  function project(value: unknown, literal = false): unknown {
    if (value instanceof ExactJsonNumber) {
      if (literal) return value;
      const number = Number(value.text);
      if (!Number.isFinite(number) || Number.isInteger(number) && !Number.isSafeInteger(number)) throw new Error("The backend returned an unsupported structural number.");
      return number;
    }
    if (Array.isArray(value)) return value.map(item => project(item, literal));
    if (value && typeof value === "object") return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, project(item, literal || ["value", "defaultValue", "configuration", "normalizedInputs"].includes(key))]));
    return value;
  }
  return project(parsed) as T;
}

/** Custom clients without raw responses must refuse potentially rounded literal material. */
export function assertSafeParsedLiterals(value: unknown, literal = false): void {
  if (literal && typeof value === "number" && (!Number.isSafeInteger(value) || Object.is(value, -0))) throw new Error("This backend transport cannot preserve the exact numeric literal. Use a transport with raw JSON response support before editing it.");
  if (Array.isArray(value)) { for (const item of value) assertSafeParsedLiterals(item, literal); }
  else if (value && typeof value === "object") for (const [key, item] of Object.entries(value)) assertSafeParsedLiterals(item, literal || ["value", "defaultValue", "configuration", "normalizedInputs"].includes(key));
}

function numericIdentity(text: string): string {
  const match = /^(-?)([0-9]+)(?:\.([0-9]+))?(?:[eE]([+-]?[0-9]+))?$/.exec(text)!;
  let digits = (match[2] + (match[3] ?? "")).replace(/^0+/, "");
  if (!digits) return "0e0";
  const trimmed = digits.replace(/0+$/, "");
  const exponent = BigInt(match[4] ?? "0") - BigInt((match[3] ?? "").length) + BigInt(digits.length - trimmed.length);
  digits = trimmed;
  return match[1] + digits + "e" + exponent;
}

export function parseConfigurationNumber(text: string, integer: boolean): JsonValue {
  const value = parseLiteralJson(text);
  if (!(value instanceof ExactJsonNumber) || integer && BigInt(numericIdentity(value.text).split("e")[1]) < 0n)
    throw new Error(integer ? "Enter a whole number." : "Enter a JSON number.");
  return value as unknown as JsonValue;
}

/** A JSON-only public contribution receives ordinary numbers, never the client's token wrappers. */
export function configurationForEditor(value: Record<string, JsonValue>): Record<string, JsonValue> {
  function convert(item: unknown): JsonValue {
    if (item instanceof ExactJsonNumber) {
      const number = Number(item.text);
      if (!Number.isFinite(number) || Object.is(number, -0) || numericIdentity(JSON.stringify(number)) !== numericIdentity(item.text))
        throw new Error("Use the standard fields to preserve the precision of these saved numbers.");
      return number;
    }
    if (Array.isArray(item)) return item.map(convert);
    if (item !== null && typeof item === "object") return Object.fromEntries(Object.entries(item).map(([key, field]) => [key, convert(field)]));
    return item as JsonValue;
  }
  return convert(value) as Record<string, JsonValue>;
}

/** Preserve the original token when an ordinary editor returns an unchanged numeric value. */
export function restoreEditorNumbers(next: JsonValue, previous: JsonValue): JsonValue {
  if (previous instanceof ExactJsonNumber && typeof next === "number" && Number.isFinite(next) &&
    numericIdentity(JSON.stringify(next)) === numericIdentity(previous.text)) return previous as unknown as JsonValue;
  if (Array.isArray(next)) return next.map((item, index) => restoreEditorNumbers(item, Array.isArray(previous) ? previous[index] : null));
  if (next !== null && typeof next === "object") return Object.fromEntries(Object.entries(next).map(([key, item]) =>
    [key, restoreEditorNumbers(item, previous !== null && typeof previous === "object" && Object.hasOwn(previous, key) ? (previous as Record<string, JsonValue>)[key] : null)]));
  return next;
}

export function copyTriggerJson<T>(value: T): T {
  if (value instanceof ExactJsonNumber) return value;
  if (Array.isArray(value)) return value.map(copyTriggerJson) as T;
  if (value !== null && typeof value === "object") return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, copyTriggerJson(item)])) as T;
  return value;
}
