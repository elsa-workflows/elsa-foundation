import { useEffect, useRef, useState } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { DraftValidationError, WorkflowDraft } from "../workflowTypes";
import { getDraftValidations } from "../api/draftValidations";
import { getDraftSignature } from "./editorHelpers";

export type DraftValidationsStatus = "idle" | "loading" | "ready" | "unavailable" | "error";

export interface DraftValidationsState {
  status: DraftValidationsStatus;
  /** null until the first probe resolves; false when the backend does not advertise the relation. */
  available: boolean | null;
  errors: DraftValidationError[];
}

// Debounce after the draft settles so a burst of edits (and the autosave that follows) collapses into a
// single validations fetch rather than one per keystroke.
const debounceMs = 600;

/**
 * Fetches the server-derived draft validations (foundation #937) after the draft settles, keyed off the
 * draft signature so it re-runs when the (auto)saved state changes. Degrades gracefully: when the
 * relation is absent the endpoint returns null, we mark it unavailable, and the caller hides the panel.
 */
export function useDraftValidations({
  context,
  draft,
  enabled = true
}: {
  context: StudioEndpointContext;
  draft: WorkflowDraft | null;
  enabled?: boolean;
}): DraftValidationsState {
  const [state, setState] = useState<DraftValidationsState>({ status: "idle", available: null, errors: [] });
  const availableRef = useRef<boolean | null>(null);
  const signature = draft ? getDraftSignature(draft) : "";
  const draftId = draft?.id ?? "";

  useEffect(() => {
    if (!enabled || !draftId) {
      setState({ status: "idle", available: availableRef.current, errors: [] });
      return;
    }
    // Once we've learned the relation is absent, stop probing on every edit.
    if (availableRef.current === false) return;

    const controller = new AbortController();
    let active = true;
    setState(previous => ({ ...previous, status: "loading" }));
    const timeout = window.setTimeout(() => {
      getDraftValidations(context, draftId, controller.signal).then(
        errors => {
          if (!active || controller.signal.aborted) return;
          if (errors === null) {
            availableRef.current = false;
            setState({ status: "unavailable", available: false, errors: [] });
            return;
          }
          availableRef.current = true;
          setState({ status: "ready", available: true, errors });
        },
        error => {
          if (!active || controller.signal.aborted || (error instanceof DOMException && error.name === "AbortError")) return;
          setState(previous => ({ status: "error", available: availableRef.current, errors: previous.errors }));
        }
      );
    }, debounceMs);

    return () => {
      active = false;
      window.clearTimeout(timeout);
      controller.abort();
    };
  }, [context, draftId, signature, enabled]);

  return state;
}
