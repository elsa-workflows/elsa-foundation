import React from "react";
import { Bot, Play, ThumbsDown, ThumbsUp, Undo2, User } from "lucide-react";
import ReactMarkdown, { type Components } from "react-markdown";
import remarkGfm from "remark-gfm";
import type { WeaverMessageViewModel } from "./weaverTypes";
import { WEAVER_AUTO_APPLY_LABELS } from "./useWeaverSession";

const PLACEHOLDER = "Awaiting assistant response…";

// Code/JSON blocks beyond either threshold are collapsed behind a disclosure so long, technical
// payloads (e.g. an activity-catalog dump) don't flood the conversation. Tuned for the narrow dock.
const COLLAPSE_LINE_THRESHOLD = 12;
const COLLAPSE_CHAR_THRESHOLD = 600;

// react-markdown renders fenced code as <pre><code>…</code></pre>; overriding <pre> lets us wrap the
// whole block in a valid <details> (a <details> inside <pre> would be invalid markup).
const markdownComponents: Components = {
  pre({ children, ...props }) {
    const text = extractCodeText(children);
    const lineCount = text.replace(/\n+$/, "").split("\n").length;
    const block = <pre {...props}>{children}</pre>;
    if (lineCount <= COLLAPSE_LINE_THRESHOLD && text.length <= COLLAPSE_CHAR_THRESHOLD) return block;
    return (
      <details className="weaver-code-collapse">
        <summary>{`Show ${lineCount} line${lineCount === 1 ? "" : "s"} of code`}</summary>
        {block}
      </details>
    );
  }
};

function extractCodeText(node: React.ReactNode): string {
  if (node === null || node === undefined || typeof node === "boolean") return "";
  if (typeof node === "string" || typeof node === "number") return String(node);
  if (Array.isArray(node)) return node.map(extractCodeText).join("");
  if (React.isValidElement(node)) return extractCodeText((node.props as { children?: React.ReactNode }).children);
  return "";
}

export function WeaverMessageList({
  messages,
  streaming,
  onFeedback,
  canStart = false,
  pendingActionIds,
  onStart,
  onWithdraw
}: {
  messages: WeaverMessageViewModel[];
  streaming: boolean;
  onFeedback?(messageId: string, rating: "positive" | "negative"): void;
  /** Preview (#3441): whether a recorded prompt may be started now; nothing ever starts on its own. */
  canStart?: boolean;
  pendingActionIds?: ReadonlySet<string>;
  onStart?(messageId: string): void;
  onWithdraw?(messageId: string): void;
}) {
  if (messages.length === 0) {
    return (
      <div className="weaver-empty">
        <Bot size={24} />
        <strong>Ask Weaver about the current Studio context.</strong>
        <span>Weaver can read context, run tools, and propose reviewable changes in the current Studio context.</span>
      </div>
    );
  }

  return (
    <ol className="weaver-messages" aria-live="polite">
      {messages.map(message => (
        <li
          key={message.id}
          className="weaver-message"
          data-message-id={message.id}
          data-role={message.role}
          data-status={message.status}
          data-accepted-policy-revision={message.acceptedPolicy?.revision}
          data-accepted-autonomy-mode={message.acceptedPolicy?.autonomyMode}
          data-status-reason={message.statusReason}
          data-notice={message.noticeCode}
        >
          <span className="weaver-message-avatar" aria-hidden="true">
            {message.role === "user" ? <User size={15} /> : <Bot size={15} />}
          </span>
          <div className="weaver-message-body">
            <WeaverMessageContent message={message} streaming={streaming} />
            {message.role === "assistant" && message.acceptedPolicy ? (
              // Attributed to the policy that admitted the turn; a later selection never relabels it.
              <small className="weaver-message-policy">
                {message.acceptedPolicy.autonomyMode
                  ? `Accepted under ${WEAVER_AUTO_APPLY_LABELS[message.acceptedPolicy.autonomyMode]}`
                  : `Accepted under policy revision ${message.acceptedPolicy.revision}`}
              </small>
            ) : null}
            <WeaverMessageOutcome message={message} streaming={streaming} canStart={canStart} pending={pendingActionIds?.has(`withdraw:${message.id}`) ?? false} onStart={onStart} onWithdraw={onWithdraw} />
            {message.role === "assistant" && message.status === "completed" && onFeedback ? (
              <div className="weaver-feedback" aria-label="Message feedback">
                <button type="button" aria-label="Helpful response" onClick={() => onFeedback(message.id, "positive")}>
                  <ThumbsUp size={13} />
                </button>
                <button type="button" aria-label="Unhelpful response" onClick={() => onFeedback(message.id, "negative")}>
                  <ThumbsDown size={13} />
                </button>
              </div>
            ) : null}
          </div>
        </li>
      ))}
    </ol>
  );
}

