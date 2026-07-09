import React, { useEffect, useMemo, useRef, useState } from "react";
import { Check, CheckCircle2, ChevronDown, Shield, X } from "lucide-react";
import type { ElsaStudioModuleApi, StudioAgentSurface } from "../../sdk";
import type { AgentClient } from "../agent/agentClient";
import { AgentContextChips } from "../agent/AgentContextChips";
import { AgentPromptStarters } from "../agent/AgentPromptStarters";
import { AgentProposalReview } from "../agent/AgentProposalReview";
import { AgentWorkflowBatchReview } from "../agent/AgentWorkflowBatchReview";
import type { subscribeAgentSessionStream } from "../agent/agentStream";
import type { AgentSessionIndicatorSession } from "../agent/AgentSessionIndicator";
import { WeaverComposer } from "./WeaverComposer";
import { WeaverMessageList } from "./WeaverMessageList";
import { WeaverTimeline } from "./WeaverTimeline";
import { useWeaverSession, type UseWeaverSession, type WeaverAutoApplyMode } from "./useWeaverSession";

const AUTO_APPLY_LABELS: Record<WeaverAutoApplyMode, string> = {
  "manual": "Manual review",
  "auto-read-only": "Auto-apply low-risk",
  "full-auto": "Autopilot"
};

// Tooltip copy for the autonomy menu.
const AUTO_APPLY_HINTS: Record<WeaverAutoApplyMode, string> = {
  "manual": "Every change is proposed for your review before anything is applied.",
  "auto-read-only": "Read-only steps run automatically; mutating changes are still proposed for review.",
  "full-auto": "Weaver applies safe changes and runs tasks autonomously, deferring only genuinely risky edits for review."
};

export function WeaverSurface({
  api,
  surface,
  variant,
  onClose,
  client,
  subscribeStream,
  onSessionIndicatorChange,
  incomingPrompt
}: {
  api: ElsaStudioModuleApi;
  surface: StudioAgentSurface;
  variant: "dock" | "full";
  onClose?(): void;
  client?: AgentClient;
  subscribeStream?: typeof subscribeAgentSessionStream;
  onSessionIndicatorChange?(sessions: AgentSessionIndicatorSession[]): void;
  incomingPrompt?: { id: string; message: string; requestId?: string } | null;
}) {
  const session = useWeaverSession({ api, surface, client, subscribeStream });
  const resultRenderers = useMemo(() => api.agent.resultRenderers.list(), [api]);
  const providerStatus = useMemo(() => describeProviderStatus(session), [session]);
  const sendRef = useRef(session.send);
  sendRef.current = session.send;
  const lastPromptIdRef = useRef<string | null>(null);

  // Memoize on the underlying state (not the per-render session object) to avoid an update loop.
  const indicatorSessions = useMemo(
    () => toIndicatorSessions(session),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [session.sessionId, session.proposals, session.workflowBatches, session.error, session.busy]
  );

  useEffect(() => {
    onSessionIndicatorChange?.(indicatorSessions);
  }, [onSessionIndicatorChange, indicatorSessions]);

  useEffect(() => () => onSessionIndicatorChange?.([]), [onSessionIndicatorChange]);

  useEffect(() => {
    if (incomingPrompt && incomingPrompt.id !== lastPromptIdRef.current && session.ready) {
      lastPromptIdRef.current = incomingPrompt.id;
      sendRef.current(incomingPrompt.message, { requestId: incomingPrompt.requestId });
    }
  }, [incomingPrompt, session.ready]);

  const composerDisabled = !session.ready;

  return (
    <section className="weaver-surface" data-variant={variant} aria-label="Weaver assistant" aria-busy={session.busy}>
      <header className="weaver-surface-header">
        <div className="weaver-surface-title">
          <span>Weaver</span>
          <strong>Workflow assistant</strong>
        </div>
        <div className="weaver-surface-actions">
          <WeaverAutoApplyMenu
            value={session.autoApplyMode}
            modes={session.allowedAutoApplyModes}
            disabled={!session.ready}
            onChange={session.setAutoApplyMode}
          />
          {onClose ? (
            <button type="button" className="weaver-close" aria-label="Close assistant" onClick={onClose}>
              <X size={16} />
            </button>
          ) : null}
        </div>
      </header>

      <div className="weaver-status" data-tone={providerStatus.tone} data-testid="weaver-provider-status">
        {providerStatus.tone === "success" ? <CheckCircle2 size={14} /> : <Shield size={14} />}
        <span>{providerStatus.label}</span>
        {session.activeProvider ? <small>{session.activeProvider.providerId} · {session.activeProvider.riskProfile}</small> : null}
      </div>

      {session.error ? <div className="weaver-alert" role="alert">{session.error}</div> : null}

      <AgentContextChips attachments={session.attachments} />
      <AgentPromptStarters prompts={session.promptStarters} disabled={composerDisabled || session.busy} onSelect={prompt => session.send(prompt)} />

      <div className="weaver-scroll">
        <WeaverMessageList messages={session.messages} streaming={session.busy} onFeedback={session.submitFeedback} />
        <WeaverTimeline entries={session.timeline} plan={session.plan} />
        <AgentWorkflowBatchReview batches={session.workflowBatches} disabled={session.busy} onApply={session.applyWorkflowBatch} onUndo={session.undoWorkflowBatch} />
        <AgentProposalReview
          proposals={session.proposals}
          disabled={session.busy}
          pendingProposalIds={session.pendingProposalIds}
          resultRenderers={resultRenderers}
          onApprove={session.approveProposal}
          onDeny={session.denyProposal}
          onExecute={session.executeProposal}
        />
      </div>

      <WeaverComposer
        disabled={composerDisabled}
        busy={session.busy}
        followups={session.followups}
        onSubmit={session.send}
        onStop={session.stop}
        onRemoveFollowup={session.removeFollowup}
      />
    </section>
  );
}

