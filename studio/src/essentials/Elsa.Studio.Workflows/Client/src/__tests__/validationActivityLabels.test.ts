import { describe, expect, it } from "vitest";
import {
  createValidationActivityLabels,
  formatValidationActivityLabel,
  humanizeValidationErrors
} from "../validationActivityLabels";
import { detectStructuralValidationErrors } from "../draftValidation";
import { flowchartStructureKind } from "../flowchartStartNode";
import type { ActivityCatalogItem, ActivityNode, ValidationError } from "../workflowTypes";

const writeLine: ActivityCatalogItem = {
  activityVersionId: "write-line-v1",
  activityTypeKey: "Elsa.Activities.Console.WriteLine",
  version: "1.0.0",
  category: "Console",
  displayName: "Write Line",
  executionType: "Task",
  inputs: [{ referenceKey: "text", name: "Text", displayName: "Text" }],
  outputs: []
};

function node(nodeId: string, extra: Record<string, unknown> = {}): ActivityNode {
  return { nodeId, activityVersionId: writeLine.activityVersionId, inputs: [], outputs: [], ...extra };
}

function flowchart(activities: ActivityNode[], connections: Array<{ from: string; to: string }>, startNodeId?: string): ActivityNode {
  return node("root", {
    activityVersionId: "flowchart-v1",
    structure: {
      kind: flowchartStructureKind,
      schemaVersion: "1",
      payload: {
        activities,
        connections: connections.map(c => ({ source: { nodeId: c.from }, target: { nodeId: c.to } })),
        startNodeId: startNodeId ?? activities[0]?.nodeId ?? null
      }
    }
  });
}

const namedId = "writeline-22ab948a";
const unnamedId = "writeline-7b09879d";
const connectedId = "http-aaaa1111";

function labelsForIssueDraft() {
  return createValidationActivityLabels({
    root: labelsForIssueDraftRoot(),
    presentation: [{ nodeId: namedId, displayName: "Say hello" }],
    catalogByVersion: new Map([[writeLine.activityVersionId, writeLine]])
  });
}

describe("formatValidationActivityLabel", () => {
  it("uses the authored name and type, then a short index for unnamed duplicates", () => {
    expect(formatValidationActivityLabel("Say hello", "Write Line")).toBe("\"Say hello\" (Write Line)");
    expect(formatValidationActivityLabel(undefined, "Write Line")).toBe("Write Line");
    expect(formatValidationActivityLabel(undefined, "Write Line", 2, 2)).toBe("Write Line #2");
  });
});

describe("createValidationActivityLabels", () => {
  it("names activities the way the canvas does and rewrites engine copy into user terms", () => {
    const labels = labelsForIssueDraft();

    expect(labels.labelFor(namedId)).toBe("\"Say hello\" (Write Line)");
    expect(labels.labelFor(unnamedId)).toBe("Write Line");

    expect(labels.humanizeMessage(
      `Required input 'Text' on activity '${namedId}' is missing or empty.`
    )).toEqual({
      message: "Required input \"Text\" on \"Say hello\" (Write Line) is empty.",
      nodeId: namedId
    });

    const rejected = labels.humanizeMessage(
      `VF-ACT-003: Activity node '${namedId}' omits required input 'text', which has no pinned default. Every published input must lower to exactly one canonical binding.`
    );
    expect(rejected).toEqual({
      message: "Required input \"Text\" on \"Say hello\" (Write Line) is empty.",
      code: "VF-ACT-003",
      nodeId: namedId
    });
    expect(rejected.message).not.toMatch(/writeline-|pinned default|canonical binding/i);
  });

  it("indexes two unnamed activities of the same type", () => {
    const first = node("writeline-11111111");
    const second = node("writeline-22222222");
    const labels = createValidationActivityLabels({
      root: flowchart([first, second], [], first.nodeId),
      catalogByVersion: new Map([[writeLine.activityVersionId, writeLine]])
    });

    expect(labels.labelFor(first.nodeId)).toBe("Write Line #1");
    expect(labels.labelFor(second.nodeId)).toBe("Write Line #2");
  });
});

describe("detectStructuralValidationErrors labels", () => {
  it("uses the Details display name instead of the internal node id", () => {
    const labels = labelsForIssueDraft();
    const errors = detectStructuralValidationErrors({ rootActivity: labelsForIssueDraftRoot() }, labels);
    const dangling = errors.find(error => error.path === unnamedId);

    expect(dangling?.message).toBe("Write Line is not connected to any other activity.");
    expect(dangling?.message).not.toContain(unnamedId);
  });
});

describe("humanizeValidationErrors", () => {
  it("rewrites server required-input copy and keeps the path for Go to", () => {
    const labels = labelsForIssueDraft();
    const errors: ValidationError[] = [{
      type: "InputOutput/MissingRequired",
      message: `Required input 'Text' on activity '${namedId}' is missing or empty.`,
      path: `${namedId}/inputs/text`
    }];

    expect(humanizeValidationErrors(errors, labels)).toEqual([{
      type: "InputOutput/MissingRequired",
      message: "Required input \"Text\" on \"Say hello\" (Write Line) is empty.",
      path: `${namedId}/inputs/text`
    }]);
  });
});

function labelsForIssueDraftRoot() {
  return flowchart(
    [node(namedId), node(connectedId, { activityVersionId: "http-v1" }), node(unnamedId)],
    [{ from: namedId, to: connectedId }],
    namedId
  );
}
