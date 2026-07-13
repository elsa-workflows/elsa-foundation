import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { clearApiCapabilityCache } from "../api/capabilities";
import type { PublicationIntent } from "../api/publishing";
import type { WorkflowDefinitionDetails, WorkflowDraft } from "../workflowTypes";
import { useWorkflowOperations } from "../workflow-editor/useWorkflowOperations";

type Operations = ReturnType<typeof useWorkflowOperations>;

let mounted: { root: ReturnType<typeof createRoot>; container: HTMLDivElement } | null = null;

afterEach(() => {
  clearApiCapabilityCache();
  if (!mounted) return;
  flushSync(() => mounted!.root.unmount());
  mounted.container.remove();
  mounted = null;
});

describe("workflow publication operations", () => {
  it("fails closed when authoritative review data cannot be loaded", async () => {
    const fixture = renderOperations({ failPolicyLoad: true });

    await fixture.current().preparePublication();
    await flushUpdates();

    expect(fixture.current().publicationReview).toBeNull();
    expect(fixture.saveDraft).not.toHaveBeenCalled();
    expect(fixture.postJson).not.toHaveBeenCalled();
    expect(fixture.setAutosavePaused).toHaveBeenNthCalledWith(1, true);
    expect(fixture.setAutosavePaused).toHaveBeenLastCalledWith(false);
    expect(fixture.setError.mock.calls.at(-1)?.[0]).toContain("Could not prepare a trustworthy publication review");
  });

  it("cancels a prepared review without any save, promote, preflight, or publish mutation", async () => {
    const fixture = renderOperations();

    await prepare(fixture);
    flushSync(() => fixture.current().cancelPublication());

    expect(fixture.saveDraft).not.toHaveBeenCalled();
    expect(fixture.postJson).not.toHaveBeenCalled();
    expect(fixture.setAutosavePaused.mock.calls).toEqual([[true], [false]]);
    expect(fixture.current().publicationReview).toBeNull();
    expect(fixture.setStatus).toHaveBeenLastCalledWith("Publication cancelled; no changes were saved, promoted, or published.");
  });

  it("publishes the immutable reviewed snapshot through save, promote, preflight, and publish", async () => {
    const source = draft();
    const fixture = renderOperations({ draft: source });
    await prepare(fixture);
    source.state.inputs = [{ name: "not-reviewed" }];

    await publish(fixture);

    expect(fixture.saveDraft).toHaveBeenCalledTimes(1);
    expect(fixture.saveDraft.mock.calls[0][0].state.inputs).toBeUndefined();
    expect(fixture.postJson.mock.calls.map(([url]) => url)).toEqual([
      "/design/workflows/drafts/draft-1/promote",
      "/publishing/workflows/version-2/preflight",
      "/publishing/workflows/version-2/publish"
    ]);
    expect(fixture.mutationOrder).toEqual(["save", "promote", "preflight", "publish"]);
    expect(fixture.current().publicationReview).toMatchObject({
      phase: "success",
      promotedVersionId: "version-2",
      proposedVersion: "2.0.0",
      published: { artifactId: "artifact-2", sourceReferenceId: "reference-2" }
    });
  });

  it("blocks local validation before any mutation", async () => {
    const invalid = draft();
    invalid.validationErrors = [{ message: "A start activity is required." }];
    const fixture = renderOperations({ draft: invalid });
    await prepare(fixture);

    await publish(fixture);

    expect(fixture.saveDraft).not.toHaveBeenCalled();
    expect(fixture.postJson).not.toHaveBeenCalled();
    expect(fixture.current().publicationReview).toMatchObject({
      phase: "validationBlocked",
      validationErrors: ["A start activity is required."]
    });
  });

  it("stops after save when the captured snapshot fails authoritative validation", async () => {
    const fixture = renderOperations({ savedValidationErrors: ["Trigger route is invalid."] });
    await prepare(fixture);

    await publish(fixture);

    expect(fixture.saveDraft).toHaveBeenCalledTimes(1);
    expect(fixture.postJson).not.toHaveBeenCalled();
    expect(fixture.current().publicationReview).toMatchObject({
      phase: "savedFailure",
      validationErrors: ["Trigger route is invalid."],
      failureMessage: expect.stringContaining("draft was saved")
    });
    expect(fixture.current().publicationReview?.savedDraft).toBeDefined();
    flushSync(() => fixture.current().cancelPublication());
    expect(fixture.setStatus).toHaveBeenLastCalledWith("Publication review closed; the draft was saved, but no version was promoted or published.");
  });

  it("retries promotion of the already-saved snapshot without saving a second time", async () => {
    const fixture = renderOperations({ failPromoteAttempts: 1 });
    await prepare(fixture);

    await publish(fixture);

    expect(fixture.current().publicationReview).toMatchObject({
      phase: "savedFailure",
      savedDraft: { id: "draft-1" },
      failureMessage: expect.stringContaining("promotion failed")
    });

    await publish(fixture);

    expect(fixture.saveDraft).toHaveBeenCalledTimes(1);
    expect(fixture.postJson.mock.calls.filter(([url]) => url.endsWith("/promote"))).toHaveLength(2);
    expect(fixture.current().publicationReview).toMatchObject({
      phase: "success",
      promotedVersionId: "version-2",
      proposedVersion: "2.0.0"
    });
  });

  it("blocks the reserved default side-by-side slot before any mutation", async () => {
    const fixture = renderOperations();
    await prepare(fixture);

    await fixture.current().confirmPublication({ action: "sideBySide", slotName: " default " });
    await flushUpdates();

    expect(fixture.saveDraft).not.toHaveBeenCalled();
    expect(fixture.postJson).not.toHaveBeenCalled();
    expect(fixture.setError).toHaveBeenLastCalledWith("The default slot is reserved for replacement publication. Choose another named slot.");
  });

  it("retains and identifies the promoted version when publishing fails", async () => {
    const fixture = renderOperations({ failPublish: true });
    await prepare(fixture);

    await publish(fixture);

    expect(fixture.current().publicationReview).toMatchObject({
      phase: "partialFailure",
      promotedVersionId: "version-2"
    });
    expect(fixture.current().publicationReview?.failureMessage).toContain("The promoted version was retained");
    expect(fixture.current().publicationReview?.failureMessage).toContain("retry Publish");
  });

  it("retries a retained promoted version without saving or promoting again", async () => {
    const fixture = renderOperations({ failPublishAttempts: 1 });
    await prepare(fixture);
    await publish(fixture);

    await publish(fixture);

    expect(fixture.saveDraft).toHaveBeenCalledTimes(1);
    expect(fixture.postJson.mock.calls.filter(([url]) => url.endsWith("/promote"))).toHaveLength(1);
    expect(fixture.postJson.mock.calls.filter(([url]) => url.endsWith("/preflight"))).toHaveLength(2);
    expect(fixture.postJson.mock.calls.filter(([url]) => url.endsWith("/publish"))).toHaveLength(2);
    expect(fixture.current().publicationReview?.phase).toBe("success");
  });
});

