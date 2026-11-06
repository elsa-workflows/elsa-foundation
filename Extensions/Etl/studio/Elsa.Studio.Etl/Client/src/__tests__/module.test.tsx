import { describe, expect, it, vi } from "vitest";
import { createContributionRegistry, createEndpointContext, studioSlots,
  type StudioActivityPropertyEditorContribution, type StudioFeatureAreaContribution } from "@elsa-workflows/studio-sdk";
import type { StudioBackendSession, StudioWorkflowRunRelatedLinkContribution } from "@elsa-workflows/studio-sdk";
import { register } from "../module";

// Real SDK registries prove this contribution uses the public slots and their ordering contracts.
describe("ETL module registration", () => {
  it("owns its routes and claims only the scalar ETL Pipeline property", () => {
    const api = {
      backend: createEndpointContext("/backend"),
      featureAreas: createContributionRegistry<StudioFeatureAreaContribution>({ slot: studioSlots.featureAreas }),
      propertyEditors: createContributionRegistry<StudioActivityPropertyEditorContribution>({ slot: studioSlots.propertyEditors }),
      dialogs: { confirm: vi.fn(async () => false), prompt: vi.fn(async () => null), alert: vi.fn(async () => {}) }
    };
    register(api);
    const area = api.featureAreas.list()[0];
    expect(area.ownedPaths).toEqual(["/etl/definitions", "/etl/connectors", "/etl/runs"]);
    expect(area.routes.map(route => route.path)).toEqual(area.ownedPaths);
    const editor = api.propertyEditors.list()[0];
    const input = { name: "Pipeline", typeName: "System.String", uiHint: "etl-published-selection" };
    const context = { activity: {}, expressionDescriptors: [] };
    expect(editor.order).toBeLessThan(500);
    expect(editor.supports(input, context)).toBe(true);
    expect(editor.supports(input, { ...context, readOnly: true })).toBe(true);
    expect(editor.supports(input, { ...context, scope: "collection" })).toBe(false);
    expect(editor.supports({ ...input, uiHint: null }, context)).toBe(false);
    expect(editor.supports({ ...input, name: "AnotherInput" }, context)).toBe(false);
  });

  it("contributes the ETL-owned native run link through the optional public Slot", () => {
    const backend = createEndpointContext("/backend");
    const api = { backend, backendSession: { profile: { id: "backend-1" } } as StudioBackendSession,
      featureAreas: createContributionRegistry<StudioFeatureAreaContribution>({ slot: studioSlots.featureAreas }),
      propertyEditors: createContributionRegistry<StudioActivityPropertyEditorContribution>({ slot: studioSlots.propertyEditors }),
      workflowRunRelatedLinks: createContributionRegistry<StudioWorkflowRunRelatedLinkContribution>({ slot: studioSlots.workflowRunRelatedLinks }),
      dialogs: { confirm: vi.fn(async () => false), prompt: vi.fn(async () => null), alert: vi.fn(async () => {}) } };
    register(api);
    const contribution = api.workflowRunRelatedLinks.list()[0];
    expect(contribution.activityTypes).toEqual(["Elsa.Etl.Workflows.Runtime.Activities.RunEtl"]);
    expect(contribution.requiresBackend).toEqual([{ id: "elsa.api.etl-runtime", contractVersion: "1" }]);
    const links = contribution.getLinks({ workflowExecutionId: "native", activityExecutionId: "activity", executableNodeId: "node",
      returnTo: "/workflows/instances/native?backend=backend-1&selected=activity", stateView: { kind: "etl.run-reference", version: 1, data: {
        runId: "etl-1", resultAddress: { workflowExecutionId: "native", activityExecutionId: "activity", nodeId: "node", registrationId: "registration", resumeTargetId: "resume" }
      } } });
    expect(links).toHaveLength(1); expect(new URL(links[0].path, "https://studio.example").searchParams.get("runId")).toBe("etl-1");
  });
});
