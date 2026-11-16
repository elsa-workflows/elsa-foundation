import React from "react";

export type WorkflowDashboardMetricTone = "accent" | "success" | "warning" | "danger";

const toneClass: Record<WorkflowDashboardMetricTone, string> = {
  accent: "workflow-dashboard-metric--accent",
  success: "workflow-dashboard-metric--success",
  warning: "workflow-dashboard-metric--warning",
  danger: "workflow-dashboard-metric--danger",
};

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
    <div className={`workflow-dashboard-metric ${toneClass[tone]}`} data-metric={label}>
      <dt className="workflow-dashboard-metric__label">{label}</dt>
      <dd className="workflow-dashboard-metric__content">
        <span className="workflow-dashboard-metric__value">{displayValue}</span>
        <span className="workflow-dashboard-metric__detail">{detail}</span>
      </dd>
    </div>
  );
}