function WeaverAutoApplyMenu({
  value,
  modes,
  disabled,
  onChange
}: {
  value: WeaverAutoApplyMode;
  modes: readonly WeaverAutoApplyMode[];
  disabled: boolean;
  onChange(mode: WeaverAutoApplyMode): void;
}) {
  const [open, setOpen] = useState(false);
  const wrapperRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;

    const closeOnOutsidePointer = (event: PointerEvent) => {
      if (!wrapperRef.current?.contains(event.target as Node)) {
        setOpen(false);
      }
    };
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setOpen(false);
      }
    };

    document.addEventListener("pointerdown", closeOnOutsidePointer);
    document.addEventListener("keydown", closeOnEscape);
    return () => {
      document.removeEventListener("pointerdown", closeOnOutsidePointer);
      document.removeEventListener("keydown", closeOnEscape);
    };
  }, [open]);

  const selectMode = (mode: WeaverAutoApplyMode) => {
    onChange(mode);
    setOpen(false);
  };

  return (
    <div className="weaver-auto-apply" ref={wrapperRef}>
      <button
        type="button"
        className="weaver-mode-button"
        aria-label="Auto-apply mode"
        aria-haspopup="menu"
        aria-expanded={open}
        disabled={disabled}
        title={AUTO_APPLY_HINTS[value]}
        onClick={() => setOpen(current => !current)}
      >
        <span>{AUTO_APPLY_LABELS[value]}</span>
        <ChevronDown size={14} aria-hidden="true" />
      </button>
      {open ? (
        <div className="weaver-mode-menu" role="menu" aria-label="Auto-apply mode">
          {modes.map(mode => (
            <button
              type="button"
              role="menuitemradio"
              aria-checked={mode === value}
              className="weaver-mode-option"
              key={mode}
              title={AUTO_APPLY_HINTS[mode]}
              onClick={() => selectMode(mode)}
            >
              <span>
                <strong>{AUTO_APPLY_LABELS[mode]}</strong>
                <small>{AUTO_APPLY_HINTS[mode]}</small>
              </span>
              {mode === value ? <Check size={14} aria-hidden="true" /> : null}
            </button>
          ))}
        </div>
      ) : null}
    </div>
  );
}

function describeProviderStatus(session: UseWeaverSession): { label: string; tone: "success" | "neutral" | "warning" } {
  const bootstrap = session.bootstrap;
  if (!bootstrap || bootstrap.enabled !== true || bootstrap.providerStatus === "unavailable" || bootstrap.providerStatus === "disabled") {
    return { label: "Weaver is unavailable. Studio remains fully usable.", tone: "warning" };
  }
  if (session.capabilities.length === 0) {
    return { label: "No Weaver capabilities are enabled for this screen.", tone: "warning" };
  }
  const directApply = session.activeProvider?.riskProfile === "sandboxed-execution" || session.activeProvider?.supportedOperations.includes("tool-approval");
  if (directApply) {
    return { label: "Agentic — tools run and changes can be applied with review.", tone: "success" };
  }
  return { label: "Agentic — Weaver can read context, run tools, and propose changes.", tone: "neutral" };
}

function toIndicatorSessions(session: UseWeaverSession): AgentSessionIndicatorSession[] {
  if (!session.sessionId) return [];
  const pendingProposals = session.proposals.filter(proposal => proposal.status === "awaiting-approval" || proposal.status === "approved").length;
  const pendingArtifacts = session.workflowBatches.filter(batch => batch.status === "ready" || batch.status === "applying").length;
  const status: AgentSessionIndicatorSession["status"] = session.error
    ? "failed"
    : pendingProposals > 0
      ? "waiting"
      : session.busy
        ? "active"
        : "background";
  return [{ id: session.sessionId, title: "Current Weaver session", status, pendingProposals, pendingArtifacts }];
}
