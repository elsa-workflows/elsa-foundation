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
          <span>{attachment.label}</span>
          {contextMetadata(attachment) ? <small>{contextMetadata(attachment)}</small> : null}
          {attachment.sensitivity === "secret-redacted" ? <em>redacted</em> : null}
        </span>
      ))}
    </div>
  );
}

function contextMetadata(attachment: StudioAgentContextAttachment) {
  // Only backend-admitted envelope metadata is shared presentation. The owning module interprets
  // resource payloads and may include an appropriate display name in its declared label.
  const origin = attachment.origin ? {
    clientSnapshot: "Client snapshot",
    resourceData: "Resource data",
    authoritativeRead: "Authoritative read",
    installedGuidance: "Installed guidance"
  }[attachment.origin] : undefined;
  const target = attachment.target;
  const resource = target ? [target.resourceType, target.resourceId].filter(Boolean).join(" ") : undefined;
  return [attachment.moduleId ?? attachment.source, origin, resource, target?.revision ? `Revision ${target.revision}` : undefined].filter(Boolean).join(" · ");
}
