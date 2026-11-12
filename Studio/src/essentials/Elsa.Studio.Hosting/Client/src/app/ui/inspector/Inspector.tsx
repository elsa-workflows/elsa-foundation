import React from "react";

export function StudioInspector({
  title,
  eyebrow,
  actions,
  children
}: {
  title: React.ReactNode;
  eyebrow?: React.ReactNode;
  actions?: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <aside className="studio-inspector">
      <div className="studio-inspector-heading">
        <div>
          {eyebrow ? <span>{eyebrow}</span> : null}
          <h3>{title}</h3>
        </div>
        {actions ? <div className="studio-inspector-actions">{actions}</div> : null}
      </div>
      {children}
    </aside>
  );
}
