import React from "react";
import type { StudioStatusTone } from "./StatusChip";

/**
 * Outlined pill tag. Distinct from the filled `StatusChip`: this is a low-emphasis,
 * transparent-fill label whose border and text pick up the tone's `-fg` colour, for
 * inline metadata (categories, counts, kinds) that shouldn't compete with a status
 * badge. Tone vocabulary is shared with `StatusChip`.
 */
export function StatusPill({
  tone = "neutral",
  children
}: {
  tone?: StudioStatusTone;
  children: React.ReactNode;
}) {
  return <span className="studio-status-pill" data-tone={tone}>{children}</span>;
}
