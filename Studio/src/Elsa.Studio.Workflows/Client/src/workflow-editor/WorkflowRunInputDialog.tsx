import { Component, useEffect, useId, useRef, useState, type ErrorInfo, type FormEvent, type KeyboardEvent as ReactKeyboardEvent, type ReactNode } from "react";
import type { StudioWorkflowRunInputEditorContribution } from "@elsa-workflows/studio-sdk";
import type { WorkflowInput } from "../workflowTypes";
import { formatArgumentType } from "../workflowReferenceAuthoring";
import {
  getWorkflowRunInputControlKind,
  parseWorkflowRunInputs,
  resolveWorkflowRunInputEditor,
  type WorkflowRunInputDrafts,
  type WorkflowRunInputValues
} from "../workflowRunInputs";

export function WorkflowRunInputDialog({ inputs, editors = [], busy = false, onSubmit, onCancel }: {
  inputs: WorkflowInput[];
  editors?: StudioWorkflowRunInputEditorContribution[];
  busy?: boolean;
  onSubmit(values: WorkflowRunInputValues): void;
  onCancel(): void;
}) {
  const titleId = useId();
  const dialogRef = useRef<HTMLElement>(null);
  const [drafts, setDrafts] = useState<WorkflowRunInputDrafts>({});
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [failedEditorKeys, setFailedEditorKeys] = useState<Set<string>>(() => new Set());

  useEffect(() => {
    const previouslyFocused = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    dialogRef.current?.querySelector<HTMLElement>("input, select, textarea, button")?.focus();
    return () => previouslyFocused?.focus();
  }, []);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const parsed = parseWorkflowRunInputs(inputs, drafts, editors, failedEditorKeys);
    const contributionFailureKeys = Object.keys(parsed.contributionFailures ?? {});
    if (contributionFailureKeys.length > 0) {
      setFailedEditorKeys(current => new Set([...current, ...contributionFailureKeys]));
      setErrors({
        ...parsed.errors,
        ...Object.fromEntries(contributionFailureKeys.map(key => [key, describeEditorFallback(inputs, key, parsed.errors[key])]))
      });
      return;
    }
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
                editors={failedEditorKeys.has(input.referenceKey) ? [] : editors}
                value={ownValue(drafts, input.referenceKey) ?? ""}
                error={ownValue(errors, input.referenceKey)}
                disabled={busy}
                onEditorFailure={() => {
                  const label = input.displayName || input.name;
                  setFailedEditorKeys(current => new Set(current).add(input.referenceKey));
                  setErrors(current => ({ ...current, [input.referenceKey]: `The ${label} editor failed. Enter a JSON value instead.` }));
                }}
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

function describeEditorFallback(inputs: WorkflowInput[], inputKey: string, error: string) {
  const input = inputs.find(candidate => candidate.referenceKey === inputKey);
  const guidance = input && getWorkflowRunInputControlKind(input) === "json"
    ? "Enter a JSON value instead."
    : "Use the standard input instead.";
  return `${error} ${guidance}`;
}

function WorkflowRunInputField({ input, editors, value, error, disabled, onEditorFailure, onChange }: {
  input: WorkflowInput;
  editors: StudioWorkflowRunInputEditorContribution[];
  value: string;
  error?: string;
  disabled: boolean;
  onEditorFailure(): void;
  onChange(value: string): void;
}) {
  const fieldId = useId();
  const descriptionId = `${fieldId}-description`;
  const errorId = `${fieldId}-error`;
  const label = input.displayName || input.name;
  const describedBy = [input.description ? descriptionId : null, error ? errorId : null].filter(Boolean).join(" ") || undefined;
  const controlProps = {
    id: fieldId,
    "aria-label": label,
    "aria-describedby": describedBy,
    "aria-invalid": Boolean(error),
    "aria-required": Boolean(input.isRequired)
  };
  const common = {
    ...controlProps,
    disabled,
    value
  };
  const editor = resolveWorkflowRunInputEditor(editors, input);
  const EditorComponent = editor?.component;
  const fallback = renderInputControl(input, common, onChange);

  return (
    <label className="wf-form-field" htmlFor={fieldId}>
      <span>{label}{input.isRequired ? " *" : ""}</span>
      <small className="wf-run-input-type">{formatArgumentType(input.type)}</small>
      {EditorComponent ? (
        <WorkflowRunInputEditorBoundary
          editorId={editor.id}
          inputKey={input.referenceKey}
          fallback={fallback}
          onError={onEditorFailure}
        >
          <EditorComponent
            input={input}
            draft={value}
            disabled={disabled}
            controlProps={controlProps}
            onChange={onChange}
          />
        </WorkflowRunInputEditorBoundary>
      ) : fallback}
      {input.description ? <small id={descriptionId}>{input.description}</small> : null}
      {error ? <small id={errorId} role="alert">{error}</small> : null}
    </label>
  );
}

class WorkflowRunInputEditorBoundary extends Component<{
  editorId: string;
  inputKey: string;
  fallback: ReactNode;
  onError(): void;
  children: ReactNode;
}, { failed: boolean }> {
  state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error(`[workflow.run-input.editors] ${this.props.editorId} failed while rendering ${this.props.inputKey}.`, error, info);
    this.props.onError();
  }

  render() {
    return this.state.failed ? this.props.fallback : this.props.children;
  }
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
