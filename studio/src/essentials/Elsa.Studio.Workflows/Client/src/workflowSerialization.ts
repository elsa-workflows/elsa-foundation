import { canonicalizeStateForWire, expandStateFromWire } from "./activityInputWire";
import type { ActivityPresentationRecord, DesignMetadataRecord, WorkflowDefinitionState, WorkflowDraft } from "./workflowTypes";
import { normalizeActivityPresentation } from "./activityPresentation";
import { collectActivityNodeIds } from "./workflowAdapter";

export interface WorkflowExportPayload {
  name?: string;
  definitionId: string;
  state: WorkflowDefinitionState;
  layout: DesignMetadataRecord[];
  activityPresentation: ActivityPresentationRecord[];
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
    layout: draft.layout,
    activityPresentation: normalizeActivityPresentation(draft.activityPresentation)
  };
}

/** Pretty-prints the editable portion of a draft (state + layout) for the Code view. */
export function serializeDraftToJson(draft: WorkflowDraft): string {
  return JSON.stringify(
    {
      state: canonicalizeStateForWire(draft.state),
      layout: draft.layout,
      activityPresentation: normalizeActivityPresentation(draft.activityPresentation)
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

  const record = parsed as { state?: unknown; layout?: unknown; activityPresentation?: unknown };
  if (!record.state || typeof record.state !== "object") {
    return { ok: false, error: "Workflow JSON is missing a valid 'state' object." };
  }
  if (record.layout !== undefined && !Array.isArray(record.layout)) {
    return { ok: false, error: "'layout' must be an array when present." };
  }
  if (record.activityPresentation !== undefined && !Array.isArray(record.activityPresentation)) {
    return { ok: false, error: "'activityPresentation' must be an array when present." };
  }

  const state = expandStateFromWire(record.state as WorkflowDefinitionState);
  const reachableNodeIds = state.rootActivity
    ? collectActivityNodeIds(state.rootActivity, new Map())
    : new Set<string>();
  const requestedPresentation = record.activityPresentation === undefined
    ? current.activityPresentation
    : record.activityPresentation as ActivityPresentationRecord[];

  return {
    ok: true,
    draft: {
      ...current,
      state,
      layout: (record.layout as DesignMetadataRecord[] | undefined) ?? current.layout,
      activityPresentation: normalizeActivityPresentation(requestedPresentation)
        .filter(presentation => reachableNodeIds.has(presentation.nodeId))
    }
  };
}

/** Triggers a browser download of the given payload as a `.json` file. */
export function downloadWorkflowJson(payload: WorkflowExportPayload, name?: string | null): void {
  const safeName = (name ?? "workflow").trim().replace(/[^\w.-]+/g, "-") || "workflow";
  downloadJsonText(JSON.stringify(payload, null, 2), `${safeName}.json`);
}

/**
 * Downloads the compiled executable artifact closure (foundation #1304) exactly as the server produced
 * it — no export payload is built and nothing is reshaped: Studio saves what the endpoint returned
 * (FR-C-004). Separate from {@link downloadWorkflowJson}, which serializes the *design* draft.
 */
export function downloadExecutableArtifactJson(closure: unknown, fileName: string): void {
  downloadJsonText(JSON.stringify(closure, null, 2), fileName);
}

/**
 * Rebuilds the download name the export endpoint puts in its `Content-Disposition`:
 * `{definitionId}-{artifactVersion}-closure.json`, with the server's own safe-name rules (a
 * conservative `[A-Za-z0-9._-]` alphabet, runs of substituted characters collapsed, leading/trailing
 * dots and dashes trimmed, segments capped).
 *
 * The header itself is unreadable from the browser: the Studio http client parses JSON and exposes no
 * response headers, and `Content-Disposition` is not CORS-safelisted (the API does not send
 * `Access-Control-Expose-Headers` for it). Reconstructing it from the published artifact's identity —
 * which is where the server reads both segments from too — keeps the saved name identical either way,
 * instead of producing a second, differently-named file for the same export.
 */
export function buildExecutableArtifactFileName(identity: {
  definitionId?: string | null;
  artifactVersion?: string | null;
}): string {
  const definitionId = safeArtifactNameSegment(identity.definitionId, "workflow");
  const artifactVersion = safeArtifactNameSegment(identity.artifactVersion, "unversioned");
  return `${definitionId}-${artifactVersion}-closure.json`;
}

const maximumArtifactNameSegmentLength = 96;

function safeArtifactNameSegment(value: string | null | undefined, fallback: string): string {
  if (!value || !value.trim()) return fallback;

  let sanitized = "";
  for (const character of value) {
    const safe = /^[A-Za-z0-9._]$/.test(character) ? character : "-";
    // Collapse runs of substituted characters so a segment of separators cannot pad the name out.
    if (safe === "-" && sanitized.endsWith("-")) continue;
    sanitized += safe;
  }

  sanitized = sanitized.replace(/^[-.]+/, "").replace(/[-.]+$/, "");
  if (sanitized.length > maximumArtifactNameSegmentLength) {
    sanitized = sanitized.slice(0, maximumArtifactNameSegmentLength).replace(/[-.]+$/, "");
  }
  return sanitized || fallback;
}

function downloadJsonText(text: string, fileName: string): void {
  const blob = new Blob([text], { type: "application/json" });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = fileName;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  URL.revokeObjectURL(url);
}
