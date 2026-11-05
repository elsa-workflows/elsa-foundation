import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioAgentContextAttachment, StudioAgentRegistry, StudioAiPromptActionContribution } from "@elsa-workflows/studio-sdk";
import { createAgentRegistry } from "@elsa-workflows/studio-sdk";
import { registerWorkflowAgent, type WorkflowAgentController } from "../workflowAgent";
import { useWorkflowInspectionContext } from "../workflow-editor/useWorkflowInspectionContext";
import { WorkflowVersionHistory } from "../workflow-editor/WorkflowVersionHistory";
import { workflowExecutableAttachment, workflowRunAttachment, workflowVersionAttachment } from "../workflowAgentContext";
import { renderRegisteredRoute, response } from "./routeRenderingHelpers";
import { waitForCondition } from "./workflowDefinitionTestHelpers";

// Spec 1193 slices F and G (stories 8 and 9): the Executable Inspector's, the version history's and the run page's read-only
// page contexts.

async function collect(agent: StudioAgentRegistry, id: string): Promise<StudioAgentContextAttachment[]> {
  const provider = agent.contextProviders.list().find(item => item.id === id)!;
  const result = await provider.collect({ surface: { route: "/workflows" }, mode: "explain" } as never);
  return Array.isArray(result) ? result : result.attachments;
}

function mount() {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  return { container, render: (node: React.ReactNode) => flushSync(() => root.render(node)), unmount: () => { flushSync(() => root.unmount()); container.remove(); } };
}

/** A user click, then the effects it schedules. */
async function click(element: HTMLElement) {
  flushSync(() => element.click());
  await new Promise(resolve => setTimeout(resolve, 0));
}

function Inspected({ controller, identity, kind = "executable" }: { controller: WorkflowAgentController; identity: Record<string, unknown> | null; kind?: "executable" | "run" }) {
  useWorkflowInspectionContext({ controller, clientId: "client-1", kind, identity });
  return null;
}

afterEach(() => vi.unstubAllGlobals());

describe("read-only page context attachments", () => {
  it("binds an Executable to its Artifact Hash and a version to its version identity, never half-formed", () => {
    expect(workflowExecutableAttachment({ artifactId: "a-1", artifactHash: "sha256:h1", sourceReferenceId: "ref-1" }, { clientId: "c", generationId: "g" })).toEqual({
      id: "workflows.executable:a-1", moduleId: "Elsa.Studio.Workflows", schemaVersion: "1", contentType: "workflows.executable",
      sensitivity: "internal", scope: "screen", origin: "clientSnapshot", source: "workflow-executable", sourceId: "a-1", label: "a-1",
      summary: "Workflow Executable a-1", target: { moduleId: "Elsa.Studio.Workflows", resourceType: "workflow-executable", resourceId: "a-1", revision: "sha256:h1" },
      client: { clientId: "c", generationId: "g" }, content: { artifactId: "a-1", artifactHash: "sha256:h1", sourceReferenceId: "ref-1" }
    });
    expect(workflowExecutableAttachment({ artifactId: "a-1" })).toBeNull();
    expect(workflowExecutableAttachment({ artifactId: "x".repeat(129), artifactHash: "h" })).toBeNull();
    expect(workflowVersionAttachment({ definitionId: "d-1", versionId: "v-1", version: "1.2.0" })).toMatchObject({
      id: "workflows.version:v-1", contentType: "workflows.version", label: "v1.2.0",
      target: { moduleId: "Elsa.Studio.Workflows", resourceType: "workflow-version", resourceId: "v-1", revision: "v-1" },
      content: { definitionId: "d-1", versionId: "v-1" }
    });
    expect(workflowVersionAttachment({ versionId: "v-1" })).toBeNull();
  });
});

