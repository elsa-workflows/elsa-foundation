import React from "react";
import { AlertCircle, Boxes } from "lucide-react";

export function WfListSkeleton({ rows = 5 }: { rows?: number }) {
  return (
    <div className="wf-grid" aria-busy="true" aria-label="Loading">
      {Array.from({ length: rows }).map((_, index) => (
        <div key={index} className="wf-skeleton wf-skeleton-row" style={{ width: `${90 - (index % 3) * 12}%` }} />
      ))}
    </div>
  );
}

export function WfEmptyState({ icon, title, description, action }: { icon?: React.ReactNode; title: string; description?: string; action?: React.ReactNode }) {
  return (
    <div className="wf-empty-state" role="status">
      <div className="wf-empty-state-icon" aria-hidden>{icon ?? <Boxes size={22} />}</div>
      <h3>{title}</h3>
      {description ? <p>{description}</p> : null}
      {action ? <div className="wf-empty-state-action">{action}</div> : null}
    </div>
  );
}

export function WfErrorCard({ message, title = "Something went wrong", action }: { message?: string; title?: string; action?: React.ReactNode }) {
  return (
    <div className="wf-error-card" role="alert">
      <AlertCircle size={18} />
      <div className="wf-error-card-body">
        <strong>{title}</strong>
        <span>{message || "Please try again, or check that the Elsa server is reachable."}</span>
        {action ? <div>{action}</div> : null}
      </div>
    </div>
  );
}
