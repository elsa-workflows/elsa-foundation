// Run after the locked Studio install: node --experimental-strip-types --test tools/ux-acceptance/test_activity_data.mjs
// Imports the real acceptance predicates; these are semantic controls, not copies of their implementation.
import assert from "node:assert/strict";
import test from "node:test";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { attachActivityDataJson, findActivityDataAdmission, resolvedFeatureClosure } from "../../Studio/tests/acceptance/activityDataJourneySupport.ts";

const clone = value => JSON.parse(JSON.stringify(value));
const expected = { expressionType: "ActivityResultMember", value: { producerNodeId: "producer", projectionKey: "Customer", isOptional: false, memberPath: ["customerId"] } };
const before = { definitionId: "owned", nodeId: "consumer", inputKey: "text", documentRevision: "before",
  snapshot: { rootActivity: { nodeId: "root", structure: { payload: { activities: [{ nodeId: "consumer", inputs: [] }] } } } },
  query: { source: { expressionType: "ActivityResult", value: { producerNodeId: "producer", projectionKey: "Customer" } }, memberPath: ["customerId"] } };
const answer = { request: before, response: { state: "supported", documentRevision: "before", analysis: { sources: [{ binding: expected }] } } };

test("fresh supported admission captures exact pre-edit offer", () => {
  assert.deepEqual(findActivityDataAdmission([answer], before, expected), answer);
});
test("post-edit provenance alone cannot stand in for admission", () => {
  const provenance = clone(answer);
  provenance.request.documentRevision = provenance.response.documentRevision = "after";
  provenance.request.snapshot.rootActivity.structure.payload.activities[0].inputs = [{ referenceKey: "text", value: expected }];
  assert.equal(findActivityDataAdmission([provenance], before, expected), undefined);
});
test("refused, wrong-target, wrong-path and changed-snapshot offers cannot certify selection", () => {
  for (const mutate of [item => { item.response.state = "invalid"; }, item => { item.request.nodeId = "other"; },
    item => { item.request.query.memberPath = ["customer", "Id"]; },
    item => { item.request.snapshot.rootActivity.structure.payload.activities[0].inputs = [{ referenceKey: "text", value: expected }]; }]) {
    const changed = clone(answer); mutate(changed);
    assert.equal(findActivityDataAdmission([changed], before, expected), undefined);
  }
});
const catalog = [
  { id: "WorkflowsPublishingApi", enabled: true, runtimeAvailable: true, dependencies: [{ id: "WorkflowsPublishing", optional: false }] },
  { id: "WorkflowsPublishing", enabled: false, runtimeAvailable: true, dependencies: [] },
  { id: "WorkflowDesignValidations", enabled: true, runtimeAvailable: true, dependencies: [] },
  { id: "JavaScriptJintEngine", enabled: false, runtimeAvailable: true, dependencies: [] }
];
test("live graph closure includes implicit mandatory dependencies", () => {
  assert.deepEqual(resolvedFeatureClosure(catalog), ["WorkflowDesignValidations", "WorkflowsPublishing", "WorkflowsPublishingApi"]);
  const composed = clone(catalog); composed[1].dependencies.push({ id: "JavaScriptJintEngine", optional: false });
  assert.ok(resolvedFeatureClosure(composed).includes("JavaScriptJintEngine"), "An unlisted engine is still in the resolved composition");
});
test("empty, ambiguous and unresolved feature facts fail closed", () => {
  assert.throws(() => resolvedFeatureClosure([]));
  assert.throws(() => resolvedFeatureClosure([...catalog, catalog[0]]));
  assert.throws(() => resolvedFeatureClosure(catalog.filter(item => item.id !== "WorkflowsPublishing")));
  assert.throws(() => resolvedFeatureClosure(catalog.map(item => item.id === "WorkflowsPublishing" ? { ...item, runtimeAvailable: false } : item)));
  assert.throws(() => resolvedFeatureClosure(catalog.map(item => item.id === "WorkflowDesignValidations" ? { ...item, enabled: false } : item)));
});

test("JSON proof attachments are actual files and preserve literal names", async () => {
  const directory = mkdtempSync(join(tmpdir(), "elsa-data-attachment-"));
  try {
    let attached;
    const info = { outputPath: name => join(directory, name), attach: async (name, options) => { attached = { name, ...options }; } };
    const value = { memberPath: ["a.b", ""], futureField: { preserved: true } };
    await attachActivityDataJson(info, "proof.json", value);
    assert.equal(attached.name, "proof.json"); assert.equal(attached.contentType, "application/json");
    assert.equal(attached.path, join(directory, "proof.json")); assert.ok(!("body" in attached));
    assert.deepEqual(JSON.parse(readFileSync(attached.path, "utf8")), value);
  } finally { rmSync(directory, { recursive: true, force: true }); }
});
