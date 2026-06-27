import React from "react";
import { AlertTriangle, Check, Loader2, Play, X } from "lucide-react";
import type { StudioAgentResultRendererContribution } from "../../sdk";
import type { AgentActionProposal } from "./agentTypes";

export function AgentProposalReview({
  proposals,
  disabled,
  pendingProposalIds,
  resultRenderers = [],
  onApprove,
  onDeny,
  onExecute
}: {
  proposals: AgentActionProposal[];
  disabled?: boolean;
  pendingProposalIds?: ReadonlySet<string>;
  resultRenderers?: StudioAgentResultRendererContribution[];
  onApprove(proposal: AgentActionProposal): void;
  onDeny(proposal: AgentActionProposal): void;
  onExecute(proposal: AgentActionProposal): void;
}) {
  if (proposals.length === 0) {
    return null;
  }

  return (
    <section className="agent-proposals" aria-label="Weaver proposals">
      {proposals.map(proposal => {
        const reviewable = isReviewable(proposal);
        const pending = pendingProposalIds?.has(proposal.id) ?? false;
        const controlsDisabled = disabled || pending || proposal.isLoading || Boolean(proposal.disabledReason);
        const resultRenderer = findResultRenderer(proposal, resultRenderers);
        const ResultRenderer = resultRenderer?.component;
        return (
          <article key={proposal.id} className="agent-proposal" data-risk={proposal.risk} data-status={proposal.status}>
            <header>
              <div className="agent-proposal-kicker">
                <span className="agent-proposal-risk"><AlertTriangle size={14} /> {formatRisk(proposal.risk)}</span>
                <span>{formatStatus(proposal.status)}</span>
              </div>
              <strong>{proposal.title}</strong>
            </header>
            <p>{proposal.summary}</p>
            {proposal.resourceTarget ? <ResourceTargetDetails proposal={proposal} /> : null}
            {proposal.operations && proposal.operations.length > 0 ? (
              <ul aria-label="Proposal operations">
                {proposal.operations.map((operation, index) => (
                  <li key={`${operation.op}-${index}`}>{operation.op}</li>
                ))}
              </ul>
            ) : null}
            {proposal.audit ? <AuditDetails proposal={proposal} /> : null}
            {ResultRenderer ? (
              <div className="agent-proposal-result" aria-label={resultRenderer.displayName}>
                <ResultRenderer proposal={proposal} resourceTarget={proposal.resourceTarget} result={proposal.result} resultType={proposal.resultType} />
              </div>
            ) : <GenericResultFallback proposal={proposal} />}
            {proposal.risks && proposal.risks.length > 0 ? (
              <ul aria-label="Proposal risks">
                {proposal.risks.map((risk, index) => <li key={`${risk}-${index}`}>{risk}</li>)}
              </ul>
            ) : null}
            {proposal.rollback ? <small>Rollback: {proposal.rollback}</small> : null}
            {proposal.error ? <small className="agent-proposal-error">{proposal.error}</small> : null}
            {proposal.isLoading ? <small className="agent-proposal-loading"><Loader2 size={14} /> Loading review state.</small> : null}
            {proposal.disabledReason ? <small className="agent-proposal-disabled">{proposal.disabledReason}</small> : null}
            {!reviewable ? <small>Proposal details are still loading. Approval is available after review details arrive.</small> : null}
            <footer>
              <button type="button" disabled={controlsDisabled || proposal.status !== "awaiting-approval" || !reviewable} onClick={() => onApprove(proposal)}>
                <Check size={14} /> Approve
              </button>
              <button type="button" disabled={controlsDisabled || proposal.status !== "awaiting-approval" || !reviewable} onClick={() => onDeny(proposal)}>
                <X size={14} /> Deny
              </button>
              <button type="button" disabled={controlsDisabled || proposal.status !== "approved" || !reviewable} onClick={() => onExecute(proposal)}>
                <Play size={14} /> Apply
              </button>
            </footer>
          </article>
        );
      })}
    </section>
  );
}

