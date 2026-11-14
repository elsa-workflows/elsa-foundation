import { describe, expect, it } from "vitest";
import type { ActivityDataBinding, ActivityDataContextResult, ActivityDataShape, ActivityDataSourceFacts } from "../api/activityDataSelection";
import { groupActivityOutputSources } from "../activityDataSelection";
import { activityDataBreadcrumbs, activityDataMemberBrowser, activityDataMemberDescriptors, activityDataTypeLabel,
  appendActivityDataMember, describeActivityDataMember, describeActivityDataMemberFinding, groupActivityDataSources,
  isActivityDataMemberPath, parentActivityDataMemberPath, readActivityDataMemberReference } from "../activityDataPickerModel";

const labels = { describeActivity: () => ({ label: "Load customer", outputLabels: {}, sharesLabel: false }),
  describeScope: (scope: string) => scope === "loop" ? "Each order" : null };
function shape(overrides: Partial<ActivityDataShape> = {}): ActivityDataShape {
  return { state: "known", kind: "object", type: { alias: "Customer", collectionKind: "Single" },
    sourceContractRevision: "c", metadataRevision: "m", profileRevision: "p", isNullable: null, presence: "unknown",
    isSensitive: null, isCredential: null, representation: null, reasonCode: null, members: [], element: null, mapValue: null, ...overrides };
}
function source(id: string, binding: ActivityDataBinding | null, overrides: Partial<ActivityDataSourceFacts> = {}): ActivityDataSourceFacts {
  return { id, displayName: "Customer", binding, reasonCode: null, availability: "definite", type: null,
    activityResultSource: null, shape: shape(), ...overrides };
}
function result(sources: ActivityDataSourceFacts[], overrides: Partial<ActivityDataContextResult> = {}): ActivityDataContextResult {
  return { state: "supported", code: null, documentRevision: "d", baseRevision: "b", memberReadsEnabled: true,
    target: { displayName: "Customer ID", constraint: null }, sources, hasMoreSources: false, ...overrides };
}
const whole = { expressionType: "ActivityResult", value: { producerNodeId: "producer", projectionKey: "$result", isOptional: false } };
function output(nodeId = "producer") {
  return source(`activity:${nodeId}:$result`, { ...whole, value: { ...whole.value, producerNodeId: nodeId } },
    { activityResultSource: { producerNodeId: nodeId, projectionKey: "$result" } });
}
const variable = (key: string, scope: string) => source(`variable:${scope}:${key}`,
  { expressionType: "Variable", value: { referenceKey: key, declaringScopeId: scope } });

