import React, { useId } from "react";
import { type DisclosureControlProps } from "./types";

export interface RevealToggleProps extends DisclosureControlProps {
  label: React.ReactNode;
  checked: boolean;
  onCheckedChange(checked: boolean): void;
  children?: React.ReactNode;
  ignoredNote?: string;
}

export function RevealToggle({
  label,
  checked,
  onCheckedChange,
  children,
  ignoredNote = "will be ignored",
  forceOpen
}: RevealToggleProps) {
  const reactId = useId();
  const groupId = `${reactId}-group`;
  const revealed = checked || Boolean(forceOpen);
  const ignored = revealed && !checked;

  return (
    <div className="studio-reveal-toggle" data-checked={checked ? "true" : "false"} data-ignored={ignored ? "true" : "false"}>
      <div className="studio-reveal-toggle-row">
        <span className="studio-reveal-toggle-label" id={`${reactId}-label`}>{label}</span>
        <button
          type="button"
          className="studio-reveal-switch"
          role="switch"
          aria-checked={checked}
          aria-labelledby={`${reactId}-label`}
          aria-controls={groupId}
          aria-expanded={revealed}
          onClick={() => onCheckedChange(!checked)}
        >
          <span className="studio-reveal-switch-thumb" aria-hidden="true" />
        </button>
      </div>
      <div
        id={groupId}
        className="studio-reveal-toggle-group"
        hidden={!revealed}
        data-ignored={ignored ? "true" : undefined}
      >
        {ignored ? <p className="studio-reveal-toggle-note">{ignoredNote}</p> : null}
        {children}
      </div>
    </div>
  );
}
