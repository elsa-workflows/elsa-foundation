import React from "react";
import { StudioRefreshControl, type StudioRefreshContribution } from "./RefreshControl";

/** A route's content frame. The host owns the main landmark and outer page gutter. */
export function StudioPage({ className, ...props }: React.ComponentPropsWithoutRef<"section">) {
  return <section className={["studio-page", className].filter(Boolean).join(" ")} {...props} />;
}

export interface StudioPageHeaderProps extends Omit<React.ComponentPropsWithoutRef<"header">, "title"> {
  title: React.ReactNode;
  description?: React.ReactNode;
  /** Optional context, only when it adds information beyond the title/navigation. */
  eyebrow?: React.ReactNode;
  icon?: React.ReactNode;
  actions?: React.ReactNode;
  actionsLabel?: string;
  /** Opt-in Contribution to the page header's contextual refresh Slot. */
  refresh?: StudioRefreshContribution;
  titleId?: string;
  /** Use 2 when the host already renders the page's h1. */
  headingLevel?: 1 | 2;
}

export function StudioPageHeader({ title, description, eyebrow, icon, actions, actionsLabel, refresh, titleId, headingLevel = 1, className, children, ...props }: StudioPageHeaderProps) {
  const Heading = headingLevel === 2 ? "h2" : "h1";
  return <header className={["studio-page-header", className].filter(Boolean).join(" ")} {...props}>
    <div className="studio-page-header-copy">
      {eyebrow ? <span className="studio-page-eyebrow">{eyebrow}</span> : null}
      <Heading id={titleId} title={typeof title === "string" ? title : undefined}>{icon ? <span aria-hidden="true">{icon}</span> : null}{title}</Heading>
      {description ? <p>{description}</p> : null}
      {children}
    </div>
    {actions || refresh ? <div className="studio-page-actions" role={actionsLabel ? "group" : undefined} aria-label={actionsLabel}>
      {actions}
      {refresh ? <StudioRefreshControl key={refresh.id} refresh={refresh} /> : null}
    </div> : null}
  </header>;
}

/** Composable controls, not an ARIA toolbar: native Tab order is retained. */
export function StudioCollectionToolbar({ className, ...props }: React.ComponentPropsWithoutRef<"div">) {
  return <div className={["studio-collection-toolbar", className].filter(Boolean).join(" ")} {...props} />;
}

/** Keep server totals, loaded counts and cursor semantics in the caller. */
export function StudioCollectionFooter({ className, ...props }: React.ComponentPropsWithoutRef<"footer">) {
  return <footer className={["studio-collection-footer", className].filter(Boolean).join(" ")} {...props} />;
}

/** Scroll boundary for a native table or an existing accessible ARIA table. */
export function StudioTableContainer({ className, children, ...props }: React.ComponentPropsWithoutRef<"div">) {
  return <div className={["studio-table-container", className].filter(Boolean).join(" ")} {...props}>{children}</div>;
}

export interface StudioResultStateProps extends Omit<React.ComponentPropsWithoutRef<"div">, "title"> {
  title: React.ReactNode;
  description?: React.ReactNode;
  actions?: React.ReactNode;
  icon?: React.ReactNode;
  tone?: "neutral" | "danger";
  /** Use 3 beneath an embedded page whose title is h2. */
  headingLevel?: 2 | 3;
}

/** Initial empty/error/no-match state. Use StudioAlert for feedback above existing results. */
export function StudioResultState({ title, description, actions, icon, tone = "neutral", headingLevel = 2, className, children, ...props }: StudioResultStateProps) {
  const Heading = headingLevel === 3 ? "h3" : "h2";
  return <div className={["studio-result-state", className].filter(Boolean).join(" ")} data-tone={tone} role={tone === "danger" ? "alert" : "status"} {...props}>
    {icon ? <span aria-hidden="true" className="studio-result-state-icon">{icon}</span> : null}
    <Heading>{title}</Heading>
    {description ? <p>{description}</p> : null}
    {children}
    {actions ? <div className="studio-page-actions">{actions}</div> : null}
  </div>;
}
