import React from "react";
import { StatusPill } from "../feedback/StatusPill";
import { DisclosureFold } from "./DisclosureFold";
import { type DisclosureControlProps } from "./types";
import { isDisclosureBoolean, useDisclosureState } from "./useDisclosureState";

export interface AdvancedSectionProps extends DisclosureControlProps {
  label?: React.ReactNode;
  summary?: React.ReactNode;
  setCount?: number;
  hasError?: boolean;
  defaultOpen?: boolean;
  allowCollapseWhenSet?: boolean;
  children: React.ReactNode;
}

export function isAdvancedSectionForced(props: Pick<AdvancedSectionProps, "forceOpen" | "hasError" | "setCount" | "allowCollapseWhenSet">): boolean {
  return Boolean(props.forceOpen || props.hasError || (!props.allowCollapseWhenSet && (props.setCount ?? 0) > 0));
}

export function AdvancedSection({
  label = "Advanced",
  summary,
  setCount = 0,
  hasError,
  defaultOpen = false,
  allowCollapseWhenSet = false,
  persistKey,
  forceOpen,
  children
}: AdvancedSectionProps) {
  const [open, setOpen] = useDisclosureState(persistKey, defaultOpen, isDisclosureBoolean);
  const forced = isAdvancedSectionForced({ forceOpen, hasError, setCount, allowCollapseWhenSet });
  const [revealedAfterForce, setRevealedAfterForce] = React.useState(false);
  React.useEffect(() => {
    if (forced) setRevealedAfterForce(true);
  }, [forced]);
  const badge = setCount > 0
    ? <StatusPill tone={hasError ? "danger" : "accent"}>{setCount} set</StatusPill>
    : hasError ? <StatusPill tone="danger">Error</StatusPill> : null;

  return (
    <DisclosureFold
      className="studio-advanced-section"
      label={label}
      summary={summary}
      badge={badge}
      open={open || revealedAfterForce}
      forced={forced}
      onOpenChange={nextOpen => {
        setRevealedAfterForce(false);
        setOpen(nextOpen);
      }}
    >
      {children}
    </DisclosureFold>
  );
}
