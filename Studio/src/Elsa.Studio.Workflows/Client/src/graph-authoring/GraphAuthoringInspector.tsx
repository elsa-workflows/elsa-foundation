import type { ReactNode } from "react";

export function GraphAuthoringInspector({
  title = "Inspector",
  actions,
  children,
  className
}: {
  title?: string;
  actions?: ReactNode;
  children: ReactNode;
  className?: string;
}) {
  return (
    <aside
      className={["wf-inspector", "graph-authoring-inspector", className].filter(Boolean).join(" ")}
      aria-label={`${title} panel`}
    >
      <div className="wf-panel-title">
        <strong>{title}</strong>
        {actions ? <span className="wf-panel-actions">{actions}</span> : null}
      </div>
      <div className="graph-authoring-inspector-content">{children}</div>
    </aside>
  );
}
