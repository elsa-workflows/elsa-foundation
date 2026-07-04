import React from "react";

export type StudioFeedbackTone = "info" | "success" | "warning" | "danger";

export function StudioAlert({
  tone = "info",
  children
}: {
  tone?: StudioFeedbackTone;
  children: React.ReactNode;
}) {
  // Urgent tones (errors/warnings) use role="alert" (assertive) so screen readers announce them
  // immediately; informational tones use role="status" (polite) to avoid interrupting the user.
  const role = tone === "danger" || tone === "warning" ? "alert" : "status";
  return <div className="studio-alert" role={role} data-tone={tone}>{children}</div>;
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
