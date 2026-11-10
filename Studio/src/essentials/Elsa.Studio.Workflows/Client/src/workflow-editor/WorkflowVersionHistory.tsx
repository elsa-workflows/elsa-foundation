import { useState } from "react";
import type { StudioWeaverRegistry, StudioAiPromptActionContribution } from "@elsa-workflows/studio-sdk";
import type { WorkflowVersionSummary } from "../workflowTypes";
import type { WorkflowAgentController } from "../workflowAgent";
import { formatDate } from "../workflowFormatting";
import { dispatchAiAction } from "./editorListHelpers";
import { useWorkflowInspectionContext } from "./useWorkflowInspectionContext";

/** The Workflows module's agent state for the version history: where a selected version's context goes, and its prompt. */
export interface WorkflowVersionHistoryAgent {
  ai: StudioWeaverRegistry;
  controller?: WorkflowAgentController;
  explainAction?: StudioAiPromptActionContribution | null;
}

/**
 * The workflow's persisted versions (spec 1193 story 8). Selecting one discloses that exact version to Weaver as
 * `workflows.version` (its identity only; the server reads the content through the owning design service) and offers to
 * explain it. Versions are immutable: nothing here changes one, and Weaver refuses to.
 */
export function WorkflowVersionHistory({ definitionId, versions, agent }: {
  definitionId: string;
  versions: WorkflowVersionSummary[];
  agent?: WorkflowVersionHistoryAgent;
}) {
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const selected = versions.find(version => version.id === selectedId) ?? null;
  useWorkflowInspectionContext({
    controller: agent?.controller, clientId: agent?.ai.clientId ?? "", kind: "version",
    identity: selected ? { definitionId, versionId: selected.id, version: selected.version } : null
  });
  const explain = () => {
    if (!selected || !agent?.explainAction) return;
    // The version history contributes the selected version itself (workflows.version-history), so the prompt attaches none.
    dispatchAiAction(agent.ai, agent.explainAction, {
      definitionId, versionId: selected.id, version: selected.version, attached: Boolean(agent.controller?.inspection)
    });
  };

  if (versions.length === 0) return <p className="wf-muted">No published versions yet.</p>;
  return (
    <>
      <ul className="wf-properties-versions" aria-label="Workflow versions">
        {versions.map(version => (
          <li key={version.id}>
            {agent ? (
              <button type="button" className="wf-properties-version-select" aria-pressed={version.id === selectedId}
                aria-label={`Select version ${version.version}`}
                onClick={() => setSelectedId(current => current === version.id ? null : version.id)}>
                <span className="wf-properties-version-tag">v{version.version}</span>
              </button>
            ) : <span className="wf-properties-version-tag">v{version.version}</span>}
            <time>{formatDate(version.createdAt)}</time>
          </li>
        ))}
      </ul>
      {selected && agent?.explainAction ? (
        <button type="button" className="wf-properties-version-explain" onClick={explain}>Explain version v{selected.version}</button>
      ) : null}
    </>
  );
}
