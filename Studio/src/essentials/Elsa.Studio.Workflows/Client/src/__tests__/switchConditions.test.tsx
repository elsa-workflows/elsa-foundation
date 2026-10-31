import React, { useState } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { CaseConditionEditor } from "../workflow-editor/CaseConditionEditor";
import { builtInPropertyEditors } from "../../../../../essentials/Elsa.Studio.Hosting/Client/src/app/propertyEditors";
import { afterEach, describe, expect, it } from "vitest";
import { addCollectionItem, canUpgradeSwitch, caseConditionInputKey, collectionSlotItems, getCollectionSlots, isPredicateOnlySwitch, moveCollectionItem, setCollectionCondition, upgradeSwitch } from "../collectionSlots";
import { createActivityNode, getChildSlots, resolveScope, resolveScopeFrames, replaceSlotActivities, planSlotNavigation } from "../workflowAdapter";
import { canonicalizeStateForWire, expandStateFromWire } from "../activityInputWire";
import { collectionActivity, leafNode } from "./fixtures";

function conditionCatalog() {
  const catalog = structuredClone(collectionActivity("switch"));
  catalog.activityVersionId = "switch-4.1";
  const facet = catalog.designFacets![0] as { schemaVersion: string; payload: { slots: Record<string, unknown>[] } };
  facet.schemaVersion = "2.0.0";
  Object.assign(facet.payload.slots[0], { itemIdProperty: "caseId", conditionProperty: "condition" });
  return catalog;
}
const catalog = conditionCatalog();
const empty = () => createActivityNode(catalog, "switch");
const collection = getCollectionSlots(empty(), catalog)[0];