function findResultRenderer(
  proposal: AgentActionProposal,
  renderers: StudioAgentResultRendererContribution[]
) {
  const props = {
    proposal,
    resourceTarget: proposal.resourceTarget,
    result: proposal.result,
    resultType: proposal.resultType
  };

  return [...renderers]
    .sort((left, right) => (left.order ?? 500) - (right.order ?? 500) || left.displayName.localeCompare(right.displayName))
    .find(renderer => renderer.id === proposal.resultRendererId
      || (!!proposal.resourceTarget?.resourceType && renderer.resourceTypes?.includes(proposal.resourceTarget.resourceType))
      || (!!proposal.resultType && renderer.resultTypes?.includes(proposal.resultType))
      || renderer.supports?.(props));
}

function GenericResultFallback({ proposal }: { proposal: AgentActionProposal }) {
  if (proposal.result === undefined) {
    return null;
  }

  return (
    <div className="agent-proposal-result" aria-label="Structured result">
      <span>Structured result</span>
      <pre>{formatStructuredValue(proposal.result)}</pre>
    </div>
  );
}

function ResourceTargetDetails({ proposal }: { proposal: AgentActionProposal }) {
  const target = proposal.resourceTarget;
  if (!target) {
    return null;
  }

  return (
    <dl className="agent-proposal-details" aria-label="Resource target">
      <div>
        <dt>Target</dt>
        <dd>{target.displayName ?? target.resourceId ?? target.resourceType}</dd>
      </div>
      <div>
        <dt>Type</dt>
        <dd>{target.resourceType}</dd>
      </div>
      {target.resourceId ? (
        <div>
          <dt>ID</dt>
          <dd>{target.resourceId}</dd>
        </div>
      ) : null}
      {target.moduleId ? (
        <div>
          <dt>Module</dt>
          <dd>{target.moduleId}</dd>
        </div>
      ) : null}
      {target.summary ? (
        <div>
          <dt>Impact</dt>
          <dd>{target.summary}</dd>
        </div>
      ) : null}
    </dl>
  );
}

function AuditDetails({ proposal }: { proposal: AgentActionProposal }) {
  const audit = proposal.audit;
  if (!audit) {
    return null;
  }

  return (
    <dl className="agent-proposal-details" aria-label="Audit state">
      <div>
        <dt>Audit</dt>
        <dd>{formatStatus(audit.state)}</dd>
      </div>
      {audit.outcome ? (
        <div>
          <dt>Outcome</dt>
          <dd>{audit.outcome}</dd>
        </div>
      ) : null}
      {audit.toolId ? (
        <div>
          <dt>Tool</dt>
          <dd>{audit.toolId}</dd>
        </div>
      ) : null}
      {audit.invocationMode ? (
        <div>
          <dt>Mode</dt>
          <dd>{formatStatus(audit.invocationMode)}</dd>
        </div>
      ) : null}
      {audit.policyResult ? (
        <div>
          <dt>Policy</dt>
          <dd>{formatStatus(audit.policyResult)}</dd>
        </div>
      ) : null}
      {audit.actor ? (
        <div>
          <dt>Actor</dt>
          <dd>{audit.actor}</dd>
        </div>
      ) : null}
      {audit.sessionId ? (
        <div>
          <dt>Session</dt>
          <dd>{audit.sessionId}</dd>
        </div>
      ) : null}
      {audit.recordedAt ? (
        <div>
          <dt>Recorded</dt>
          <dd>{audit.recordedAt}</dd>
        </div>
      ) : null}
    </dl>
  );
}

function formatRisk(risk: AgentActionProposal["risk"]) {
  return risk.split("-").join(" ");
}

function formatStatus(status: string) {
  return status.split("-").join(" ");
}

function formatStructuredValue(value: unknown) {
  if (typeof value === "string") {
    return value;
  }

  try {
    return JSON.stringify(value, null, 2);
  } catch {
    return String(value);
  }
}

function isReviewable(proposal: AgentActionProposal) {
  return proposal.reviewReady !== false
    && Boolean(proposal.revision)
    && Boolean(proposal.title.trim())
    && Boolean(proposal.summary.trim())
    && ((proposal.operations?.length ?? 0) > 0 || Boolean(proposal.resourceTarget));
}
