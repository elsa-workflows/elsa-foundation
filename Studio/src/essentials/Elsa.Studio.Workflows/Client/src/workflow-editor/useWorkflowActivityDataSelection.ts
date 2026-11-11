import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { canonicalizeStateForWire } from "../activityInputWire";
import { indexActivityPresentation, resolveActivityLabel } from "../activityPresentation";
import type { ActivityDataInputFinding, ActivityDataProducerDescription, ActivityDataSelectionHost } from "../activityDataSelection";
import { hasWorkflowActivityDataSelection, requestWorkflowActivityData, type ActivityDataTarget } from "../api/activityDataSelection";
import { parseValidationErrorPath } from "../validationDiagnostics";
import { findActivityNode } from "../workflowAdapter";
import type { ActivityCatalogItem, ValidationError, WorkflowDraft } from "../workflowTypes";
import { getDraftRevision, getDraftSignature } from "./editorHelpers";

interface WorkflowActivityDataSelectionParams {
  context: StudioEndpointContext;
  /** False where the editor does not own a server workflow draft, such as a template document. */
  enabled: boolean;
  /** The live draft the author sees, including edits not yet saved. */
  draft: WorkflowDraft | null;
  /** The draft as the server last acknowledged it (useWorkflowPersistence). Its authoring revision is the request base. */
  persistedDraft: WorkflowDraft | null;
  catalogByVersion: Map<string, ActivityCatalogItem>;
  /** The editor's merged validation results; only those of the saved document are shown on a reference. */
  validationErrors: ValidationError[];
  revealActivity(nodeId: string): void;
}

/**
 * Choose data for a workflow draft. Each request analyzes the author's current document as a snapshot against the
 * acknowledged saved revision, so browsing never waits for, triggers or bypasses a save, and held or manual edits are
 * analyzed as they are. The answer belongs to the document revision it was asked for.
 */
export function useWorkflowActivityDataSelection({
  context,
  enabled,
  draft,
  persistedDraft,
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

  const latest = useRef({ draft, persistedDraft });
  latest.current = { draft, persistedDraft };
  const documentRevision = draft ? getDraftRevision(draft) : "";
  const baseRevision = acknowledgedRevision(draft, persistedDraft);

  const requestSources = useCallback((target: ActivityDataTarget, signal: AbortSignal) => {
    const { draft: current, persistedDraft: saved } = latest.current;
    const base = acknowledgedRevision(current, saved);
    if (!current || !base) return Promise.reject(new Error("The draft has no acknowledged revision."));
    return requestWorkflowActivityData(contextRef.current, {
      definitionId: current.definitionId,
      draftId: current.id,
      expectedBaseRevision: base,
      documentRevision: getDraftRevision(current),
      target,
      snapshot: canonicalizeStateForWire(current.state)
    }, signal);
  }, []);

  const root = draft?.state.rootActivity;
  const presentation = draft?.activityPresentation;
  const describeActivity = useCallback((nodeId: string): ActivityDataProducerDescription | null => {
    const node = root ? findActivityNode(root, nodeId, catalogByVersion) : null;
    if (!node) return null;
    const item = catalogByVersion.get(node.activityVersionId);
    return {
      label: resolveActivityLabel(indexActivityPresentation(presentation).get(nodeId), item, item?.activityTypeKey ?? node.activityVersionId),
      outputLabels: readOutputLabels(item)
    };
  }, [catalogByVersion, presentation, root]);

  // Findings describe the document the server last validated: shown only while the live draft is that document.
  const findingsCurrent = !!draft && !!persistedDraft && persistedDraft.id === draft.id &&
    getDraftSignature(persistedDraft) === getDraftSignature(draft);
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

  return useMemo(() => available && enabled && draft ? {
    documentRevision,
    baseRevision,
    requestSources,
    describeActivity,
    revealActivity,
    findingsFor
  } : null, [available, baseRevision, describeActivity, documentRevision, draft, enabled, findingsFor, requestSources, revealActivity]);
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
