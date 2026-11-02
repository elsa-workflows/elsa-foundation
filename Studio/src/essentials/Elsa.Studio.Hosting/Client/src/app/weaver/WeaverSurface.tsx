import React, { useEffect, useId, useMemo, useRef, useState } from "react";
import { Check, CheckCircle2, ChevronDown, Shield, X } from "lucide-react";
import type { ElsaStudioModuleApi, StudioAgentPromptRequest, StudioAgentSurface } from "../../sdk";
import type { AgentClient } from "../agent/agentClient";
import { AgentContextChips } from "../agent/AgentContextChips";
import { AgentPromptStarters } from "../agent/AgentPromptStarters";
import { AgentToolResults } from "../agent/AgentToolResults";
import { AgentProposalReview } from "../agent/AgentProposalReview";
import type { subscribeAgentSessionStream } from "../agent/agentStream";
import type { AgentSessionIndicatorSession } from "../agent/AgentSessionIndicator";
import { WeaverComposer } from "./WeaverComposer";
import { WeaverMessageList } from "./WeaverMessageList";
import { WeaverTimeline } from "./WeaverTimeline";
import { useWeaverSession, WEAVER_AUTO_APPLY_LABELS, type UseWeaverSession, type WeaverAutoApplyMode, type WeaverAutoApplyNotice, type WeaverAutoApplyState } from "./useWeaverSession";

