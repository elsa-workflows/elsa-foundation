import React, { useEffect, useId, useMemo, useRef, useState } from "react";
import { Check, CheckCircle2, ChevronDown, History, Plus, Shield, X } from "lucide-react";
import type { ElsaStudioModuleApi, StudioWeaverPromptRequest, StudioWeaverSurface } from "../../sdk";
import type { WeaverClient } from "./weaverClient";
import { WeaverContextChips } from "./WeaverContextChips";
import { WeaverPromptStarters } from "./WeaverPromptStarters";
import { WeaverToolResults } from "./WeaverToolResults";
import { WeaverProposalReview } from "./WeaverProposalReview";
import type { subscribeWeaverSessionStream } from "./weaverStream";
import type { WeaverSessionIndicatorSession } from "./WeaverSessionIndicator";
import type { WeaverRecoveryMode, WeaverSessionSummary } from "./weaverTypes";
import { WeaverComposer } from "./WeaverComposer";
import { WeaverMessageList } from "./WeaverMessageList";
import { WeaverTimeline } from "./WeaverTimeline";
import { useWeaverSession, WEAVER_AUTO_APPLY_LABELS, type UseWeaverSession, type WeaverAutoApplyMode, type WeaverAutoApplyNotice, type WeaverAutoApplyState, type WeaverRecoveryNotice } from "./useWeaverSession";

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
  surface: StudioWeaverSurface;
  variant: "dock" | "full";
  onClose?(): void;
  client?: WeaverClient;
  subscribeStream?: typeof subscribeWeaverSessionStream;
  onSessionIndicatorChange?(sessions: WeaverSessionIndicatorSession[]): void;
  incomingPrompt?: (StudioWeaverPromptRequest & { id: string }) | null;
  onPromptConsumed?(id: string): void;
}) {
  const session = useWeaverSession({ api, surface, client, subscribeStream });
  const resultRenderers = useMemo(() => api.weaver.resultRenderers.list(), [api]);
  const resultControls = useMemo(() => api.weaver.resultControls?.list() ?? [], [api]);
  const providerStatus = useMemo(() => describeProviderStatus(session), [session]);
  const sendRef = useRef(session.send);
  sendRef.current = session.send;
  const lastPromptIdRef = useRef<string | null>(null);
  const autoApplyStatusId = useId();
  const autoApplyStatus = describeAutoApplyStatus(session.autoApplyState, session.pendingAutoApplyMode, session.autoApplyUnsupported, session.autoApplyNotice);
  const recoveryScope = describeRecoveryScope(session.bootstrap?.recovery);

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

      <WeaverRecovery session={session} />
      {recoveryScope ? <p className="weaver-recovery-scope" role="note" data-recovery={session.bootstrap?.recovery}>{recoveryScope}</p> : null}

      <WeaverContextChips attachments={session.attachments} />
      {session.contextDiagnostics.length ? <div role="status" aria-label="Context limitations">{session.contextDiagnostics.map((item, index) => <p key={`${item.contributorId}-${index}`}>{item.contributorId}: {item.message}</p>)}</div> : null}
      <WeaverPromptStarters prompts={session.promptStarters} disabled={composerDisabled || session.busy} onSelect={prompt => session.send(prompt)} />

      <div className="weaver-scroll">
        <WeaverMessageList
          messages={session.messages}
          streaming={session.busy}
          onFeedback={session.submitFeedback}
          canStart={session.sessionId !== null && session.chatState === "idle" && !session.autoApplyBlocked && !session.reopening}
          pendingActionIds={session.pendingProposalIds}
          onStart={session.startRecordedTurn}
          onWithdraw={session.withdrawMessage}
        />
        <WeaverTimeline entries={session.timeline} plan={session.plan} />
        <WeaverToolResults results={session.toolResults} renderers={resultRenderers} controls={resultControls} />
        <WeaverProposalReview
          proposals={session.proposals}
          disabled={session.busy}
          pendingProposalIds={session.pendingProposalIds}
          resultRenderers={resultRenderers}
          resultControls={resultControls}
          onApprove={session.approveProposal}
          onDeny={session.denyProposal}
          onExecute={session.executeProposal}
          onRetryConfirmation={session.retryConfirmation}
          onResolve={session.resolveProposal}
          resolveDisabled={session.autoApplyBlocked}
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

/**
 * Preview (#3441): the owner's recent sessions and what reopening one found. Reopening reads the recorded state
 * only; a prompt, decision or resolution always needs its own explicit action.
 */
function WeaverRecovery({ session }: { session: UseWeaverSession }) {
  const notice = session.recoveryNotice;
  const offered = session.sessionId === null && session.chatState === "idle" ? session.recentSessions : [];
  if (!notice && offered.length === 0 && !session.reopening && (session.sessionId === null || session.chatState === "streaming")) return null;
  return (
    <section className="weaver-recovery" aria-label="Conversation recovery" data-reopening={session.reopening || undefined}>
      {session.reopening ? <p role="status">Reading the conversation's recorded state…</p> : null}
      {notice ? (
        <div className="weaver-recovery-notice" role="status" data-kind={notice.kind} data-limit={notice.kind === "limit" ? notice.limit : undefined}>
          <span>{describeRecoveryNotice(notice)}</span>
          {notice.kind === "owned-elsewhere" || notice.kind === "lost" ? (
            // A reread waits while a mode change is unconfirmed, as prompts and recorded actions do.
            <button type="button" disabled={session.reopening || session.autoApplyBlocked} onClick={() => session.reopenSession(notice.sessionId)}>Try again</button>
          ) : null}
          {notice.kind === "limit" ? (
            <button type="button" disabled={session.busy || session.pendingProposalIds.has("continue-session")} onClick={session.continueInNewSession}>Continue in a new session</button>
          ) : null}
        </div>
      ) : null}
      {offered.length > 0 ? (
        <div className="weaver-recent-sessions">
          <strong><History size={13} aria-hidden="true" /> Recent conversations</strong>
          <ul aria-label="Recent conversations">
            {offered.map(item => (
              <li key={item.id}>
                <button type="button" disabled={session.reopening} data-session-id={item.id} onClick={() => session.reopenSession(item.id)}>
                  {describeSessionSummary(item)}
                </button>
              </li>
            ))}
          </ul>
        </div>
      ) : null}
      {session.sessionId !== null && session.chatState !== "streaming" ? (
        <button type="button" className="weaver-new-conversation" onClick={session.startNewConversation}><Plus size={13} aria-hidden="true" /> New conversation</button>
      ) : null}
    </section>
  );
}

/** Preview (#3441): a short note when this backend does not recover conversations across a server restart. */
function describeRecoveryScope(recovery: WeaverRecoveryMode | undefined): string | null {
  switch (recovery) {
    case "processLifetime":
      return "Conversations survive a reload while this server runs; a server restart ends them.";
    case "unsupported":
      return "This server cannot recover conversations: a server restart ends them.";
    default:
      return null;
  }
}

function describeSessionSummary(summary: WeaverSessionSummary) {
  const updated = Number.isFinite(Date.parse(summary.updatedAt)) ? new Date(summary.updatedAt).toLocaleString() : summary.updatedAt;
  const reviews = summary.pendingReviewCount > 0 ? ` · ${summary.pendingReviewCount} awaiting review` : "";
  const status = summary.status === "active" ? "" : ` · ${summary.status}`;
  // Preview (#3441): a session at its documented limit is offered with that outcome, so reopening it is not a surprise.
  const limited = summary.recoveryState?.startsWith("limit-reached:") ? " · session limit reached" : "";
  return `${summary.title ?? "Conversation"} · ${updated}${reviews}${status}${limited}`;
}

function describeRecoveryNotice(notice: WeaverRecoveryNotice): string {
  switch (notice.kind) {
    case "owned-elsewhere":
      return `Another Studio server process is working on this conversation, so it cannot be opened here yet.${notice.retryAfterSeconds !== undefined ? ` Try again in about ${notice.retryAfterSeconds} seconds.` : " Try again shortly."}`;
    case "unavailable":
      return `This conversation's recorded state is unavailable, so nothing was opened or changed. ${notice.message}`;
    case "not-found":
      return "This conversation is not available.";
    case "closed":
      return `This conversation has ended (${notice.status}). Its recorded history is shown; your next prompt starts a new conversation.`;
    case "lost":
      return `The conversation could not be read. Nothing was changed. ${notice.message}`;
    case "action-refused":
      return notice.message;
    case "limit":
      // The server's message names the bound it reached; Studio does not restate the limits.
      return `${notice.message?.trim() || "Session limit reached: this conversation has no room left for more context."} It stays readable and nothing was discarded; continue in a new session, which carries over only a link to this one.`;
  }
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

function toIndicatorSessions(session: UseWeaverSession): WeaverSessionIndicatorSession[] {
  if (!session.sessionId) return [];
  const pendingProposals = session.proposals.filter(proposal => proposal.status === "awaiting-approval" || proposal.status === "approved").length;
  const pendingArtifacts = 0;
  const status: WeaverSessionIndicatorSession["status"] = session.error
    ? "failed"
    : pendingProposals > 0
      ? "waiting"
      : session.busy
        ? "active"
        : "background";
  return [{ id: session.sessionId, title: "Current Weaver session", status, pendingProposals, pendingArtifacts }];
}