describe("run page context (spec 1193 FR-005, FR-029)", () => {
  it("binds one exact Run to its instance and the selected activity execution, never the latest run or a half-formed reference", () => {
    expect(workflowRunAttachment({ workflowExecutionId: "run-1", activityExecutionId: "act-1" }, { clientId: "c", generationId: "g" })).toEqual({
      id: "workflows.run:run-1", moduleId: "Elsa.Studio.Workflows", schemaVersion: "1", contentType: "workflows.run",
      sensitivity: "internal", scope: "screen", origin: "clientSnapshot", source: "workflow-run", sourceId: "run-1", label: "Run run-1 · act-1",
      summary: "Workflow Run run-1, activity execution act-1",
      target: { moduleId: "Elsa.Studio.Workflows", resourceType: "workflow-run", resourceId: "run-1", revision: "act-1" },
      client: { clientId: "c", generationId: "g" }, content: { workflowExecutionId: "run-1", activityExecutionId: "act-1" }
    });
    expect(workflowRunAttachment({ workflowExecutionId: "run-1" })).toMatchObject({
      target: { resourceId: "run-1", revision: "run-1" }, content: { workflowExecutionId: "run-1", activityExecutionId: null } });
    expect(workflowRunAttachment({ definitionId: "d-1", latest: true })).toBeNull();
    expect(workflowRunAttachment({ workflowExecutionId: "run-1", activityExecutionId: "" })).toBeNull();
    expect(workflowRunAttachment({ workflowExecutionId: "run-1", activityExecutionId: "run-1" })).toBeNull();
    expect(workflowRunAttachment({ workflowExecutionId: "x".repeat(257) })).toBeNull();
  });

  it("publishes the shown Run, follows the selected activity execution and withdraws it when unloaded or unmounted", async () => {
    const agent = createAgentRegistry();
    const controller = registerWorkflowAgent(agent);
    const host = mount();
    host.render(<Inspected kind="run" controller={controller} identity={{ workflowExecutionId: "run-1", activityExecutionId: null }} />);
    const [first] = await collect(agent, "workflows.run-inspector");
    expect(first).toMatchObject({ contentType: "workflows.run", target: { resourceId: "run-1", revision: "run-1" }, client: { clientId: "client-1" } });
    host.render(<Inspected kind="run" controller={controller} identity={{ workflowExecutionId: "run-1", activityExecutionId: "act-2" }} />);
    const current = await collect(agent, "workflows.run-inspector");
    expect(current.map(item => item.target?.revision)).toEqual(["act-2"]);
    expect(current[0].client?.generationId).toBe(first.client?.generationId);
    // Never mixed into the Executable Inspector's context.
    expect(await collect(agent, "workflows.executable-inspector")).toEqual([]);
    host.render(<Inspected kind="run" controller={controller} identity={null} />);
    expect(await collect(agent, "workflows.run-inspector")).toEqual([]);
    host.render(<Inspected kind="run" controller={controller} identity={{ workflowExecutionId: "run-1", activityExecutionId: "act-2" }} />);
    expect(await collect(agent, "workflows.run-inspector")).toHaveLength(1);
    host.unmount();
    expect(await collect(agent, "workflows.run-inspector")).toEqual([]);
  });
});

describe("Executable Inspector context (spec 1193 FR-028)", () => {
  it("publishes the loaded Executable bound to its hash, replaces it on a new hash and withdraws it when unloaded or unmounted", async () => {
    const agent = createAgentRegistry();
    const controller = registerWorkflowAgent(agent);
    const host = mount();
    host.render(<Inspected controller={controller} identity={{ artifactId: "a-1", artifactHash: "sha256:h1", sourceReferenceId: null }} />);
    const [first] = await collect(agent, "workflows.executable-inspector");
    expect(first).toMatchObject({ contentType: "workflows.executable", target: { resourceId: "a-1", revision: "sha256:h1" },
      client: { clientId: "client-1" }, content: { artifactId: "a-1", artifactHash: "sha256:h1", sourceReferenceId: null } });
    host.render(<Inspected controller={controller} identity={{ artifactId: "a-1", artifactHash: "sha256:h2", sourceReferenceId: null }} />);
    const current = await collect(agent, "workflows.executable-inspector");
    expect(current.map(item => item.target?.revision)).toEqual(["sha256:h2"]);
    // The same page instance keeps one client generation across its identity changes.
    expect(current[0].client?.generationId).toBe(first.client?.generationId);
    host.render(<Inspected controller={controller} identity={null} />);
    expect(await collect(agent, "workflows.executable-inspector")).toEqual([]);
    host.render(<Inspected controller={controller} identity={{ artifactId: "a-1", artifactHash: "sha256:h2" }} />);
    expect(await collect(agent, "workflows.executable-inspector")).toHaveLength(1);
    host.unmount();
    expect(await collect(agent, "workflows.executable-inspector")).toEqual([]);
  });

  it("is fed by the routed inspector while its executable is shown, and the Explain action leaves the context to it", async () => {
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("/executables/artifact-1")) return response({
        artifactId: "artifact-1", artifactHash: "sha256:abc", createdAt: "2026-06-18T01:00:00Z", rootActivityType: "Elsa.Activities.Primitives.Activities.WriteLine",
        rootActivityVersion: "1.0.0", nodeCount: 1, resumeTargetCount: 0, liveSourceReferenceCount: 1, retainedExecutionCount: 0,
        rootActivity: { executableNodeId: "exec-root", authoredActivityId: "root", activityType: "Elsa.Activities.Primitives.Activities.WriteLine",
          activityTypeVersion: "1.0.0", structureKind: null, inputBindings: [], childSlots: [], connections: [] },
        metadata: {}, chosenReference: { sourceReferenceId: "ref-1", selection: "newest-live", layout: [] }, references: []
      });
      if (url.includes("/activities")) return response({ activities: [] });
      return response(null, 404);
    }));
    const dispatchPrompt = vi.fn();
    const explain: StudioAiPromptActionContribution = { id: "weaver.workflows.explain-executable", label: "Explain executable", placement: "selection",
      contextKind: "workflow-executable", createPrompt: context => ({ message: "Explain", mode: "enqueue", attachments: [], context } as never) };
    const { api, container, unmount } = await renderRegisteredRoute("/workflows/executables/artifact-1", configured => {
      configured.agent.promptActions.add(explain);
      configured.agent.dispatchPrompt = dispatchPrompt;
    });
    // Resolve the routed lazy page and its data before checking the passive context registration.
    await waitForCondition(() => expect(container.querySelector("section[aria-label='Executable canvas']")).not.toBeNull());
    await vi.waitFor(async () => expect(await collect(api.agent, "workflows.executable-inspector")).toHaveLength(1));
    const [attachment] = await collect(api.agent, "workflows.executable-inspector");
    expect(attachment).toMatchObject({ id: "workflows.executable:artifact-1", target: { resourceType: "workflow-executable", resourceId: "artifact-1", revision: "sha256:abc" },
      content: { artifactId: "artifact-1", artifactHash: "sha256:abc", sourceReferenceId: "ref-1" }, client: { clientId: api.agent.clientId } });

    const button = [...container.querySelectorAll("button")].find(item => item.textContent?.includes("Explain"))!;
    await click(button);
    expect(dispatchPrompt).toHaveBeenCalledWith(expect.objectContaining({
      context: { artifactId: "artifact-1", artifactHash: "sha256:abc", sourceReferenceId: "ref-1", attached: true }
    }));
    await unmount();
    expect(await collect(api.agent, "workflows.executable-inspector")).toEqual([]);
  });
});

