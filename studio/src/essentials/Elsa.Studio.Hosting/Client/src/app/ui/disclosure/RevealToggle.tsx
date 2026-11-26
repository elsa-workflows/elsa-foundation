import React, { useId } from "react";
import { Eye, EyeOff } from "lucide-react";
import { type DisclosureControlProps } from "./types";

export interface RevealToggleProps extends DisclosureControlProps {
  label: React.ReactNode;
  checked: boolean;
  onCheckedChange(checked: boolean): void;
  children?: React.ReactNode;
  ignoredNote?: string;
  /** Toggle masking in an existing field instead of disclosing a child group. */
  controlsId?: string;
  disabled?: boolean;
}

export function RevealToggle({
  label,
  checked,
  onCheckedChange,
  children,
  ignoredNote = "will be ignored",
  forceOpen,
  controlsId,
  disabled
}: RevealToggleProps) {
  const reactId = useId();
  const groupId = `${reactId}-group`;
  const revealed = checked || Boolean(forceOpen);
  const ignored = revealed && !checked;

  if (controlsId) return <button type="button" disabled={disabled} aria-label={typeof label === "string" ? label : undefined}
    title={typeof label === "string" ? label : undefined} aria-controls={controlsId} aria-pressed={checked} onClick={() => onCheckedChange(!checked)}>
    {checked ? <EyeOff size={16} aria-hidden /> : <Eye size={16} aria-hidden />}
    {typeof label !== "string" ? label : null}
  </button>;

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
