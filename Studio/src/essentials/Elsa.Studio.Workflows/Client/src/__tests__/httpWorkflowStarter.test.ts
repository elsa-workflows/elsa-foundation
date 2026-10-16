import { describe, expect, it } from "vitest";
import { canonicalizeStateForWire } from "../activityInputWire";
import { getCreateInitialState } from "../workflow-editor/editorHelpers";
import { createHttpStarterState, httpStarterActivities } from "../workflow-editor/httpWorkflowStarter";
import type { ActivityNode, WorkflowDefinitionState } from "../workflowTypes";
import { flowchartActivity, writeLine } from "./fixtures";

const endpoint = { ...writeLine, activityVersionId: "current-http-endpoint", activityTypeKey: "Elsa.Activities.Http.Activities.HttpEndpoint" };
const response = { ...writeLine, activityVersionId: "current-http-response", activityTypeKey: "Elsa.Activities.Http.Activities.WriteHttpResponse" };
const catalog = [flowchartActivity, endpoint, response];

describe("HTTP workflow starter", () => {
  it("authors a connected POST endpoint and JSON response using this server's catalog, without captures or an explicit response mode", () => {
    const state = getCreateInitialState({ name: "Hello", description: "", rootActivityVersionId: null, starter: "http" }, catalog);
    const wire = canonicalizeStateForWire(state as WorkflowDefinitionState);
    const root = wire.rootActivity!;
    const payload = root.structure!.payload as { activities: ActivityNode[]; startNodeId: string; connections: { source: { nodeId: string }; target: { nodeId: string } }[] };
    const [requestNode, responseNode] = payload.activities;
    expect(root.activityVersionId).toBe(flowchartActivity.activityVersionId);
    expect(requestNode.activityVersionId).toBe(endpoint.activityVersionId);
    expect(responseNode.activityVersionId).toBe(response.activityVersionId);
    expect(payload.startNodeId).toBe(requestNode.nodeId);
    expect(payload.connections).toEqual([expect.objectContaining({ source: { nodeId: requestNode.nodeId, port: "Done" }, target: { nodeId: responseNode.nodeId } })]);
    expect(requestNode.inputs).toEqual(expect.arrayContaining([
      { referenceKey: "CanStartWorkflow", value: { expressionType: "Literal", value: "true" } },
      { referenceKey: "Path", value: { expressionType: "Literal", value: "/hello" } },
      { referenceKey: "SupportedMethods", value: { expressionType: "Object", value: '["POST"]' } }
    ]));
    expect(requestNode.inputs.some(input => (input as { referenceKey: string }).referenceKey === "ResponseMode")).toBe(false);
    expect(responseNode.inputs).toEqual(expect.arrayContaining([
      { referenceKey: "ContentType", value: { expressionType: "Literal", value: "application/json" } },
      { referenceKey: "Body", value: { expressionType: "Literal", value: '{"message":"Hello from Elsa"}' } }
    ]));
    expect(payload.activities.every(activity => activity.outputs.length === 0)).toBe(true);
  });

  it("does not offer a starter with missing or unavailable activities", () => {
    for (const missing of catalog) {
      const incomplete = catalog.filter(activity => activity !== missing);
      expect(httpStarterActivities(incomplete).every(Boolean)).toBe(false);
      expect(() => createHttpStarterState(incomplete)).toThrow(/starter needs/);
    }
    expect(() => createHttpStarterState([flowchartActivity, { ...endpoint, available: false }, response])).toThrow(/starter needs/);
  });

  it("does not mutate catalog templates or reuse node identities across definitions", () => {
    const before = structuredClone(catalog);
    const first = createHttpStarterState(catalog);
    const second = createHttpStarterState(catalog);
    expect(first.rootActivity.nodeId).not.toBe(second.rootActivity.nodeId);
    expect(catalog).toEqual(before);
  });
});
