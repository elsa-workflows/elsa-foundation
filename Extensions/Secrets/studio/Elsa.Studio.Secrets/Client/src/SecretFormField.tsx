import React, { useId } from "react";

type ControlAttributes = {
  id: string;
  "aria-describedby"?: string;
  "aria-invalid"?: true;
  disabled?: boolean;
};

/** Module-local prototype. Exercise the contract here before promoting it to Studio UI. */
export function SecretFormField({
  label, value, hint, error, disabled, raised = false, trailing, controlId, children
}: {
  controlId?: string;
  label: string;
  value: string;
  hint?: React.ReactNode;
  error?: React.ReactNode;
  disabled?: boolean;
  raised?: boolean;
  trailing?: React.ReactNode;
  children(attributes: ControlAttributes): React.ReactNode;
}) {
  const generatedId = useId();
  const id = controlId ?? generatedId;
  const descriptionId = hint || error ? `${id}-help` : undefined;
  return (
    <div className="secrets-compact-field" data-raised={raised || value.length > 0} data-invalid={!!error} data-disabled={!!disabled} data-long-label={label.length > 44}>
      <div className={`secrets-compact-control${trailing ? " secrets-value-input" : ""}`} data-trailing={!!trailing}>
        {children({ id, "aria-describedby": descriptionId, "aria-invalid": error ? true : undefined, disabled })}
        <label htmlFor={id}>{label}</label>
        {trailing ? <div className="secrets-compact-trailing">{trailing}</div> : null}
      </div>
      {descriptionId ? <p id={descriptionId} className={error ? "secrets-field-error" : "secrets-field-hint"}>{error || hint}</p> : null}
    </div>
  );
}
