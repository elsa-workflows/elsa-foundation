import type { StudioExpressionEditorSnapshot, StudioExpressionSourceInsertion } from "@elsa-workflows/studio-sdk";
import { readPortableExpressionDefinition } from "./activityProperties";
import { createPortableExpressionDefinition } from "./workflowInputArgumentBindings";
import { readActivityDataParameterSource, sameParameterSource, type ActivityDataContextResult, type ActivityDataSourceFacts } from "./api/activityDataSelection";

type HistoryValue = { type: "activity-expression-v1"; representation: "raw" } |
  { type: "activity-expression-v1"; representation: "portable"; definition: Record<string, unknown> };
type Refusal = { supported: false; reason: string };
const refuse = (reason: string): Refusal => ({ supported: false, reason });
const headers = ["language", "source", "resultType", "parameters", "options", "capabilityProfile"];

/** Non-text owner state travels through the editor's opaque, immutable history payload. */
export function expressionEditingState(value: unknown, syntax: string): { supported: true; historyValue: HistoryValue } | Refusal {
  if (typeof value === "string") return { supported: true, historyValue: { type: "activity-expression-v1", representation: "raw" } };
  const definition = readPortableExpressionDefinition(value, syntax);
  if (!definition || definition.capabilityProfile !== "binding-pure-v1" || Object.keys(definition).some(key =>
    headers.some(header => header.toLowerCase() === key.toLowerCase() && header !== key)))
    return refuse("This expression definition is preserved. Insert data supports source text and the binding-pure-v1 portable profile.");
  const { source: _source, ...nonText } = definition;
  return { supported: true, historyValue: { type: "activity-expression-v1", representation: "portable", definition: nonText } };
}

export function expressionValueFromHistory(source: string, payload: unknown, syntax: string): { value: unknown } | null {
  if (!payload || typeof payload !== "object") return null;
  const value = payload as Partial<HistoryValue>;
  if (value.type !== "activity-expression-v1") return null;
  if (value.representation === "raw") return { value: source };
  if (value.representation !== "portable" || !value.definition || typeof value.definition !== "object") return null;
  const definition = { ...value.definition, source };
  return readPortableExpressionDefinition(definition, syntax) ? { value: definition } : null;
}

function collectionKind(value: unknown): string | null {
  // TypeReference's omitted collectionKind means Single; this is its wire default, not a CLR inference.
  if (value == null) return "single";
  return typeof value === "string" && ["single", "array", "list", "hashset", "dictionary"].includes(value.toLowerCase()) ? value.toLowerCase() : null;
}

/** Plans only: the caller applies source, owner payload and selection with the current editor controller. */
export function planActivityExpressionInsertion({ value, syntax, snapshot, result, source, adapter }: {
  value: unknown; syntax: string; snapshot: StudioExpressionEditorSnapshot; result: ActivityDataContextResult;
  source: ActivityDataSourceFacts; adapter: StudioExpressionSourceInsertion;
}) {
  if ((result.state !== "supported" && result.state !== "empty") || result.expressionParametersEnabled !== true || result.expressionType !== syntax ||
      !result.target || result.target.isCredential === true || !source.parameterSource || !source.parameterPath ||
      source.shape?.isSensitive === true || source.shape?.isCredential === true ||
      !result.sources.some(item => item.id === source.id && sameParameterSource(item.parameterSource, source.parameterSource) &&
        JSON.stringify(item.parameterPath) === JSON.stringify(source.parameterPath)))
    return refuse("Choose a current, authorized expression source again.");
  const parameter = readActivityDataParameterSource(source.parameterSource);
  const target = result.target.type;
  if (!parameter || !target?.alias || collectionKind(target.collectionKind) === null)
    return refuse("This input does not disclose a supported portable result type.");
  const state = expressionEditingState(value, syntax);
  if (!state.supported) return state;
  const definition = createPortableExpressionDefinition(value, syntax, { alias: target.alias, collectionKind: target.collectionKind ?? "Single" });
  if (!definition || definition.source !== snapshot.source) return refuse("The expression changed. Choose the insertion position again.");
  if (definition.resultType.alias !== target.alias || collectionKind(definition.resultType.collectionKind) !== collectionKind(target.collectionKind))
    return refuse("The portable result type does not match this input. The authored definition is preserved.");
  const candidates = Object.entries(definition.parameters).filter(([, binding]) => sameParameterSource(binding, parameter)).map(([alias]) => alias);
  let nextAlias = "data", suffix = 2;
  while (Object.prototype.hasOwnProperty.call(definition.parameters, nextAlias)) nextAlias = `data${suffix++}`;
  candidates.push(nextAlias);
  let lastReason = "This syntax cannot insert the selected reference at this position.";
  for (const alias of candidates) {
    let rendered: ReturnType<StudioExpressionSourceInsertion["render"]>;
    try { rendered = adapter.render({ alias, memberPath: source.parameterPath, source: snapshot.source, selection: snapshot.selection }); }
    catch { return refuse("This installed editor could not insert the reference. The expression is preserved."); }
    if (!rendered.supported) { lastReason = rendered.reason; continue; }
    if (typeof rendered.insert !== "string") return refuse("This syntax returned unsupported insertion text.");
    const relative = rendered.selection ?? { anchor: rendered.insert.length, head: rendered.insert.length };
    if (![relative.anchor, relative.head].every(position => Number.isInteger(position) && position >= 0 && position <= rendered.insert.length))
      return refuse("This syntax returned an unsupported insertion selection.");
    const nextDefinition = Object.prototype.hasOwnProperty.call(definition.parameters, alias) ? definition :
      { ...definition, parameters: { ...definition.parameters, [alias]: parameter } };
    const history = expressionEditingState(nextDefinition, syntax);
    if (!history.supported) return history;
    const start = Math.min(snapshot.selection.anchor, snapshot.selection.head);
    return { supported: true as const, alias, insert: rendered.insert, historyValue: history.historyValue,
      selection: { anchor: start + relative.anchor, head: start + relative.head } };
  }
  return refuse(lastReason);
}
