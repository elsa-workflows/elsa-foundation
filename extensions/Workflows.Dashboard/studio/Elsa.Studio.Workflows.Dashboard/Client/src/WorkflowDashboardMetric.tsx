import React from "react";
import { StudioStatTile } from "@elsa-workflows/studio-ui";

export type WorkflowDashboardMetricTone = "accent" | "success" | "warning" | "danger";

/** Shared definition/run metric treatment: label first, exact count second, meaning underneath. */
export function WorkflowDashboardMetric({
  label,
  value,
  detail,
  tone = "accent",
}: {
  label: string;
  value: number | string;
  detail: string;
  tone?: WorkflowDashboardMetricTone;
}) {
  const displayValue = typeof value === "number" ? value.toLocaleString() : value;

  return (
    <div data-metric={label} data-tone={tone}>
      <StudioStatTile label={label} value={displayValue} delta={detail} />
    </div>
  );
}
