import { useEffect, useRef, useState } from "react";
import { StatusChip, StudioButton, StudioVersionHistory } from "@elsa-workflows/studio-ui";
import type { StudioWeaverRegistry, StudioAiPromptActionContribution } from "@elsa-workflows/studio-sdk";
import type { WorkflowDefinitionVersionDetails, WorkflowVersionSummary } from "../workflowTypes";
import type { WorkflowWeaverController } from "../workflowWeaver";
import { formatDate } from "../workflowFormatting";
import { dispatchAiAction } from "./editorListHelpers";
import { useWorkflowInspectionContext } from "./useWorkflowInspectionContext";

/** The Workflows module's Weaver state for the version history: where a selected version's context goes, and its prompt. */
export interface WorkflowVersionHistoryWeaver {
  ai: StudioWeaverRegistry;
  controller?: WorkflowWeaverController;
  explainAction?: StudioAiPromptActionContribution | null;
}

/**
 * The workflow's persisted versions (spec 1193 story 8). Selecting one discloses that exact version to Weaver as
 * `workflows.version` (its identity only; the server reads the content through the owning design service) and offers to
 * explain it. Versions are immutable: nothing here changes one, and Weaver refuses to.
 */
export function WorkflowVersionHistory({ definitionId, versions, latestVersionId, publishedVersionId, workingDraft = false, workingDraftSourceVersion, loadVersion, weaver }: {
  definitionId: string;
  versions: WorkflowVersionSummary[];
  latestVersionId?: string | null;
  publishedVersionId?: string | null;
  workingDraft?: boolean;
  workingDraftSourceVersion?: string | null;
  loadVersion(versionId: string): Promise<WorkflowDefinitionVersionDetails>;
  weaver?: WorkflowVersionHistoryWeaver;
}) {
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [snapshot, setSnapshot] = useState<WorkflowDefinitionVersionDetails | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string>();
  const requestGeneration = useRef(0);
  const selected = versions.find(version => version.id === selectedId) ?? null;
  useEffect(() => () => { requestGeneration.current++; }, []);
  useWorkflowInspectionContext({
    controller: weaver?.controller, clientId: weaver?.ai.clientId ?? "", kind: "version",
    identity: selected ? { definitionId, versionId: selected.id, version: selected.version } : null
  });
  const explain = () => {
    if (!selected || !weaver?.explainAction) return;
    // The version history contributes the selected version itself (workflows.version-history), so the prompt attaches none.
    dispatchAiAction(weaver.ai, weaver.explainAction, {
      definitionId, versionId: selected.id, version: selected.version, attached: Boolean(weaver.controller?.inspection)
    });
  };
  const selectVersion = (id: string) => {
    const version = versions.find(item => item.id === id);
    if (!version) return;
    const request = ++requestGeneration.current;
    if (selectedId === id) {
      setSelectedId(null); setSnapshot(null); setLoading(false); setError(undefined);
      return;
    }
    setSelectedId(id); setSnapshot(null); setLoading(true); setError(undefined);
    void loadVersion(id).then(value => {
      if (request === requestGeneration.current) setSnapshot(value);
    }).catch(cause => {
      if (request === requestGeneration.current) setError(cause instanceof Error ? cause.message : "Could not load this workflow version.");
    }).finally(() => {
      if (request === requestGeneration.current) setLoading(false);
    });
  };
  const latestId = latestVersionId ?? versions[0]?.id ?? null;
  const workingDraftNote = workingDraft ? <div className="wf-version-working-draft">
    <StatusChip tone="warning">Working draft</StatusChip>
    <p>Changes in the editor are separate from these immutable saved versions.</p>
    {workingDraftSourceVersion ? <small>Based on saved version {workingDraftSourceVersion}.</small> : null}
  </div> : null;
  const detail = snapshot && selected ? <div className="wf-version-snapshot">
    <div className="wf-version-snapshot-status">
      <StatusChip tone="neutral">Saved version</StatusChip>
      {selected.id === latestId ? <StatusChip tone="success">Latest saved</StatusChip> : null}
      {selected.id === publishedVersionId ? <StatusChip tone="accent">Published</StatusChip> : null}
    </div>
    <h4>{snapshot.definition.name}</h4>
    <p>Version {snapshot.version} · {formatDate(selected.createdAt)}</p>
    {snapshot.definition.description ? <p>{snapshot.definition.description}</p> : null}
    <p>This immutable snapshot is read-only. Inspecting it does not change the working draft.</p>
    <details>
      <summary>View technical details</summary>
      <pre>{JSON.stringify({ state: snapshot.state, layout: snapshot.layout, activityPresentation: snapshot.activityPresentation ?? [] }, null, 2)}</pre>
    </details>
  </div> : loading ? <p role="status">Loading version snapshot…</p> : error ? <p role="alert">{error}</p> : null;
  return (
    <>
      <StudioVersionHistory
        label="Workflow version history"
        items={versions.map(version => ({
          id: version.id,
          ariaLabel: `Select version ${version.version}`,
          title: `Version ${version.version}`,
          subtitle: formatDate(version.createdAt),
          status: <span className="wf-version-snapshot-status">
            {version.id === latestId ? <StatusChip tone="success">Latest saved</StatusChip> : null}
            {version.id === publishedVersionId ? <StatusChip tone="accent">Published</StatusChip> : null}
          </span>
        }))}
        selectedId={selectedId}
        onSelect={selectVersion}
        emptyState="No published versions yet."
        selectionHint="Choose a saved version to inspect its read-only snapshot."
        detail={detail}
        listFooter={workingDraftNote}
      />
      {selected && weaver?.explainAction ? (
        <StudioButton type="button" className="wf-properties-version-explain" onClick={explain}>Explain version v{selected.version}</StudioButton>
      ) : null}
    </>
  );
}
