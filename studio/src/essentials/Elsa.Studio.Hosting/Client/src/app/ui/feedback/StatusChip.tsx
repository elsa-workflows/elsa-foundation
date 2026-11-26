import React from "react";

export type StudioStatusTone = "neutral" | "success" | "warning" | "danger" | "accent";

export function StatusChip({
  tone = "neutral",
  children
}: {
  tone?: StudioStatusTone;
  children: React.ReactNode;
}) {
  return <span className="studio-status-chip" data-tone={tone}>{children}</span>;
}
