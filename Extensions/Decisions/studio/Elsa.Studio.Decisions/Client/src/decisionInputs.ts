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

export function formatDecisionInputsJson(value: Record<string, Scalar>): string {
  return `${JSON.stringify(value, null, 2)}\n`;
}

export function parseDecisionInputsJson(text: string): { valid: true; value: Record<string, Scalar> } | { valid: false; message: string } {
  try {
    const parsed: unknown = text.trim() ? JSON.parse(text) : {};
    if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) return { valid: false, message: "JSON must be an object of tagged values." };
    const value = asDecisionValueMap(parsed);
    if (Object.keys(parsed).some(key => !isScalar((parsed as Record<string, unknown>)[key]))) {
      return { valid: false, message: "Each entry needs { kind, value } tags." };
    }
    return { valid: true, value };
  } catch {
    return { valid: false, message: "JSON is not valid." };
  }
}

export function isScalar(value: unknown): value is Scalar {
  return !!value && typeof value === "object" && "kind" in value && "value" in value
    && (value.kind === "number" || value.kind === "string" || value.kind === "boolean" || value.kind === "null");
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
