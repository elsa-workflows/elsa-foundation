import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { listActiveAgentDraftChoices } from "../agentsApi";

const workflowApi = vi.hoisted(() => ({ draftSummary: vi.fn(), versionSummary: vi.fn(), listVersions: vi.fn() }));

vi.mock("@elsa-workflows/studio-workflows/authoring", () => ({
  ActivityDefinitionDraftEditor: (props: Record<string, unknown>) => <div data-testid="shared-editor"
    data-draft-id={props.draftId as string | undefined} data-version-id={props.versionId as string | undefined}
    data-read-only={String(props.readOnly)} data-start-editing={String(props.allowStartEditing)}>
    <button onClick={() => (props.onManage as (section: string) => void)("versions")}>Versions</button>
  </div>,
  getActivityDefinitionDraftSummary: workflowApi.draftSummary,
  getActivityDefinitionVersionSummary: workflowApi.versionSummary,
  listAllActivityDefinitionVersions: workflowApi.listVersions
}));

import { AgentWorkspace } from "../AgentWorkspace";

const cleanup = new Set<() => void>();
const originalUrl = `${window.location.pathname}${window.location.search}`;
afterEach(() => {
  for (const dispose of cleanup) dispose();
  cleanup.clear();
  vi.clearAllMocks();
  window.history.replaceState({}, "", originalUrl);
});

async function settle() {
  for (let index = 0; index < 8; index++) await new Promise(resolve => setTimeout(resolve, 0));
}

function fakeContext(options: { drafts?: Array<{ draftId: string; revision?: number; presentationLabel?: string }>; failDraftList?: boolean } = {}) {
  workflowApi.draftSummary.mockImplementation(async (_context: StudioEndpointContext, definitionId: string, draftId: string) => ({
    draft: { draftId, definitionId, revision: 1, providerKey: "elsa.agent", providerSchemaVersion: "2", status: "Active", updatedAt: "2026-01-01T00:00:00Z" }, actions: []
  }));
  workflowApi.versionSummary.mockImplementation(async (_context: StudioEndpointContext, definitionId: string, versionId: string) => ({
    version: { versionId, definitionId, version: "1.0.0", lifecycle: "Active", publishedAt: "2026-01-01T00:00:00Z" }, providerKey: "elsa.agent", providerSchemaVersion: "2", isRecommended: true, actions: []
  }));
  workflowApi.listVersions.mockResolvedValue([]);
  const getJson = vi.fn(async (path: string) => {
    if (path.includes("/drafts?")) {
      if (options.failDraftList) throw new Error("Drafts could not be read.");
      return { items: (options.drafts ?? []).map(draft => ({ draft: { ...draft, revision: draft.revision ?? 2 } })) };
    }
    throw new Error(`Unexpected request: ${path}`);
  });
  return { context: { http: { getJson } } as unknown as StudioEndpointContext, getJson };
}

async function render(context: StudioEndpointContext, path: string, navigate = vi.fn()) {
  window.history.replaceState({}, "", path);
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(<AgentWorkspace context={context} navigate={navigate} activityEditors={[]} />));
  await settle();
  cleanup.add(() => { root.unmount(); container.remove(); });
  return { container, navigate };
}