describe("workflow version history context (spec 1193 FR-026)", () => {
  it("discloses the selected exact version, explains it with the context left to the page, and withdraws it on deselect", async () => {
    const agent = createAgentRegistry();
    const controller = registerWorkflowAgent(agent);
    const dispatchPrompt = vi.fn();
    agent.dispatchPrompt = dispatchPrompt;
    const explainAction: StudioAiPromptActionContribution = { id: "weaver.workflows.explain-version", label: "Explain version", placement: "selection",
      contextKind: "workflow-version", createPrompt: context => ({ message: "Explain", mode: "enqueue", attachments: [], context } as never) };
    const versions = [{ id: "v-1", version: "1.0.0", createdAt: "2026-06-01T00:00:00Z" }, { id: "v-2", version: "2.0.0", createdAt: "2026-06-02T00:00:00Z" }];
    const host = mount();
    host.render(<WorkflowVersionHistory definitionId="d-1" versions={versions} agent={{ ai: agent, controller, explainAction }} />);
    expect(await collect(agent, "workflows.version-history")).toEqual([]);
    expect(host.container.textContent).not.toContain("Explain version");

    const select = host.container.querySelector<HTMLButtonElement>('button[aria-label="Select version 2.0.0"]')!;
    await click(select);
    expect(select.getAttribute("aria-pressed")).toBe("true");
    const [attachment] = await collect(agent, "workflows.version-history");
    expect(attachment).toMatchObject({ id: "workflows.version:v-2", contentType: "workflows.version",
      target: { resourceType: "workflow-version", resourceId: "v-2", revision: "v-2" }, content: { definitionId: "d-1", versionId: "v-2" } });

    const explain = [...host.container.querySelectorAll("button")].find(item => item.textContent === "Explain version v2.0.0")!;
    await click(explain);
    expect(dispatchPrompt).toHaveBeenCalledWith(expect.objectContaining({ context: { definitionId: "d-1", versionId: "v-2", version: "2.0.0", attached: true } }));

    await click(select);
    expect(select.getAttribute("aria-pressed")).toBe("false");
    expect(await collect(agent, "workflows.version-history")).toEqual([]);
    host.unmount();
  });

  it("lists versions without selection when no Weaver state is composed", () => {
    const host = mount();
    host.render(<WorkflowVersionHistory definitionId="d-1" versions={[{ id: "v-1", version: "1.0.0", createdAt: "2026-06-01T00:00:00Z" }]} />);
    expect(host.container.textContent).toContain("v1.0.0");
    expect(host.container.querySelector("button")).toBeNull();
    host.render(<WorkflowVersionHistory definitionId="d-1" versions={[]} />);
    expect(host.container.textContent).toContain("No published versions yet.");
    host.unmount();
  });
});
