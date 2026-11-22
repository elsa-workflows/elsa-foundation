import { StudioField } from "@elsa-workflows/studio-ui";
import React, { useId } from "react";
import type { StudioUserTaskFormProps } from "@elsa-workflows/studio-sdk";
import { builtInTaskFieldControls, resolveTaskFieldControl, type TaskFieldControlSource } from "./fieldControls";
import type { TaskForm } from "./taskApi";

function TaskFormRenderer({ form: supplied, value, onChange, readOnly, errors, controls = builtInTaskFieldControls }: StudioUserTaskFormProps & { controls?: TaskFieldControlSource }) {
  const form = supplied as TaskForm;
  const formId = useId();
  return <div className="user-task-fields">{form.fields.map((field, index) => {
    const resolved = resolveTaskFieldControl(field, controls);
    if (resolved.status !== "available" || !resolved.control) return <div className="user-task-field user-task-field--unavailable" role="alert" key={field.key}>
      <strong>{field.label}</strong><p>{resolved.reason ?? "The field control is unavailable."}</p>
    </div>;
    const id = `${formId}-field-${index}`;
    const disabled = readOnly || field.readOnly;
    const error = errors && Object.hasOwn(errors, field.key) ? errors[field.key] : undefined;
    const Control = resolved.control.component;
    const change = (next: unknown) => {
      if (disabled) return;
      const updated = { ...value, [field.key]: next };
      if (next === undefined || (next === "" && field.kind !== "text" && field.kind !== "multiline")) delete updated[field.key];
      onChange(updated);
    };
    const current = Object.hasOwn(value, field.key) ? value[field.key] : undefined;
    const control = <Control field={field} id={id} disabled={disabled} required={field.required} value={current} error={error} onChange={change} />;
    if (resolved.control.ownsFieldLabel) return <div className="user-task-field user-task-field--options" key={field.key}>
      {control}{field.help ? <small id={`${id}-help`}>{field.help}</small> : null}{error ? <small id={`${id}-error`} role="alert">{error}</small> : null}
    </div>;
    return <StudioField className="user-task-field" key={field.key} controlId={id} label={<>{field.label}{field.required && !disabled ? " *" : ""}</>}>
      {control}
      {field.help ? <small id={`${id}-help`}>{field.help}</small> : null}{error ? <small id={`${id}-error`} role="alert">{error}</small> : null}
    </StudioField>;
  })}</div>;
}

export function BasicTaskForm(props: StudioUserTaskFormProps) {
  return <TaskFormRenderer {...props} />;
}

export function ContributedTaskForm(props: StudioUserTaskFormProps & { controls?: TaskFieldControlSource }) {
  return <TaskFormRenderer {...props} />;
}