describe("Agent workspace resolution", () => {
  it("follows the cursor to include every active Agent draft choice", async () => {
    const getJson = vi.fn()
      .mockResolvedValueOnce({ items: [{ draft: { draftId: "draft-a", revision: 1 } }], hasMore: true, continuation: "next-page" })
      .mockResolvedValueOnce({ items: [{ draft: { draftId: "draft-b", revision: 2 } }], hasMore: false, continuation: null });
    const context = { http: { getJson } } as unknown as StudioEndpointContext;

    await expect(listActiveAgentDraftChoices(context, "copywriter")).resolves.toMatchObject([{ draftId: "draft-a" }, { draftId: "draft-b" }]);
    expect(getJson).toHaveBeenNthCalledWith(1, "/design/activities/definitions/copywriter/drafts?limit=100&providerKey=elsa.agent&status=Active", expect.anything());
    expect(getJson).toHaveBeenNthCalledWith(2, "/design/activities/definitions/copywriter/drafts?limit=100&providerKey=elsa.agent&status=Active&cursor=next-page", expect.anything());
  });

  it("routes a single active draft to its exact workspace link", async () => {
    const { context, getJson } = fakeContext({ drafts: [{ draftId: "draft-1", revision: 7 }] });
    const navigate = vi.fn();
    const { container } = await render(context, "/agents/workspace/copywriter", navigate);

    expect(container.querySelector("[data-testid='shared-editor']")).toBeNull();
    expect(navigate).toHaveBeenCalledWith("/agents/workspace/copywriter/drafts/draft-1");
    expect(getJson).toHaveBeenCalledWith("/design/activities/definitions/copywriter/drafts?limit=100&providerKey=elsa.agent&status=Active", expect.anything());
    expect(workflowApi.draftSummary).toHaveBeenCalledWith(context, "copywriter", "draft-1", expect.anything());
  });

  it("offers an in-workspace choice for parallel drafts without timestamp guessing", async () => {
    const { context } = fakeContext({ drafts: [
      { draftId: "draft-a", revision: 3, presentationLabel: "Alternate" },
      { draftId: "draft-b", revision: 8, presentationLabel: "Current work" },
      { draftId: "draft-c", revision: 4, presentationLabel: "Earlier work" }
    ] });
    const { container, navigate } = await render(context, "/agents/workspace/copywriter");

    expect(container.textContent).toContain("Studio will not guess from timestamps.");
    expect(container.textContent).toContain("Alternate");
    expect(container.textContent).toContain("Current work");
    expect(container.textContent).toContain("Earlier work");
    expect([...container.querySelectorAll("button")].filter(button => button.textContent === "Open draft")).toHaveLength(3);
    expect(navigate).not.toHaveBeenCalled();
    [...container.querySelectorAll("button")].find(button => button.textContent === "Open draft")!.click();
    await settle();
    expect(navigate).toHaveBeenCalledWith("/agents/workspace/copywriter/drafts/draft-a");
  });

  it("does not fall back to a published version when the draft lookup fails", async () => {
    const { context, getJson } = fakeContext({ failDraftList: true });
    const { container } = await render(context, "/agents/workspace/copywriter?fallbackVersion=published-1");

    expect(container.textContent).toContain("Drafts could not be read.");
    expect(container.querySelector("[data-testid='shared-editor']")).toBeNull();
    expect(workflowApi.versionSummary).not.toHaveBeenCalled();
  });

  it("opens an explicit exact version read-only", async () => {
    const { context } = fakeContext();
    const { container } = await render(context, "/agents/workspace/copywriter/versions/published-1");
    const editor = container.querySelector<HTMLElement>("[data-testid='shared-editor']");

    expect(editor?.dataset.versionId).toBe("published-1");
    expect(editor?.dataset.readOnly).toBe("true");
    expect(editor?.dataset.startEditing).toBe("false");
    expect(workflowApi.versionSummary).toHaveBeenCalledWith(context, "copywriter", "published-1", expect.anything());
  });

  it("lists only Agent-owned published versions and opens the selected version read-only", async () => {
    const { context } = fakeContext();
    workflowApi.listVersions.mockResolvedValue([
      { version: { versionId: "agent-version", definitionId: "copywriter", version: "1.1.0", lifecycle: "Active", publishedAt: "2026-01-01T00:00:00Z" }, providerKey: "elsa.agent" },
      { version: { versionId: "workflow-version", definitionId: "copywriter", version: "2.0.0", lifecycle: "Active", publishedAt: "2026-01-02T00:00:00Z" }, providerKey: "elsa.activity-graph" }
    ]);
    const { container, navigate } = await render(context, "/agents/workspace/copywriter/drafts/draft-1");
    [...container.querySelectorAll("button")].find(button => button.textContent === "Versions")!.click();
    await settle();

    expect(container.textContent).toContain("Version 1.1.0");
    expect(container.textContent).not.toContain("2.0.0");
    [...container.querySelectorAll("button")].find(button => button.textContent === "Open read-only")!.click();
    expect(navigate).toHaveBeenCalledWith("/agents/workspace/copywriter/versions/agent-version");
  });

  it("does not render a draft from another provider", async () => {
    workflowApi.draftSummary.mockResolvedValueOnce({ draft: { draftId: "draft-1", providerKey: "elsa.activity-graph" }, actions: [] });
    const { context } = fakeContext();
    const { container } = await render(context, "/agents/workspace/copywriter/drafts/draft-1");

    expect(container.textContent).toContain("not an Agent draft");
    expect(container.querySelector("[data-testid='shared-editor']")).toBeNull();
  });

  it("opens the published fallback in the workspace with explicit draft creation enabled", async () => {
    const { context } = fakeContext();
    const { container } = await render(context, "/agents/workspace/copywriter?fallbackVersion=published-1");
    const editor = container.querySelector<HTMLElement>("[data-testid='shared-editor']");

    expect(editor?.dataset.versionId).toBe("published-1");
    expect(editor?.dataset.readOnly).toBe("false");
    expect(editor?.dataset.startEditing).toBe("true");
  });
});