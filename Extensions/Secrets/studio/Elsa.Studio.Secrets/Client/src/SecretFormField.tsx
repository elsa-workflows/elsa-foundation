import React, { useId } from "react";

type ControlAttributes = {
  id: string;
  "aria-describedby"?: string;
  "aria-invalid"?: true;
  disabled?: boolean;
};

/**
 * Outlined Secret dialog field: one native control with a persistent label set into its border,
 * and a single hint or error associated with the control. Module-local until another module needs it.
 */
export function SecretFormField({
  label, hint, error, disabled, trailing, controlId, children
}: {
  controlId?: string;
  label: string;
  hint?: React.ReactNode;
  error?: React.ReactNode;
  disabled?: boolean;
  trailing?: React.ReactNode;
  children(attributes: ControlAttributes): React.ReactNode;
}) {
  const generatedId = useId();
  const id = controlId ?? generatedId;
  const descriptionId = hint || error ? `${id}-help` : undefined;
  return (
    <div className="secrets-compact-field" data-invalid={!!error} data-disabled={!!disabled}>
      <div className={`secrets-compact-control${trailing ? " secrets-value-input" : ""}`} data-trailing={!!trailing}>
        {children({ id, "aria-describedby": descriptionId, "aria-invalid": error ? true : undefined, disabled })}
        <label htmlFor={id}>{label}</label>
        {trailing ? <div className="secrets-compact-trailing">{trailing}</div> : null}
      </div>
      {descriptionId ? <p id={descriptionId} className={error ? "secrets-field-error" : "secrets-field-hint"}>{error || hint}</p> : null}
    </div>
  );
}
