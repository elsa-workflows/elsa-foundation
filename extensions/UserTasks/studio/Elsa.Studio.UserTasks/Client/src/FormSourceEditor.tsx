import React, { useEffect, useState } from "react";
import { jsonLanguageAdapter, StudioCodeEditor, type StudioCodeDiagnostic } from "@elsa-workflows/studio-code-editor";
import { StudioButton } from "@elsa-workflows/studio-ui";
import { parseTaskFormSource, serializeTaskFormSource } from "./formDesignerModel";
import type { TaskFieldControlSource } from "./fieldControls";
import type { TaskForm } from "./taskApi";

export function FormSourceEditor({ form, disabled = false, fieldControls, onApply, onUnappliedChange }: {
  form: TaskForm;
  disabled?: boolean;
  fieldControls?: TaskFieldControlSource;
  onApply(form: TaskForm): void;
  onUnappliedChange?(unapplied: boolean): void;
}) {
  const serialized = serializeTaskFormSource(form);
  const [text, setText] = useState(serialized);
  const [baseline, setBaseline] = useState(serialized);
  const [error, setError] = useState<string | null>(null);
  const dirty = text !== baseline;
  const diagnostics: StudioCodeDiagnostic[] = error ? [{ severity: "error", message: error }] : [];

  useEffect(() => {
    setText(serialized);
    setBaseline(serialized);
    setError(null);
  }, [serialized]);

  const apply = () => {
    try {
      const nextForm = parseTaskFormSource(text, form, fieldControls);
      onApply(nextForm);
      const nextSerialized = serializeTaskFormSource(nextForm);
      setText(nextSerialized);
      setBaseline(nextSerialized);
      setError(null);
      onUnappliedChange?.(false);
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "The form source could not be applied.");
    }
  };

  const reset = () => {
    setText(baseline);
    setError(null);
    onUnappliedChange?.(false);
  };

  return <section className="user-task-designer__source" aria-label="Form JSON source">
    <div className="user-task-designer__source-toolbar">
      <p>Edit the form definition as JSON. Apply updates the canvas; saving or publishing requires applied source.</p>
      <div className="user-task-designer__source-actions">
        <StudioButton variant="ghost" disabled={disabled || !dirty} onClick={reset}>Reset</StudioButton>
        <StudioButton variant="primary" disabled={disabled || !dirty} onClick={apply}>Apply</StudioButton>
      </div>
    </div>
    {error ? <p className="user-task-designer__source-error" role="alert">{error}</p> : null}
    <div className="user-task-designer__source-editor">
      <StudioCodeEditor
        ariaLabel="Form JSON"
        document={{ uri: `elsa://user-tasks/forms/${encodeURIComponent(form.id || "new")}.json`, language: "json", value: text }}
        readOnly={disabled}
        languageAdapter={jsonLanguageAdapter}
        diagnostics={diagnostics}
        minHeight="100%"
        theme="studio"
        onChange={next => {
          if (disabled) return;
          setText(next.value);
          if (error) setError(null);
          onUnappliedChange?.(next.value !== baseline);
        }}
      />
    </div>
  </section>;
}