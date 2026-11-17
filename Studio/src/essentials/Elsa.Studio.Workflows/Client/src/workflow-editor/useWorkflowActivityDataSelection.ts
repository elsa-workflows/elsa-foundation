import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { canonicalizeStateForWire } from "../activityInputWire";
import { indexActivityPresentation, resolveActivityLabel } from "../activityPresentation";
import type { ActivityDataInputFinding, ActivityDataProducerDescription, ActivityDataSelectionHost } from "../activityDataSelection";
import { hasWorkflowActivityDataSelection, requestWorkflowActivityData, type ActivityDataTarget } from "../api/activityDataSelection";
import { parseValidationErrorPath } from "../validationDiagnostics";
import { getChildSlots } from "../workflowAdapter";
import type { ActivityCatalogItem, ActivityNode, ValidationError, WorkflowDraft } from "../workflowTypes";
import { getDraftContentRevision, getDraftSignature } from "./editorHelpers";

interface WorkflowActivityDataSelectionParams {
  context: StudioEndpointContext;
  /** False where the editor does not own a server workflow draft, such as a template document. */
  enabled: boolean;
  /** The live draft the author sees, including edits not yet saved. */
  draft: WorkflowDraft | null;
  /** The draft as the server last acknowledged it (useWorkflowPersistence). Its authoring revision is the request base. */
  persistedDraft: WorkflowDraft | null;
  /** True while the save queue is writing; a stale answer is then waited out instead of reported. */
  saving: boolean;
  catalogByVersion: Map<string, ActivityCatalogItem>;
  /** The editor's merged validation results; only those of the saved document are shown on a reference. */
  validationErrors: ValidationError[];
  revealActivity(nodeId: string): void;
}

/**
 * Choose data for a workflow draft. Each request analyzes the author's current document as a snapshot against the
 * acknowledged saved revision, so browsing never waits for, triggers or bypasses a save, and held or manual edits are
 * analyzed as they are. The answer belongs to the document's SHA-256 content revision it was asked for. Nothing here
 * serializes the draft while the feature is unavailable.
 */
export function useWorkflowActivityDataSelection({
  context,
  enabled,
  draft,
  persistedDraft,
  saving,
  catalogByVersion,
  validationErrors,
  revealActivity
}: WorkflowActivityDataSelectionParams): ActivityDataSelectionHost | null {
  const [available, setAvailable] = useState(false);
  const contextRef = useRef(context);
  contextRef.current = context;
  const baseUrl = context.baseUrl ?? "";

  useEffect(() => {
    setAvailable(false);
    if (!enabled) return;
    let active = true;
    hasWorkflowActivityDataSelection(contextRef.current).then(
      supported => { if (active) setAvailable(supported); },
      () => { if (active) setAvailable(false); }
    );
    return () => { active = false; };
  }, [baseUrl, enabled]);

  const on = available && enabled && draft != null;
  const latest = useRef({ draft, persistedDraft });
  latest.current = { draft, persistedDraft };
  // The document reducer replaces the draft object on every edit, so identity is enough to recompute; other
  // editor renders reuse the revision instead of serializing the draft again.
  const documentRevision = useMemo(() => on && draft ? getDraftContentRevision(draft) : "", [draft, on]);
  const baseRevision = on ? acknowledgedRevision(draft, persistedDraft) : "";

  const requestSources = useCallback((target: ActivityDataTarget, signal: AbortSignal, skip: number) => {
    const { draft: current, persistedDraft: saved } = latest.current;
    const base = acknowledgedRevision(current, saved);
    if (!current || !base) return Promise.reject(new Error("The draft has no acknowledged revision."));
    return requestWorkflowActivityData(contextRef.current, {
      definitionId: current.definitionId,
      draftId: current.id,
      expectedBaseRevision: base,
      documentRevision: getDraftContentRevision(current),
      target,
      snapshot: canonicalizeStateForWire(current.state),
      skip
    }, signal);
  }, []);

  const root = on ? draft!.state.rootActivity : undefined;
  const presentation = on ? draft!.activityPresentation : undefined;
  // Labels for every activity in the document, so a reference can tell two same-named activities apart.
  const labels = useMemo(() => {
    const byNodeId = new Map<string, { node: ActivityNode; label: string }>();
    if (!root) return { byNodeId, counts: new Map<string, number>() };
    const presentationByNodeId = indexActivityPresentation(presentation);
    const counts = new Map<string, number>();
    const visit = (node: ActivityNode, depth: number) => {
      if (depth > 100 || byNodeId.has(node.nodeId)) return;
      const item = catalogByVersion.get(node.activityVersionId);
      const label = resolveActivityLabel(presentationByNodeId.get(node.nodeId), item, item?.activityTypeKey ?? node.activityVersionId);
      byNodeId.set(node.nodeId, { node, label });
      counts.set(label, (counts.get(label) ?? 0) + 1);
      for (const slot of getChildSlots(node, catalogByVersion)) for (const child of slot.activities) visit(child, depth + 1);
    };
    visit(root, 0);
    return { byNodeId, counts };
  }, [catalogByVersion, presentation, root]);
  const describeActivity = useCallback((nodeId: string): ActivityDataProducerDescription | null => {
    const entry = labels.byNodeId.get(nodeId);
    if (!entry) return null;
    return {
      label: entry.label,
      outputLabels: readOutputLabels(catalogByVersion.get(entry.node.activityVersionId)),
      sharesLabel: (labels.counts.get(entry.label) ?? 0) > 1
    };
  }, [catalogByVersion, labels]);

  // Findings describe the document the server last validated: shown only while the live draft is that document.
  const findingsCurrent = useMemo(() => on && !!draft && !!persistedDraft && persistedDraft.id === draft.id &&
    getDraftSignature(persistedDraft) === getDraftSignature(draft), [draft, on, persistedDraft]);
  const findingsFor = useCallback((target: ActivityDataTarget): ActivityDataInputFinding[] => {
    if (!findingsCurrent) return [];
    return validationErrors.flatMap(error => {
      const path = parseValidationErrorPath(error.path);
      const type = error.type ?? error.code ?? "";
      return path.nodeId === target.nodeId && path.bag === "inputs" && path.referenceKey === target.inputKey && type
        ? [{ type, message: error.message ?? "" }]
        : [];
    });
  }, [findingsCurrent, validationErrors]);

  return useMemo(() => on ? {
    documentRevision,
    baseRevision,
    saving,
    requestSources,
    describeActivity,
    revealActivity,
    findingsFor
  } : null, [baseRevision, describeActivity, documentRevision, findingsFor, on, requestSources, revealActivity, saving]);
}

function acknowledgedRevision(draft: WorkflowDraft | null, saved: WorkflowDraft | null) {
  return draft && saved && saved.id === draft.id && saved.definitionId === draft.definitionId ? saved.authoringRevision ?? "" : "";
}

function readOutputLabels(item: ActivityCatalogItem | undefined): Record<string, string> {
  const labels: Record<string, string> = {};
  for (const output of item?.outputs ?? []) {
    if (!output || typeof output !== "object") continue;
    const record = output as Record<string, unknown>;
    const key = typeof record.referenceKey === "string" ? record.referenceKey : typeof record.name === "string" ? record.name : "";
    const label = typeof record.displayName === "string" && record.displayName.trim() ? record.displayName
      : typeof record.name === "string" ? record.name : "";
    if (key && label) labels[key] = label;
  }
  return labels;
}