async function prepare(fixture: ReturnType<typeof renderOperations>) {
  await fixture.current().preparePublication();
  await flushUpdates();
  expect(fixture.current().publicationReview).not.toBeNull();
}

async function publish(fixture: ReturnType<typeof renderOperations>) {
  const intent: PublicationIntent = { action: "replace", slotName: "default", expectedPublicationId: "publication-1" };
  await fixture.current().confirmPublication(intent);
  await flushUpdates();
}

async function flushUpdates() {
  await new Promise(resolve => setTimeout(resolve, 0));
  flushSync(() => undefined);
}

function renderOperations(options: {
  draft?: WorkflowDraft;
  failPublish?: boolean;
  failPublishAttempts?: number;
  failPromoteAttempts?: number;
  savedValidationErrors?: string[];
  failPolicyLoad?: boolean;
} = {}) {
  const sourceDraft = options.draft ?? draft();
  const mutationOrder: string[] = [];
  let publishAttempts = 0;
  let promoteAttempts = 0;
  const getJson = vi.fn(async (url: string) => {
    if (url === "/capabilities") return capabilities;
    if (url === "/publishing/workflows/definition-1/policy") {
      if (options.failPolicyLoad) throw new Error("policy unavailable");
      return { defaultAction: "replace", defaultSlotName: "default", source: "host" };
    }
    if (url === "/publishing/workflows/definition-1/slots") return { items: [slot()] };
    if (url === "/design/workflows/versions/version-1") return { id: "version-1", version: "1.0.0", definition: details().definition, state: sourceDraft.state, layout: [] };
    throw new Error(`Unexpected GET ${url}`);
  });
  const postJson = vi.fn(async (url: string) => {
    if (url === "/design/workflows/drafts/draft-1/promote") {
      mutationOrder.push("promote");
      promoteAttempts += 1;
      if (promoteAttempts <= (options.failPromoteAttempts ?? 0)) throw new Error("promotion unavailable");
      return { id: "version-2", version: "2.0.0" };
    }
    if (url === "/publishing/workflows/version-2/preflight") {
      mutationOrder.push("preflight");
      return {
        definitionId: "definition-1",
        versionId: "version-2",
        slotName: "default",
        resolvedAction: "replace",
        policySource: "request",
        canActivate: true,
        triggers: [],
        conflicts: []
      };
    }
    if (url === "/publishing/workflows/version-2/publish") {
      mutationOrder.push("publish");
      publishAttempts += 1;
      if (options.failPublish || publishAttempts <= (options.failPublishAttempts ?? 0)) throw new Error("activation timed out");
      return {
        publicationId: "publication-2",
        definitionId: "definition-1",
        versionId: "version-2",
        artifactId: "artifact-2",
        slotName: "default",
        sourceReferenceId: "reference-2",
        status: "active"
      };
    }
    throw new Error(`Unexpected POST ${url}`);
  });
  const context = {
    baseUrl: `test://publication-${Math.random()}`,
    http: { getJson, postJson }
  } as unknown as StudioEndpointContext;
  const saveDraft = vi.fn(async (snapshot: WorkflowDraft) => {
    mutationOrder.push("save");
    const saved = structuredClone(snapshot);
    saved.validationErrors = (options.savedValidationErrors ?? []).map(message => ({ message }));
    return saved;
  });
  const callbacks = {
    reload: vi.fn(async () => undefined),
    startTestRun: vi.fn(),
    clearTestRun: vi.fn(),
    setPublishedArtifact: vi.fn(),
    setOperation: vi.fn(),
    setStatus: vi.fn(),
    setError: vi.fn(),
    setActiveRightPanelId: vi.fn(),
    setInspectorCollapsed: vi.fn(),
    setAutosavePaused: vi.fn()
  };
  let current: Operations | null = null;
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  mounted = { root, container };
  flushSync(() => root.render(<Harness onRender={value => { current = value; }} params={{
    context,
    draft: sourceDraft,
    details: details(),
    catalog: [],
    busy: false,
    saveDraft,
    ...callbacks
  }} />));
  return {
    current: () => current!,
    saveDraft,
    getJson,
    postJson,
    mutationOrder,
    ...callbacks
  };
}

