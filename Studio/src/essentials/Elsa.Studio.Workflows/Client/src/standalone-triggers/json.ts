import { serializeWorkflowExecutionPayload } from "../workflowRunInputs";
import type { JsonValue } from "./types";

/** Only local authored JSON literals use this wrapper; it never becomes an API wire discriminator. */
class ExactJsonNumber { constructor(readonly text: string) {} }

/** Preserve number tokens before JSON.parse can round a declared-input literal. Strings remain untouched. */
export function parseLiteralJson(text: string): JsonValue {
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
