import React from "react";
import { CopyableIdentifier, StudioAlert } from "@elsa-workflows/studio-ui";
import type { InvocationEvidence } from "./playgroundApi";

type Turn = NonNullable<InvocationEvidence["turn"]>;

/**
 * The permitted evidence of one specialist invocation, shared by the playground and the invocation page: a truthful
 * outcome-unknown warning, the native fault, the accepted output when the caller may see it, and the exact version,
 * model, requests, usage, application and conversation.
 */
export function InvocationEvidenceDetails({ invocation, valuesPermitted, terminal, versionLabel, outcomeUnknownAdvice }: {
  invocation: InvocationEvidence; valuesPermitted: boolean; terminal: boolean; versionLabel: string; outcomeUnknownAdvice: string;
}) {
  const turn = invocation.turn;
  return (
    <>
      {turn?.status === "OutcomeUnknown" ? (
        <StudioAlert tone="warning">
          The model may have received this request, but no result was accepted. It is not retried automatically. {outcomeUnknownAdvice}
        </StudioAlert>
      ) : null}
      {invocation.fault ? (
        <StudioAlert tone="danger"><span data-testid="evidence-fault">{invocation.fault.message ?? "The agent did not complete."}</span> <code>{invocation.fault.code}</code></StudioAlert>
      ) : null}
      {invocation.output ? <Output output={invocation.output} valuesPermitted={valuesPermitted} /> : null}
      <dl className="agent-playground__facts">
        <dt>{versionLabel}</dt><dd>{invocation.specialist.version} <CopyableIdentifier label={`${versionLabel} version`} value={invocation.specialist.definitionVersionId} /></dd>
        <dt>Model</dt><dd>{invocation.model.modelId} on {invocation.model.connectionName}</dd>
        {turn ? <><dt>Model requests</dt><dd data-testid="evidence-requests">{turn.modelRequests.length} of at most {turn.maximumModelCalls}</dd></> : null}
        {turn ? <><dt>Usage</dt><dd data-testid="evidence-usage">{usageText(turn.usage, terminal)}</dd></> : null}
        {turn ? <><dt>Result</dt><dd data-testid="evidence-application">{applicationText(turn.application)}</dd></> : null}
        {turn?.conversation ? <><dt>Conversation</dt><dd>
          <CopyableIdentifier label="Conversation" value={turn.conversation.conversationId} />
          {turn.conversation.currentRevision != null ? ` at revision ${turn.conversation.currentRevision}` : " (no longer readable)"}
        </dd></> : null}
        {turn?.contentPurgedAt ? <><dt>Content</dt><dd>Removed under content retention</dd></> : null}
      </dl>
    </>
  );
}

function Output({ output, valuesPermitted }: { output: NonNullable<InvocationEvidence["output"]>; valuesPermitted: boolean }) {
  if (!valuesPermitted)
    return <p className="agent-playground__hint" data-testid="evidence-output-withheld">Seeing the result needs permission to inspect captured values in this run.</p>;
  if (output.captureMode !== "Payload" || output.value === undefined || output.value === null)
    return <p className="agent-playground__hint" data-testid="evidence-output-withheld">The host's capture policy keeps this result out of this view ({output.captureMode}).</p>;
  return typeof output.value === "string"
    ? <div className="agent-playground__text" data-testid="evidence-output">{output.value}</div>
    : <pre className="agent-playground__json" data-testid="evidence-output">{JSON.stringify(output.value, null, 2)}</pre>;
}

export function applicationText(application: Turn["application"]) {
  switch (application) {
    case "NotAccepted": return "No accepted result";
    case "Pending": return "Accepted; the agent has not finished yet";
    case "SettlementPending": return "Accepted and used; settlement is being recorded";
    case "Applied": return "Accepted and applied";
    case "NotApplied": return "Accepted, but the run ended before applying it";
  }
}

export function usageText(usage: Turn["usage"], terminal: boolean) {
  if (!usage) return terminal ? "Not reported" : "Not reported yet";
  if (!usage.usageAvailable) return "The provider did not report usage";
  const part = (value: number | null | undefined) => value == null ? "unknown" : String(value);
  return `${part(usage.inputTokens)} input, ${part(usage.outputTokens)} output, ${part(usage.totalTokens)} total tokens`;
}
