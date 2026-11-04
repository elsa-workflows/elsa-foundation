import { Bot } from "lucide-react";
import { describe, expect, it, vi } from "vitest";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { register } from "../module";

function registered() {
  const routes = vi.fn();
  const relatedLinks = vi.fn();
  const featureAreas = vi.fn();
  const activityEditors = vi.fn();
  register({ routes: { add: routes }, featureAreas: { add: featureAreas }, activityEditors: { add: activityEditors }, workflowRunRelatedLinks: { add: relatedLinks },
    backend: {} } as unknown as ElsaStudioModuleApi);
  return { routes, featureAreas, activityEditors, relatedLinks };
}

describe("agent specialists module", () => {
  it("adds the Agents area behind the generic activity design capability", () => {
    const { featureAreas } = registered();

    expect(featureAreas).toHaveBeenCalledTimes(1);
    const area = featureAreas.mock.calls[0][0];
    expect(area).toMatchObject({ id: "agents", ownedPaths: ["/agents"], required: false, nav: { path: "/agents", icon: Bot } });
    expect(area.routes).toEqual([expect.objectContaining({ path: "/agents", requiresBackend: [{ id: "elsa.api.activity-design", contractVersion: "1" }] })]);
  });

  it("contributes one implementation form per specialist manifest schema, needing the authoring API", () => {
    const { activityEditors } = registered();

    const contributions = activityEditors.mock.calls.map(call => call[0]);
    expect(contributions.map(x => [x.providerKey, x.providerSchemaVersion])).toEqual([["elsa.specialist", "1"], ["elsa.specialist", "2"]]);
    for (const contribution of contributions)
      expect(contribution.requiresBackend).toEqual(expect.arrayContaining([{ id: "elsa.api.agent-specialists", contractVersion: "1" }]));
    expect("allowContinuity" in contributions[0].createInitialImplementation().payload).toBe(false);
    expect(contributions[1].createInitialImplementation().payload).toMatchObject({ allowContinuity: false });
  });

  it("keeps the review route behind the agent reviews capability", () => {
    const { routes } = registered();

    expect(routes).toHaveBeenCalledWith(expect.objectContaining({
      path: "/agents/reviews/:workflowExecutionId/:activityExecutionId",
      requiresBackend: [{ id: "elsa.api.agent-reviews", contractVersion: "1" }]
    }));
  });

  it("links any agent occurrence in run inspection to its invocation page, decided by the state view", () => {
    const { relatedLinks } = registered();
    const contribution = relatedLinks.mock.calls[0][0];
    expect(contribution).toMatchObject({ anyActivityType: true, viewKind: "elsa.specialist.invocation", viewVersion: 1,
      requiresBackend: [{ id: "elsa.api.agent-specialist-inspection", contractVersion: "1", relations: ["agent-specialist-invocation-evidence"] }] });
    const context = { workflowExecutionId: "run 1", activityExecutionId: "act-1", executableNodeId: "node-1", returnTo: "/workflows/instances/run%201" };
    expect(contribution.getLinks({ ...context, stateView: { kind: "elsa.specialist.invocation", version: 1, data: { invocation: true } } }))
      .toEqual([{ id: "invocation", label: "Agent invocation", path: "/agents/invocations/run%201/act-1" }]);
    expect(contribution.getLinks({ ...context, stateView: { kind: "elsa.specialist.invocation", version: 1, data: { invocation: false } } })).toEqual([]);
  });

  it("keeps the invocation page behind the occurrence evidence capability", () => {
    const { routes } = registered();

    expect(routes).toHaveBeenCalledWith(expect.objectContaining({
      path: "/agents/invocations/:workflowExecutionId/:activityExecutionId",
      requiresBackend: [{ id: "elsa.api.agent-specialist-inspection", contractVersion: "1", relations: ["agent-specialist-invocation-evidence"] }]
    }));
  });

  it("keeps the playground behind the generic draft Test Run and the agent evidence capabilities", () => {
    const { routes } = registered();

    expect(routes).toHaveBeenCalledWith(expect.objectContaining({
      path: "/agents/drafts/:draftId/playground",
      requiresBackend: [
        { id: "elsa.api.activity-design", contractVersion: "1" },
        { id: "elsa.api.publishing", contractVersion: "1", relations: ["activity-draft-test-run-dispatch", "activity-draft-test-run-cancel"] },
        { id: "elsa.api.agent-specialist-inspection", contractVersion: "1", relations: ["agent-specialist-test-run-evidence"] }
      ]
    }));
  });
});
