import type { StudioActivityInputDescriptor, StudioExpressionEditorContext } from "@elsa-workflows/studio-sdk";
import type { Scalar } from "./decisionsApi";
import { initial } from "./CasePanel";
import type { Clause } from "./decisionXml";

export const decisionInputsEditorId = "decisions.inputs";

export function isDecisionInputsDescriptor(descriptor: Pick<StudioActivityInputDescriptor, "name" | "referenceKey" | "typeName">): boolean {
  const name = descriptor.referenceKey?.trim() || descriptor.name;
  return name === "Inputs" && /DecisionValue/i.test(descriptor.typeName ?? "");
}

export function isDecisionInputsContext(context: Pick<StudioExpressionEditorContext, "syntax" | "descriptor">): boolean {
  return (context.syntax === "Object" || context.syntax === "Literal") && isDecisionInputsDescriptor(context.descriptor);
}

export function parseDecisionPin(value: string): { assetId: string; selection: string } | null {
  const split = value.lastIndexOf("@");
  if (split <= 0 || split === value.length - 1) return null;
  return { assetId: value.slice(0, split), selection: value.slice(split + 1) };
}

export function readDecisionPin(activity: unknown): string {
  if (!activity || typeof activity !== "object") return "";
  const record = activity as Record<string, unknown>;
  return readLiteralString(record.Decision) || readLiteralString(record.decision);
}

export function clauseKind(type: string): Exclude<Scalar["kind"], "null"> {
  return type === "number" ? "number" : type === "boolean" ? "boolean" : "string";
}

export function isScalar(value: unknown): value is Scalar {
  if (!value || typeof value !== "object" || Array.isArray(value)) return false;
  const record = value as { kind?: unknown; value?: unknown };
  if (record.kind === "null") return record.value === null;
  if (record.kind === "boolean") return record.value === "true" || record.value === "false";
  return (record.kind === "number" || record.kind === "string") && typeof record.value === "string";
}

export function asDecisionValueMap(value: unknown): Record<string, Scalar> {
  const source = asObject(value);
  const result: Record<string, Scalar> = {};
  for (const [key, item] of Object.entries(source)) {
    if (isScalar(item)) result[key] = item;
  }
  return result;
}

export function seedDecisionInputs(value: unknown, clauses: Clause[]): Record<string, Scalar> | null {
  const current = asDecisionValueMap(value);
  const next = { ...current };
  let changed = false;
  for (const clause of clauses) {
    if (isScalar(next[clause.name])) continue;
    next[clause.name] = initial(clause.type);
    changed = true;
  }
  return changed ? next : null;
}

export function extraDecisionInputs(value: Record<string, Scalar>, clauses: Clause[]): Array<{ name: string; value: Scalar }> {
  const known = new Set(clauses.map(clause => clause.name));
  return Object.entries(value).filter(([name]) => !known.has(name)).map(([name, item]) => ({ name, value: item }));
}

export function readDecisionInputs(activity: unknown): unknown {
  if (!activity || typeof activity !== "object") return {};
  const record = activity as Record<string, unknown>;
  return readWrappedValue(record.Inputs) ?? readWrappedValue(record.inputs) ?? {};
}

export function isValidDecisionNumber(value: string | null | undefined): boolean {
  if (value == null || value.length === 0 || value.length > 4096) return false;
  const offset = value[0] === "-" ? 1 : 0;
  if (offset === value.length) return false;
  let dot = -1;
  let digits = 0;
  for (let index = offset; index < value.length; index++) {
    const character = value[index];
    if (character === "." && dot < 0) dot = index;
    else if (character >= "0" && character <= "9") digits++;
    else return false;
  }
  return digits > 0 && dot !== value.length - 1;
}

