import React from "react";
import { StudioField } from "@elsa-workflows/studio-ui";
import { AlignLeft, CalendarDays, CheckSquare, List, Sigma, Type } from "lucide-react";
import type { StudioContributionComposition, StudioContributionRegistry, StudioUserTaskFieldControlContribution, StudioUserTaskFieldControlEditorProps, StudioUserTaskFieldControlRenderProps } from "@elsa-workflows/studio-sdk";
import type { TaskField } from "./taskApi";

const configurationLimit = 16_384;
const configurationDepthLimit = 16;
const controlIdentityPattern = /^[a-z0-9][a-z0-9.-]*\/[1-9]\d*$/;

function createField(kind: string, fields: readonly unknown[]): TaskField {
  const existing = new Set((fields as TaskField[]).map(field => field.key.trim()));
  let suffix = 1;
  while (existing.has(`${kind}${suffix}`)) suffix++;
  const labels: Record<string, string> = { text: "Text field", multiline: "Long text field", number: "Number field", boolean: "Yes / no field", date: "Date field", choice: "Choice field" };
  return { key: `${kind}${suffix}`, label: `${labels[kind] ?? "Field"} ${suffix}`, kind, required: false, readOnly: false, ...(kind === "choice" ? { choices: ["Option 1"] } : {}) };
}

function fieldOf(props: StudioUserTaskFieldControlRenderProps | StudioUserTaskFieldControlEditorProps) {
  return props.field as TaskField;
}

function TextControl(props: StudioUserTaskFieldControlRenderProps) {
  const field = fieldOf(props);
  return <input id={props.id} type="text" value={typeof props.value === "string" ? props.value : ""} maxLength={field.maxLength}
    disabled={props.disabled} aria-invalid={Boolean(props.error)} onChange={event => props.onChange(event.target.value)} />;
}

function MultilineControl(props: StudioUserTaskFieldControlRenderProps) {
  const field = fieldOf(props);
  return <textarea id={props.id} value={typeof props.value === "string" ? props.value : ""} maxLength={field.maxLength}
    disabled={props.disabled} aria-invalid={Boolean(props.error)} onChange={event => props.onChange(event.target.value)} />;
}

function NumberControl(props: StudioUserTaskFieldControlRenderProps) {
  const field = fieldOf(props);
  return <input id={props.id} type="number" value={typeof props.value === "number" ? props.value : ""} min={field.minimum} max={field.maximum}
    disabled={props.disabled} aria-invalid={Boolean(props.error)} onChange={event => props.onChange(event.target.value === "" ? undefined : Number(event.target.value))} />;
}

function BooleanControl(props: StudioUserTaskFieldControlRenderProps) {
  const field = fieldOf(props);
  const display = field.presentation?.display ?? "dropdown";
  const trueLabel = field.presentation?.trueLabel ?? "Yes";
  const falseLabel = field.presentation?.falseLabel ?? "No";
  const unansweredLabel = field.presentation?.unansweredLabel ?? (display === "checkbox" ? "Not answered" : "Choose…");
  const current = props.value;
  const clear = () => props.onChange(undefined);
  if (display === "checkbox") return <div className="user-task-boolean-checkbox">
    <label htmlFor={props.id}>{field.label}{props.required ? " *" : ""}</label>
    <input id={props.id} type="checkbox" checked={current === true} disabled={props.disabled} aria-invalid={Boolean(props.error)} onChange={event => props.onChange(event.target.checked)} />
    <small role="status">{typeof current === "boolean" ? current ? trueLabel : falseLabel : unansweredLabel}</small>
    {typeof current === "boolean" && !props.disabled ? <button type="button" className="user-task-field-clear" onClick={clear}>Clear answer</button> : null}
  </div>;
  if (display === "radio") return <fieldset className="user-task-field-options" disabled={props.disabled} aria-invalid={Boolean(props.error)} aria-required={props.required}>
    <legend>{field.label}{props.required ? " *" : ""}</legend>
    <label><input id={props.id} type="radio" name={`${props.id}-answer`} checked={current === true} onChange={() => props.onChange(true)} />{trueLabel}</label>
    <label><input type="radio" name={`${props.id}-answer`} checked={current === false} onChange={() => props.onChange(false)} />{falseLabel}</label>
    {current === undefined ? <small role="status">{unansweredLabel}</small> : null}
    {typeof current === "boolean" && !props.disabled ? <button type="button" className="user-task-field-clear" onClick={clear}>Clear answer</button> : null}
  </fieldset>;
  return <><label htmlFor={props.id}>{field.label}{props.required ? " *" : ""}</label><select id={props.id} value={typeof current === "boolean" ? String(current) : ""} disabled={props.disabled} aria-invalid={Boolean(props.error)}
    onChange={event => props.onChange(event.target.value === "" ? undefined : event.target.value === "true")}>
    <option value="">{unansweredLabel}</option><option value="true">{trueLabel}</option><option value="false">{falseLabel}</option>
  </select></>;
}

