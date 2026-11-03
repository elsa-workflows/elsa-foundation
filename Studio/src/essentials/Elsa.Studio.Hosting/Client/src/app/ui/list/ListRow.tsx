import React from "react";

/**
 * Dense list primitives codifying the container-slab + flat hairline-row pattern (the
 * same shape as `.studio-data-grid`): ONE bordered container surface, rows that are
 * transparent with a hairline `border-bottom` and an accent-soft hover. This is the
 * reusable form of the "corduroy" fix — modules that need a simple vertical list of
 * resources should reach for this instead of painting each row as its own slab.
 */
export function StudioListContainer({
  className,
  children,
  ...rest
}: React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div className={className ? `studio-list ${className}` : "studio-list"} role="list" {...rest}>
      {children}
    </div>
  );
}

/**
 * A single flat row. When `onSelect` is provided the row becomes activatable (roving
 * focus is left to the caller / container) and exposes `aria-selected`; otherwise it is a
 * plain listitem. `leading`/`trailing` are optional control slots flanking the
 * title+subtitle stack.
 */
export function StudioListRow({
  title,
  subtitle,
  leading,
  trailing,
  selected,
  onSelect,
  className,
  ...rest
}: {
  title: React.ReactNode;
  subtitle?: React.ReactNode;
  leading?: React.ReactNode;
  trailing?: React.ReactNode;
  selected?: boolean;
  onSelect?(): void;
} & Omit<React.HTMLAttributes<HTMLDivElement>, "onSelect" | "title">) {
  const selectable = Boolean(onSelect);
  const classes = ["studio-list-row"];
  if (selected) classes.push("selected");
  if (className) classes.push(className);

  return (
    <div
      className={classes.join(" ")}
      role="listitem"
      aria-selected={selectable ? Boolean(selected) : undefined}
      tabIndex={selectable ? 0 : undefined}
      onClick={onSelect}
      onKeyDown={selectable ? event => {
        if (event.key === "Enter" || event.key === " ") {
          event.preventDefault();
          onSelect?.();
        }
      } : undefined}
      {...rest}
    >
      {leading !== undefined ? <span className="studio-list-row-leading">{leading}</span> : null}
      <span className="studio-list-row-body">
        <span className="studio-list-row-title">{title}</span>
        {subtitle !== undefined ? <span className="studio-list-row-subtitle">{subtitle}</span> : null}
      </span>
      {trailing !== undefined ? <span className="studio-list-row-trailing">{trailing}</span> : null}
    </div>
  );
}
