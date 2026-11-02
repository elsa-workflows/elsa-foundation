import { qualifyStudioBackendPath } from "@elsa-workflows/studio-sdk";
import React, { useEffect, useState } from "react";
import type { ElsaStudioModuleApi, StudioActivityPropertyEditorProps } from "@elsa-workflows/studio-sdk";
import { StudioButton } from "@elsa-workflows/studio-ui";
import { forms, targets, message, type FormVersion } from "./taskApi";
export function ActionsEditor({ value, disabled, onChange, onValidationChange }: StudioActivityPropertyEditorProps) {
  const items = Array.isArray(value) ? value.map(String) : [];
  const invalid = !items.length || items.some(item => !item.trim() || item !== item.trim() || item === "Unmatched") || new Set(items).size !== items.length;
  useEffect(() => { onValidationChange?.(invalid); return () => onValidationChange?.(false); }, [invalid, onValidationChange]);
  return <div>{items.map((item, index) => <div className="user-task-actions" key={index}><label>Action {index + 1}<input disabled={disabled} value={item} onChange={event => onChange(items.map((entry, i) => i === index ? event.target.value : entry))} /></label>
    <StudioButton disabled={disabled} onClick={() => onChange(items.filter((_, i) => i !== index))}>Remove action {index + 1}</StudioButton></div>)}
    <StudioButton disabled={disabled || items.length >= 20} onClick={() => onChange([...items, ""])}>Add action</StudioButton>
    {invalid ? <p role="alert">Enter unique, nonempty action names without surrounding spaces.</p> : null}</div>;
}
export function AssignmentEditor({ api, group, value, disabled, onChange }: StudioActivityPropertyEditorProps & { api: ElsaStudioModuleApi; group: boolean }) {
  const [items, setItems] = useState<{ id: string; name: string }[]>([]); const [error, setError] = useState(""); const [skip, setSkip] = useState(0); const [more, setMore] = useState(false);
  useEffect(() => { let active = true; void targets(api, skip).then(page => { if (active) { setItems(previous => skip ? [...previous, ...(group ? page.groups : page.users)] : group ? page.groups : page.users); setMore(group ? page.moreGroups : page.moreUsers); setError(""); } }, failure => { if (active) setError(message(failure)); }); return () => { active = false; }; }, [api, group, skip]);
  return <div><select disabled={disabled} aria-label={group ? "Task group" : "Task assignee"} value={typeof value === "string" ? value : ""} onChange={event => onChange(event.target.value || null)}>
    <option value="">None</option>{value && !items.some(item => item.id === value) ? <option value={String(value)}>{String(value)}</option> : null}{items.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select>
    {more ? <StudioButton disabled={disabled} onClick={() => setSkip(skip + 100)}>More choices</StudioButton> : null}{error ? <p role="alert">{error}</p> : null}</div>;
}
export function FormEditor({ api, value, disabled, onChange }: StudioActivityPropertyEditorProps & { api: ElsaStudioModuleApi }) {
  const [items, setItems] = useState<FormVersion[]>([]); const [error, setError] = useState(""); const [nextPage, setNextPage] = useState<string | null>(null);
  useEffect(() => { let active = true; void forms(api).then(next => { if (active) { setItems(next.items); setNextPage(next.next ?? null); } }, failure => { if (active) setError(message(failure)); }); return () => { active = false; }; }, [api]);
  const ids = [...new Set(items.map(item => item.form.id))];
  return <div><select disabled={disabled} aria-label="Task form" value={typeof value === "string" ? value : ""} onChange={event => onChange(event.target.value || null)}><option value="">No form</option>
    {value && !ids.includes(String(value)) ? <option value={String(value)}>{String(value)}</option> : null}{ids.map(id => <option key={id} value={id}>{items.find(item => item.form.id === id)?.form.name} ({id})</option>)}</select>
    {nextPage ? <StudioButton disabled={disabled} onClick={() => { void forms(api, nextPage).then(page => { setItems(previous => [...previous, ...page.items]); setNextPage(page.next ?? null); }, failure => setError(message(failure))); }}>More forms</StudioButton> : null}
    <p>Select the exact published version in Form Version. <a href={qualifyStudioBackendPath("/user-tasks/forms")}>Manage reusable forms</a></p>{error ? <p role="alert">{error}</p> : null}</div>;
}
