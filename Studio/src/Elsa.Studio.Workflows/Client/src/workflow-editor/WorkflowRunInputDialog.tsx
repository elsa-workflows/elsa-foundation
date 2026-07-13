import { useEffect, useId, useRef, useState, type FormEvent, type KeyboardEvent as ReactKeyboardEvent } from "react";
import type { WorkflowInput } from "../workflowTypes";
import { formatArgumentType } from "../workflowReferenceAuthoring";
import {
  getWorkflowRunInputControlKind,
  parseWorkflowRunInputs,
  type WorkflowRunInputDrafts,
  type WorkflowRunInputValues
} from "../workflowRunInputs";

export function WorkflowRunInputDialog({ inputs, busy = false, onSubmit, onCancel }: {
  inputs: WorkflowInput[];
  busy?: boolean;
  onSubmit(values: WorkflowRunInputValues): void;
  onCancel(): void;
}) {
  const titleId = useId();
  const dialogRef = useRef<HTMLElement>(null);
  const [drafts, setDrafts] = useState<WorkflowRunInputDrafts>({});
  const [errors, setErrors] = useState<Record<string, string>>({});

  useEffect(() => {
    const previouslyFocused = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    dialogRef.current?.querySelector<HTMLElement>("input, select, textarea, button")?.focus();
    return () => previouslyFocused?.focus();
  }, []);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const parsed = parseWorkflowRunInputs(inputs, drafts);
    setErrors(parsed.errors);
    if (Object.keys(parsed.errors).length === 0) onSubmit(parsed.values);
  };

  return (
    <div className="wf-dialog-backdrop" role="presentation">
      <section
        ref={dialogRef}
        className="wf-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        onKeyDown={event => {
          if (event.key === "Escape" && !busy) onCancel();
          if (event.key === "Tab") trapFocus(event, dialogRef.current);
        }}
      >
        <form onSubmit={submit} noValidate>
          <div className="wf-dialog-heading">
            <div>
              <h3 id={titleId}>Run workflow</h3>
              <p>Provide the workflow inputs for this run.</p>
            </div>
          </div>
          <div className="wf-run-input-list">
            {inputs.map(input => (
              <WorkflowRunInputField
                key={input.referenceKey}
                input={input}
                value={ownValue(drafts, input.referenceKey) ?? ""}
                error={ownValue(errors, input.referenceKey)}
                disabled={busy}
                onChange={value => {
                  setDrafts(current => ({ ...current, [input.referenceKey]: value }));
                  setErrors(current => {
                    if (!ownValue(current, input.referenceKey)) return current;
                    const next = { ...current };
                    delete next[input.referenceKey];
                    return next;
                  });
                }}
              />
            ))}
          </div>
          <div className="wf-dialog-actions">
            <button type="button" onClick={onCancel} disabled={busy}>Cancel</button>
            <button type="submit" disabled={busy}>{busy ? "Starting..." : "Run workflow"}</button>
          </div>
        </form>
      </section>
    </div>
  );
}

function WorkflowRunInputField({ input, value, error, disabled, onChange }: {
  input: WorkflowInput;
  value: string;
  error?: string;
  disabled: boolean;
  onChange(value: string): void;
}) {
  const fieldId = useId();
  const descriptionId = `${fieldId}-description`;
  const errorId = `${fieldId}-error`;
  const label = input.displayName || input.name;
  const describedBy = [input.description ? descriptionId : null, error ? errorId : null].filter(Boolean).join(" ") || undefined;
  const common = {
    id: fieldId,
    "aria-label": label,
    "aria-describedby": describedBy,
    "aria-invalid": Boolean(error),
    "aria-required": Boolean(input.isRequired),
    disabled,
    value
  };

  return (
    <label className="wf-form-field" htmlFor={fieldId}>
      <span>{label}{input.isRequired ? " *" : ""}</span>
      <small className="wf-run-input-type">{formatArgumentType(input.type)}</small>
      {renderInputControl(input, common, onChange)}
      {input.description ? <small id={descriptionId}>{input.description}</small> : null}
      {error ? <small id={errorId} role="alert">{error}</small> : null}
    </label>
  );
}

function renderInputControl(
  input: WorkflowInput,
  common: {
    id: string;
    "aria-label": string;
    "aria-describedby": string | undefined;
    "aria-invalid": boolean;
    "aria-required": boolean;
    disabled: boolean;
    value: string;
  },
  onChange: (value: string) => void
) {
  const controlKind = getWorkflowRunInputControlKind(input);
  if (controlKind === "json") {
    return (
      <textarea
        {...common}
        rows={3}
        placeholder={input.type.collectionKind === "Single" ? "Enter JSON" : "Enter a JSON array"}
        onChange={event => onChange(event.currentTarget.value)}
      />
    );
  }
  if (controlKind === "boolean") {
    return (
      <select {...common} onChange={event => onChange(event.currentTarget.value)}>
        <option value="">Not set</option>
        <option value="true">True</option>
        <option value="false">False</option>
      </select>
    );
  }
  return (
    <input
      {...common}
      type={controlKind === "integer" || controlKind === "number" ? "number" : controlKind === "datetime" ? "datetime-local" : "text"}
      step={controlKind === "integer" ? "1" : controlKind === "number" ? "any" : undefined}
      onChange={event => onChange(event.currentTarget.value)}
    />
  );
}

function ownValue<T>(record: Record<string, T>, key: string): T | undefined {
  return Object.prototype.hasOwnProperty.call(record, key) ? record[key] : undefined;
}

function trapFocus(event: ReactKeyboardEvent, dialog: HTMLElement | null) {
  if (!dialog) return;
  const focusable = [...dialog.querySelectorAll<HTMLElement>(
    "button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex='-1'])"
  )];
  if (focusable.length === 0) return;
  const first = focusable[0];
  const last = focusable[focusable.length - 1];
  if (event.shiftKey && document.activeElement === first) {
    event.preventDefault();
    last.focus();
  } else if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault();
    first.focus();
  }
}
