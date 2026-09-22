import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { clearApiCapabilityCache } from "../api/capabilities";
import type { WorkflowDraft } from "../workflowTypes";
import { autosaveDelayMs } from "../workflow-editor/constants";
import { useWorkflowPersistence } from "../workflow-editor/useWorkflowPersistence";

type Persistence = ReturnType<typeof useWorkflowPersistence>;

let root: ReturnType<typeof createRoot> | null = null;
let container: HTMLDivElement | null = null;

beforeEach(() => vi.useFakeTimers());

afterEach(() => {
  if (root) flushSync(() => root!.unmount());
  container?.remove();
  root = null;
  container = null;
  clearApiCapabilityCache();
  vi.useRealTimers();
});

describe("publication review autosave pause", () => {
  it("cancels an already-pending autosave and gives Cancel a fresh debounce before autosave resumes", async () => {
    const putJson = vi.fn(async () => draft());
    const context = {
      baseUrl: `test://publication-autosave-${Math.random()}`,
      http: {
        getJson: vi.fn(async () => capabilities),
        putJson
      }
    } as unknown as StudioEndpointContext;
    let persistence: Persistence | null = null;
    container = document.createElement("div");
    document.body.appendChild(container);
    root = createRoot(container);

    flushSync(() => root!.render(<Harness context={context} onRender={value => { persistence = value; }} />));
    await vi.advanceTimersByTimeAsync(autosaveDelayMs - 1);

    // Opening publication review pauses autosave and destroys the timer that is one millisecond from firing.
    flushSync(() => persistence!.setAutosavePaused(true));
    // Cancel resumes autosave, but it must schedule a new full debounce rather than reviving the old timer.
    flushSync(() => persistence!.setAutosavePaused(false));

    await vi.advanceTimersByTimeAsync(1);
    expect(putJson).not.toHaveBeenCalled();

    await vi.advanceTimersByTimeAsync(autosaveDelayMs - 1);
    expect(putJson).toHaveBeenCalledTimes(1);
  });
});

function Harness({ context, onRender }: { context: StudioEndpointContext; onRender(value: Persistence): void }) {
  onRender(useWorkflowPersistence({
    context,
    draft: draft(),
    editDraft: vi.fn(),
    setStatus: vi.fn(),
    setError: vi.fn()
  }));
  return null;
}

function draft(): WorkflowDraft {
  return {
    id: "draft-1",
    definitionId: "definition-1",
    state: { rootActivity: { nodeId: "root", activityVersionId: "root-v1", inputs: [], outputs: [] } },
    layout: [],
    validationErrors: []
  };
}

const capabilities = {
  capabilities: [{
    id: "elsa.api.workflow-design",
    contractVersion: "1",
    links: [{ rel: "workflow-drafts", href: "design/workflows/drafts/{draftId}", templated: true }]
  }]
};
