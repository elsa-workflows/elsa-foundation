import React from "react";

export function WorkflowDashboardUnavailable({ title, errorCode }: { title: string; errorCode?: string }) {
  return (
    <div className="workflow-dashboard-unavailable" role="status">
      <strong>{title}</strong>
      <p>Try refreshing the widget.</p>
      {errorCode && (
        <details className="workflow-dashboard-error-details">
          <summary>Technical details</summary>
          <code>{errorCode}</code>
        </details>
      )}
    </div>
  );
}