function DateControl(props: StudioUserTaskFieldControlRenderProps) {
  return <input id={props.id} type="date" value={typeof props.value === "string" ? props.value : ""} disabled={props.disabled}
    aria-invalid={Boolean(props.error)} onChange={event => props.onChange(event.target.value || undefined)} />;
}

function ChoiceControl(props: StudioUserTaskFieldControlRenderProps) {
  const field = fieldOf(props);
  const choices = field.choices ?? [];
  if (field.presentation?.display === "radio") return <fieldset className="user-task-field-options" disabled={props.disabled} aria-invalid={Boolean(props.error)} aria-required={props.required}>
    <legend>{field.label}{props.required ? " *" : ""}</legend>
    {choices.map((choice, index) => <label key={`${choice}-${index}`}><input id={index === 0 ? props.id : undefined} type="radio" name={`${props.id}-answer`} value={choice}
      checked={props.value === choice} onChange={() => props.onChange(choice)} />{choice}</label>)}
    {typeof props.value === "string" && props.value && !props.disabled ? <button type="button" className="user-task-field-clear" onClick={() => props.onChange(undefined)}>Clear answer</button> : null}
  </fieldset>;
  return <><label htmlFor={props.id}>{field.label}{props.required ? " *" : ""}</label><select id={props.id} value={typeof props.value === "string" ? props.value : ""} disabled={props.disabled} aria-invalid={Boolean(props.error)}
    onChange={event => props.onChange(event.target.value || undefined)}>
    <option value="">Choose…</option>{choices.map((choice, index) => <option key={`${choice}-${index}`} value={choice}>{choice}</option>)}
  </select></>;
}

function updateField(props: StudioUserTaskFieldControlEditorProps, update: Partial<TaskField>) {
  props.onChange({ ...fieldOf(props), ...update });
}

function ChoiceProperties(props: StudioUserTaskFieldControlEditorProps) {
  const field = fieldOf(props);
  return <StudioField label="Choices, one per line"><textarea rows={4} value={field.choices?.join("\n") ?? ""} disabled={props.disabled}
    onChange={event => updateField(props, { choices: event.target.value.split("\n") })} /></StudioField>;
}

function NumberProperties(props: StudioUserTaskFieldControlEditorProps) {
  const field = fieldOf(props);
  return <><StudioField label="Minimum"><input type="number" value={field.minimum ?? ""} disabled={props.disabled}
    onChange={event => updateField(props, { minimum: event.target.value === "" ? undefined : Number(event.target.value) })} /></StudioField>
    <StudioField label="Maximum"><input type="number" value={field.maximum ?? ""} disabled={props.disabled}
      onChange={event => updateField(props, { maximum: event.target.value === "" ? undefined : Number(event.target.value) })} /></StudioField></>;
}

