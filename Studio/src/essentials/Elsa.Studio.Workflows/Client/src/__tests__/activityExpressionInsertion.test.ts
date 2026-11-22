import { expect, it } from "vitest";
import { expressionEditingState, expressionValueFromHistory, planActivityExpressionInsertion } from "../activityExpressionInsertion";
import type { ActivityDataContextResult, ActivityDataSourceFacts } from "../api/activityDataSelection";
import type { StudioExpressionSourceInsertion } from "@elsa-workflows/studio-sdk";

const parameter = { kind: "ActivityResult" as const, producerNodeId: "fetch", projectionKey: "$result", isOptional: false };
const source: ActivityDataSourceFacts = { id: "activity:fetch:$result", displayName: "Untrusted label", binding: null, reasonCode: "EarlierProducerRun", availability: "definite",
  activityResultSource: parameter, parameterSource: parameter, parameterPath: ["a.b", ""], type: null };
const result: ActivityDataContextResult = { state: "supported", code: null, documentRevision: "revision", baseRevision: "base", sources: [source], hasMoreSources: false,
  expressionParametersEnabled: true, expressionType: "JavaScript", memberReadsEnabled: false,
  target: { displayName: "Target", constraint: null, type: { alias: "String", collectionKind: "Single" } } };
// Syntax is supplied by a contribution; the host planner only uses its returned bytes and relative selection.
const adapter: StudioExpressionSourceInsertion = { render: ({ alias }) => ({ supported: true, insert: `REFERENCE(${alias})`, selection: { anchor: 3, head: 3 } }) };
const snapshot = { source: "left+right", selection: { anchor: 5, head: 10 } };
function plan(value: unknown = snapshot.source, packet = result, choice = source, renderer = adapter) {
  return planActivityExpressionInsertion({ value, syntax: "JavaScript", snapshot, result: packet, source: choice, adapter: renderer });
}
function definition(parameters: Record<string, unknown> = {}) {
  return { language: "JavaScript", source: snapshot.source, resultType: { alias: "String", collectionKind: "Single" }, parameters,
    options: { custom: true }, capabilityProfile: "binding-pure-v1", metadata: { manual: "keep" }, extra: { untouched: true } };
}
it("promotes source using the authoritative alias/collection shape and never a source label", () => {
  const planned = plan(); expect(planned.supported).toBe(true);
  if (!planned.supported) return;
  expect(planned.alias).toBe("data"); expect(planned.selection).toEqual({ anchor: 8, head: 8 });
  const next = expressionValueFromHistory("left+" + planned.insert, planned.historyValue, "JavaScript")!.value as ReturnType<typeof definition>;
  expect(next).toMatchObject({ source: "left+REFERENCE(data)", language: "JavaScript", resultType: result.target!.type, parameters: { data: parameter }, options: {}, capabilityProfile: "binding-pure-v1" });
});
it("reuses only a complete identical binding and preserves manual carrier fields", () => {
  const original = definition({ customer: parameter, manual: { kind: "Literal", value: 2 } });
  const planned = plan(original); expect(planned.supported).toBe(true);
  if (!planned.supported) return;
  expect(planned.alias).toBe("customer");
  const restored = expressionValueFromHistory("changed", planned.historyValue, "JavaScript")!.value;
  expect(restored).toEqual({ ...original, source: "changed" }); expect(original.source).toBe(snapshot.source);
});
it.each([
  { ...parameter, isOptional: true }, { ...parameter, projectionKey: "other" }, { ...parameter, producerNodeId: "other" },
  { ...parameter, extraSemanticField: true }, { kind: "Variable", declaringScopeNodeId: "fetch", variableKey: "$result" }
])("allocates a collision-free alias for a different role/identity (%j)", binding => {
  const planned = plan(definition({ data: binding, data2: { kind: "Literal", value: 42 } }));
  expect(planned.supported).toBe(true); if (planned.supported) expect(planned.alias).toBe("data3");
});
it("preserves raw representation on Undo instead of keeping a newly added parameter", () => {
  const raw = expressionEditingState(snapshot.source, "JavaScript"); expect(raw.supported).toBe(true);
  if (raw.supported) expect(expressionValueFromHistory(snapshot.source, raw.historyValue, "JavaScript")).toEqual({ value: snapshot.source });
});
it.each(["unsupported-profile", "ambiguous-header", "different-result", "stale-source"])("preserves unsupported manual content: %s", scenario => {
  const value = definition();
  if (scenario === "unsupported-profile") value.capabilityProfile = "custom-profile";
  if (scenario === "ambiguous-header") Object.assign(value, { Source: "other source" });
  if (scenario === "different-result") value.resultType.alias = "Other";
  if (scenario === "stale-source") value.source = "newer";
  expect(plan(value).supported).toBe(false);
});
it("requires an actual capability/offer and validates adapter-relative selection", () => {
  expect(plan(snapshot.source, { ...result, expressionParametersEnabled: false }).supported).toBe(false);
  expect(plan(snapshot.source, { ...result, sources: [] }).supported).toBe(false);
  expect(plan(snapshot.source, result, source, { render: () => ({ supported: true, insert: "x", selection: { anchor: 100, head: 100 } }) }).supported).toBe(false);
  expect(plan(snapshot.source, result, source, { render: () => ({ supported: false, reason: "Not here" }) })).toEqual({ supported: false, reason: "Not here" });
  expect(plan(snapshot.source, result, source, { render: () => { throw new Error("broken contribution"); } }).supported).toBe(false);
  expect(plan(snapshot.source, result, source, { render: () => ({ supported: true, insert: undefined! }) }).supported).toBe(false);
});
