import React from "react";

import { X } from "lucide-react";
import { StudioIconButton } from "../forms/Button";

export type StudioFeedbackTone = "info" | "success" | "warning" | "danger";

export function StudioAlert({
  tone = "info",
  dismiss,
  children,
  ...elementProps
}: {
  tone?: StudioFeedbackTone;
  dismiss?: { label: string; onDismiss(): void };
  children: React.ReactNode;
} & Pick<React.ComponentPropsWithRef<"div">, "id" | "ref" | "tabIndex">) {
  // Urgent tones (errors/warnings) use role="alert" (assertive) so screen readers announce them
  // immediately; informational tones use role="status" (polite) to avoid interrupting the user.
  const role = tone === "danger" || tone === "warning" ? "alert" : "status";
  if (dismiss) return <div {...elementProps} className="studio-alert studio-alert-dismissible" data-tone={tone}>
    <div role={role}>{children}</div>
    <StudioIconButton aria-label={dismiss.label} onClick={dismiss.onDismiss}><X /></StudioIconButton>
  </div>;
  return <div {...elementProps} className="studio-alert" role={role} data-tone={tone}>{children}</div>;
}

export function EmptyState({
  icon,
  children
}: {
  icon?: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <div className="studio-empty-state">
      {icon}
      <span>{children}</span>
    </div>
  );
}

/**
 * Shimmering placeholder rows for content that is still loading. The rows fade in after a
 * short delay (see the `.studio-skeleton` styles), so responses that resolve quickly never
 * flash a loading state at all.
 */
export function SkeletonRows({
  rows = 5,
  label = "Loading"
}: {
  rows?: number;
  label?: string;
}) {
  return (
    <div className="studio-skeleton-list" role="status" aria-live="polite" aria-busy="true" aria-label={label}>
      {Array.from({ length: rows }).map((_, index) => (
        <div key={index} className="studio-skeleton studio-skeleton-row" style={{ width: `${92 - (index % 3) * 14}%` }} />
      ))}
    </div>
  );
}
