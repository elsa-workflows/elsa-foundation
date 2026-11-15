import { describe, expect, it } from "vitest";
import {
  applyActivityDuplicatePlan,
  allocateActivityNodeId,
  matchActivityClipboardShortcut,
  pasteActivitiesIntoDraft,
  planActivityDuplicate,
  snapshotSelectedActivities,
  uniqueCopiedDisplayName
} from "../workflow-editor/duplicateActivities";
import {
  flowchartActivity,
  collectionActivity,
  flowchartNode,
  forEachActivity,
  forEachNode,
  leafNode,
  sequenceActivity,
  sequenceNode,
  writeLine
} from "./fixtures";
import type { ActivityNode, WorkflowDraft } from "../workflowTypes";
import { canonicalizeStateForWire, expandStateFromWire } from "../activityInputWire";
import { collectActivityNodeIds } from "../workflowAdapter";

const catalog = new Map([
  [writeLine.activityVersionId, writeLine],
  [flowchartActivity.activityVersionId, flowchartActivity],
  [forEachActivity.activityVersionId, forEachActivity],
  [sequenceActivity.activityVersionId, sequenceActivity]
]);

function sequentialIds() {
  let index = 0;
  return (prefix: string, usedIds: Set<string>) =>
    allocateActivityNodeId(prefix, usedIds, () => {
      index += 1;
      return `copy${index}`;
    });
}

function draftOf(root: ActivityNode, extras: Partial<WorkflowDraft> = {}): WorkflowDraft {
  return {
    id: "draft-1",
    definitionId: "definition-1",
    state: { rootActivity: root },
    layout: [],
    activityPresentation: [],
    validationErrors: [],
    ...extras
  };
}

function connectedFlowchart(children: ActivityNode[], connections: Array<{ from: string; to: string; id?: string }>): ActivityNode {
  const root = flowchartNode("flow-root", children);
  root.structure!.payload.connections = connections.map((connection, index) => ({
    id: connection.id ?? `flow-${index}`,
    source: { nodeId: connection.from, port: "Done" },
    target: { nodeId: connection.to }
  }));
  root.structure!.payload.startNodeId = children[0]?.nodeId ?? null;
  return root;
}

describe("activity clipboard shortcuts", () => {
  it("matches copy, paste, and duplicate only with Ctrl/Cmd and without Shift or Alt", () => {
    const modifiers = { metaKey: false, ctrlKey: true, altKey: false, shiftKey: false };
    expect(matchActivityClipboardShortcut({ ...modifiers, key: "c" })).toBe("copy");
    expect(matchActivityClipboardShortcut({ ...modifiers, key: "V" })).toBe("paste");
    expect(matchActivityClipboardShortcut({ ...modifiers, key: "d" })).toBe("duplicate");
    expect(matchActivityClipboardShortcut({ ...modifiers, shiftKey: true, key: "d" })).toBeNull();
    expect(matchActivityClipboardShortcut({ ...modifiers, altKey: true, key: "c" })).toBeNull();
    expect(matchActivityClipboardShortcut({ metaKey: false, ctrlKey: false, altKey: false, shiftKey: false, key: "d" })).toBeNull();
  });
});

