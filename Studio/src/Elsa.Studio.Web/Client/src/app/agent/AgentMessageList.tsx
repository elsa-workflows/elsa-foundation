import React from "react";
import { Bot, ThumbsDown, ThumbsUp, User } from "lucide-react";
import type { AgentMessageViewModel } from "./agentTypes";

export function AgentMessageList({
  messages,
  onFeedback
}: {
  messages: AgentMessageViewModel[];
  onFeedback?(messageId: string, rating: "positive" | "negative"): void;
}) {
  if (messages.length === 0) {
    return (
      <div className="agent-empty-state">
        <Bot size={22} />
        <span>Ask Weaver to explain the current screen or active workflow.</span>
      </div>
    );
  }

  return (
    <ol className="agent-message-list" aria-live="polite">
      {messages.map(message => (
        <li key={message.id} className="agent-message" data-role={message.role} data-status={message.status}>
          <span className="agent-message-avatar" aria-hidden="true">
            {message.role === "user" ? <User size={15} /> : <Bot size={15} />}
          </span>
          <div className="agent-message-body">
            <p>{message.content}</p>
            {message.role === "assistant" && message.status === "completed" && onFeedback ? (
              <div className="agent-feedback" aria-label="Message feedback">
                <button type="button" aria-label="Helpful response" onClick={() => onFeedback(message.id, "positive")}>
                  <ThumbsUp size={14} />
                </button>
                <button type="button" aria-label="Unhelpful response" onClick={() => onFeedback(message.id, "negative")}>
                  <ThumbsDown size={14} />
                </button>
              </div>
            ) : null}
          </div>
        </li>
      ))}
    </ol>
  );
}
