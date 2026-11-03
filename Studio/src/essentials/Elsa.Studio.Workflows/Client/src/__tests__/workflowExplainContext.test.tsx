import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it } from "vitest";
import type { StudioActivityDescriptor } from "@elsa-workflows/studio-sdk";
import { createAgentRegistry } from "@elsa-workflows/studio-sdk";
import { registerWorkflowAgent } from "../workflowAgent";
import { useWorkflowContextBridge, workflowContextCandidateBudget } from "../workflow-editor/useWorkflowContextBridge";
import { admittedSnapshotLength, collectWorkflowContextDiagnostics, hostSerializedLength, workflowPropertyContentBudget, workflowPropertyContentLimit } from "../workflow-editor/workflowExplainContext";
import type { ActivityCatalogItem, ActivityNode, ValidationError, WorkflowDefinitionDetails, WorkflowDraft } from "../workflowTypes";
import { flowchartActivity, writeLine } from "./fixtures";
import shared from "./workflowExplainSnapshot.json";

// Spec 1193 slice D (FR-014, FR-016–FR-018, SC-005): the active-draft snapshot discloses the designer's diagnostics, graph
// activities by definition and version, and the selected activity's declared properties. Credential and sensitive values
// never leave the browser: they are reported present and withheld, and a bound Secret Reference by name, type and scope.
const canary = shared.canary;
const apiActivity: ActivityCatalogItem = {
  ...writeLine, activityVersionId: "activity-api-call-v1", activityTypeKey: "Acme.ApiCall", displayName: "API call",
  inputs: [
    { referenceKey: "url", name: "Url", typeName: "String" },
    { referenceKey: "script", name: "Script", typeName: "String" },
    { referenceKey: "apiKey", name: "ApiKey", typeName: "String", isCredential: true, isSensitive: true },
    { referenceKey: "signingKey", name: "SigningKey", typeName: "String", isCredential: true, isSensitive: true },
    { referenceKey: "password", name: "Password", typeName: "String", uiHint: "password" },
    { referenceKey: "body", name: "Body", typeName: "Object" },
    { referenceKey: "unset", name: "Unset", typeName: "String" }
  ]
};
const approval: ActivityCatalogItem = {
  ...writeLine, activityVersionId: "activity-approval-v3", activityTypeKey: "Acme.Approval", displayName: "Approval", version: "3",
  activityDefinitionId: "definition-approval", activityDefinitionVersionId: "activity-approval-v3", activityDefinitionVersion: "3"
};
const catalogByVersion = new Map([writeLine, flowchartActivity, apiActivity, approval].map(item => [item.activityVersionId, item]));

function wrapped(type: string, value: unknown) {
  return { typeName: "String", expression: { type, value } };
}

function apiNode(overrides: Record<string, unknown> = {}): ActivityNode {
  return {
    nodeId: "api", activityVersionId: apiActivity.activityVersionId, inputs: [], outputs: [],
    url: wrapped("Literal", "https://example.test/orders"),
    script: wrapped("JavaScript", "return `Order ${getVariable('orderId')}`;"),
    apiKey: wrapped("Literal", canary),
    signingKey: wrapped("Secret", { name: "crm-signing-key", typeName: "RsaKey", scope: "tenant" }),
    password: wrapped("Literal", canary + "-password"),
    body: wrapped("Object", { order: 1 }),
    ...overrides
  };
}

function draftWith(children: ActivityNode[], validationErrors: ValidationError[] = []): WorkflowDraft {
  const nested: ActivityNode = { nodeId: "nested", activityVersionId: flowchartActivity.activityVersionId, inputs: [], outputs: [], structure: {
    kind: "elsa.flowchart.structure", schemaVersion: "1.0.0", payload: { activities: [{ nodeId: "inner", activityVersionId: approval.activityVersionId, inputs: [], outputs: [] }],
      connections: [], startNodeId: "inner", nodeMetadata: {}, connectionMetadata: {} } } };
  return {
    id: "draft", definitionId: "definition",
    state: { rootActivity: { nodeId: "flow", activityVersionId: flowchartActivity.activityVersionId, inputs: [], outputs: [], structure: {
      kind: "elsa.flowchart.structure", schemaVersion: "1.0.0", payload: { activities: [...children, nested], connections: [], startNodeId: children[0]?.nodeId ?? null,
        nodeMetadata: {}, connectionMetadata: {} } } } },
    layout: [], activityPresentation: [], validationErrors
  };
}

function descriptorOf(item: ActivityCatalogItem): StudioActivityDescriptor {
  return { typeName: item.activityTypeKey, name: item.displayName, displayName: item.displayName, category: item.category, kind: "Action",
    inputs: item.inputs as StudioActivityDescriptor["inputs"], outputs: [], ports: [] } as unknown as StudioActivityDescriptor;
}

