import React from "react";
import { Bot, ThumbsDown, ThumbsUp, User } from "lucide-react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import type { AgentMessageViewModel } from "../agent/agentTypes";

const PLACEHOLDER = "Awaiting assistant response…";

export function WeaverMessageList({
  messages,
  streaming,
  onFeedback
}: {
  messages: AgentMessageViewModel[];
  streaming: boolean;
  onFeedback?(messageId: string, rating: "positive" | "negative"): void;
}) {
  if (messages.length === 0) {
    return (
      <div className="weaver-empty">
        <Bot size={24} />
        <strong>Ask Weaver about the current Studio context.</strong>
        <span>Weaver can read context, run tools, and propose reviewable changes to your workflow.</span>
      </div>
    );
  }

  return (
    <ol className="weaver-messages" aria-live="polite">
      {messages.map(message => (
        <li key={message.id} className="weaver-message" data-role={message.role} data-status={message.status}>
          <span className="weaver-message-avatar" aria-hidden="true">
            {message.role === "user" ? <User size={15} /> : <Bot size={15} />}
          </span>
          <div className="weaver-message-body">
            <WeaverMessageContent message={message} streaming={streaming} />
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

function WeaverMessageContent({ message, streaming }: { message: AgentMessageViewModel; streaming: boolean }) {
  const isAwaiting = message.content === PLACEHOLDER || message.content.length === 0;
  const showCaret = streaming && message.role === "assistant" && message.status === "streaming";

  if (message.role === "assistant" && !isAwaiting) {
    return (
      <div className="weaver-markdown">
        <ReactMarkdown remarkPlugins={[remarkGfm]}>{message.content}</ReactMarkdown>
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