const AUTO_APPLY_LABELS = WEAVER_AUTO_APPLY_LABELS;

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
  incomingPrompt,
  onPromptConsumed
}: {
  api: ElsaStudioModuleApi;
  surface: StudioAgentSurface;
  variant: "dock" | "full";
  onClose?(): void;
  client?: AgentClient;
  subscribeStream?: typeof subscribeAgentSessionStream;
  onSessionIndicatorChange?(sessions: AgentSessionIndicatorSession[]): void;
  incomingPrompt?: (StudioAgentPromptRequest & { id: string }) | null;
  onPromptConsumed?(id: string): void;
}) {
  const session = useWeaverSession({ api, surface, client, subscribeStream });
  const resultRenderers = useMemo(() => api.agent.resultRenderers.list(), [api]);
  const providerStatus = useMemo(() => describeProviderStatus(session), [session]);
  const sendRef = useRef(session.send);
  sendRef.current = session.send;
  const lastPromptIdRef = useRef<string | null>(null);
  const autoApplyStatusId = useId();
  const autoApplyStatus = describeAutoApplyStatus(session.autoApplyState, session.pendingAutoApplyMode, session.autoApplyUnsupported, session.autoApplyNotice);

  // Memoize on the underlying state (not the per-render session object) to avoid an update loop.
  const indicatorSessions = useMemo(
    () => toIndicatorSessions(session),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [session.sessionId, session.proposals, session.error, session.busy]
  );

  useEffect(() => {
    onSessionIndicatorChange?.(indicatorSessions);
  }, [onSessionIndicatorChange, indicatorSessions]);

  useEffect(() => () => onSessionIndicatorChange?.([]), [onSessionIndicatorChange]);

  useEffect(() => {
    if (incomingPrompt && incomingPrompt.id !== lastPromptIdRef.current && session.bootstrap !== null) {
      lastPromptIdRef.current = incomingPrompt.id;
      onPromptConsumed?.(incomingPrompt.id);
      sendRef.current(incomingPrompt);
    }
  }, [incomingPrompt, onPromptConsumed, session.bootstrap, session.ready]);

  // An unacknowledged mode change blocks dependent prompts, including module actions routed through send().
  const composerDisabled = !session.ready || session.chatState === "awaiting-review" || session.chatState === "awaiting-confirmation" || session.autoApplyBlocked;

  return (
    <section className="weaver-surface" data-variant={variant} aria-label="Weaver assistant" aria-busy={session.busy}>
      <header className="weaver-surface-header">
        <div className="weaver-surface-title">
          <span>Weaver</span>
          <strong>Studio assistant</strong>
        </div>
        <div className="weaver-surface-actions">
          <WeaverAutoApplyMenu
            value={session.autoApplyMode}
            modes={session.allowedAutoApplyModes}
            state={session.autoApplyState}
            pendingMode={session.pendingAutoApplyMode}
            disabled={!session.ready || session.autoApplyUnsupported !== null}
            statusId={autoApplyStatus ? autoApplyStatusId : undefined}
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

      {autoApplyStatus ? (
        <div className="weaver-mode-status" id={autoApplyStatusId} role="status" aria-label="Auto-apply mode status" data-autonomy-state={session.autoApplyState}>
          <span>{autoApplyStatus}</span>
          {session.autoApplyState === "unresolved" ? (
            <button type="button" className="weaver-mode-retry" onClick={session.retryAutoApplyMode}>Retry mode change</button>
          ) : null}
        </div>
      ) : null}

      {session.error ? <div className="weaver-alert" role="alert">{session.error}</div> : null}

      <AgentContextChips attachments={session.attachments} />
      {session.contextDiagnostics.length ? <div role="status" aria-label="Context limitations">{session.contextDiagnostics.map((item, index) => <p key={`${item.contributorId}-${index}`}>{item.contributorId}: {item.message}</p>)}</div> : null}
      <AgentPromptStarters prompts={session.promptStarters} disabled={composerDisabled || session.busy} onSelect={prompt => session.send(prompt)} />

      <div className="weaver-scroll">
        <WeaverMessageList messages={session.messages} streaming={session.busy} onFeedback={session.submitFeedback} />
        <WeaverTimeline entries={session.timeline} plan={session.plan} />
        <AgentToolResults results={session.toolResults} renderers={resultRenderers} />
        <AgentProposalReview
          proposals={session.proposals}
          disabled={session.busy}
          pendingProposalIds={session.pendingProposalIds}
          resultRenderers={resultRenderers}
          onApprove={session.approveProposal}
          onDeny={session.denyProposal}
          onExecute={session.executeProposal}
          onRetryConfirmation={session.retryConfirmation}
        />
      </div>

      <WeaverComposer
        disabled={composerDisabled}
        busy={session.busy || (session.chatState !== "idle" && session.pendingProposalIds.size > 0)}
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
  state,
  pendingMode,
  disabled,
  statusId,
  onChange
}: {
  value: WeaverAutoApplyMode;
  modes: readonly WeaverAutoApplyMode[];
  state: WeaverAutoApplyState;
  pendingMode: WeaverAutoApplyMode | null;
  disabled: boolean;
  statusId?: string;
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
    <div className="weaver-auto-apply" ref={wrapperRef} data-autonomy-state={state} data-pending-mode={pendingMode ?? undefined}>
      {/* The button always names the mode in effect (acknowledged); a pending request is only described. */}
      <button
        type="button"
        className="weaver-mode-button"
        aria-label="Auto-apply mode"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-busy={pendingMode !== null}
        aria-describedby={statusId}
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
              // One change per conversation at a time: other modes are unavailable until it resolves.
              disabled={pendingMode !== null && mode !== pendingMode}
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

/** Accessible copy for the selector's pending, unresolved, unavailable and settled outcomes. */
function describeAutoApplyStatus(
  state: WeaverAutoApplyState,
  pendingMode: WeaverAutoApplyMode | null,
  unsupported: "unsupported" | "unreadable" | null,
  notice: WeaverAutoApplyNotice | null
): string | null {
  if (state === "pending" && pendingMode) {
    return `Changing to ${AUTO_APPLY_LABELS[pendingMode]}. It is not in effect until the server confirms it; new prompts wait until then.`;
  }
  if (state === "unresolved" && pendingMode) {
    return `The change to ${AUTO_APPLY_LABELS[pendingMode]} was sent but not confirmed, so it is not in effect and new prompts are blocked. Retry checks the same request without applying it twice.`;
  }
  if (unsupported === "unsupported") return "This conversation cannot change its auto-apply mode. Stop and start a new conversation to use a different mode.";
  if (unsupported === "unreadable") return "This conversation cannot confirm its auto-apply mode, so the mode cannot be changed here.";
  if (!notice) return null;
  switch (notice.kind) {
    case "clamped":
      return `The server applied ${AUTO_APPLY_LABELS[notice.acknowledgedMode]} instead of ${AUTO_APPLY_LABELS[notice.requestedMode]}, within this conversation's limits.`;
    case "reconciled":
      return `The mode changed while your request was being confirmed. The server's current mode is ${AUTO_APPLY_LABELS[notice.acknowledgedMode]}.`;
    case "busy":
      return `Weaver is still working in this conversation, so the mode was not changed to ${AUTO_APPLY_LABELS[notice.requestedMode]}. Try again when it is idle.`;
    case "closed":
      return `This conversation has ended, so the mode was not changed to ${AUTO_APPLY_LABELS[notice.requestedMode]}.`;
    case "starting":
      return `The conversation is starting, so ${AUTO_APPLY_LABELS[notice.requestedMode]} was not applied. Choose it again once the conversation has started.`;
    case "conflict":
      return `The change to ${AUTO_APPLY_LABELS[notice.pendingMode]} is still being confirmed, so ${AUTO_APPLY_LABELS[notice.requestedMode]} was not sent.`;
    case "refused":
      return `The mode was not changed to ${AUTO_APPLY_LABELS[notice.requestedMode]}: ${notice.message}`;
  }
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
  const pendingArtifacts = 0;
  const status: AgentSessionIndicatorSession["status"] = session.error
    ? "failed"
    : pendingProposals > 0
      ? "waiting"
      : session.busy
        ? "active"
        : "background";
  return [{ id: session.sessionId, title: "Current Weaver session", status, pendingProposals, pendingArtifacts }];
}
