import React, { useState } from "react";
import { MessageSquarePlus, Send, Square, X } from "lucide-react";
import type { WeaverFollowup } from "./useWeaverSession";

export function WeaverComposer({
  disabled,
  busy,
  followups,
  placeholder = "Ask Weaver…",
  onSubmit,
  onStop,
  onRemoveFollowup
}: {
  disabled?: boolean;
  busy: boolean;
  followups: WeaverFollowup[];
  placeholder?: string;
  onSubmit(message: string): void;
  onStop(): void;
  onRemoveFollowup(id: string): void;
}) {
  const [message, setMessage] = useState("");

  function submit() {
    const trimmed = message.trim();
    if (!trimmed || disabled) return;
    onSubmit(trimmed);
    setMessage("");
  }

  function onKeyDown(event: React.KeyboardEvent<HTMLTextAreaElement>) {
    if (event.key === "Enter" && !event.shiftKey) {
      event.preventDefault();
      submit();
    }
  }

  return (
    <div className="weaver-composer">
      {followups.length > 0 ? (
        <div className="weaver-followups" aria-label="Queued follow-ups">
          {followups.map(followup => (
            <span key={followup.id} className="weaver-followup-chip">
              <MessageSquarePlus size={12} />
              <span>{followup.message}</span>
              <button type="button" aria-label="Remove queued follow-up" onClick={() => onRemoveFollowup(followup.id)}>
                <X size={11} />
              </button>
            </span>
          ))}
        </div>
      ) : null}
      <div className="weaver-composer-input">
        <textarea
          aria-label="Ask Weaver"
          value={message}
          disabled={disabled}
          rows={2}
          placeholder={busy ? "Add a follow-up to steer Weaver…" : placeholder}
          onChange={event => setMessage(event.target.value)}
          onKeyDown={onKeyDown}
        />
        {busy ? (
          <button type="button" className="weaver-stop" aria-label="Stop Weaver" onClick={onStop}>
            <Square size={14} /> Stop
          </button>
        ) : (
          <button type="button" className="weaver-send" aria-label="Send message" disabled={disabled || message.trim().length === 0} onClick={submit}>
            <Send size={14} />
          </button>
        )}
      </div>
    </div>
  );
}