/** Preview (#3441): the recorded outcome of a message that did not simply complete, with its explicit actions. */
function WeaverMessageOutcome({ message, streaming, canStart, pending, onStart, onWithdraw }: {
  message: WeaverMessageViewModel;
  streaming: boolean;
  canStart: boolean;
  pending: boolean;
  onStart?(messageId: string): void;
  onWithdraw?(messageId: string): void;
}) {
  const label = describeOutcome(message, streaming);
  if (!label) return null;
  const notStarted = message.role === "user" && message.status === "pending";
  const resumable = message.role === "user" && (notStarted || message.status === "streaming") && !streaming;
  return (
    <div className="weaver-message-outcome" role="status">
      <small>{label}</small>
      {resumable && onStart ? (
        <div className="weaver-message-actions">
          <button type="button" disabled={!canStart || pending} onClick={() => onStart(message.id)}><Play size={13} /> {notStarted ? "Start" : "Continue"}</button>
          {notStarted && onWithdraw ? <button type="button" disabled={!canStart || pending} onClick={() => onWithdraw(message.id)}><Undo2 size={13} /> Withdraw</button> : null}
        </div>
      ) : null}
    </div>
  );
}

function describeOutcome(message: WeaverMessageViewModel, streaming: boolean): string | null {
  if (message.status === "cancelled") {
    if (message.statusReason === "interrupted") return "Interrupted. The turn stopped when its connection or the server ended. Nothing was sent again.";
    if (message.statusReason === "user-stop") return "Stopped by you.";
    if (message.statusReason === "withdrawn") return "Withdrawn before it started.";
    return message.role === "user" ? "Cancelled. The turn's stream closed before it finished. Nothing was sent again." : null;
  }
  if (message.role === "user" && message.status === "pending" && !streaming) return "Not started. Start runs this accepted prompt once; it is never sent again.";
  if (message.role === "user" && message.status === "streaming" && !streaming) return "Paused or still running elsewhere. Continue runs it once; it is never sent again.";
  if (message.role === "assistant" && message.status === "failed") return "Not completed. This partial answer was not accepted.";
  return null;
}

function WeaverMessageContent({ message, streaming }: { message: WeaverMessageViewModel; streaming: boolean }) {
  const isAwaiting = message.content === PLACEHOLDER || message.content.length === 0;
  const showCaret = streaming && message.role === "assistant" && message.status === "streaming";

  if (message.role === "assistant" && !isAwaiting) {
    return (
      <div className="weaver-markdown">
        <ReactMarkdown remarkPlugins={[remarkGfm]} components={markdownComponents}>{message.content}</ReactMarkdown>
        {showCaret ? <span className="weaver-caret" aria-hidden="true" /> : null}
      </div>
    );
  }

  if (isAwaiting && showCaret) {
    return <p className="weaver-message-text"><span className="weaver-caret" aria-hidden="true" /></p>;
  }

  // User, tool, error, system, and not-yet-streamed content render as plain text (no markdown / no raw-JSON surprises).
  return <p className="weaver-message-text">{isAwaiting ? "…" : message.content}</p>;
}
