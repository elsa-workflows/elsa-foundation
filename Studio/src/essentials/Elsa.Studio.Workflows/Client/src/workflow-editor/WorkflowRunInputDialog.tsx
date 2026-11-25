import { localDateTimeToIso, resolveStudioTimeZone, useStudioTimeZone } from "@elsa-workflows/studio-sdk";
import { typeAllowsNull } from "../typeNullability";
import { StudioDialog, StudioDialogFooter, StudioField } from "@elsa-workflows/studio-ui";
import { Component, useId, useRef, useState, type ErrorInfo, type FormEvent, type ReactNode } from "react";
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

export function WorkflowRunInputDialog({ inputs, editors = [], busy = false, returnFocusTo, onSubmit, onCancel }: {
  inputs: WorkflowInput[];
  editors?: StudioWorkflowRunInputEditorContribution[];
  busy?: boolean;
  returnFocusTo?: HTMLElement | null;
  onSubmit(values: WorkflowRunInputValues): void;
  onCancel(): void;
}) {
  const timeZone = useStudioTimeZone();
  const firstInput = useRef<HTMLElement | null>(null);
  const [drafts, setDrafts] = useState<WorkflowRunInputDrafts>({});
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [failedEditorKeys, setFailedEditorKeys] = useState<Set<string>>(() => new Set());

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
    <StudioDialog open title="Run workflow" description={`Provide the workflow inputs for this run. Dates use ${resolveStudioTimeZone(timeZone)}. A repeated hour uses its earlier offset.`}
      onCloseAutoFocus={returnFocusTo ? event => {
        event.preventDefault();
        if (document.activeElement === document.body && returnFocusTo.isConnected) returnFocusTo.focus();
      } : undefined}
      onClose={onCancel} initialFocusRef={firstInput} dismissDisabled={busy} dismissOnOutsideClick={false}
      noValidate onSubmit={submit}
      footer={<StudioDialogFooter onCancel={onCancel} cancelDisabled={busy} confirmDisabled={busy}
        confirmLabel={busy ? "Starting…" : "Run workflow"} />}>
      <div className="wf-run-input-list" ref={element => {
        firstInput.current = element?.querySelector<HTMLElement>("input, select, textarea, button") ?? null;
      }}>
        {inputs.map(input => (
          <WorkflowRunInputField
            key={input.referenceKey}
            input={input}
            editors={failedEditorKeys.has(input.referenceKey) ? [] : editors}
            value={ownValue(drafts, input.referenceKey) ?? ""}
            isNull={ownValue(drafts, input.referenceKey) === null}
            onNull={() => {
              setDrafts(current => ({ ...current, [input.referenceKey]: null }));
              setErrors(current => { const next = { ...current }; delete next[input.referenceKey]; return next; });
            }}
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
            onOmit={Object.prototype.hasOwnProperty.call(drafts, input.referenceKey) ? () => {
              setDrafts(current => {
                const next = { ...current };
                delete next[input.referenceKey];
                return next;
              });
              setErrors(current => {
                const next = { ...current };
                delete next[input.referenceKey];
                return next;
              });
            } : undefined}
          />
        ))}
      </div>
    </StudioDialog>
  );
}

function describeEditorFallback(inputs: WorkflowInput[], inputKey: string, error: string) {
  const input = inputs.find(candidate => candidate.referenceKey === inputKey);
  const guidance = input && getWorkflowRunInputControlKind(input) === "json"
    ? "Enter a JSON value instead."
    : "Use the standard input instead.";
  return `${error} ${guidance}`;
}

export function WorkflowRunInputField({ input, editors, value, error, disabled, onEditorFailure, onChange, onOmit, isNull = false, onNull }: {
  input: WorkflowInput;
  editors: StudioWorkflowRunInputEditorContribution[];
  value: string;
  error?: string;
  disabled: boolean;
  onEditorFailure(): void;
  onChange(value: string): void;
  onOmit?(): void;
  isNull?: boolean;
  onNull?(): void;
}) {
  useStudioTimeZone();
  const fieldId = useId();
  const descriptionId = `${fieldId}-description`;
  const errorId = `${fieldId}-error`;
  const label = input.displayName || input.name;
  const describedBy = [input.description ? descriptionId : null, error ? errorId : null].filter(Boolean).join(" ") || undefined;
  const hasDefault = Object.prototype.hasOwnProperty.call(input, "defaultValue") && input.defaultValue !== undefined;
  const required = Boolean(input.isRequired);
  const canUseEmptyString = input.type.collectionKind === "Single" && ["string", "timespan", "uri"].includes(input.type.alias.trim().split(".").pop()?.toLowerCase() ?? "");
  const controlProps = {
    id: fieldId,
    "aria-label": label,
    "data-studio-field-control": "",
    "aria-describedby": describedBy,
    "aria-invalid": Boolean(error),
    "aria-required": required
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
    <StudioField className="wf-form-field" controlId={fieldId} label={<>{label}{required ? " *" : ""}</>} meta={<small className="wf-run-input-type">{formatArgumentType(input.type)}</small>}>
      {isNull ? <><input {...controlProps} value="null" readOnly disabled={disabled} /><small role="status">Sending null for {label}</small></> : EditorComponent ? (
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
      {!isNull && getWorkflowRunInputControlKind(input) === "datetime" && value && !error ? <small>{dateOffsetPreview(value)}</small> : null}
      {onNull && typeAllowsNull(input.type) ? <button type="button" aria-label={`${isNull ? "Enter a value" : "Send null"} for ${label}`} onClick={isNull ? () => onChange("") : onNull} disabled={disabled}>{isNull ? "Enter a value" : "Send null"}</button> : null}
      {input.description ? <small id={descriptionId}>{input.description}</small> : null}
      {hasDefault && !input.isRequired ? <small>Uses default if omitted.</small> : null}
      {hasDefault && canUseEmptyString ? (
        <button type="button" onClick={() => onChange("")} disabled={disabled}>Use empty string</button>
      ) : null}
      {!input.isRequired && onOmit ? (
        <button type="button" aria-label={`${hasDefault ? "Use default" : "Omit"} for ${label}`} onClick={onOmit} disabled={disabled}>{hasDefault ? "Use default" : "Omit"}</button>
      ) : null}
      {error ? <small id={errorId} role="alert">{error}</small> : null}
    </StudioField>
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

function dateOffsetPreview(value: string) {
  try { return `Sent as ${localDateTimeToIso(value)}`; } catch (error) { return error instanceof Error ? error.message : "Enter a valid date and time."; }
}