describe("Switch case conditions", () => {
  it("seeds stable identities and false Boolean predicates independently from labels", () => {
    const node = addCollectionItem(addCollectionItem(empty(), collection), collection);
    const cases = collectionSlotItems(node, collection)!;
    expect(cases[0].caseId).not.toBe(cases[1].caseId);
    for (const item of cases) expect(item.condition).toEqual({ referenceKey: caseConditionInputKey(item.caseId as string), value: { expressionType: "Literal", value: false } });
    expect(cases.map(item => item.match)).toEqual(["Case 1", "Case 2"]);
  });

  it("retains an unbound predicate when cleared, preserving branch and unknown fields", () => {
    let node = addCollectionItem(empty(), collection);
    const item = collectionSlotItems(node, collection)![0];
    item.activity = leafNode("branch"); item.annotation = { future: true };
    const id = item.caseId as string;
    node = setCollectionCondition(node, collection, id, { value: { expressionType: "JavaScript", value: "true" }, conversion: { mode: "Strict" } });
    const cleared = setCollectionCondition(node, collection, id, undefined);
    expect(collectionSlotItems(cleared, collection)![0]).toEqual({ ...item, condition: { referenceKey: caseConditionInputKey(id) } });
    expect(setCollectionCondition(cleared, collection, "deleted", {})).toBe(cleared);
  });

  it("keeps the same branch address on reorder and rename and refuses deleted stale addresses", () => {
    let node = addCollectionItem(addCollectionItem(empty(), collection), collection);
    const original = getChildSlots(node, catalog)[0];
    node = moveCollectionItem(node, collection, original.collectionItemId!, 1);
    const current = getChildSlots(node, catalog)[1];
    expect(current.id).toBe(original.id);
    (node.structure!.payload.cases as Record<string, unknown>[])[1].match = "Approved";
    const frame = { kind: "root" as const, ownerNodeId: node.nodeId, slotId: original.id, label: "Switch / Case 1" };
    expect(resolveScope(node, [frame], catalog)?.slot.label).toBe("Approved");
    expect(resolveScopeFrames(node, [frame], catalog)[0].label).toBe("Switch / Approved");
    const updated = replaceSlotActivities(node, original, [leafNode("target")]);
    expect(getChildSlots(updated, catalog)[1].activities[0].nodeId).toBe("target");
    const removed = { ...updated, structure: { ...updated.structure!, payload: { ...updated.structure!.payload, cases: [(updated.structure!.payload.cases as unknown[])[0]] } } };
    expect(resolveScope(removed, [frame], catalog)).toBeNull();
    expect(replaceSlotActivities(removed, original, [leafNode("wrong")])).toBe(removed);
  });

  it("upgrades in one immutable mutation without converting existing matches or changing descendants", () => {
    const legacy = collectionActivity("switch");
    const node = createActivityNode(legacy, "old");
    node.inputs = [{ referenceKey: "Value", value: { expressionType: "Literal", value: "Approved" } }];
    const branch = leafNode("existing");
    node.structure!.payload = { cases: [{ match: "Approved", activity: branch, annotation: 7 }], default: leafNode("default"), unknown: true };
    const upgraded = upgradeSwitch(node, catalog);
    expect(upgraded.inputs).toBe(node.inputs);
    expect(upgraded.activityVersionId).toBe(catalog.activityVersionId);
    expect(upgraded.structure!.payload).toMatchObject(node.structure!.payload);
    expect((upgraded.structure!.payload.cases as Record<string, unknown>[])[0]).toMatchObject({ activity: branch, annotation: 7, caseId: expect.any(String) });
    expect((upgraded.structure!.payload.cases as Record<string, unknown>[])[0].condition).toBeUndefined();
    expect(node.structure!.schemaVersion).toBe("1.0.0");
    const roundtrip = expandStateFromWire(canonicalizeStateForWire({ rootActivity: upgraded })).rootActivity!;
    expect(roundtrip.structure).toEqual(upgraded.structure);
  });

  it.each([null, [], "opaque"])("declines malformed v1/v2 payload %j without crashing the inspector", payload => {
    const old = createActivityNode(collectionActivity("switch"), "old");
    old.structure!.payload = payload as unknown as Record<string, unknown>;
    expect(canUpgradeSwitch(old)).toBe(false);
    const current = empty(); current.structure!.payload = payload as unknown as Record<string, unknown>;
    expect(isPredicateOnlySwitch(current)).toBe(false);
  });

  it("keeps explicit root case navigation and descendant breadcrumbs stable after reorder and rename", () => {
    let node = addCollectionItem(addCollectionItem(empty(), collection), collection);
    const slot = getChildSlots(node, catalog)[0];
    const plan = planSlotNavigation([], node, node.nodeId, slot, "Switch / Case 1", catalog)!;
    expect(plan.frames).toHaveLength(1);
    node = moveCollectionItem(node, collection, slot.collectionItemId!, 1);
    expect(resolveScope(node, plan.frames, catalog)!.slot.id).toBe(slot.id);
    const branch = { nodeId: "branch", activityVersionId: "sequence", inputs: [], outputs: [], structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities: [] } } };
    node = replaceSlotActivities(node, slot, [branch]);
    const versions = [catalog, { ...catalog, activityVersionId: "sequence", designFacets: [{ kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { mode: "sequence", supportsScopedVariables: true, slots: [{ name: "Activities", property: "activities", displayName: "Activities", cardinality: "many" }], initialPayload: { activities: [] } } }] }];
    const currentSlot = getChildSlots(node, versions).find(item => item.id === slot.id)!;
    const childPlan = planSlotNavigation([], node, node.nodeId, currentSlot, "Switch / stale", versions)!;
    (node.structure!.payload.cases as Record<string, unknown>[])[1].match = "Approved";
    expect(resolveScopeFrames(node, childPlan.frames, versions).at(-1)!.label).toBe("Switch / Approved");
  });

  it("refuses activation-only secret upgrades and preserves malformed future cases", () => {
    const node = createActivityNode(collectionActivity("switch"), "old");
    node.inputs = [{ referenceKey: "Value", value: { expressionType: "Secret", value: { secretId: "secret" } } }];
    expect(canUpgradeSwitch(node)).toBe(false);
    expect(upgradeSwitch(node, catalog)).toBe(node);
    node.inputs = []; node.structure!.payload.cases = [{ match: "ok", caseId: "future" }];
    expect(upgradeSwitch(node, catalog)).toBe(node);
  });
});