function snapshot(draft: WorkflowDraft, options: { selected?: ActivityNode | null; validationErrors?: ValidationError[] } = {}) {
  const agent = createAgentRegistry();
  const controller = registerWorkflowAgent(agent);
  const details = { definition: { id: "definition", name: "Orders" } } as WorkflowDefinitionDetails;
  const selected = options.selected ?? null;
  function Probe() {
    useWorkflowContextBridge({ controller, clientId: agent.clientId, details, draft, selectedNode: selected, selectedNodeId: selected?.nodeId ?? null,
      selectedDescriptor: selected ? descriptorOf(catalogByVersion.get(selected.activityVersionId)!) : null,
      inspectedNode: null, inspectedDescriptor: null, inspectedIsScopeOwner: false, catalogByVersion, validationErrors: options.validationErrors });
    return null;
  }
  const container = document.createElement("div");
  const root = createRoot(container);
  flushSync(() => root.render(<Probe />));
  const attachment = controller.snapshot!;
  flushSync(() => root.unmount());
  return attachment.content as Record<string, unknown>;
}

type Property = { referenceKey: string; isCredential: boolean; isSensitive: boolean; value: Record<string, unknown> };

describe("workflow explain context", () => {
  it("discloses the selected activity's declared properties with expression types and bounded content, and withholds credentials", () => {
    const node = apiNode();
    const content = snapshot(draftWith([node]), { selected: node });
    const selected = content.selectedActivity as { nodeId: string; activityVersionId: string; properties: Property[] };
    expect(selected).toMatchObject({ nodeId: "api", activityVersionId: apiActivity.activityVersionId });
    const byKey = Object.fromEntries(selected.properties.map(property => [property.referenceKey, property]));
    expect(byKey.url.value).toEqual({ kind: "expression", expressionType: "Literal", content: "https://example.test/orders", truncated: false, length: 27 });
    expect(byKey.script.value).toMatchObject({ kind: "expression", expressionType: "JavaScript", content: "return `Order ${getVariable('orderId')}`;" });
    // Present and withheld: the credential literal never leaves the browser.
    expect(byKey.apiKey).toMatchObject({ isCredential: true, isSensitive: true, value: { kind: "withheld", expressionType: "Literal" } });
    // A bound Secret Reference by name, type and scope only.
    expect(byKey.signingKey.value).toEqual({ kind: "secretReference", secretReference: { name: "crm-signing-key", typeName: "RsaKey", scope: "tenant" } });
    // A password-hinted input is masked too, although the catalog does not declare it sensitive.
    expect(byKey.password).toMatchObject({ isCredential: false, isSensitive: true, value: { kind: "withheld" } });
    expect(byKey.body.value).toMatchObject({ kind: "expression", expressionType: "Object", content: "{\"order\":1}" });
    expect(byKey.unset.value).toEqual({ kind: "unset" });
    expect(JSON.stringify(content)).not.toContain(canary);
    expect(content.secretReferences).toEqual([{ name: "crm-signing-key", typeName: "RsaKey", scope: "tenant" }]);
  });

  it("bounds each property's content and marks the truncation, within one budget for all properties", () => {
    const long = "x".repeat(workflowPropertyContentLimit + 500);
    const inputs = Array.from({ length: 12 }, (_, index) => ({ referenceKey: "input" + index, name: "Input" + index, typeName: "String" }));
    const wide: ActivityCatalogItem = { ...apiActivity, activityVersionId: "activity-wide-v1", inputs };
    catalogByVersion.set(wide.activityVersionId, wide);
    const node: ActivityNode = { nodeId: "wide", activityVersionId: wide.activityVersionId, inputs: [], outputs: [],
      ...Object.fromEntries(inputs.map(input => [input.referenceKey, wrapped("JavaScript", long)])) };
    const content = snapshot(draftWith([node]), { selected: node });
    const selected = content.selectedActivity as { truncated: boolean; properties: Property[] };
    expect(selected.truncated).toBe(true);
    const disclosed = selected.properties.map(property => property.value as { content: string; truncated: boolean; length: number });
    expect(disclosed[0]).toMatchObject({ truncated: true, length: long.length });
    expect(disclosed[0].content).toHaveLength(workflowPropertyContentLimit);
    expect(disclosed.every(value => value.truncated && value.length === long.length)).toBe(true);
    expect(disclosed.reduce((total, value) => total + value.content.length, 0)).toBeLessThanOrEqual(workflowPropertyContentBudget);
  });

  it("discloses only the designer's own diagnostics, by activity and input", () => {
    const errors: ValidationError[] = [
      { path: "api/inputs/url", type: "Expressions/UnresolvedVariable", message: "Variable 'orderId' is not visible from this activity's scope.", severity: "Error" },
      { path: "api", type: "Structure/DisconnectedNode", message: "API call is not connected.", severity: "Warning" },
      { path: "$workflow", type: "Workflow/NoDescription", message: "The workflow has no description." }
    ];
    const content = snapshot(draftWith([apiNode()], [{ path: "stale", message: "A reconciled draft error the panel replaced." }]), { validationErrors: errors });
    expect(content.diagnostics).toEqual([
      { severity: "error", type: "Expressions/UnresolvedVariable", nodeId: "api", property: "url", message: "Variable 'orderId' is not visible from this activity's scope." },
      { severity: "warning", type: "Structure/DisconnectedNode", nodeId: "api", message: "API call is not connected." },
      { severity: "error", type: "Workflow/NoDescription", message: "The workflow has no description." }
    ]);
    expect(content).not.toHaveProperty("diagnosticsOmitted");
  });

  it("reports how many diagnostics were left out beyond its bound", () => {
    const errors = Array.from({ length: 70 }, (_, index) => ({ path: "api", message: "Issue " + index }));
    const { diagnostics, omitted } = collectWorkflowContextDiagnostics(errors);
    expect(diagnostics).toHaveLength(64);
    expect(omitted).toBe(6);
  });

  it("references graph activities by definition identity and version, never their bodies", () => {
    const content = snapshot(draftWith([apiNode()]));
    const activities = content.activities as Array<{ id: string }>;
    expect(content.graphActivities).toEqual([{ activityDefinitionId: "definition-approval", activityVersionId: "activity-approval-v3", version: "3",
      nodes: [activities.findIndex(activity => activity.id === "inner")] }]);
  });

  it("leaves the property context out, then the diagnostics, rather than push a snapshot over the host's attachment budget", () => {
    // Long descriptions bring the draft near the host's 32 KiB attachment budget without the explanation additions.
    const children = Array.from({ length: 60 }, (_, index) => ({ nodeId: "w-" + index, activityVersionId: writeLine.activityVersionId, inputs: [], outputs: [] }) as ActivityNode);
    const node = apiNode({ script: wrapped("JavaScript", "y".repeat(workflowPropertyContentLimit)), url: wrapped("Literal", "z".repeat(workflowPropertyContentLimit)) });
    const draft = { ...draftWith([...children, node]), activityPresentation: children.map(child => ({ nodeId: child.nodeId, description: "d".repeat(300) })) };
    const errors = Array.from({ length: 64 }, (_, index) => ({ path: "w-" + index, message: "m".repeat(400) }));
    const content = snapshot(draft, { selected: node, validationErrors: errors });
    // The host bounds the server's admitted projection (descriptions and editor-only fields are not admitted).
    expect(admittedSnapshotLength(content)).toBeLessThanOrEqual(workflowContextCandidateBudget);
    expect(content).not.toHaveProperty("selectedActivity");
    expect(content.diagnostics).toEqual([]);
    expect(content.diagnosticsOmitted).toBe(64);
    // The structure an Operation Batch needs stays: only the explanation additions gave way.
    expect(content).toHaveProperty("structure");
  });

  it("produces exactly the shared snapshot the server admits against the owning catalog", () => {
    // Elsa.Agent.Tests (WorkflowExplainContextTests) admits this same content strictly; a drift on either side fails one suite.
    const node = apiNode();
    const errors: ValidationError[] = [
      { path: "api/inputs/url", type: "Expressions/UnresolvedVariable", message: "Variable 'orderId' is not visible from this activity's scope.", severity: "Error" },
      { path: "inner", type: "Structure/DisconnectedNode", message: "Approval is not connected.", severity: "Warning" }
    ];
    const content = snapshot(draftWith([node]), { selected: node, validationErrors: errors });
    expect(content).toEqual(shared.content);
    // The size guard's estimate of the host's measure; the server test holds the real measure to the same number.
    expect(admittedSnapshotLength(content)).toBe(shared.admittedLength);
    expect(JSON.stringify(content)).not.toContain(canary);
    // The catalog the server checks against declares the same inputs the designer's descriptor does.
    expect(shared.catalog[0].inputs.map(input => [input.referenceKey, input.isCredential]))
      .toEqual(apiActivity.inputs.map(input => { const item = input as { referenceKey: string; isCredential?: boolean }; return [item.referenceKey, item.isCredential === true]; }));
  });

  it("measures the host's escaped serialization, not the browser's", () => {
    expect(hostSerializedLength({ text: "a<b" })).toBe(JSON.stringify({ text: "a<b" }).length + 5);
    expect(hostSerializedLength({ text: "é" })).toBe(JSON.stringify({ text: "é" }).length + 5);
  });
});