describe("data source families", () => {
  it("presents results first, preserving server order, identities and explicit outer-variable choices", () => {
    const input = source("input:stable-key", { expressionType: "WorkflowRequest", value: { memberKey: "StoredName", path: null } });
    const request = source("request:payload", { expressionType: "WorkflowRequest", value: { memberKey: "payload" } });
    const near = variable("near-key", "loop"), outer = variable("outer-key", "workflow");
    const sources = [input, near, outer, request, output("second"), output("first")];
    const before = structuredClone(sources);
    const families = groupActivityDataSources(result(sources), labels);
    expect(families.map(group => group.family)).toEqual(["results", "inputs", "request", "variables"]);
    expect(families[0]!.options.map(option => option.source.id)).toEqual(["activity:second:$result", "activity:first:$result"]);
    expect(families[0]!.options.every(option => option.disambiguation)).toBe(true);
    const variables = families[3]!.options;
    expect(variables.map(option => [option.source.id, option.shadowed, option.disambiguation])).toEqual([
      [near.id, false, "Each order"], [outer.id, true, "Workflow"]
    ]);
    expect(variables[1]!.source.binding).toBe(outer.binding);
    expect(variables[1]!.unavailableReason).toBeNull();
    expect(sources).toEqual(before);
    expect(groupActivityOutputSources(sources, labels.describeActivity)).toHaveLength(2);
  });

  it("disambiguates duplicate names within a scope without treating them as outer shadows", () => {
    const options = groupActivityDataSources(result([variable("a", "loop"), variable("b", "loop")]), labels)[0]!.options;
    expect(options.map(option => option.disambiguation)).toEqual(["Each order · a", "Each order · b"]);
    expect(options.every(option => !option.shadowed)).toBe(true);
  });

  it("retains scope identities when distinct containers have the same friendly scope label", () => {
    const sameLabels = { ...labels, describeScope: () => "Orders" };
    const options = groupActivityDataSources(result([variable("same", "inner"), variable("same", "outer")]), sameLabels)[0]!.options;
    expect(options.map(option => option.disambiguation)).toEqual(["Orders (inner)", "Orders (outer)"]);
    const binding = { expressionType: "VariableMember", value: { referenceKey: "same", declaringScopeId: "outer", memberPath: ["id"] } };
    expect(describeActivityDataMember(binding, options.map(option => option.source), sameLabels)?.text).toContain("Orders");
    expect(describeActivityDataMember(binding, options.map(option => option.source), sameLabels)?.text).toContain("outer");
  });

  it("keeps future, unavailable, conditional and restricted sources disabled, while excluding protected metadata", () => {
    const sources = [output(), source("future", { expressionType: "FutureSource", value: {} }),
      { ...output("loop"), binding: null, reasonCode: "EarlierProducerRun" }, { ...output("conditional"), binding: null, availability: "conditional", reasonCode: "ConditionalOccurrence" },
      { ...output("secret"), shape: shape({ isSensitive: true }) }];
    const groups = groupActivityDataSources(result(sources, { target: { displayName: "ID", constraint: {
      owner: "test", message: "Only literals", allowedExpressionTypes: ["Literal"] } } }), labels);
    expect(groups.flatMap(group => group.options).map(option => option.source.id)).not.toContain("activity:secret:$result");
    expect(groups.flatMap(group => group.options).every(option => option.unavailableReason)).toBe(true);
    expect(groupActivityDataSources(result(sources, { state: "invalid" }), labels)).toEqual([]);
    expect(groupActivityDataSources(result(sources), { ...labels, readOnly: true })).toEqual([]);
    expect(groupActivityDataSources(result(sources, { target: { displayName: "Secret", constraint: null, isCredential: true } }), labels)).toEqual([]);
  });
});

describe("server-admitted optional result reads", () => {
  const offered = (member: boolean) => ({ ...output(), availability: "conditional", reasonCode: "ConditionalOccurrence",
    binding: { expressionType: member ? "ActivityResultMember" : "ActivityResult",
      value: { ...whole.value, isOptional: true, ...(member ? { memberPath: ["customer"] } : {}) } },
    shape: shape({ state: "unknown" }) });
  it.each([false, true])("keeps an admitted optional result selectable (member:%s)", member => {
    const candidate = offered(member);
    const option = groupActivityDataSources(result([candidate]), labels)[0]!.options[0]!;
    expect(option.unavailableReason).toBeNull();
    expect(option.source.binding).toBe(candidate.binding);
    expect(option.source).toMatchObject({ availability: "conditional", reasonCode: "ConditionalOccurrence" });
  });
  it.each([false, true])("keeps admitted optional member browsing available (member:%s)", member => {
    const candidate = offered(member);
    const model = activityDataMemberBrowser(result([candidate]), candidate.id, member ? ["customer"] : [], labels);
    expect(model).toMatchObject({ reason: null, allowLiteral: true });
    expect(activityDataMemberBrowser(result([candidate], { memberReadsEnabled: false }), candidate.id, member ? ["customer"] : [], labels))
      .toMatchObject({ allowLiteral: false, members: [] });
  });
});

