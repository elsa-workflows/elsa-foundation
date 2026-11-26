import React, { useId } from "react";

export function DisclosureFold({
  label,
  summary,
  badge,
  open,
  forced,
  onOpenChange,
  children,
  className,
  bodyClassName
}: {
  label: React.ReactNode;
  summary?: React.ReactNode;
  badge?: React.ReactNode;
  open: boolean;
  forced?: boolean;
  onOpenChange(open: boolean): void;
  children: React.ReactNode;
  className?: string;
  bodyClassName?: string;
}) {
  const reactId = useId();
  const bodyId = `${reactId}-body`;
  const expanded = open || Boolean(forced);

  return (
    <div className={["studio-disclosure-fold", className].filter(Boolean).join(" ")} data-open={expanded ? "true" : "false"}>
      <button
        type="button"
        className="studio-disclosure-fold-trigger"
        aria-expanded={expanded}
        aria-controls={bodyId}
        onClick={() => {
          if (forced && expanded) return;
          onOpenChange(!expanded);
        }}
      >
        <span className="studio-disclosure-fold-chevron" aria-hidden="true" />
        <span className="studio-disclosure-fold-label">{label}</span>
        {summary ? <span className="studio-disclosure-fold-summary">{summary}</span> : null}
        {badge}
      </button>
      <div
        id={bodyId}
        className={["studio-disclosure-fold-body", bodyClassName].filter(Boolean).join(" ")}
        hidden={!expanded}
      >
        {children}
      </div>
    </div>
  );
}
