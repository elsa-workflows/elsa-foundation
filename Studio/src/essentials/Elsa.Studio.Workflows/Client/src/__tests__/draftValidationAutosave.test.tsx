import { useEffect } from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { DraftValidationError, WorkflowDraft } from "../workflowTypes";
import { combineValidationErrors } from "../draftValidation";
import { ValidationPanel } from "../workflow-editor/editorPanels";
import { useWorkflowDocument } from "../workflow-editor/workflowDocument";
import { useWorkflowPersistence } from "../workflow-editor/useWorkflowPersistence";
import { useDraftValidations } from "../workflow-editor/useDraftValidations";

const api = vi.hoisted(() => ({ save: vi.fn(), validations: vi.fn() }));
vi.mock("../api/workflowDesign", () => ({ updateDraft: api.save }));
vi.mock("../api/draftValidations", () => ({ getDraftValidations: api.validations }));
const context = { baseUrl: "https://backend.example" } as StudioEndpointContext;
const missingPipeline = { type: "InputOutput/MissingRequired", path: "run-etl/inputs/Pipeline", message: 'Required input "Pipeline" on Run Etl is empty.' };
const initial: WorkflowDraft = {
  id: "draft-1", definitionId: "definition-1", layout: [], validationErrors: [missingPipeline],
  state: { rootActivity: { nodeId: "root", activityVersionId: "sequence-v1", inputs: [], outputs: [], structure: {
    kind: "Sequence", schemaVersion: "1", payload: { activities: [{ nodeId: "run-etl", activityVersionId: "run-etl-v1", inputs: [], outputs: [] }] }
  } } }
};
const serverErrors: DraftValidationError[] = [{ code: missingPipeline.type, nodeId: "run-etl", member: "inputs/Pipeline", message: missingPipeline.message }];
let root: Root, container: HTMLDivElement;
let document: ReturnType<typeof useWorkflowDocument>;
let persistence: ReturnType<typeof useWorkflowPersistence>;
let saved: WorkflowDraft;
const noop = () => {};

function Probe() {
  document = useWorkflowDocument();
  persistence = useWorkflowPersistence({ context, draft: document.state.draft, editDraft: document.editDraft, setError: noop, setStatus: noop });
  const { markSaved } = persistence;
  const { loadDraft } = document;
  useEffect(() => { markSaved(initial); loadDraft(initial); }, [loadDraft, markSaved]);
  const validations = useDraftValidations({ context, draft: document.state.draft, persistedDraft: persistence.persistedDraft });
  const draft = document.state.draft;
  return draft ? <ValidationPanel draft={draft} errors={combineValidationErrors({ draftErrors: draft.validationErrors, serverErrors: validations.errors })} onRepair={noop} /> : null;
}

async function settle() { await vi.advanceTimersByTimeAsync(0); flushSync(() => root.render(<Probe />)); }
async function advance(milliseconds: number) { await vi.advanceTimersByTimeAsync(milliseconds); await settle(); }
function removeEtl() {
  flushSync(() => document.editDraft(({ draft }) => ({ ...draft!, state: { ...draft!.state, rootActivity: { ...draft!.state.rootActivity!, structure: {
    kind: "Sequence", schemaVersion: "1", payload: { activities: [] }
  } } } })));
}

beforeEach(async () => {
  vi.useFakeTimers(); vi.resetAllMocks(); saved = initial;
  api.validations.mockImplementation(async () => {
    const activities = saved.state.rootActivity?.structure?.payload.activities;
    return Array.isArray(activities) && activities.length ? serverErrors : [];
  });
  api.save.mockImplementation(async (_context, draft: WorkflowDraft) => (saved = { ...draft, validationErrors: [] }));
  container = window.document.createElement("div"); window.document.body.append(container); root = createRoot(container);
  flushSync(() => root.render(<Probe />)); await settle(); await advance(600); await settle();
  expect(container.textContent).toContain(missingPipeline.message);
  api.validations.mockClear();
});
afterEach(() => { flushSync(() => root.unmount()); container.remove(); vi.useRealTimers(); });

it("revalidates after autosave, never reading the old persisted activity during the validation debounce", async () => {
  removeEtl();
  await advance(600); await settle();
  expect(api.validations).not.toHaveBeenCalled();
  await advance(600); await settle();
  await advance(600); await settle();
  expect(api.save).toHaveBeenCalledTimes(1);
  expect(api.validations).toHaveBeenCalledTimes(1);
  expect(container.textContent).not.toContain(missingPipeline.message);
  expect(container.textContent).toContain("No validation errors");
});

it("waits for a slow save to complete before validating the removed activity", async () => {
  let finish!: (draft: WorkflowDraft) => void;
  api.save.mockImplementation((_context, _draft: WorkflowDraft) => new Promise<WorkflowDraft>(resolve => {
    finish = result => { saved = result; resolve(result); };
  }));
  removeEtl();
  await advance(2000); await settle();
  expect(api.save).toHaveBeenCalledTimes(1);
  expect(api.validations).not.toHaveBeenCalled();
  finish({ ...document.state.draft!, validationErrors: [] }); await settle();
  await advance(600); await settle();
  expect(api.validations).toHaveBeenCalledTimes(1);
  expect(container.textContent).not.toContain(missingPipeline.message);
});

it("removes the deleted activity diagnostic once autosave and validation settle", async () => {
  removeEtl();
  await advance(1200); await settle();
  await advance(600); await settle();
  expect(api.save).toHaveBeenCalledTimes(1);
  expect(container.textContent).not.toContain(missingPipeline.message);
  expect(container.textContent).toContain("No validation errors");
});

it("revalidates a manual save when autosave is disabled", async () => {
  flushSync(() => persistence.setAutosaveEnabled(false));
  removeEtl(); await advance(2000);
  expect(api.validations).not.toHaveBeenCalled();
  expect(api.save).not.toHaveBeenCalled();
  await persistence.saveDraft(document.state.draft!, "Saved"); await settle();
  await advance(600);
  expect(api.validations).toHaveBeenCalledTimes(1);
  expect(container.textContent).not.toContain(missingPipeline.message);
});