function TextProperties(props: StudioUserTaskFieldControlEditorProps) {
  const field = fieldOf(props);
  return <StudioField label="Maximum characters"><input type="number" min="1" step="1" value={field.maxLength ?? ""} disabled={props.disabled}
    onChange={event => updateField(props, { maxLength: event.target.value === "" ? undefined : Number(event.target.value) })} /></StudioField>;
}

function BooleanProperties(props: StudioUserTaskFieldControlEditorProps) {
  const field = fieldOf(props);
  const display = field.presentation?.display ?? "dropdown";
  return <>
    <StudioField label="Display"><select value={display} disabled={props.disabled} onChange={event => updateField(props, { presentation: { ...field.presentation, display: event.target.value as "checkbox" | "dropdown" | "radio" } })}>
      <option value="checkbox">Checkbox</option><option value="radio">Radio buttons</option><option value="dropdown">Dropdown</option>
    </select></StudioField>
    <label className="user-task-designer__checkbox"><input type="checkbox" checked={Boolean(field.mustBeTrue)} disabled={props.disabled}
      onChange={event => updateField(props, { mustBeTrue: event.target.checked || undefined })} />Must be checked</label>
    <p>Requires Yes for actions that require form answers. Optional actions may leave it unanswered.</p>
    {display !== "checkbox" ? <fieldset className="user-task-designer__answer-labels"><legend>Answer labels</legend>
      {(["trueLabel", "falseLabel", "unansweredLabel"] as const).map((key, index) => {
        const labels = ["Yes label", "No label", "Unanswered label"];
        const placeholders = ["Yes", "No", "Choose…"];
        return <StudioField key={key} label={labels[index]}><input value={field.presentation?.[key] ?? ""} placeholder={placeholders[index]} maxLength={256} disabled={props.disabled}
          onChange={event => updateField(props, { presentation: { display, ...field.presentation, [key]: event.target.value || undefined } })} /></StudioField>;
      })}</fieldset> : null}
  </>;
}

export const builtInTaskFieldControls: StudioUserTaskFieldControlContribution[] = [
  { id: "elsa.forms.text/1", legacyKind: "text", label: "Text input", valueKind: "string", icon: <Type size={16} aria-hidden="true" />, propertiesLabel: "Validation", createField: fields => createField("text", fields), component: TextControl, propertiesEditor: TextProperties },
  { id: "elsa.forms.multiline/1", legacyKind: "multiline", label: "Long text", valueKind: "string", icon: <AlignLeft size={16} aria-hidden="true" />, propertiesLabel: "Validation", createField: fields => createField("multiline", fields), component: MultilineControl, propertiesEditor: TextProperties },
  { id: "elsa.forms.number/1", legacyKind: "number", label: "Number", valueKind: "number", icon: <Sigma size={16} aria-hidden="true" />, propertiesLabel: "Validation", createField: fields => createField("number", fields), component: NumberControl, propertiesEditor: NumberProperties },
  { id: "elsa.forms.boolean/1", legacyKind: "boolean", label: "Yes / no", valueKind: "boolean", icon: <CheckSquare size={16} aria-hidden="true" />, propertiesLabel: "Validation", ownsFieldLabel: true, createField: fields => createField("boolean", fields), component: BooleanControl, propertiesEditor: BooleanProperties },
  { id: "elsa.forms.date/1", legacyKind: "date", label: "Date", valueKind: "string", icon: <CalendarDays size={16} aria-hidden="true" />, createField: fields => createField("date", fields), component: DateControl },
  { id: "elsa.forms.choice/1", legacyKind: "choice", label: "Choice", valueKind: "string", icon: <List size={16} aria-hidden="true" />, propertiesLabel: "Choices", ownsFieldLabel: true, createField: fields => createField("choice", fields), component: ChoiceControl, propertiesEditor: ChoiceProperties }
];

export type TaskFieldControlSource = StudioContributionRegistry<StudioUserTaskFieldControlContribution> | readonly StudioUserTaskFieldControlContribution[];
export interface ResolvedTaskFieldControl { status: "available" | "missing" | "ambiguous" | "unavailable"; control?: StudioUserTaskFieldControlContribution; reason?: string }