let dispose: (() => void) | undefined;
afterEach(() => { dispose?.(); dispose = undefined; });

it("uses the standard syntax selector and commits only the case argument", () => {
  const initial = addCollectionItem(empty(), collection);
  const item = collectionSlotItems(initial, collection)![0];
  const key = caseConditionInputKey(item.caseId as string);
  const js = { language: "JavaScript", source: "args.approved", resultType: { alias: "Boolean" }, parameters: {}, options: {}, capabilityProfile: "Default" };
  let current = setCollectionCondition(initial, collection, item.caseId as string, { value: { expressionType: "JavaScript", value: js } });
  (collectionSlotItems(current, collection)![0].condition as Record<string, unknown>).referenceKey = "foreign-key";
  function Harness() {
    const [activity, setActivity] = useState(current);
    return <CaseConditionEditor activity={activity} collection={collection} item={collectionSlotItems(activity, collection)![0]}
      editors={builtInPropertyEditors} expressionEditors={[{ id: "test-js", supports: context => context.syntax === "JavaScript",
        surfaces: { inline: props => <textarea aria-label="Case expression" value={String(props.value)}
          onChange={event => props.onChange(event.target.value)} /> } }]}
      expressionDescriptors={[{ type: "Literal", editingMode: "literal" }, { type: "JavaScript", editingMode: "text" }, { type: "Secret", editingMode: "reference" }]}
      expressionDescriptorStatus="ready" descriptorStatus="ready" visibleVariables={[]} scopeStatus="ready"
      onChange={next => { current = next; setActivity(next); }} />;
  }
  const element = document.createElement("div"); document.body.append(element);
  const root = createRoot(element);
  dispose = () => { flushSync(() => root.unmount()); element.remove(); };
  flushSync(() => root.render(<Harness />));
  expect(element.textContent).toContain("Condition");
  expect(element.querySelector(".wf-syntax-picker-trigger")?.getAttribute("aria-label")).toContain("JavaScript");
  const textarea = element.querySelector("textarea")!;
  expect(textarea.value).toBe("args.approved");
  flushSync(() => {
    Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value")!.set!.call(textarea, "args.approved === true");
    textarea.dispatchEvent(new Event("input", { bubbles: true }));
  });
  expect(collectionSlotItems(current, collection)![0].condition).toMatchObject({ referenceKey: key,
    value: { expressionType: "JavaScript", value: { source: "args.approved === true" } } });
  expect(current.inputs).toEqual([]);
  expect(current[key]).toBeUndefined();
});


it("discards a delayed expression edit after its case editor unmounts", () => {
  let current = addCollectionItem(empty(), collection);
  const item = collectionSlotItems(current, collection)![0];
  current = setCollectionCondition(current, collection, item.caseId as string, { value: { expressionType: "JavaScript", value: "true" } });
  let lateEdit: ((value: unknown) => void) | undefined;
  const element = document.createElement("div"); document.body.append(element);
  const root = createRoot(element);
  flushSync(() => root.render(<CaseConditionEditor activity={current} collection={collection} item={collectionSlotItems(current, collection)![0]}
    editors={[]} expressionEditors={[{ id: "delayed", supports: () => true, surfaces: { inline: props => {
      lateEdit = props.onChange;
      return <span>Pending editor</span>;
    } } }]}
    expressionDescriptors={[{ type: "JavaScript", editingMode: "text" }]} expressionDescriptorStatus="ready" descriptorStatus="ready"
    visibleVariables={[]} scopeStatus="ready" onChange={next => { current = next; }} />));
  expect(lateEdit).toBeDefined();
  flushSync(() => root.unmount()); element.remove();
  const afterDelete = { ...current, structure: { ...current.structure!, payload: { ...current.structure!.payload, cases: [] } } };
  current = afterDelete;
  lateEdit!("false");
  expect(current).toBe(afterDelete);
});
