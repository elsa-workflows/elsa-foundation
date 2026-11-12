import React, { useId } from "react";
import type { StudioUserTaskFormProps } from "@elsa-workflows/studio-sdk";
import type { TaskForm } from "./taskApi";

export function BasicTaskForm({ form: supplied, value, onChange, readOnly, errors }: StudioUserTaskFormProps) {
  const form = supplied as TaskForm;
  const formId = useId();
  return <div className="user-task-fields">{form.fields.map((field, index) => {
    const id = `${formId}-field-${index}`;
    const disabled = readOnly || field.readOnly;
    const error = errors && Object.hasOwn(errors, field.key) ? errors[field.key] : undefined;
    const common = { id, disabled, "aria-invalid": Boolean(error), "aria-describedby": `${id}-help ${id}-error` };
    const change = (next: unknown) => { const updated = { ...value, [field.key]: next }; if (next === "" && field.kind !== "text" && field.kind !== "multiline") delete updated[field.key]; onChange(updated); };
    const current = Object.hasOwn(value, field.key) ? value[field.key] : undefined;
    return <div className="user-task-field" key={field.key}>
      <label htmlFor={id}>{field.label}{field.required ? " *" : ""}</label>
      {field.kind === "multiline" ? <textarea {...common} value={String(current ?? "")} maxLength={field.maxLength} onChange={event => change(event.target.value)} />
        : field.kind === "boolean" ? <select {...common} value={typeof current === "boolean" ? String(current) : ""} onChange={event => change(event.target.value === "" ? "" : event.target.value === "true")}><option value="">Choose…</option><option value="true">Yes</option><option value="false">No</option></select>
        : field.kind === "choice" ? <select {...common} value={String(current ?? "")} onChange={event => change(event.target.value)}><option value="">Choose…</option>{field.choices?.map(choice => <option key={choice}>{choice}</option>)}</select>
        : <input {...common} type={field.kind === "number" ? "number" : field.kind === "date" ? "date" : "text"} value={typeof current === "number" || typeof current === "string" ? current : ""}
          min={field.minimum} max={field.maximum} maxLength={field.maxLength} onChange={event => change(field.kind === "number" && event.target.value !== "" ? Number(event.target.value) : event.target.value)} />}
      <small id={`${id}-help`}>{field.help}</small><small id={`${id}-error`} role={error ? "alert" : undefined}>{error}</small>
    </div>;
  })}</div>;
}
