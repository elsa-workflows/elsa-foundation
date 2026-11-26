import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ActivityDefinitionVersionView } from "../activityDefinitionTypes";
import type { ActivityNode, WorkflowDraft } from "../workflowTypes";
import { activityVersionChangePinnedReason, createActivityVersionChangePrecondition, findActivityOccurrence } from "../workflow-editor/activityVersionChangeModel";
import { useWorkflowEditorSaveCommands } from "../workflow-editor/useWorkflowEditorSaveCommands";

// Spec 1193 FR-010: the editor's toolbar Save, template Back and activity version change each write a snapshot fixed
// when the author acts, so each captures the history line with it, before any await; the version change is refused
// while a pinned test-run snapshot is shown. These drive the hook WorkflowEditor uses.

let root: Root, container: HTMLDivElement;
let commands: ReturnType<typeof useWorkflowEditorSaveCommands>;
let line: string[];
let editable: boolean;
const calls = {
  saveDraft: vi.fn(), loadDraft: vi.fn(), save: vi.fn(), flushDefinitionMeta: vi.fn(), onBack: vi.fn(),
  setAutosavePaused: vi.fn(), setError: vi.fn(), setStatus: vi.fn(), setNavigationPending: vi.fn()
};

function Probe({ draft }: { draft: WorkflowDraft }) {
  commands = useWorkflowEditorSaveCommands({ draft, canEdit: () => editable, lineage: () => line, ...calls });
  return null;
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(done => { resolve = done; });
  return { promise, resolve };
}

const flush = () => new Promise(resolve => setTimeout(resolve, 0));

beforeEach(() => {
  for (const mock of Object.values(calls)) mock.mockReset();
  calls.flushDefinitionMeta.mockResolvedValue(true);
  calls.save.mockResolvedValue(undefined);
  line = ["batch-1"]; editable = true;
  container = document.createElement("div"); document.body.append(container); root = createRoot(container);
  flushSync(() => root.render(<Probe draft={draft()} />));
});
afterEach(() => { flushSync(() => root.unmount()); container.remove(); });

describe("editor save commands capture the history line with their snapshot", () => {
  it("refuses to open the version change while a pinned test-run snapshot is shown", () => {
    editable = false;
    flushSync(() => commands.openVersionChange(occurrence(), version()));

    expect(calls.setError).toHaveBeenCalledWith(activityVersionChangePinnedReason);
    expect(calls.setAutosavePaused).not.toHaveBeenCalled();
    expect(commands.versionChange).toBeNull();
  });

  it("refuses to apply a version change while pinned, saving nothing", async () => {
    editable = false;
    await expect(commands.applyVersionChange(request())).rejects.toThrow(activityVersionChangePinnedReason);
    expect(calls.saveDraft).not.toHaveBeenCalled();
  });

  it("applies the version change with the line captured before its save is awaited", async () => {
    const save = deferred<WorkflowDraft>();
    calls.saveDraft.mockReturnValueOnce(save.promise);
    const applying = commands.applyVersionChange(request());
    // The author undoes while the save is in flight; the written snapshot still carries the line of the click.
    line = [];
    const [written, status, options] = calls.saveDraft.mock.calls[0];
    expect(findActivityOccurrence((written as WorkflowDraft).state.rootActivity, "first")?.activityVersionId).toBe("version-2");
    save.resolve(written as WorkflowDraft);
    await applying;

    expect(status).toBe("Activity version changed");
    expect(options).toEqual({ lineage: ["batch-1"] });
    expect(calls.loadDraft).toHaveBeenCalledWith(written);
  });

  it("the toolbar Save passes the line of the click, not the line after the metadata flush", async () => {
    const meta = deferred<boolean>();
    calls.flushDefinitionMeta.mockReturnValueOnce(meta.promise);
    commands.saveFromToolbar();
    line = [];
    meta.resolve(true);
    await flush();

    expect(calls.save).toHaveBeenCalledWith({ lineage: ["batch-1"] });
  });

  it("a template's Back saves its click-time draft with the line of the click, then leaves", async () => {
    const meta = deferred<boolean>();
    calls.flushDefinitionMeta.mockReturnValueOnce(meta.promise);
    calls.saveDraft.mockImplementationOnce(async (value: WorkflowDraft) => value);
    const snapshot = draft();
    commands.saveAndGoBack(snapshot);
    line = [];
    meta.resolve(true);
    await flush();

    expect(calls.saveDraft).toHaveBeenCalledWith(snapshot, "Saved", { lineage: ["batch-1"] });
    expect(calls.onBack).toHaveBeenCalledTimes(1);
    expect(calls.setNavigationPending.mock.calls).toEqual([[true], [false]]);
  });
});

function request() {
  const value = draft();
  return { precondition: createActivityVersionChangePrecondition(value, occurrence()), targetVersionId: "version-2", scope: "occurrence" as const };
}

function occurrence(): ActivityNode {
  return { nodeId: "first", activityVersionId: "version-1", inputs: [], outputs: [] };
}

function version() {
  return { versionId: "version-1" } as unknown as ActivityDefinitionVersionView;
}

function draft(): WorkflowDraft {
  return {
    id: "draft-1", definitionId: "workflow-1", sourceVersionId: "published-source", layout: [], validationErrors: [],
    state: { rootActivity: { nodeId: "root", activityVersionId: "flowchart", inputs: [], outputs: [], structure: {
      kind: "Flowchart", schemaVersion: "1", payload: { activities: [occurrence()], connections: [] } } } }
  };
}
