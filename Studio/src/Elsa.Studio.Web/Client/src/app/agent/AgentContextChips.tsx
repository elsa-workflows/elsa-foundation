import React from "react";
import type { StudioAgentContextAttachment } from "../../sdk";

export function AgentContextChips({ attachments }: { attachments: StudioAgentContextAttachment[] }) {
  if (attachments.length === 0) {
    return null;
  }

  return (
    <div className="agent-context-chips" aria-label="Weaver context">
      {attachments.map(attachment => (
        <span key={attachment.id} className="agent-context-chip" data-sensitivity={attachment.sensitivity}>
          {attachment.label}
          {attachment.sensitivity === "secret-redacted" ? <em>redacted</em> : null}
        </span>
      ))}
    </div>
  );
}
