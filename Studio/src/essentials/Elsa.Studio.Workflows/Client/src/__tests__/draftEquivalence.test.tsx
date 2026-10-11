import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { clearApiCapabilityCache } from "../api/capabilities";
import { findPublishedEquivalent } from "../workflow-editor/editorHelpers";
import { WorkflowRuntimePanel } from "../workflow-editor/editorPanels";
import { useDraftEquivalence } from "../workflow-editor/useDraftEquivalence";
import type { WorkflowExecutableSummary, WorkflowTestRunView } from "../workflowTypes";
import { activationSlot, publicationRecord } from "./fixtures/publicationSlots";

const definitionId = "definition-1";

function executable(overrides: Partial<WorkflowExecutableSummary> = {}): WorkflowExecutableSummary {
  return {
    artifactId: "artifact-1",
    artifactVersion: "2.0.0",
    artifactHash: "hash-1",
    definitionId,
    definitionVersionId: "version-2",
    createdAt: "2026-07-01T00:00:00Z",
    publishedAt: "2026-07-01T00:00:00Z",
    rootActivityType: "Elsa.Activities.Sequence.Activities.Sequence",
    rootActivityVersion: "1.0.0",
    nodeCount: 3,
    resumeTargetCount: 0,
    ...overrides
  };
}

function testRun(overrides: Partial<WorkflowTestRunView> = {}): WorkflowTestRunView {
  return {
    testRunId: "test-run-1",
    definitionId,
    definitionVersionId: "draft",
    artifactId: "artifact-1",
    workflowExecutionId: "execution-1",
    status: "Running",
    ...overrides
  };
}

describe("findPublishedEquivalent", () => {
  it("matches a published executable of the definition with the test run's artifact id", () => {
    const match = executable();
    const others = [executable({ artifactId: "artifact-0", definitionVersionId: "version-1" })];

    expect(findPublishedEquivalent("artifact-1", [...others, match], definitionId)).toBe(match);
  });

  it("matches through sourceId when the definition id lives on the reference source", () => {
    const match = executable({ definitionId: "other", sourceId: definitionId });

    expect(findPublishedEquivalent("artifact-1", [match], definitionId)).toBe(match);
  });

  it("ignores executables of other definitions", () => {
    expect(findPublishedEquivalent("artifact-1", [executable({ definitionId: "other" })], definitionId)).toBeNull();
  });

  it("ignores rows without a publish timestamp (e.g. the test run's own reference)", () => {
    expect(findPublishedEquivalent("artifact-1", [executable({ publishedAt: null })], definitionId)).toBeNull();
  });

  it("resolves to no signal when the test run carries no artifact id", () => {
    expect(findPublishedEquivalent(null, [executable()], definitionId)).toBeNull();
    expect(findPublishedEquivalent(undefined, [executable()], definitionId)).toBeNull();
  });
});

describe("WorkflowRuntimePanel equivalence caption", () => {
  let active: { root: Root; container: HTMLElement } | null = null;

  afterEach(() => {
    if (active) {
      flushSync(() => active!.root.unmount());
      active.container.remove();
      active = null;
    }
  });

  function render(ui: React.ReactElement) {
    const container = document.createElement("div");
    document.body.appendChild(container);
    const root = createRoot(container);
    flushSync(() => root.render(ui));
    active = { root, container };
    return container;
  }

  it("surfaces the behavioral-identity message when the test run resolves to a published artifact", () => {
    const container = render(
      <WorkflowRuntimePanel testRun={testRun()} publishedEquivalent={executable()} onOpenRun={() => {}} />
    );

    expect(container.querySelector(".wf-runtime-equivalence")?.textContent)
      .toContain("Current draft is behaviorally identical to published v2.0.0");
  });

  it("shows no equivalence message without a published match", () => {
    const container = render(
      <WorkflowRuntimePanel testRun={testRun()} publishedEquivalent={null} onOpenRun={() => {}} />
    );

    expect(container.querySelector(".wf-runtime-equivalence")).toBeNull();
  });

  it("suppresses the caption on a rejected test run even when the artifact id matches", () => {
    const container = render(
      <WorkflowRuntimePanel
        testRun={testRun({ status: "Rejected", reason: "Reference gate closed" })}
        publishedEquivalent={executable()}
        onOpenRun={() => {}}
      />
    );

    expect(container.querySelector(".wf-runtime-equivalence")).toBeNull();
  });

  it("drops a stale signal computed for an earlier test run", () => {
    const container = render(
      <WorkflowRuntimePanel
        testRun={testRun({ artifactId: "artifact-2" })}
        publishedEquivalent={executable({ artifactId: "artifact-1" })}
        onOpenRun={() => {}}
      />
    );

    expect(container.querySelector(".wf-runtime-equivalence")).toBeNull();
  });
});

