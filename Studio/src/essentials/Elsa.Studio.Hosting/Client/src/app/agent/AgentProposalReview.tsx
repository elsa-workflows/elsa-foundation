import React, { Suspense } from "react";
import { AlertTriangle, Check, Loader2, Play, X } from "lucide-react";
import { presentsResourceModule, type StudioAgentResultControlContribution, type StudioAgentResultRendererContribution, type StudioAgentResultRendererProps } from "../../sdk";
import { AgentResultBoundary } from "./AgentResultBoundary";
import type { AgentActionProposal, AgentOwnerResolutionClaim } from "./agentTypes";

const RESOLUTION_LABELS: Record<AgentOwnerResolutionClaim, string> = {
  "applied": "It was applied",
  "not-applied": "It was not applied",
  "unknown": "I can't tell"
};

const RESOLUTION_STATEMENTS: Record<AgentOwnerResolutionClaim, string> = {
  "applied": "You stated that this change was applied.",
  "not-applied": "You stated that this change was not applied.",
  "unknown": "You stated that you cannot tell whether this change was applied."
};

export function AgentProposalReview({
  proposals,
  disabled,
  pendingProposalIds,
  resultRenderers = [],
  resultControls = [],
  onApprove,
  onDeny,
  onExecute,
  onRetryConfirmation,
  onResolve,
  resolveDisabled
}: {
  proposals: AgentActionProposal[];
  disabled?: boolean;
  pendingProposalIds?: ReadonlySet<string>;
  resultRenderers?: StudioAgentResultRendererContribution[];
  /** The resource-owning modules' controls for a result type, rendered after its renderer. */
  resultControls?: StudioAgentResultControlContribution[];
  onApprove(proposal: AgentActionProposal): void;
  onDeny(proposal: AgentActionProposal): void;
  onExecute(proposal: AgentActionProposal): void;
  onRetryConfirmation?(proposal: AgentActionProposal): void;
  /** Preview (#3441): records the owner's statement about an uncertain client operation. */
  onResolve?(proposal: AgentActionProposal, claim: AgentOwnerResolutionClaim): void;
  /** True while a resolution must wait, for example while a mode change is unconfirmed. */
  resolveDisabled?: boolean;
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
        const resolvable = Boolean(onResolve) && proposal.status === "uncertain" && !proposal.confirmationPending && !proposal.ownerResolution
          && proposal.preparedInvocation?.executionLocation === "clientWorkingState";
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
            <AgentResultBoundary key={`${proposal.id}:${proposal.status}`} fallback={<GenericResultFallback proposal={proposal} />}><ProposalPresentation proposal={proposal} renderers={resultRenderers} controls={resultControls} /></AgentResultBoundary>
            {proposal.risks && proposal.risks.length > 0 ? (
              <ul aria-label="Proposal risks">
                {proposal.risks.map((risk, index) => <li key={`${risk}-${index}`}>{risk}</li>)}
              </ul>
            ) : null}
            {proposal.rollback ? <small>Rollback: {proposal.rollback}</small> : null}
            {proposal.error ? <small className="agent-proposal-error">{proposal.error}</small> : null}
            {proposal.isLoading ? <small className="agent-proposal-loading"><Loader2 size={14} /> Loading review state.</small> : null}
            {proposal.disabledReason ? <small className="agent-proposal-disabled">{proposal.disabledReason}</small> : null}
            {proposal.ownerResolution ? (
              <small className="agent-proposal-resolution" data-claim={proposal.ownerResolution.claim}>
                {RESOLUTION_STATEMENTS[proposal.ownerResolution.claim]} This is your statement, not a receipt; a later receipt from the module still takes precedence.
              </small>
            ) : null}
            {resolvable ? (
              <div className="agent-proposal-resolve" role="group" aria-label="Resolve uncertain change">
                <small>Weaver cannot confirm whether this change was applied, and it will not run it again. Check the resource, then say what you found.</small>
                {(Object.keys(RESOLUTION_LABELS) as AgentOwnerResolutionClaim[]).map(claim => (
                  <button key={claim} type="button" disabled={disabled || pending || resolveDisabled} onClick={() => onResolve!(proposal, claim)}>{RESOLUTION_LABELS[claim]}</button>
                ))}
              </div>
            ) : null}
            {!reviewable ? <small>Proposal details are still loading. Approval is available after review details arrive.</small> : null}
            <footer>
              {proposal.confirmationPending && onRetryConfirmation ? <button type="button" disabled={controlsDisabled} onClick={() => onRetryConfirmation(proposal)}>Retry confirmation</button> : null}
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

function ProposalPresentation({ proposal, renderers, controls = [] }: { proposal: AgentActionProposal; renderers: StudioAgentResultRendererContribution[]; controls?: StudioAgentResultControlContribution[] }) {
  const renderer = findResultRenderer(proposal, renderers);
  const Component = renderer?.component;
  const props: StudioAgentResultRendererProps = { proposal, resourceTarget: proposal.resourceTarget, result: proposal.result, resultType: proposal.resultType,
    preparedInvocation: proposal.preparedInvocation, toolResult: proposal.toolResult };
  const presentation = Component ? <div className="agent-proposal-result" aria-label={renderer.displayName}><Suspense fallback={<div><p>Loading module presentation…</p><GenericResultFallback proposal={proposal} /></div>}><Component {...props} /></Suspense></div> : <GenericResultFallback proposal={proposal} />;
  const payload = proposal.toolResult?.payload ?? proposal.preparedInvocation?.preview;
  return <>{presentation}<ResultControls controls={controls} props={props} resourceModuleId={proposal.resourceTarget?.moduleId} typeId={payload?.typeId ?? proposal.resultType} schemaVersion={payload?.schemaVersion} /></>;
}

/** Every control the resource-owning module contributes for this result type, after the renderer, each isolated. */
export function ResultControls({ controls, props, resourceModuleId, typeId, schemaVersion }: {
  controls: StudioAgentResultControlContribution[]; props: StudioAgentResultRendererProps; resourceModuleId?: string | null; typeId?: string; schemaVersion?: string;
}) {
  const matching = controls.filter(control => !!typeId && control.resultTypes.includes(typeId) && presentsResourceModule(control, resourceModuleId)
    && (!control.schemaVersions || control.schemaVersions.includes(schemaVersion ?? "")))
    .sort((left, right) => (left.order ?? 500) - (right.order ?? 500) || left.displayName.localeCompare(right.displayName));
  return <>{matching.map(control => {
    const Control = control.component;
    return <AgentResultBoundary key={control.id} fallback={null}><Suspense fallback={null}><div className="agent-result-control" aria-label={control.displayName}><Control {...props} /></div></Suspense></AgentResultBoundary>;
  })}</>;
}

function findResultRenderer(
  proposal: AgentActionProposal,
  renderers: StudioAgentResultRendererContribution[]
) {
  const props = {
    proposal,
    resourceTarget: proposal.resourceTarget,
    result: proposal.result,
    resultType: proposal.resultType,
    preparedInvocation: proposal.preparedInvocation,
    toolResult: proposal.toolResult
  };
  const explicitRenderer = renderers.find(renderer => renderer.id === proposal.resultRendererId);
  if (explicitRenderer && presentsResourceModule(explicitRenderer, proposal.resourceTarget?.moduleId)
    && (!explicitRenderer.schemaVersions || explicitRenderer.schemaVersions.includes(proposal.toolResult?.payload?.schemaVersion ?? proposal.preparedInvocation?.preview.schemaVersion ?? ""))) {
    return explicitRenderer;
  }

  return [...renderers]
    .filter(renderer => presentsResourceModule(renderer, proposal.resourceTarget?.moduleId)
      && (!renderer.schemaVersions || renderer.schemaVersions.includes(proposal.toolResult?.payload?.schemaVersion ?? proposal.preparedInvocation?.preview.schemaVersion ?? "")))
    .sort((left, right) => (left.order ?? 500) - (right.order ?? 500) || left.displayName.localeCompare(right.displayName))
    .find(renderer => (!!proposal.resourceTarget?.resourceType && renderer.resourceTypes?.includes(proposal.resourceTarget.resourceType))
      || (!!proposal.resultType && renderer.resultTypes?.includes(proposal.resultType))
      || renderer.supports?.(props));
}

function GenericResultFallback({ proposal }: { proposal: AgentActionProposal }) {
  const result = proposal.result ?? proposal.toolResult?.payload ?? proposal.toolResult?.receipt?.evidence ?? proposal.preparedInvocation?.preview;
  if (result === undefined) return null;

  return (
    <div className="agent-proposal-result" aria-label="Structured result">
      <span>Structured result</span>
      {proposal.toolResult?.receipt ? <small>Observed revision: {proposal.toolResult.receipt.after.revision ?? "unversioned"}</small> : null}
      <pre>{formatStructuredValue(result)}</pre>
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
      {target.revision ? <div><dt>Revision</dt><dd>{target.revision}</dd></div> : null}
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
