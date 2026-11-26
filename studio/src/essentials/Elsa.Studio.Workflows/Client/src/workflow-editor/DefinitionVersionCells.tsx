import { StatusChip } from "@elsa-workflows/studio-ui";
import { ChevronDown } from "lucide-react";
import type { WorkflowDefinitionSummary } from "../workflowTypes";

export function LatestDefinitionVersion({ definition }: { definition: WorkflowDefinitionSummary }) {
  const latestVersion = definition.latestVersion;
  if (!definition.draftId) {
    return latestVersion
      ? <span className="wf-definition-version-number">{latestVersion}</span>
      : <span className="wf-definition-version-empty">No version</span>;
  }

  const draftDetails = definition.draftSourceVersion
    ? latestVersion && latestVersion !== definition.draftSourceVersion
      ? `Draft based on version ${definition.draftSourceVersion}. Latest saved version is ${latestVersion}.`
      : `Draft based on saved version ${definition.draftSourceVersion}.`
    : latestVersion
      ? `Draft present. Latest saved version is ${latestVersion}.`
      : "Draft present. No saved version yet.";

  return <>
    {latestVersion ? <span className="wf-definition-version-number">{latestVersion}</span> : null}
    <details className="wf-definition-draft-details" onClick={event => event.stopPropagation()}>
      <summary className="wf-definition-draft-badge" aria-label="Draft details"><StatusChip tone="warning">Draft<ChevronDown size={10} className="wf-definition-draft-chevron" aria-hidden="true" /></StatusChip></summary>
      <span className="wf-definition-draft-copy">{draftDetails}</span>
    </details>
  </>;
}

export function PublishedDefinitionVersion({ definition }: { definition: WorkflowDefinitionSummary }) {
  const state = definition.publicationState;
  if (state === "published" && definition.publishedVersion) {
    return <StatusChip tone="success">{definition.publishedVersion}</StatusChip>;
  }
  if (state === "retracted" || state === "retired") {
    return <StatusChip tone={state === "retracted" ? "warning" : "neutral"}>{state === "retracted" ? "Retracted" : "Retired"}</StatusChip>;
  }
  return <span className="wf-definition-version-empty">{state === "draft" ? "Not published" : "Unavailable"}</span>;
}
