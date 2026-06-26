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
          {formatContextLabel(attachment)}
          {attachment.sensitivity === "secret-redacted" ? <em>redacted</em> : null}
        </span>
      ))}
    </div>
  );
}

function formatContextLabel(attachment: StudioAgentContextAttachment) {
  if (!isRecord(attachment.content)) return attachment.label;

  const hints: string[] = [];
  const selection = attachment.content.selection ?? attachment.content.selectionHint;
  if (isRecord(selection) && typeof selection.nodeId === "string" && selection.nodeId) {
    hints.push("using selected node");
  } else if (typeof attachment.content.selectedNodeId === "string" && attachment.content.selectedNodeId) {
    hints.push("using selected node");
  }

  const catalog = attachment.content.activityCatalog ?? attachment.content.boundedActivityCatalog;
  if (Array.isArray(catalog)) hints.push(`${catalog.length} catalog item${catalog.length === 1 ? "" : "s"} considered`);

  const redactions = attachment.content.redactions ?? attachment.content.redactionNotes;
  if (Array.isArray(redactions) && redactions.length > 0) hints.push("secrets/runtime payloads excluded");

  return hints.length > 0 ? `${attachment.label}: ${hints.join(", ")}` : attachment.label;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}
