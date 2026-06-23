import React, { useState } from "react";
import { Send } from "lucide-react";

export function AgentComposer({
  disabled,
  placeholder = "Ask about this Studio screen...",
  onSubmit
}: {
  disabled?: boolean;
  placeholder?: string;
  onSubmit(message: string): void;
}) {
  const [message, setMessage] = useState("");

  function submit(event: React.FormEvent) {
    event.preventDefault();
    const trimmed = message.trim();
    if (!trimmed || disabled) {
      return;
    }

    onSubmit(trimmed);
    setMessage("");
  }

  return (
    <form className="agent-composer" onSubmit={submit}>
      <label className="sr-only" htmlFor="studio-agent-composer">Ask Weaver</label>
      <textarea
        id="studio-agent-composer"
        value={message}
        disabled={disabled}
        rows={2}
        placeholder={placeholder}
        onChange={event => setMessage(event.target.value)}
      />
      <button type="submit" disabled={disabled || message.trim().length === 0} aria-label="Send message">
        <Send size={16} />
      </button>
    </form>
  );
}
