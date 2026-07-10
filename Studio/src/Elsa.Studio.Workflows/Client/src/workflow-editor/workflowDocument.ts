import { useReducer, useMemo } from "react";
import type { WorkflowDraft } from "../workflowTypes";
import type { ScopeFrame } from "../workflowAdapter";
import type { WorkflowTestRunState } from "./editorTypes";

// The workflow editor's document state — the interdependent cluster that used to cascade through many
// separate setDraft/setFrames/setSelectedNodeId/setTestRun/setPublishedArtifactId calls scattered across
// WorkflowEditor. Modelling it as one reducer makes the transitions (and, crucially, *what resets when*)
// explicit and unit-testable, instead of implied by the ordering of setState calls in each handler.
export interface WorkflowDocumentState {
  // The open draft. null until the definition has loaded.
  draft: WorkflowDraft | null;
  // Scope navigation breadcrumb: which nested activity slot the canvas is currently showing.
  frames: ScopeFrame[];
  // Selected activity within the current scope (drives the inspector).
  selectedNodeId: string | null;
  // Last transient test run; rendered only while its signature still matches the live draft.
  testRun: WorkflowTestRunState | null;
  // Artifact id of the most recent promote/publish, surfaced by the Artifacts panel.
  publishedArtifactId: string | null;
}

export const initialWorkflowDocumentState: WorkflowDocumentState = {
  draft: null,
  frames: [],
  selectedNodeId: null,
  testRun: null,
  publishedArtifactId: null
};

// An edit recipe receives the *current* document state and returns the next draft (or null to leave the
// draft untouched). Recipes preserve the functional-update safety of the old `setDraft(current => ...)`
// pattern — they always run against the latest state — while keeping the cascade rules in the reducer.
export type WorkflowDraftRecipe = (state: WorkflowDocumentState) => WorkflowDraft | null;

export type WorkflowDocumentAction =
  // Wholesale draft load: initial load, post-promote reload, JSON apply, and undo/redo restore. Resets
  // scope + selection but leaves testRun/publishedArtifact (they self-invalidate against the new draft).
  | { type: "draftLoaded"; draft: WorkflowDraft | null }
  // Weaver batch apply/undo replaces the whole design, so scope, selection, the pending test run and the
  // last published artifact are all invalidated together.
  | { type: "draftReplacedByBatch"; draft: WorkflowDraft; selectedNodeId: string | null }
  // In-place edit that keeps the current scope + selection (property edits, canvas commit, activity update,
  // debounced save merge).
  | { type: "draftEdited"; recipe: WorkflowDraftRecipe }
  // Edit that also moves the selection to a known node (adding/wrapping a root or a scoped activity).
  | { type: "draftEditedAndSelected"; recipe: WorkflowDraftRecipe; selectedNodeId: string | null }
  | { type: "selectionChanged"; selectedNodeId: string | null }
  // Scope navigation (breadcrumb clicks, slot entry, variable-repair jumps). Slot entry always goes
  // through here with a full frame path computed by planSlotNavigation — descending through a slot's
  // single container child needs multi-frame hops, so there is no single-frame "enter slot" transition.
  | { type: "scopeNavigated"; frames: ScopeFrame[]; selectedNodeId: string | null }
  | { type: "testRunStarted"; testRun: WorkflowTestRunState }
  | { type: "testRunCleared" }
  | { type: "publishedArtifactChanged"; publishedArtifactId: string | null };

export function workflowDocumentReducer(state: WorkflowDocumentState, action: WorkflowDocumentAction): WorkflowDocumentState {
  switch (action.type) {
    case "draftLoaded":
      return { ...state, draft: action.draft, frames: [], selectedNodeId: null };
    case "draftReplacedByBatch":
      return {
        ...state,
        draft: action.draft,
        frames: [],
        selectedNodeId: action.selectedNodeId,
        testRun: null,
        publishedArtifactId: null
      };
    case "draftEdited": {
      const draft = action.recipe(state);
      return draft ? { ...state, draft } : state;
    }
    case "draftEditedAndSelected": {
      const draft = action.recipe(state);
      return { ...state, draft: draft ?? state.draft, selectedNodeId: action.selectedNodeId };
    }
    case "selectionChanged":
      return state.selectedNodeId === action.selectedNodeId ? state : { ...state, selectedNodeId: action.selectedNodeId };
    case "scopeNavigated":
      return { ...state, frames: action.frames, selectedNodeId: action.selectedNodeId };
    case "testRunStarted":
      return { ...state, testRun: action.testRun };
    case "testRunCleared":
      return state.testRun === null ? state : { ...state, testRun: null };
    case "publishedArtifactChanged":
      return { ...state, publishedArtifactId: action.publishedArtifactId };
    default:
      return state;
  }
}

// Hook wrapper exposing the reducer as a small set of intent-named methods. WorkflowEditor and its
// extracted hooks drive the document exclusively through these, so no handler touches the raw setters.
export function useWorkflowDocument() {
  const [state, dispatch] = useReducer(workflowDocumentReducer, initialWorkflowDocumentState);

  const actions = useMemo(() => ({
    // Load a freshly fetched / applied draft (or clear it). Used by load(), JSON apply, and undo/redo.
    loadDraft(draft: WorkflowDraft | null) {
      dispatch({ type: "draftLoaded", draft });
    },
    // Replace the draft from a Weaver batch apply/undo, invalidating scope, selection, run and artifact.
    replaceDraftByBatch(draft: WorkflowDraft, selectedNodeId: string | null) {
      dispatch({ type: "draftReplacedByBatch", draft, selectedNodeId });
    },
    // In-place draft edit that keeps scope + selection.
    editDraft(recipe: WorkflowDraftRecipe) {
      dispatch({ type: "draftEdited", recipe });
    },
    // Draft edit that also selects a node (add/wrap root, add scoped activity).
    editDraftAndSelect(recipe: WorkflowDraftRecipe, selectedNodeId: string | null) {
      dispatch({ type: "draftEditedAndSelected", recipe, selectedNodeId });
    },
    select(selectedNodeId: string | null) {
      dispatch({ type: "selectionChanged", selectedNodeId });
    },
    // Jump to an explicit breadcrumb path (breadcrumb clicks, variable-repair navigation).
    navigateToScope(frames: ScopeFrame[], selectedNodeId: string | null = null) {
      dispatch({ type: "scopeNavigated", frames, selectedNodeId });
    },
    resetToRoot() {
      dispatch({ type: "scopeNavigated", frames: [], selectedNodeId: null });
    },
    startTestRun(testRun: WorkflowTestRunState) {
      dispatch({ type: "testRunStarted", testRun });
    },
    clearTestRun() {
      dispatch({ type: "testRunCleared" });
    },
    setPublishedArtifact(publishedArtifactId: string | null) {
      dispatch({ type: "publishedArtifactChanged", publishedArtifactId });
    }
  }), []);

  return { state, ...actions };
}

export type WorkflowDocumentApi = ReturnType<typeof useWorkflowDocument>;
