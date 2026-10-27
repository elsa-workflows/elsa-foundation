import React from "react";
import { Sparkles } from "lucide-react";
import { AgentSessionIndicator, type AgentSessionIndicatorSession } from "./AgentSessionIndicator";

export function AgentLauncher({
  open,
  disabled,
  sessions = [],
  onClick
}: {
  open: boolean;
  disabled?: boolean;
  sessions?: AgentSessionIndicatorSession[];
  onClick(): void;
}) {
  return (
    <button
      type="button"
      className="agent-launcher"
      aria-expanded={open}
      aria-controls="studio-agent-panel"
      disabled={disabled}
      onClick={onClick}
    >
      <Sparkles size={16} />
      Weaver
      <AgentSessionIndicator sessions={sessions} />
    </button>
  );
}