describe("reference-aware activity copies", () => {
  const result = (kind = "ActivityResult", producerNodeId = "producer") => ({
    expressionType: kind,
    value: { producerNodeId, projectionKey: "$result", ...(kind.endsWith("Member") ? { memberPath: ["a.b", ""] } : {}) }
  });
  const portable = (parameters: Record<string, unknown>, language = "MissingLanguage") => ({
    expressionType: language, value: { language, source: 'producer["a.b"][""]', parameters,
      resultType: { alias: "String", collectionKind: "Single" }, options: { producerNodeId: "producer" },
      capabilityProfile: "binding-pure-v1", metadata: { producerNodeId: "producer" } }
  });
  const argument = (value: unknown, referenceKey = "text") => ({ referenceKey, value, policy: { producerNodeId: "producer" } });
  const variable = (scope: string, kind = "Variable") => ({ expressionType: kind, value: {
    referenceKey: "producer", declaringScopeId: scope, ...(kind.endsWith("Member") ? { memberPath: ["a.b", ""] } : {}) } });
  const propertyCatalog = new Map(catalog).set(writeLine.activityVersionId, { ...writeLine,
    inputs: [{ name: "Text", referenceKey: "text", typeName: "String", isWrapped: true }],
    outputs: [{ name: "Result", referenceKey: "result", typeName: "String" }] });
  function copy(children: ActivityNode[], selected = children.map(node => node.nodeId), lookup = propertyCatalog) {
    const root = sequenceNode("root", children);
    const draft = draftOf(root);
    const before = structuredClone(draft);
    const snapshot = snapshotSelectedActivities({ selectedNodeIds: selected, scopeActivities: children, scopeOwner: root,
      catalog: lookup, layout: [], presentation: [] })!;
    const pasted = pasteActivitiesIntoDraft({ draft, frames: [], snapshot, catalog: lookup, allocateId: sequentialIds() })!;
    expect(draft).toEqual(before);
    expect(snapshot.activities).toEqual(children.filter(node => selected.includes(node.nodeId)));
    const copies = (pasted.draft.state.rootActivity!.structure!.payload.activities as ActivityNode[]).slice(children.length);
    return { ...pasted, copies, snapshot };
  }

  it.each(["ActivityResult", "ActivityResultMember", "portable"])('remaps %s siblings in either selected-tree order', kind => {
    for (const reverse of [false, true]) {
      const producer = leafNode("producer");
      const consumer = leafNode("consumer");
      const binding = kind === "portable" ? portable({ producer: { kind: "ActivityResult", producerNodeId: "producer", projectionKey: "$result" } }) : result(kind);
      consumer.inputs = [argument(binding)];
      const copied = copy(reverse ? [consumer, producer] : [producer, consumer]);
      const copiedConsumer = copied.copies.find(node => node.nodeId === copied.idMap.get("consumer"))!;
      const expected = structuredClone(binding);
      if (kind === "portable") (expected.value as Record<string, unknown>).parameters = {
        producer: { kind: "ActivityResult", producerNodeId: copied.idMap.get("producer"), projectionKey: "$result" }
      };
      else (expected.value as Record<string, unknown>).producerNodeId = copied.idMap.get("producer");
      expect(copiedConsumer.inputs).toEqual([argument(expected)]);
    }
  });

  it.each([false, true])('remaps actual expanded input properties and legacy scopes (expanded: %s)', expanded => {
    const producer = leafNode("producer"), consumer = leafNode("consumer");
    consumer.inputs = [argument({ ...result(), value: { ...result().value, producerScopeId: "scope" } })];
    const scope = sequenceNode("scope", [producer, consumer]);
    const node = expanded ? expandStateFromWire({ rootActivity: scope }).rootActivity! : scope;
    const copied = copy([node]);
    const wire = canonicalizeStateForWire(copied.draft.state);
    const copiedScope = (wire.rootActivity!.structure!.payload.activities as ActivityNode[])[1];
    const copiedConsumer = (copiedScope.structure!.payload.activities as ActivityNode[])[1];
    expect(copiedConsumer.inputs).toEqual([argument({ ...result(), value: { ...result().value,
      producerNodeId: copied.idMap.get("producer"), producerScopeId: copied.idMap.get("scope") } })]);
  });

  it.each(["Variable", "VariableMember", "portable", "capture", "intrinsic"])('moves only the owned %s lexical identity', role => {
    const reader = leafNode("reader");
    reader.inputs = [argument(role === "portable" ? portable({ producer: { kind: "Variable", declaringScopeNodeId: "scope", variableKey: "producer" } }) : variable("scope", role === "VariableMember" ? role : "Variable")),
      argument(variable("workflow"), "workflow"), argument(variable("activity-graph"), "reusable-root"), argument(variable("external"), "external")];
    reader.outputs = [argument(variable("scope"), "result")];
    reader.intrinsic = { kind: "Set", valueType: { alias: "String", collectionKind: "Single" }, variable: { referenceKey: "producer", declaringScopeId: "scope" } };
    const scope = sequenceNode("scope", [reader]);
    scope.structure!.payload.variables = [{ referenceKey: "producer", name: "Same label", type: { alias: "String" } }];
    const copied = copy([scope]);
    const node = (copied.copies[0].structure!.payload.activities as ActivityNode[])[0];
    const expected = role === "portable" ? portable({ producer: { kind: "Variable", declaringScopeNodeId: copied.idMap.get("scope"), variableKey: "producer" } })
      : variable(copied.idMap.get("scope")!, role === "VariableMember" ? role : "Variable");
    expect(node.inputs).toEqual([argument(expected), ...reader.inputs.slice(1)]);
    expect(node.outputs).toEqual([argument(variable(copied.idMap.get("scope")!), "result")]);
    expect(node.intrinsic).toEqual({ ...reader.intrinsic, variable: { referenceKey: "producer", declaringScopeId: copied.idMap.get("scope") } });
  });

  it("remaps expanded captures without changing their public output keys or extras", () => {
    const reader = leafNode("reader");
    reader.outputs = [argument(variable("scope"), "result")];
    const scope = expandStateFromWire({ rootActivity: sequenceNode("scope", [reader]) }).rootActivity!;
    const copied = copy([scope]);
    const node = (copied.copies[0].structure!.payload.activities as ActivityNode[])[0];
    expect(node.result).toEqual({ target: { referenceKey: "producer", declaringScopeId: copied.idMap.get("scope") },
      argumentExtras: { policy: { producerNodeId: "producer" } } });
  });

  it("follows collection-owned Switch children and projected predicates, preserving case identity", () => {
    const item = collectionActivity("switch");
    const facet = item.designFacets![0] as { schemaVersion: string; payload: { slots: Record<string, unknown>[] } };
    facet.schemaVersion = "2.0.0";
    Object.assign(facet.payload.slots[0], { itemIdProperty: "caseId", conditionProperty: "condition" });
    const consumer = leafNode("consumer"); consumer.inputs = [argument(result("ActivityResultMember"))];
    const branch = sequenceNode("branch", [leafNode("producer"), consumer]);
    const node: ActivityNode = { nodeId: "switch", activityVersionId: item.activityVersionId, inputs: [], outputs: [],
      structure: { kind: "elsa.switch.structure", schemaVersion: "2.0.0", payload: { cases: [{ caseId: "producer", match: "Same label", annotation: { producerNodeId: "producer" },
        activity: branch, condition: argument(result(), "Case:producer:Condition") }], default: leafNode("default") } } };
    const copied = copy([node], [node.nodeId], new Map(propertyCatalog).set(item.activityVersionId, item));
    const cases = copied.copies[0].structure!.payload.cases as Record<string, unknown>[];
    const copiedBranch = cases[0].activity as ActivityNode;
    expect(copiedBranch.nodeId).toBe(copied.idMap.get("branch"));
    expect(cases[0]).toMatchObject({ caseId: "producer", match: "Same label", annotation: { producerNodeId: "producer" },
      condition: argument({ ...result(), value: { ...result().value, producerNodeId: copied.idMap.get("producer") } }, "Case:producer:Condition") });
    expect((copiedBranch.structure!.payload.activities as ActivityNode[])[1].inputs).toEqual([
      argument({ ...result("ActivityResultMember"), value: { ...result("ActivityResultMember").value, producerNodeId: copied.idMap.get("producer") } })]);
    expect(collectActivityNodeIds(copied.copies[0], new Map(propertyCatalog).set(item.activityVersionId, item)).size).toBe(5);
  });

  it.each(["producer", "missing"])('keeps consumer-only external identity %s and does not match display labels', identity => {
    const consumer = leafNode("consumer"); consumer.inputs = [argument(result("ActivityResultMember", identity))];
    const copied = copy([leafNode("producer"), consumer], ["consumer"]);
    expect(copied.copies[0].inputs).toEqual(consumer.inputs);
  });

  it("uses a fresh complete map for repeated paste and keeps every earlier copy untouched", () => {
    const consumer = leafNode("consumer"); consumer.inputs = [argument(result())];
    const first = copy([leafNode("producer"), consumer]);
    const before = structuredClone(first.draft);
    const second = pasteActivitiesIntoDraft({ draft: first.draft, frames: [], snapshot: first.snapshot, catalog: propertyCatalog, allocateId: sequentialIds() })!;
    expect(first.draft).toEqual(before);
    const nodes = second.draft.state.rootActivity!.structure!.payload.activities as ActivityNode[];
    expect(nodes[5].inputs).toEqual([argument({ ...result(), value: { ...result().value, producerNodeId: second.idMap.get("producer") } })]);
    expect(second.idMap.get("producer")).not.toBe(first.idMap.get("producer"));
  });

  it("preserves literal, unknown and malformed lookalikes, member segments, aliases, source and metadata", () => {
    const consumer = leafNode("consumer");
    const lookalike = portable({ producer: { kind: "ActivityResult", producerNodeId: "producer", projectionKey: "$result" } }).value;
    consumer.inputs = [argument({ expressionType: "Object", value: lookalike }), argument({ expressionType: "Literal", value: lookalike }),
      argument({ expressionType: "FutureReference", value: result().value }), argument({ expressionType: "ActivityResult", value: { producerNodeId: "producer" } }),
      argument({ expressionType: "ActivityResultMember", value: { ...result().value, memberPath: [] } }),
      argument({ expressionType: "MissingLanguage", value: { language: "MissingLanguage", source: "producer", parameters: lookalike.parameters } }),
      argument(portable({ producer: { kind: "Literal", value: result().value }, unknown: { kind: "FutureReference", producerNodeId: "producer" } }))];
    consumer.metadata = { expression: { type: "ActivityResult", value: result().value }, producerNodeId: "producer" };
    consumer.opaqueResult = { target: { referenceKey: "producer", declaringScopeId: "producer" }, extra: "opaque undeclared property" };
    const copied = copy([leafNode("producer"), consumer]);
    expect(copied.copies[1]).toEqual({ ...consumer, nodeId: copied.idMap.get("consumer") });
  });

  it("does not treat a structural connection ID as an activity producer or lexical scope", () => {
    const consumer = leafNode("consumer"); consumer.inputs = [argument(result("ActivityResult", "edge-only")), argument(variable("edge-only"), "other")];
    const nested = connectedFlowchart([leafNode("producer"), consumer], [{ from: "producer", to: "consumer", id: "edge-only" }]);
    const copied = copy([nested]);
    const node = (copied.copies[0].structure!.payload.activities as ActivityNode[])[1];
    expect(node.inputs).toEqual(consumer.inputs);
    expect((copied.copies[0].structure!.payload.connections as Record<string, unknown>[])[0].id).not.toBe("edge-only");
  });
});

