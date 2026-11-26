import { promotionRecord as record, refusePromotion as refuse } from "./blockPromotionTypes";
import type { PromotionExpressionSupport } from "./promotionExpressionSupport";

type Fields = Record<string, unknown>;
export interface PromotionReference {
  kind: "input" | "variable" | "result";
  key: string;
  scope?: string;
  projection?: string;
  authored: Fields;
}

const text = (value: unknown) => typeof value === "string" && value.trim() ? value : undefined;

/** Reads only the reference envelopes owned by the expression contracts. Never matches arbitrary strings. */
export function readPromotionReference(kind: string, value: unknown, path: string, parameter = false): PromotionReference | null {
  const normalized = kind.toLowerCase();
  if (["literal", "object", "default", "secret", "connection"].includes(normalized)) return null;
  const fields = record(value) ? value : {};
  const allowed = normalized === "input" ? [parameter ? "memberKey" : "referenceKey"]
    : normalized === "workflowrequest" ? ["memberKey"]
    : normalized === "variable" ? parameter ? ["variableKey", "declaringScopeNodeId"] : ["referenceKey", "declaringScopeId"]
    : normalized === "activityresult" ? ["producerNodeId", "projectionKey", "producerScopeId", "isOptional"] : [];
  if (Object.keys(fields).some(key => !allowed.includes(key) && !(parameter && key === "kind")))
    refuse("unsupported-reference-metadata", path, "This reference carries metadata without a proven promotion rewrite.");
  if (normalized === "input" || normalized === "workflowrequest") {
    const key = text(fields[parameter || normalized === "workflowrequest" ? "memberKey" : "referenceKey"]);
    if (!key) refuse("malformed-input", path, "This input reference has no stable declaration identity.");
    return { kind: "input", key, authored: fields };
  }
  if (normalized === "variable") {
    const key = parameter ? text(fields.variableKey) : text(typeof value === "string" ? value : fields.referenceKey);
    if (!key) refuse("malformed-variable", path, "This variable reference has no stable declaration identity.");
    return { kind: "variable", key, scope: text(fields[parameter ? "declaringScopeNodeId" : "declaringScopeId"]), authored: fields };
  }
  if (normalized === "activityresult") {
    const key = text(fields.producerNodeId), projection = text(fields.projectionKey);
    if (!key || !projection || fields.isOptional !== undefined && fields.isOptional !== false) refuse("unsupported-result", path, "Promotion requires an explicit, non-optional result projection.");
    return { kind: "result", key, projection, scope: text(fields.producerScopeId), authored: fields };
  }
  refuse("unsupported-reference", path, "This reference or expression form needs a preservation proof before promotion.");
}

/** The callback receives the actual mutable slot in a detached candidate, not an arbitrary JSON rewrite. */
export function rewritePromotionArgument(argument: unknown, path: string, support: PromotionExpressionSupport | undefined,
  rewrite: (reference: PromotionReference, path: string, parameter: boolean) => { kind: string; value: unknown } | null) {
  if (!record(argument) || typeof argument.referenceKey !== "string" || !record(argument.value) || typeof argument.value.expressionType !== "string")
    refuse("malformed-argument", path, "Repair this argument before promoting its containing block.");
  const binding = argument.value;
  const syntax = binding.expressionType as string;
  const rewriteChecked = (reference: PromotionReference, at: string, parameter: boolean) => {
    const next = rewrite(reference, at, parameter);
    if (next && (argument.evaluatorType != null || argument.autoEvaluate === false))
      refuse("custom-evaluation", at, "Keep this custom or deferred evaluator in its current scope.");
    return next;
  };
  if (["javascript", "liquid", "python", "csharp"].includes(syntax.toLowerCase()) ||
    record(binding.value) && typeof binding.value.language === "string") {
    const expression = binding.value;
    if (!record(expression) || typeof expression.language !== "string" || typeof expression.source !== "string"
      || !record(expression.parameters) || !record(expression.resultType) || !record(expression.options)
      || expression.capabilityProfile !== "binding-pure-v1" || !support?.allows(expression))
      refuse("opaque-expression", path, "This expression has no language-owned proof of closed explicit parameter reads. Keep the block embedded.");
    for (const [key, value] of Object.entries(expression.parameters)) {
      const at = `${path}.value.value.parameters.${key}`;
      if (!record(value) || typeof value.kind !== "string") refuse("malformed-parameter", at, "Repair this explicit expression parameter before promotion.");
      const reference = readPromotionReference(value.kind, value, at, true);
      if (!reference) continue;
      const next = rewriteChecked(reference, at, true);
      if (next) expression.parameters[key] = next.value;
    }
    return;
  }
  const reference = readPromotionReference(syntax, binding.value, path);
  if (!reference) return;
  const next = rewriteChecked(reference, path, false);
  if (next) argument.value = { ...binding, expressionType: next.kind, value: next.value };
}

export function promotionInputRead(key: string, parameter: boolean) {
  return parameter ? { kind: "WorkflowRequest", value: { kind: "WorkflowRequest", memberKey: key } }
    : { kind: "Input", value: { referenceKey: key } };
}

export function promotionVariableRead(key: string, scope: string, parameter: boolean) {
  return parameter ? { kind: "Variable", value: { kind: "Variable", declaringScopeNodeId: scope, variableKey: key } }
    : { kind: "Variable", value: { referenceKey: key, declaringScopeId: scope } };
}

export function promotionResultRead(nodeId: string, projection: string, parameter: boolean, producerScopeId?: string) {
  return { kind: "ActivityResult", value: { ...(parameter ? { kind: "ActivityResult" } : {}), producerNodeId: nodeId, projectionKey: projection, ...(producerScopeId ? { producerScopeId } : {}) } };
}
