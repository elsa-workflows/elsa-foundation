import React from "react";
import { Sparkles } from "lucide-react";

export function AgentLauncher({
  open,
  disabled,
  onClick
}: {
  open: boolean;
  disabled?: boolean;
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
    </button>
  );
}