describe("useDraftEquivalence", () => {
  let active: { root: Root; container: HTMLElement } | null = null;
  const record = publicationRecord("default", { artifactId: "artifact-1", versionId: "version-2", activatedAt: "2026-09-02T00:00:00Z" });

  afterEach(() => {
    clearApiCapabilityCache();
    if (!active) return;
    flushSync(() => active!.root.unmount());
    active.container.remove();
    active = null;
  });

  function resolveEquivalence(options: { slotReads?: boolean; record?: boolean } = {}) {
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return { capabilities: [
        { id: "elsa.api.publishing", contractVersion: "1", links: [
          { rel: "publication-record", href: "publishing/publications/{publicationId}", templated: true }
        ] },
        { id: "elsa.api.runtime", contractVersion: "1", links: [
          { rel: "workflow-executables", href: "runtime/workflows/executables" },
          ...(options.slotReads ?? true
            ? [{ rel: "workflow-activation-slots", href: "runtime/workflows/activation-slots/{definitionId}", templated: true }]
            : [])
        ] }
      ] };
      if (url === "/runtime/workflows/executables?scope=All&includeRetired=true") return [executable({ publishedAt: null })];
      if (url === "/runtime/workflows/activation-slots/definition-1") return { items: [
        activationSlot("default", { activeActivationId: record.publicationId, sourceKind: "publishing" })
      ] };
      if (url === "/publishing/publications/publication-default" && (options.record ?? true)) return record;
      throw Object.assign(new Error(`Not found: ${url}`), { status: 404 });
    });
    const context = { baseUrl: `test://draft-equivalence-${Math.random()}`, http: { getJson } } as unknown as StudioEndpointContext;
    const values: Array<WorkflowExecutableSummary | null> = [];
    function Harness() {
      values.push(useDraftEquivalence(context, definitionId, testRun()));
      return null;
    }
    const container = document.createElement("div");
    document.body.appendChild(container);
    const root = createRoot(container);
    flushSync(() => root.render(<Harness />));
    active = { root, container };
    return { getJson, latest: () => values.at(-1) ?? null };
  }

  it("resolves the published equivalent through the Runtime slot and its publication record", async () => {
    const { latest } = resolveEquivalence();

    await vi.waitFor(() => expect(latest()).not.toBeNull());
    expect(latest()).toMatchObject({
      artifactId: "artifact-1",
      definitionId,
      definitionVersionId: "version-2",
      publishedAt: "2026-09-02T00:00:00Z"
    });
  });

  it("resolves to no signal when the backend cannot provide publication slots", async () => {
    const { getJson, latest } = resolveEquivalence({ slotReads: false });

    await vi.waitFor(() => expect(getJson).toHaveBeenCalledWith("/runtime/workflows/executables?scope=All&includeRetired=true"));
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(latest()).toBeNull();
    expect(getJson.mock.calls.some(([url]) => url.includes("activation-slots") || url.includes("/publications/"))).toBe(false);
  });

  it("resolves to no signal when the publication record behind the slot cannot be read", async () => {
    const { getJson, latest } = resolveEquivalence({ record: false });

    await vi.waitFor(() => expect(getJson).toHaveBeenCalledWith("/publishing/publications/publication-default"));
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(latest()).toBeNull();
  });
});
