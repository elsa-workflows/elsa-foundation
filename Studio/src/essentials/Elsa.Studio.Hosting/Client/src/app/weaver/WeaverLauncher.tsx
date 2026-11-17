import React from "react";
import { Sparkles } from "lucide-react";
import { WeaverSessionIndicator, type WeaverSessionIndicatorSession } from "./WeaverSessionIndicator";

export function WeaverLauncher({
  open,
  disabled,
  sessions = [],
  onClick
}: {
  open: boolean;
  disabled?: boolean;
  sessions?: WeaverSessionIndicatorSession[];
  onClick(): void;
}) {
  return (
    <button
      type="button"
      className="weaver-launcher"
      aria-expanded={open}
      aria-controls="studio-weaver-panel"
      disabled={disabled}
      onClick={onClick}
    >
      <Sparkles size={16} />
      Weaver
      <WeaverSessionIndicator sessions={sessions} />
    </button>
  );
}
