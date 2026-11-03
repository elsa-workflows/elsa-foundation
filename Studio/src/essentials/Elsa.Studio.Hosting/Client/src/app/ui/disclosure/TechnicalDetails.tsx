import React from "react";
import { DisclosureFold } from "./DisclosureFold";
import { type DisclosureControlProps } from "./types";
import { isDisclosureBoolean, useDisclosureState } from "./useDisclosureState";

export interface TechnicalDetailsProps extends DisclosureControlProps {
  label?: React.ReactNode;
  summary?: React.ReactNode;
  defaultOpen?: boolean;
  children: React.ReactNode;
}

export function TechnicalDetails({
  label = "Technical details",
  summary,
  defaultOpen = false,
  persistKey,
  forceOpen,
  children
}: TechnicalDetailsProps) {
  const [open, setOpen] = useDisclosureState(persistKey, defaultOpen, isDisclosureBoolean);

  return (
    <DisclosureFold
      className="studio-technical-details"
      bodyClassName="studio-technical-details-body"
      label={label}
      summary={summary}
      open={open}
      forced={forceOpen}
      onOpenChange={setOpen}
    >
      {children}
    </DisclosureFold>
  );
}