export function listAvailableTaskFieldControls(source: TaskFieldControlSource = builtInTaskFieldControls) {
  const all = Array.isArray(source)
    ? source.map(contribution => ({ contribution, availability: { state: "available" as const } }))
    : (source as StudioContributionRegistry<StudioUserTaskFieldControlContribution>).compose({ includeUnavailable: true, includeHidden: true });
  const counts = new Map<string, number>();
  for (const item of all) counts.set(item.contribution.id, (counts.get(item.contribution.id) ?? 0) + 1);
  return all.filter(item => item.availability.state === "available" && counts.get(item.contribution.id) === 1 && controlIdentityPattern.test(item.contribution.id)).map(item => item.contribution);
}

function compositions(source: TaskFieldControlSource): Pick<StudioContributionComposition<StudioUserTaskFieldControlContribution>, "contribution" | "availability">[] {
  if (Array.isArray(source)) return source.map(contribution => ({ contribution, availability: { state: "available" } }));
  return (source as StudioContributionRegistry<StudioUserTaskFieldControlContribution>).compose({ includeUnavailable: true, includeHidden: true });
}

export function resolveTaskFieldControl(field: Pick<TaskField, "kind" | "controlId">, source: TaskFieldControlSource = builtInTaskFieldControls): ResolvedTaskFieldControl {
  const all = compositions(source);
  let id = field.controlId;
  if (!id) {
    const legacyMatches = all.filter(item => item.contribution.legacyKind === field.kind);
    if (legacyMatches.length > 1) return { status: "ambiguous", reason: `More than one field control claims legacy field kind “${field.kind}”.` };
    id = legacyMatches[0]?.contribution.id;
  }
  if (!id) return { status: "missing", reason: `No field control is registered for “${field.kind}”.` };
  if (!controlIdentityPattern.test(id)) return { status: "unavailable", reason: `Field control identity “${id}” must be namespaced and versioned.` };
  const matches = all.filter(item => item.contribution.id === id);
  if (!matches.length) return { status: "missing", reason: `Field control “${id}” is unavailable. Install the module that provides it.` };
  if (matches.length !== 1) return { status: "ambiguous", reason: `More than one module registered field control “${id}”.` };
  const [match] = matches;
  if (match.availability.state !== "available") return { status: "unavailable", reason: match.availability.reason ?? `Field control “${id}” is blocked by the host.` };
  if (field.controlId && field.kind !== match.contribution.valueKind) return { status: "unavailable", reason: `Field control “${id}” requires ${match.contribution.valueKind} values, but this field declares ${field.kind}.` };
  return { status: "available", control: match.contribution };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function configurationDepth(value: unknown, depth = 0): number {
  if (depth > configurationDepthLimit) return depth;
  if (Array.isArray(value)) return Math.max(depth, ...value.map(item => configurationDepth(item, depth + 1)));
  if (isRecord(value)) return Math.max(depth, ...Object.values(value).map(item => configurationDepth(item, depth + 1)));
  return depth;
}

export function validateTaskFieldControlConfiguration(control: StudioUserTaskFieldControlContribution, configuration: unknown): string[] {
  const shapeErrors = validateTaskFieldConfigurationShape(configuration);
  if (shapeErrors.length) return shapeErrors;
  return control.validateConfiguration?.(configuration) ?? [];
}

export function validateTaskFieldConfigurationShape(configuration: unknown): string[] {
  if (configuration === undefined) return [];
  if (!isRecord(configuration)) return ["Control configuration must be a JSON object."];
  let serialized: string;
  try { serialized = JSON.stringify(configuration); } catch { return ["Control configuration must be valid JSON."]; }
  if (serialized.length > configurationLimit) return ["Control configuration exceeds the supported size limit."];
  if (configurationDepth(configuration) > configurationDepthLimit) return ["Control configuration exceeds the supported nesting limit."];
  return [];
}