describe("literal member paths", () => {
  it("keeps exact names, supports empty keys and separates dotted names from nested paths", () => {
    const path = ["billing.address", "", 'a/b"c'];
    const next = appendActivityDataMember(path, "CustomerId")!;
    expect(next).toEqual([...path, "CustomerId"]);
    expect(path).toEqual(["billing.address", "", 'a/b"c']);
    expect(parentActivityDataMemberPath(next)).toEqual(path);
    expect(parentActivityDataMemberPath([])).toEqual([]);
    expect(activityDataBreadcrumbs(path).map(crumb => crumb.label)).toEqual(["Whole value", '"billing.address"', '""', '"a/b\\"c"']);
    expect(activityDataBreadcrumbs(["a.b"])[1]!.key).not.toBe(activityDataBreadcrumbs(["a", "b"])[2]!.key);
  });
  it("enforces both bounds in UTF-16 units without truncating or normalizing a segment", () => {
    expect(isActivityDataMemberPath(Array(64).fill(""))).toBe(true);
    expect(appendActivityDataMember(Array(64).fill(""), "")).toBeNull();
    expect(isActivityDataMemberPath(["😀".repeat(2048)])).toBe(true);
    expect(appendActivityDataMember(["😀".repeat(2048)], "a")).toBeNull();
    for (const invalid of [[], [null], [1], Array(1), Array(65).fill(""), ["x".repeat(4097)]])
      expect(isActivityDataMemberPath(invalid)).toBe(false);
  });
});

describe("member reference presentation", () => {
  it.each([
    ["ActivityResultMember", { ...whole.value }], ["VariableMember", { referenceKey: "customer", declaringScopeId: "loop" }],
    ["WorkflowRequestMember", { memberKey: "StoredName" }], ["InputMember", { referenceKey: "stable-key" }]
  ])("reads %s literal paths without changing the canonical payload", (expressionType, identity) => {
    const binding = { expressionType, value: { ...identity, memberPath: ["a.b", ""] } };
    const copy = structuredClone(binding);
    expect(readActivityDataMemberReference(binding)).toMatchObject({ kind: expressionType, ...identity, memberPath: ["a.b", ""] });
    expect(binding).toEqual(copy);
    expect(activityDataMemberDescriptors.find(descriptor => descriptor.type === expressionType)?.editingMode).toBe("reference");
  });
  it("rejects malformed, scoped-result, empty and unsupported member references", () => {
    for (const binding of [{ expressionType: "ActivityResultMember", value: { ...whole.value, producerScopeId: "scope", memberPath: ["id"] } },
      { expressionType: "InputMember", value: { referenceKey: "key", memberPath: [] } },
      { expressionType: "VariableMember", value: { referenceKey: "key", memberPath: ["id"] } },
      { expressionType: "WorkflowRequestMember", value: { memberKey: "key", path: "legacy.path", memberPath: ["id"] } },
      { expressionType: "FutureSource", value: { memberPath: ["id"] } }]) expect(readActivityDataMemberReference(binding)).toBeNull();
  });
  it.each([
    { expressionType: "ActivityResultMember", value: { ...whole.value } },
    { expressionType: "VariableMember", value: { referenceKey: "customer", declaringScopeId: "loop" } },
    { expressionType: "WorkflowRequestMember", value: { memberKey: "request" } },
    { expressionType: "InputMember", value: { referenceKey: "input" } }
  ].flatMap(binding => ["legacy", null].map(path => ({ ...binding, value: { ...binding.value, memberPath: ["id"], path } }))))("rejects own path:$value.path without altering the stored $expressionType payload", binding => {
    const before = structuredClone(binding);
    expect(readActivityDataMemberReference(binding)).toBeNull();
    expect(describeActivityDataMember(binding, [], labels)).toBeNull();
    expect(binding).toEqual(before);
  });
  it("shows literal provenance and only the actual focused result type, never guesses target compatibility", () => {
    const binding = { expressionType: "ActivityResultMember", value: { ...whole.value, memberPath: ["billing.address", ""] } };
    const leaf = shape({ kind: "scalar", type: { alias: "String", collectionKind: "Single" } });
    expect(describeActivityDataMember(binding, [output()], labels, leaf)).toMatchObject({
      text: 'Load customer › Whole output › "billing.address" › ""', typeLabel: "String", typeKnowledge: "known"
    });
    expect(describeActivityDataMember(binding, [output()], labels)?.typeLabel).toBeNull();
    const variableBinding = { expressionType: "VariableMember", value: { referenceKey: "key", declaringScopeId: "loop", memberPath: ["id"] } };
    expect(describeActivityDataMember(variableBinding, [], labels, leaf)).toMatchObject({ typeLabel: null, typeKnowledge: "deferred", sourceLabel: "key (Each order)" });
    expect(activityDataTypeLabel({ alias: "String", collectionKind: "Dictionary" })).toBe("Dictionary<String, String>");
  });
});

