import { canonicalizeStateForWire, expandStateFromWire } from "./activityInputWire";
import type { DesignMetadataRecord, WorkflowDefinitionState, WorkflowDraft } from "./workflowTypes";

export interface WorkflowExportPayload {
  name?: string;
  definitionId: string;
  state: WorkflowDefinitionState;
  layout: DesignMetadataRecord[];
}

/**
 * Builds the JSON payload for an exported workflow. The `state` is canonicalized
 * with the same transform used by {@link updateDraft}, so the file matches what is
 * persisted on the wire and can be re-imported losslessly.
 */
export function buildExportPayload(draft: WorkflowDraft, name?: string | null): WorkflowExportPayload {
  return {
    ...(name ? { name } : {}),
    definitionId: draft.definitionId,
    state: canonicalizeStateForWire(draft.state),
    layout: draft.layout
  };
}

/** Pretty-prints the editable portion of a draft (state + layout) for the Code view. */
export function serializeDraftToJson(draft: WorkflowDraft): string {
  return JSON.stringify(
    {
      state: canonicalizeStateForWire(draft.state),
      layout: draft.layout
    },
    null,
    2
  );
}

export type DraftFromJsonResult =
  | { ok: true; draft: WorkflowDraft }
  | { ok: false; error: string };

/**
 * Parses Code-view JSON back into a draft, mirroring {@link serializeDraftToJson}.
 * Accepts either the editable `{ state, layout }` shape or a full export payload.
 * Returns a structured error instead of throwing so callers can surface diagnostics.
 */
export function buildDraftFromJson(text: string, current: WorkflowDraft): DraftFromJsonResult {
  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch (e) {
    return { ok: false, error: e instanceof Error ? e.message : String(e) };
  }

  if (!parsed || typeof parsed !== "object") {
    return { ok: false, error: "Workflow JSON must be an object with a 'state' property." };
  }

  const record = parsed as { state?: unknown; layout?: unknown };
  if (!record.state || typeof record.state !== "object") {
    return { ok: false, error: "Workflow JSON is missing a valid 'state' object." };
  }
  if (record.layout !== undefined && !Array.isArray(record.layout)) {
    return { ok: false, error: "'layout' must be an array when present." };
  }

  return {
    ok: true,
    draft: {
      ...current,
      state: expandStateFromWire(record.state as WorkflowDefinitionState),
      layout: (record.layout as DesignMetadataRecord[] | undefined) ?? current.layout
    }
  };
}

/** Triggers a browser download of the given payload as a `.json` file. */
export function downloadWorkflowJson(payload: WorkflowExportPayload, name?: string | null): void {
  const safeName = (name ?? "workflow").trim().replace(/[^\w.-]+/g, "-") || "workflow";
  const blob = new Blob([JSON.stringify(payload, null, 2)], { type: "application/json" });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = `${safeName}.json`;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  URL.revokeObjectURL(url);
}
