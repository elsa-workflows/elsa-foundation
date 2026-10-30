import React, { Suspense } from "react";
import { AgentResultBoundary } from "./AgentResultBoundary";
import type { StudioAgentResultRendererContribution, StudioAgentToolResult } from "../../sdk";

/** Displays module payloads opaquely through registered presentation, with a safe structured fallback. */
export function AgentToolResults({ results, renderers }: { results: StudioAgentToolResult[]; renderers: StudioAgentResultRendererContribution[] }) {
  return <section aria-label="Module results">{results.map(result => {
    const payload = result.payload ?? result.receipt?.evidence;
    return <article key={result.toolCallId} data-succeeded={result.succeeded}><p>{result.content}</p>
      {result.receipt ? <div><small>{result.receipt.scope === "clientWorkingState" ? "Local working state" : "Persisted resource"}</small>
        <p>Observed target: {result.receipt.after.resourceId ?? result.receipt.after.resourceType} · revision {result.receipt.after.revision ?? "unversioned"}</p></div> : null}
      <AgentResultBoundary key={result.toolCallId} fallback={<pre>{JSON.stringify(payload, null, 2)}</pre>}><ToolResultPresentation result={result} renderers={renderers} /></AgentResultBoundary>
    </article>;
  })}</section>;
}

function ToolResultPresentation({ result, renderers }: { result: StudioAgentToolResult; renderers: StudioAgentResultRendererContribution[] }) {
  const payload = result.payload ?? result.receipt?.evidence;
  const props = { proposal: undefined, resourceTarget: result.receipt?.after, result: payload, resultType: payload?.typeId, toolResult: result };
  const matches = renderers.filter(item => (!result.receipt?.after.moduleId || !item.moduleId || item.moduleId === result.receipt.after.moduleId)
    && (!item.schemaVersions || item.schemaVersions.includes(payload?.schemaVersion ?? ""))
    && (item.resultTypes?.includes(payload?.typeId ?? "") || item.supports?.(props)));
  const Component = matches.length === 1 ? matches[0].component : undefined;
  const fallback = payload ? <pre>{JSON.stringify(payload, null, 2)}</pre> : null;
  return Component ? <Suspense fallback={fallback}><Component {...props} /></Suspense> : fallback;
}