describe("member browsing facts", () => {
  it.each(["unknown", "truncated", "unavailable"] as const)("allows exact literal entry for %s object metadata", state => {
    const candidate = { ...output(), shape: shape({ state, isSensitive: null }) };
    expect(activityDataMemberBrowser(result([candidate]), candidate.id, [], labels)).toMatchObject({ state, allowLiteral: true, typeKnowledge: "deferred" });
  });
  it("shows declared literal keys, excludes protected members and never invents collection or map steps", () => {
    const candidate = { ...output(), shape: shape({ members: [
      { serializedName: "a.b", displayName: "Address", shape: shape() }, { serializedName: "", displayName: "Empty", shape: shape() },
      { serializedName: "secret", displayName: "Secret", shape: shape({ isCredential: true }) }
    ] }) };
    const model = activityDataMemberBrowser(result([candidate]), candidate.id, [], labels);
    expect(model.members.map(member => member.memberPath)).toEqual([["a.b"], [""]]);
    expect(model.allowLiteral).toBe(false);
    const map = { ...candidate, shape: shape({ kind: "map", mapValue: shape() }) };
    expect(activityDataMemberBrowser(result([map]), map.id, [], labels)).toMatchObject({ allowLiteral: true, members: [] });
    const collection = { ...candidate, shape: shape({ kind: "collection", element: shape() }) };
    expect(activityDataMemberBrowser(result([collection]), collection.id, [], labels)).toMatchObject({ allowLiteral: false, members: [] });
  });
  it("withholds disabled, stale-path, missing and ambiguous sources instead of treating old root metadata as a member", () => {
    const candidate = { ...output(), shape: shape({ state: "unknown", members: [
      { serializedName: "id", displayName: "ID", shape: shape({ kind: "scalar" }) }
    ] }) };
    for (const context of [result([candidate], { memberReadsEnabled: false }), result([candidate], { state: "denied" }), result([]), result([candidate, candidate])])
      expect(activityDataMemberBrowser(context, candidate.id, [], labels)).toMatchObject({ allowLiteral: false, members: [], typeLabel: null });
    expect(activityDataMemberBrowser(result([candidate]), candidate.id, ["id"], labels)).toMatchObject({ members: [], allowLiteral: false });
    expect(describeActivityDataMemberFinding({ type: "ActivityData/SourceRestricted" })).toContain("does not accept");
  });
  it("uses the current focused member packet for a nested path, rather than guessing from a root shape", () => {
    const path = ["literal.dot", ""];
    const focused = { ...output(), binding: { expressionType: "ActivityResultMember", value: { ...whole.value, memberPath: path } },
      shape: shape({ members: [{ serializedName: "id", displayName: "ID", shape: shape({ kind: "scalar" }) }] }) };
    expect(activityDataMemberBrowser(result([focused]), focused.id, path, labels).members[0]!.memberPath).toEqual([...path, "id"]);
    expect(activityDataMemberBrowser(result([focused]), focused.id, [], labels)).toMatchObject({ members: [], allowLiteral: false });
  });
  it("keeps admitted scoped-result and legacy-request whole reads, but never offers their member form", () => {
    const scoped = { ...output(), binding: { expressionType: "ActivityResult", value: { ...whole.value, producerScopeId: "loop" } } };
    const request = source("input:key", { expressionType: "WorkflowRequest", value: { memberKey: "request", path: "legacy.path" } });
    const context = result([scoped, request]);
    expect(groupActivityDataSources(context, labels).flatMap(group => group.options).every(option => !option.unavailableReason)).toBe(true);
    for (const candidate of [scoped, request]) expect(activityDataMemberBrowser(context, candidate.id, [], labels)).toMatchObject({ members: [], allowLiteral: false });
  });
  it.each(["known", "unknown", "truncated"] as const)("never infers an array index from %s collection metadata", state => {
    const candidate = { ...output(), shape: shape({ kind: "collection", state, element: shape() }) };
    expect(activityDataMemberBrowser(result([candidate]), candidate.id, [], labels)).toMatchObject({ members: [], allowLiteral: false });
  });

});