export function invalidDecisionInputs(value: Record<string, Scalar>, clauses: Clause[]): Array<{ name: string; tableType: string }> {
  const invalid: Array<{ name: string; tableType: string }> = [];
  for (const clause of clauses) {
    const stored = value[clause.name];
    if (!stored || stored.kind === "null") continue;
    const expected = clauseKind(clause.type);
    if (stored.kind !== expected) continue;
    if (expected === "number" && !isValidDecisionNumber(stored.value)) invalid.push({ name: clause.name, tableType: expected });
    if (expected === "boolean" && stored.value !== "true" && stored.value !== "false") invalid.push({ name: clause.name, tableType: expected });
  }
  return invalid;
}

function readWrappedValue(value: unknown): unknown {
  if (value && typeof value === "object" && "expression" in value) {
    return (value as { expression?: { value?: unknown } }).expression?.value;
  }
  return value;
}

export function mismatchedDecisionInputs(value: Record<string, Scalar>, clauses: Clause[]): Array<{ name: string; storedKind: Scalar["kind"]; tableType: string }> {
  return clauses.flatMap(clause => {
    const stored = value[clause.name];
    if (!stored || stored.kind === "null") return [];
    const expected = clauseKind(clause.type);
    return stored.kind === expected ? [] : [{ name: clause.name, storedKind: stored.kind, tableType: expected }];
  });
}

export function displayedDecisionInput(clause: Clause, stored: Scalar | undefined): Scalar {
  if (!stored) return initial(clause.type);
  if (stored.kind === "null") return stored;
  const kind = clauseKind(clause.type);
  if (stored.kind === kind) return stored;
  return { kind, value: stored.value ?? (kind === "boolean" ? "false" : "") };
}

export function writtenDecisionInput(clause: Clause, next: Scalar): Scalar {
  if (next.kind === "null") return { kind: "null", value: null };
  const kind = clauseKind(clause.type);
  const value = next.value == null ? (kind === "boolean" ? "false" : "") : String(next.value);
  return kind === "boolean" ? { kind, value: value === "true" ? "true" : "false" } : { kind, value };
}

export function matchDecisionInputTypes(value: Record<string, Scalar>, clauses: Clause[]): Record<string, Scalar> | null {
  if (!mismatchedDecisionInputs(value, clauses).length) return null;
  const next = { ...value };
  for (const clause of clauses) {
    if (!next[clause.name]) continue;
    next[clause.name] = writtenDecisionInput(clause, next[clause.name]);
  }
  return next;
}

export function removeDecisionInputs(value: Record<string, Scalar>, names: string[]): Record<string, Scalar> {
  const drop = new Set(names);
  return Object.fromEntries(Object.entries(value).filter(([name]) => !drop.has(name)));
}

export function formatDecisionInputsJson(value: Record<string, Scalar>): string {
  return `${JSON.stringify(value, null, 2)}\n`;
}

export function parseDecisionInputsJson(text: string): { valid: true; value: Record<string, Scalar> } | { valid: false; message: string } {
  try {
    const parsed: unknown = text.trim() ? JSON.parse(text) : {};
    if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) return { valid: false, message: "JSON must be an object of tagged values." };
    const value = asDecisionValueMap(parsed);
    if (Object.keys(parsed).some(key => !isScalar((parsed as Record<string, unknown>)[key]))) {
      return { valid: false, message: "Each entry needs { kind, value } tags with a string or null value." };
    }
    return { valid: true, value };
  } catch {
    return { valid: false, message: "JSON is not valid." };
  }
}

function asObject(value: unknown): Record<string, unknown> {
  if (value && typeof value === "object" && !Array.isArray(value)) return value as Record<string, unknown>;
  if (typeof value !== "string") return {};
  const trimmed = value.trim();
  if (!trimmed) return {};
  try {
    const parsed: unknown = JSON.parse(trimmed);
    return parsed && typeof parsed === "object" && !Array.isArray(parsed) ? parsed as Record<string, unknown> : {};
  } catch {
    return {};
  }
}

function readLiteralString(value: unknown): string {
  if (typeof value === "string") return value;
  if (!value || typeof value !== "object") return "";
  const expression = "expression" in value ? (value as { expression?: { value?: unknown } }).expression : undefined;
  return typeof expression?.value === "string" ? expression.value : "";
}
