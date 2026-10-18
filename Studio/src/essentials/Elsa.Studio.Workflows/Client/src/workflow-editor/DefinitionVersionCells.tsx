import type { WorkflowDefinitionSummary } from "../workflowTypes";

export function LatestDefinitionVersion({ definition }: { definition: WorkflowDefinitionSummary }) {
  if (!definition.draftId) return <>{definition.latestVersion ?? "No version"}</>;
  return <>
    <span className="wf-status-badge" data-status="suspended">Draft</span>
    {definition.draftSourceVersion ? <small>Based on {definition.draftSourceVersion}</small> : null}
    {definition.latestVersion && definition.latestVersion !== definition.draftSourceVersion
      ? <small>Latest saved {definition.latestVersion}</small>
      : null}
  </>;
}

export function PublishedDefinitionVersion({ definition }: { definition: WorkflowDefinitionSummary }) {
  const state = definition.publicationState;
  if (state === "published" && definition.publishedVersion) {
    return <span className="wf-status-badge" data-status="completed">{definition.publishedVersion}</span>;
  }
  if (state === "retracted" || state === "retired") {
    return <span className="wf-status-badge" data-status="cancelled">{state === "retracted" ? "Retracted" : "Retired"}</span>;
  }
  return <span className="wf-definition-version-empty">{state === "draft" ? "Not published" : "Unavailable"}</span>;
}
