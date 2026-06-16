import React from "react";

export type StudioFeedbackTone = "info" | "success" | "warning" | "danger";

export function StudioAlert({
  tone = "info",
  children
}: {
  tone?: StudioFeedbackTone;
  children: React.ReactNode;
}) {
  return <div className="studio-alert" data-tone={tone}>{children}</div>;
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
