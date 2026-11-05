import { qualifyStudioBackendPath } from "@elsa-workflows/studio-sdk";
import { Trash2 } from "lucide-react";
import React, { useCallback, useEffect, useRef, useState } from "react";
import type { ElsaStudioModuleApi, StudioActivityPropertyEditorProps } from "@elsa-workflows/studio-sdk";
import { StudioButton } from "@elsa-workflows/studio-ui";
import { forms, targets, message, type FormVersion } from "./taskApi";
export function ActionsEditor({ value, disabled, onChange, onValidationChange }: StudioActivityPropertyEditorProps) {
  let literal: unknown = value;
  if (typeof literal === "string") { try { literal = JSON.parse(literal); } catch { literal = null; } }
  const items = Array.isArray(literal) ? literal.map(String) : [];
  const invalid = !items.length || items.some(item => !item.trim() || item !== item.trim() || item === "Unmatched") || new Set(items).size !== items.length;
  useEffect(() => { onValidationChange?.(invalid); return () => onValidationChange?.(false); }, [invalid, onValidationChange]);
  return <div className="user-task-property-editor">{items.map((item, index) => <div className="user-task-action-row" key={index}><label>Action {index + 1}<input className="studio-input" disabled={disabled} value={item} onChange={event => onChange(items.map((entry, i) => i === index ? event.target.value : entry))} /></label>
    <StudioButton variant="ghost" aria-label={`Remove action ${index + 1}`} title={`Remove action ${index + 1}`} disabled={disabled} onClick={() => onChange(items.filter((_, i) => i !== index))}><RemoveIcon /></StudioButton></div>)}
    <StudioButton disabled={disabled || items.length >= 20} onClick={() => onChange([...items, ""])}>Add action</StudioButton>
    {invalid ? <p role="alert">Enter unique, nonempty action names without surrounding spaces.</p> : null}</div>;
}
export function AssignmentEditor({ api, group, value, disabled, onChange, context, onValidationChange }: StudioActivityPropertyEditorProps & { api: ElsaStudioModuleApi; group: boolean }) {
  const assignment = context.inputValues;
  const canValidate = assignment && Object.hasOwn(assignment, "AssigneeId") && Object.hasOwn(assignment, "GroupId");
  const hasUser = typeof assignment?.AssigneeId === "string" && Boolean(assignment.AssigneeId.trim());
  const hasGroup = typeof assignment?.GroupId === "string" && Boolean(assignment.GroupId.trim());
  const invalid = Boolean(canValidate && hasUser === hasGroup);
  useEffect(() => { onValidationChange?.(invalid); return () => onValidationChange?.(false); }, [invalid, onValidationChange]);
  const [items, setItems] = useState<{ id: string; name: string }[]>([]); const [error, setError] = useState("");
  const [skip, setSkip] = useState(0); const [more, setMore] = useState(false); const [loading, setLoading] = useState(false);
  const busy = useRef(false); const generation = useRef(0);
  const load = useCallback(async (offset: number, append: boolean) => {
    if (busy.current) return; busy.current = true; setLoading(true); const current = generation.current;
    try {
      const page = await targets(api, offset); if (current !== generation.current) return;
      const next = group ? page.groups : page.users;
      setItems(previous => append ? [...previous, ...next] : next); setMore(group ? page.moreGroups : page.moreUsers);
      setSkip(offset + 100); setError("");
    } catch (failure) { if (current === generation.current) setError(message(failure)); }
    finally { if (current === generation.current) { busy.current = false; setLoading(false); } }
  }, [api, group]);
  useEffect(() => {
    ++generation.current; busy.current = false; setItems([]); setMore(false); setSkip(0); void load(0, false);
    return () => { ++generation.current; busy.current = false; };
  }, [load]);
  return <div className="user-task-property-editor"><select className="studio-input" aria-invalid={invalid} disabled={disabled} aria-label={group ? "Task group" : "Task assignee"} value={typeof value === "string" ? value : ""} onChange={event => onChange(event.target.value || null)}>
    <option value="">None</option>{value && !items.some(item => item.id === value) ? <option value={String(value)}>{String(value)}</option> : null}{items.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select>
    {invalid && !group && <p role="alert">{hasUser ? "Choose either an assignee or a group; clear the other assignment." : "Select an assignee or a group for this task."}</p>}
    {more ? <StudioButton disabled={disabled || loading} onClick={() => void load(skip, true)}>More choices</StudioButton> : null}{error ? <p role="alert">{error}</p> : null}</div>;
}
function RemoveIcon() {
  return <Trash2 size={18} aria-hidden="true" />;
}
export { RemoveIcon };

export function FormEditor({ api, value, disabled, onChange, context, descriptor, onValidationChange }: StudioActivityPropertyEditorProps & { api: ElsaStudioModuleApi }) {
  const [items, setItems] = useState<FormVersion[]>([]); const [error, setError] = useState(""); const [loading, setLoading] = useState(true); const [reload, setReload] = useState(0);
  useEffect(() => {
    let active = true; setLoading(true); setError("");
    void (async () => {
      const loaded: FormVersion[] = []; let cursor: string | null = null; const seen = new Set<string>();
      do {
        const page = await forms(api, cursor); if (!active) return;
        loaded.push(...page.items); cursor = page.next ?? null;
        if (cursor && seen.has(cursor)) throw new Error("Form paging did not advance. Reload forms to try again.");
        if (cursor) seen.add(cursor);
      } while (cursor);
      if (active) setItems(loaded.sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt) || b.form.version.localeCompare(a.form.version, undefined, { numeric: true })));
    })().catch(failure => { if (active) setError(message(failure)); }).finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [api, reload]);
  const versionMode = descriptor.uiHint === "user-task-form-version" || descriptor.name === "FormVersion";
  const related = readFormId(context.activity);
  const dynamicForm = versionMode && related.dynamic;
  const formId = versionMode ? context.inputValues?.FormId ?? related.value : value;
  const selected = typeof value === "string" ? value : "";
  const versions = items.filter(item => item.form.id === formId);
  const ids = [...new Set(items.map(item => item.form.id))];
  const options = versionMode ? versions.map(item => ({ value: item.form.version, label: `Version ${item.form.version}` })) : ids.map(id => ({ value: id, label: `${items.find(item => item.form.id === id)!.form.name} (${id})` }));
  const unavailable = Boolean(selected && !options.some(item => item.value === selected));
  const invalid = !loading && !error && !dynamicForm && (versionMode ? Boolean(formId && (!selected || unavailable)) : unavailable);
  useEffect(() => { onValidationChange?.(invalid); return () => onValidationChange?.(false); }, [invalid, onValidationChange]);
  if (dynamicForm) return <div className="user-task-property-editor"><input className="studio-input" aria-label="Task form version" value={selected} disabled={disabled} onChange={event => onChange(event.target.value || null)} /><p>The form ID is an expression. Enter the exact immutable version that expression will resolve.</p></div>;
  return <div className="user-task-property-editor">
    <select className="studio-input" disabled={disabled || loading || Boolean(error) || (versionMode && !formId)} aria-label={versionMode ? "Task form version" : "Task form"} value={selected} aria-invalid={invalid} onChange={event => {
      const id = event.target.value;
      if (versionMode) onChange(id || null);
      else if (context.setLiteralInputs) context.setLiteralInputs({ FormId: id || null, FormVersion: items.find(item => item.form.id === id)?.form.version ?? null });
      else onChange(id || null);
    }}>
      <option value="">{loading ? "Loading forms…" : versionMode ? "Select a published version" : "No form"}</option>
      {unavailable && <option value={selected}>{selected}{!loading && !error ? " (unavailable)" : ""}</option>}
      {options.map(item => <option key={item.value} value={item.value}>{item.label}</option>)}
    </select>
    {versionMode ? <p>Published versions stay fixed. Open tasks keep their original form.</p> : <>
      <p>Selecting a form pins its latest published version.</p>
      <a className="studio-button" data-variant="secondary" data-size="sm" href={qualifyStudioBackendPath("/user-tasks/forms")}>Manage forms</a>
      {!context.setLiteralInputs && <p role="status">Choose a published version below to finish selecting this form.</p>}
    </>}
    {error && <div role="alert"><p>{error}</p><StudioButton disabled={disabled} onClick={() => setReload(value => value + 1)}>Retry loading forms</StudioButton></div>}
    {invalid && <p role="alert">The selected {versionMode ? "version" : "form"} is unavailable. Choose a published form and version.</p>}
  </div>;
}

function readFormId(activity: unknown): { value: unknown; dynamic: boolean } {
  if (!activity || typeof activity !== "object") return { value: null, dynamic: false };
  const record = activity as Record<string, unknown>;
  const wire = Array.isArray(record.inputs) ? record.inputs.find(item => item?.referenceKey === "FormId") : undefined;
  const raw = record.FormId ?? record.formId ?? wire?.value;
  if (raw == null || typeof raw === "string") return { value: raw, dynamic: false };
  if (typeof raw !== "object") return { value: null, dynamic: true };
  const wrapped = raw as Record<string, unknown>;
  const expression = (wrapped.expression ?? wrapped) as Record<string, unknown>;
  const syntax = expression.type ?? expression.expressionType;
  return { value: expression.value, dynamic: syntax !== "Literal" };
}
