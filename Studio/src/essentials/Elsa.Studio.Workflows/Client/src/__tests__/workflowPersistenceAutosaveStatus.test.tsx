import { useEffect, useState } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { clearApiCapabilityCache } from "../api/capabilities";
import type { WorkflowDraft } from "../workflowTypes";
import { autosaveDelayMs } from "../workflow-editor/constants";
import { useWorkflowPersistence } from "../workflow-editor/useWorkflowPersistence";
import type { WorkflowEditorDocumentAdapter } from "../workflow-editor/WorkflowEditorDocumentAdapter";

type Persistence = ReturnType<typeof useWorkflowPersistence>;

const noop = () => {};
const saveDraftApi = vi.fn(async (_context: StudioEndpointContext, draft: WorkflowDraft) => draft);
const identitySave = { save: saveDraftApi } as unknown as WorkflowEditorDocumentAdapter;
let root: ReturnType<typeof createRoot> | null = null;
let container: HTMLDivElement | null = null;
let context: StudioEndpointContext;
let persistence: Persistence | null = null;
let currentDraft: WorkflowDraft | null = null;
let setDraft!: (recipe: (draft: WorkflowDraft | null) => WorkflowDraft | null) => void;

beforeEach(() => {
  vi.useFakeTimers();
  saveDraftApi.mockClear();
});

afterEach(() => {
  if (root) flushSync(() => root!.unmount());
  container?.remove();
  root = null;
  container = null;
  persistence = null;
  currentDraft = null;
  clearApiCapabilityCache();
  vi.useRealTimers();
});

describe("cancelled autosave status", () => {
  it("shows unsaved changes when Autosave is turned off during a pending debounce", async () => {
    await mount();
    await editLayout();
    await advance(autosaveDelayMs - 1);
    expect(displayedStatus()).toBe("Autosaving...");
    expect(saveDraftApi).not.toHaveBeenCalled();

    flushSync(() => persistence!.setAutosaveEnabled(false));
    await settle();
    expect(displayedStatus()).toBe("Unsaved changes");

    await advance(autosaveDelayMs + 1);
    expect(saveDraftApi).not.toHaveBeenCalled();
    expect(displayedStatus()).toBe("Unsaved changes");
  });

  it("restores the prior saved status when the pending draft matches the last save", async () => {
    await mount();
    await editLayout();
    await advance(autosaveDelayMs);
    expect(saveDraftApi).toHaveBeenCalledTimes(1);
    expect(displayedStatus()).toBe("Autosaved");

    const saved = currentDraft!;
    await editLayout(40);
    await advance(autosaveDelayMs - 1);
    expect(displayedStatus()).toBe("Autosaving...");

    flushSync(() => setDraft(() => saved));
    await settle();
    expect(displayedStatus()).toBe("Autosaved");
    expect(saveDraftApi).toHaveBeenCalledTimes(1);

    await advance(autosaveDelayMs + 1);
    expect(saveDraftApi).toHaveBeenCalledTimes(1);
    expect(displayedStatus()).toBe("Autosaved");
  });

  it("restores Autosaved after undo to the last save while Autosave is off", async () => {
    await mount();
    await editLayout();
    await advance(autosaveDelayMs);
    expect(displayedStatus()).toBe("Autosaved");
    const saved = currentDraft!;

    await editLayout(40);
    await advance(autosaveDelayMs - 1);
    expect(displayedStatus()).toBe("Autosaving...");

    flushSync(() => persistence!.setAutosaveEnabled(false));
    await settle();
    expect(displayedStatus()).toBe("Unsaved changes");

    flushSync(() => setDraft(() => saved));
    await settle();
    expect(displayedStatus()).toBe("Autosaved");
    expect(saveDraftApi).toHaveBeenCalledTimes(1);
  });

  it("shows unsaved changes for further edits while Autosave is already off", async () => {
    await mount();
    await editLayout();
    await advance(autosaveDelayMs);
    expect(displayedStatus()).toBe("Autosaved");

    flushSync(() => persistence!.setAutosaveEnabled(false));
    await settle();
    expect(displayedStatus()).toBe("Autosaved");

    await editLayout(40);
    expect(displayedStatus()).toBe("Unsaved changes");
    expect(saveDraftApi).toHaveBeenCalledTimes(1);

    await advance(autosaveDelayMs + 1);
    expect(saveDraftApi).toHaveBeenCalledTimes(1);
    expect(displayedStatus()).toBe("Unsaved changes");
  });
});

async function mount() {
  context = { baseUrl: `test://autosave-status-${Math.random()}`, http: {} } as StudioEndpointContext;
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
  flushSync(() => root!.render(<Probe />));
  await settle();
  expect(displayedStatus()).toBe("");
  expect(saveDraftApi).not.toHaveBeenCalled();
}

async function editLayout(x = 10) {
  flushSync(() => setDraft(draft => draft ? { ...draft, layout: [{ nodeId: "root", x, y: 0 }] } : draft));
  await settle();
}

async function advance(milliseconds: number) {
  await vi.advanceTimersByTimeAsync(milliseconds);
  await settle();
}

async function settle() {
  await vi.advanceTimersByTimeAsync(0);
  flushSync(() => root!.render(<Probe />));
}

function displayedStatus() {
  return container?.querySelector("[role='status']")?.textContent ?? "";
}

function Probe() {
  const [draft, setDraftState] = useState<WorkflowDraft | null>(null);
  const [currentStatus, setStatus] = useState("");
  const current = useWorkflowPersistence({
    context,
    draft,
    documentAdapter: identitySave,
    editDraft: noop,
    setStatus,
    setError: noop
  });
  const { markSaved } = current;
  useEffect(() => {
    markSaved(initialDraft);
    setDraftState(initialDraft);
  }, [markSaved]);
  persistence = current;
  currentDraft = draft;
  setDraft = recipe => setDraftState(present => recipe(present));
  return <span role="status">{currentStatus}</span>;
}

const initialDraft: WorkflowDraft = {
  id: "draft-1",
  definitionId: "definition-1",
  state: { rootActivity: { nodeId: "root", activityVersionId: "root-v1", inputs: [], outputs: [] } },
  layout: [],
  validationErrors: []
};