function Harness({ params, onRender }: {
  params: Parameters<typeof useWorkflowOperations>[0];
  onRender(value: Operations): void;
}) {
  onRender(useWorkflowOperations(params));
  return null;
}

function draft(): WorkflowDraft {
  return {
    id: "draft-1",
    definitionId: "definition-1",
    sourceVersionId: "version-1",
    state: { rootActivity: { nodeId: "root", activityVersionId: "root-v1", inputs: [], outputs: [] } },
    layout: [],
    validationErrors: []
  };
}

function details(): WorkflowDefinitionDetails {
  return {
    definition: {
      id: "definition-1",
      name: "Orders",
      createdAt: "2026-07-01T00:00:00Z",
      lastModifiedAt: "2026-07-01T00:00:00Z",
      latestVersion: "1.0.0",
      versionCount: 1
    },
    draft: draft(),
    versions: [{ id: "version-1", version: "1.0.0", createdAt: "2026-07-01T00:00:00Z" }]
  };
}

function slot() {
  return {
    definitionId: "definition-1",
    slotName: "default",
    status: "active",
    publication: {
      publicationId: "publication-1",
      definitionId: "definition-1",
      versionId: "version-1",
      artifactId: "artifact-1",
      artifactVersion: "1.0.0",
      slotName: "default",
      sourceReferenceId: "reference-1",
      status: "active"
    }
  };
}

const capabilities = {
  capabilities: [
    {
      id: "elsa.api.workflow-design",
      contractVersion: "1",
      links: [
        { rel: "workflow-drafts", href: "design/workflows/drafts/{draftId}", templated: true },
        { rel: "workflow-versions", href: "design/workflows/versions/{versionId}", templated: true }
      ]
    },
    {
      id: "elsa.api.publishing",
      contractVersion: "1",
      links: [
        { rel: "publication-policy", href: "publishing/workflows/{definitionId}/policy", templated: true },
        { rel: "publication-slots", href: "publishing/workflows/{definitionId}/slots", templated: true },
        { rel: "publication-preflight", href: "publishing/workflows/{versionId}/preflight", templated: true },
        { rel: "workflow-publish", href: "publishing/workflows/{versionId}/publish", templated: true }
      ]
    }
  ]
};
