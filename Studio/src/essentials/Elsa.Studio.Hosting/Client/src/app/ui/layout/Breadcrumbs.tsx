import React from "react";
import "./Breadcrumbs.css";

export interface StudioBreadcrumbLink {
  href: string;
  label: React.ReactNode;
}

export interface StudioBreadcrumbsProps extends Omit<React.ComponentPropsWithoutRef<"nav">, "children"> {
  items: readonly (StudioBreadcrumbLink | { label: React.ReactNode; onClick: () => void })[];
  current: React.ReactNode;
  renderLink?: (item: StudioBreadcrumbLink) => React.ReactNode;
}

export function StudioBreadcrumbs({ items, current, renderLink, className, ...props }: StudioBreadcrumbsProps) {
  return <nav className={["studio-breadcrumbs", className].filter(Boolean).join(" ")} aria-label="Breadcrumb" {...props}>
    <ol>
      {items.map((item, index) => <li key={`${"href" in item ? item.href : "action"}:${index}`}>
        {index > 0 ? <span className="studio-breadcrumb-separator" aria-hidden="true">›</span> : null}
        {"onClick" in item
          ? <button className="studio-breadcrumb-action" type="button" onClick={item.onClick}>{item.label}</button>
          : renderLink ? renderLink(item) : <a href={item.href}>{item.label}</a>}
      </li>)}
      <li className="studio-breadcrumb-current-item">
        {items.length ? <span className="studio-breadcrumb-separator" aria-hidden="true">›</span> : null}
        <div className="studio-breadcrumb-current" aria-current="page">{current}</div>
      </li>
    </ol>
  </nav>;
}