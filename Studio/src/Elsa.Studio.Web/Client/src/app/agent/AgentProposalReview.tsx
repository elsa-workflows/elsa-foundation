import React from "react";
import { AlertTriangle, Check, Play, X } from "lucide-react";
import type { AgentActionProposal } from "./agentTypes";

export function AgentProposalReview({
  proposals,
  disabled,
  pendingProposalIds,
  onApprove,
  onDeny,
  onExecute
}: {
  proposals: AgentActionProposal[];
  disabled?: boolean;
  pendingProposalIds?: ReadonlySet<string>;
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
        return (
          <article key={proposal.id} className="agent-proposal" data-risk={proposal.risk} data-status={proposal.status}>
            <header>
              <span className="agent-proposal-risk"><AlertTriangle size={14} /> {formatRisk(proposal.risk)}</span>
              <strong>{proposal.title}</strong>
            </header>
            <p>{proposal.summary}</p>
            {proposal.operations && proposal.operations.length > 0 ? (
              <ul>
                {proposal.operations.map((operation, index) => (
                  <li key={`${operation.op}-${index}`}>{operation.op}</li>
                ))}
              </ul>
            ) : null}
            {proposal.rollback ? <small>Rollback: {proposal.rollback}</small> : null}
            {!reviewable ? <small>Proposal details are still loading. Approval is available after review details arrive.</small> : null}
            <footer>
              <button type="button" disabled={disabled || pending || proposal.status !== "awaiting-approval" || !reviewable} onClick={() => onApprove(proposal)}>
                <Check size={14} /> Approve
              </button>
              <button type="button" disabled={disabled || pending || proposal.status !== "awaiting-approval" || !reviewable} onClick={() => onDeny(proposal)}>
                <X size={14} /> Deny
              </button>
              <button type="button" disabled={disabled || pending || proposal.status !== "approved" || !reviewable} onClick={() => onExecute(proposal)}>
                <Play size={14} /> Execute
              </button>
            </footer>
          </article>
        );
      })}
    </section>
  );
}

function formatRisk(risk: AgentActionProposal["risk"]) {
  return risk.split("-").join(" ");
}

function isReviewable(proposal: AgentActionProposal) {
  return proposal.reviewReady !== false
    && Boolean(proposal.revision)
    && Boolean(proposal.title.trim())
    && Boolean(proposal.summary.trim())
    && (proposal.operations?.length ?? 0) > 0;
}