describe("copied display names", () => {
  it("assigns (copy) then increments", () => {
    const taken = new Set(["write line"]);
    expect(uniqueCopiedDisplayName("Write Line", taken)).toBe("Write Line (copy)");
    expect(uniqueCopiedDisplayName("Write Line", taken)).toBe("Write Line (copy 2)");
    expect(uniqueCopiedDisplayName("Write Line", taken)).toBe("Write Line (copy 3)");
  });
});

describe("in-workflow activity duplicate remapping", () => {
  it("assigns a fresh id and name to a selected leaf", () => {
    const root = flowchartNode("flow-root", [leafNode("write-one")]);
    const snapshot = snapshotSelectedActivities({
      selectedNodeIds: ["write-one"],
      scopeActivities: [leafNode("write-one")],
      scopeOwner: root,
      catalog,
      layout: [{ nodeId: "write-one", x: 80, y: 40 }],
      presentation: []
    });
    expect(snapshot).not.toBeNull();

    const result = pasteActivitiesIntoDraft({
      draft: draftOf(root, { layout: [{ nodeId: "write-one", x: 80, y: 40 }] }),
      frames: [],
      snapshot: snapshot!,
      catalog,
      allocateId: sequentialIds()
    });

    expect(result).not.toBeNull();
    const activities = result!.draft.state.rootActivity?.structure?.payload.activities as ActivityNode[];
    expect(activities.map(activity => activity.nodeId)).toEqual(["write-one", "writeone-copy1"]);
    expect(result!.selectedNodeIds).toEqual(["writeone-copy1"]);
    expect(result!.draft.activityPresentation).toEqual([
      { nodeId: "writeone-copy1", displayName: "Write Line (copy)", description: undefined }
    ]);
    expect(result!.draft.layout).toEqual([
      { nodeId: "write-one", x: 80, y: 40 },
      { nodeId: "writeone-copy1", x: 120, y: 80 }
    ]);
  });

  it("copies only connections between selected siblings", () => {
    const left = leafNode("left");
    const right = leafNode("right");
    const extra = leafNode("extra");
    const root = connectedFlowchart([left, right, extra], [
      { from: "left", to: "right", id: "kept" },
      { from: "right", to: "extra", id: "outside" }
    ]);
    const snapshot = snapshotSelectedActivities({
      selectedNodeIds: ["left", "right"],
      scopeActivities: [left, right, extra],
      scopeOwner: root,
      catalog,
      layout: [],
      presentation: []
    });

    expect(snapshot?.siblingConnections).toHaveLength(1);
    expect(snapshot?.siblingConnections[0]).toMatchObject({
      source: { nodeId: "left" },
      target: { nodeId: "right" }
    });

    const result = pasteActivitiesIntoDraft({
      draft: draftOf(root),
      frames: [],
      snapshot: snapshot!,
      catalog,
      allocateId: sequentialIds()
    });

    const connections = result!.draft.state.rootActivity?.structure?.payload.connections as Array<Record<string, unknown>>;
    expect(connections).toHaveLength(3);
    expect(connections[2]).toMatchObject({
      source: { nodeId: "left-copy1" },
      target: { nodeId: "right-copy2" }
    });
    expect(connections[2]?.id).not.toBe("kept");
    expect(connections.some(connection => {
      const target = connection.target as { nodeId?: string };
      return target.nodeId === "extra" && connection.id !== "outside";
    })).toBe(false);
  });

  it("remaps nested container children, start node, and internal connections", () => {
    const nested = connectedFlowchart(
      [leafNode("inner-start"), leafNode("inner-end")],
      [{ from: "inner-start", to: "inner-end", id: "inner-flow" }]
    );
    nested.nodeId = "inner-flow";
    const root = flowchartNode("flow-root", [nested]);
    const snapshot = snapshotSelectedActivities({
      selectedNodeIds: ["inner-flow"],
      scopeActivities: [nested],
      scopeOwner: root,
      catalog,
      layout: [
        { nodeId: "inner-flow", x: 10, y: 10 },
        { nodeId: "inner-start", x: 0, y: 0 },
        { nodeId: "inner-end", x: 200, y: 0 }
      ],
      presentation: [{ nodeId: "inner-start", displayName: "Start here" }]
    });

    const result = pasteActivitiesIntoDraft({
      draft: draftOf(root, {
        layout: [
          { nodeId: "inner-flow", x: 10, y: 10 },
          { nodeId: "inner-start", x: 0, y: 0 },
          { nodeId: "inner-end", x: 200, y: 0 }
        ],
        activityPresentation: [{ nodeId: "inner-start", displayName: "Start here" }]
      }),
      frames: [],
      snapshot: snapshot!,
      catalog,
      allocateId: sequentialIds()
    });

    const copies = result!.draft.state.rootActivity?.structure?.payload.activities as ActivityNode[];
    expect(copies).toHaveLength(2);
    const copy = copies[1];
    expect(copy.nodeId).toBe("innerflow-copy1");
    expect(copy.nodeId).not.toBe("inner-flow");
    const children = copy.structure?.payload.activities as ActivityNode[];
    expect(children.map(child => child.nodeId)).toEqual(["innerstart-copy2", "innerend-copy3"]);
    expect(copy.structure?.payload.startNodeId).toBe("innerstart-copy2");
    expect(copy.structure?.payload.connections).toEqual([
      expect.objectContaining({
        source: { nodeId: "innerstart-copy2", port: "Done" },
        target: { nodeId: "innerend-copy3" }
      })
    ]);
    expect(result!.draft.layout.find(record => record.nodeId === "innerflow-copy1")).toEqual({
      nodeId: "innerflow-copy1",
      x: 50,
      y: 50
    });
    expect(result!.draft.layout.find(record => record.nodeId === "innerstart-copy2")).toEqual({
      nodeId: "innerstart-copy2",
      x: 0,
      y: 0
    });
    expect(result!.draft.activityPresentation?.map(record => record.displayName)).toEqual([
      "Start here",
      "Flowchart (copy)",
      "Start here"
    ]);
    const allIds = copies.flatMap(activity => [
      activity.nodeId,
      ...((activity.structure?.payload.activities as ActivityNode[] | undefined)?.map(child => child.nodeId) ?? [])
    ]);
    expect(new Set(allIds).size).toBe(allIds.length);
  });

  it("remaps a ForEach body child so the copy has no duplicate node id", () => {
    const container = forEachNode("each-1", leafNode("body-1"));
    const root = flowchartNode("flow-root", [container]);
    const snapshot = snapshotSelectedActivities({
      selectedNodeIds: ["each-1"],
      scopeActivities: [container],
      scopeOwner: root,
      catalog,
      layout: [],
      presentation: []
    });

    const result = pasteActivitiesIntoDraft({
      draft: draftOf(root),
      frames: [],
      snapshot: snapshot!,
      catalog,
      allocateId: sequentialIds()
    });

    const copies = result!.draft.state.rootActivity?.structure?.payload.activities as ActivityNode[];
    const originalBody = (copies[0].structure?.payload.body as ActivityNode).nodeId;
    const copiedBody = (copies[1].structure?.payload.body as ActivityNode).nodeId;
    expect(originalBody).toBe("body-1");
    expect(copiedBody).toBe("body1-copy2");
    expect(copiedBody).not.toBe(originalBody);
    expect(copies[1].nodeId).toBe("each1-copy1");
  });

  it("applies the pasted activities in one draft so undo can treat it as a single step", () => {
    const root = flowchartNode("flow-root", [leafNode("write-one"), leafNode("write-two")]);
    const snapshot = snapshotSelectedActivities({
      selectedNodeIds: ["write-one", "write-two"],
      scopeActivities: [leafNode("write-one"), leafNode("write-two")],
      scopeOwner: root,
      catalog,
      layout: [],
      presentation: []
    });
    const original = draftOf(root);
    const plan = planActivityDuplicate({
      snapshot: snapshot!,
      draft: original,
      frames: [],
      catalog,
      allocateId: sequentialIds()
    });
    const next = applyActivityDuplicatePlan(original, [], plan!, catalog);

    expect(next).not.toBe(original);
    expect((original.state.rootActivity?.structure?.payload.activities as ActivityNode[]).map(activity => activity.nodeId))
      .toEqual(["write-one", "write-two"]);
    expect((next!.state.rootActivity?.structure?.payload.activities as ActivityNode[]).map(activity => activity.nodeId))
      .toEqual(["write-one", "write-two", "writeone-copy1", "writetwo-copy2"]);
  });

  it("duplicates a Sequence step immediately after its source without copying top-level positions", () => {
    const source = leafNode("first");
    const root = sequenceNode("sequence-root", [source, leafNode("last")]);
    const layout = [{ nodeId: "first", x: 600, y: 80 }];
    const snapshot = snapshotSelectedActivities({ selectedNodeIds: ["first"], scopeActivities: [source], scopeOwner: root, catalog, layout, presentation: [] });
    const result = pasteActivitiesIntoDraft({
      draft: draftOf(root, { layout }), frames: [], snapshot: snapshot!, catalog,
      allocateId: sequentialIds(), insertAfterNodeId: "first"
    });

    expect((result!.draft.state.rootActivity!.structure!.payload.activities as ActivityNode[]).map(activity => activity.nodeId))
      .toEqual(["first", "first-copy1", "last"]);
    expect(result!.draft.layout).toEqual(layout);
    expect(result!.draft.activityPresentation).toEqual([{ nodeId: "first-copy1", displayName: "Write Line (copy)", description: undefined }]);
  });

  it("does not write a connections list when pasting into a Sequence", () => {
    const left = leafNode("left");
    const right = leafNode("right");
    const flowchart = connectedFlowchart([left, right], [{ from: "left", to: "right", id: "kept" }]);
    const snapshot = snapshotSelectedActivities({
      selectedNodeIds: ["left", "right"],
      scopeActivities: [left, right],
      scopeOwner: flowchart,
      catalog,
      layout: [],
      presentation: []
    });
    expect(snapshot?.siblingConnections).toHaveLength(1);

    const sequence = sequenceNode("sequence-root", [leafNode("existing")]);
    const result = pasteActivitiesIntoDraft({
      draft: draftOf(sequence),
      frames: [],
      snapshot: snapshot!,
      catalog,
      allocateId: sequentialIds()
    });

    const payload = result!.draft.state.rootActivity?.structure?.payload as Record<string, unknown>;
    expect(Object.prototype.hasOwnProperty.call(payload, "connections")).toBe(false);
    expect((payload.activities as ActivityNode[]).map(activity => activity.nodeId))
      .toEqual(["existing", "left-copy1", "right-copy2"]);
  });

  it("assigns unique names when the same snapshot is pasted into the latest draft twice", () => {
    const root = flowchartNode("flow-root", [leafNode("write-one")]);
    const snapshot = snapshotSelectedActivities({
      selectedNodeIds: ["write-one"],
      scopeActivities: [leafNode("write-one")],
      scopeOwner: root,
      catalog,
      layout: [],
      presentation: []
    });
    const first = pasteActivitiesIntoDraft({
      draft: draftOf(root),
      frames: [],
      snapshot: snapshot!,
      catalog,
      allocateId: sequentialIds()
    });
    const second = pasteActivitiesIntoDraft({
      draft: first!.draft,
      frames: [],
      snapshot: snapshot!,
      catalog,
      allocateId: sequentialIds()
    });

    expect(second!.draft.activityPresentation?.map(record => record.displayName)).toEqual([
      "Write Line (copy)",
      "Write Line (copy 2)"
    ]);
  });

  it("returns null when nothing in the current scope is selected", () => {
    const root = flowchartNode("flow-root", [leafNode("write-one")]);
    expect(snapshotSelectedActivities({
      selectedNodeIds: ["missing"],
      scopeActivities: [leafNode("write-one")],
      scopeOwner: root,
      catalog,
      layout: [],
      presentation: []
    })).toBeNull();
  });
});
