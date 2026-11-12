import React, { Suspense } from "react";
import { WeaverResultBoundary } from "./WeaverResultBoundary";
import { presentsResourceModule, type StudioWeaverResultControlContribution, type StudioWeaverResultRendererContribution, type StudioWeaverToolResult } from "../../sdk";
import { ResultControls } from "./WeaverProposalReview";

/** Displays module payloads opaquely through registered presentation, with a safe structured fallback. */
export function WeaverToolResults({ results, renderers, controls = [] }: { results: StudioWeaverToolResult[]; renderers: StudioWeaverResultRendererContribution[]; controls?: StudioWeaverResultControlContribution[] }) {
  return <section aria-label="Module results">{results.map(result => {
    const payload = result.payload ?? result.receipt?.evidence;
    return <article key={result.toolCallId} data-succeeded={result.succeeded}><p>{result.content}</p>
      {result.receipt ? <div><small>{formatEffectScope(result.receipt.scope)}</small>
        <p>Observed target: {result.receipt.after.resourceId ?? result.receipt.after.resourceType} · revision {result.receipt.after.revision ?? "unversioned"}</p></div> : null}
      <WeaverResultBoundary key={result.toolCallId} fallback={<pre>{JSON.stringify(payload, null, 2)}</pre>}><ToolResultPresentation result={result} renderers={renderers} controls={controls} /></WeaverResultBoundary>
    </article>;
  })}</section>;
}

function ToolResultPresentation({ result, renderers, controls = [] }: { result: StudioWeaverToolResult; renderers: StudioWeaverResultRendererContribution[]; controls?: StudioWeaverResultControlContribution[] }) {
  const payload = result.payload ?? result.receipt?.evidence;
  const props = { proposal: undefined, resourceTarget: result.receipt?.after, result: payload, resultType: payload?.typeId, toolResult: result };
  const resourceModuleId = result.receipt?.after.moduleId;
  const matches = renderers.filter(item => (!resourceModuleId || presentsResourceModule(item, resourceModuleId))
    && (!item.schemaVersions || item.schemaVersions.includes(payload?.schemaVersion ?? ""))
    && (item.resultTypes?.includes(payload?.typeId ?? "") || item.supports?.(props)));
  const Component = matches.length === 1 ? matches[0].component : undefined;
  const fallback = payload ? <pre>{JSON.stringify(payload, null, 2)}</pre> : null;
  const controlled = <ResultControls controls={controls} props={props} resourceModuleId={resourceModuleId} typeId={payload?.typeId} schemaVersion={payload?.schemaVersion} />;
  return <>{Component ? <Suspense fallback={fallback}><Component {...props} /></Suspense> : fallback}{controlled}</>;
}

/** Scope is an owning receipt's wire value, never inferred from its opaque payload or business verdict. */
function formatEffectScope(scope: unknown): string {
  const value = typeof scope === "string" ? scope.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase() : undefined;
  if (scope === 0 || value === "client-working-state") return "Local working state";
  if (scope === 1 || value === "server-persisted") return "Persisted resource";
  if (scope === 2 || value === "no-persisted-effect") return "No persisted resource change";
  return "Effect scope unavailable";
}
