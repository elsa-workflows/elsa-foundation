import { qualifyStudioBackendPath } from "@elsa-workflows/studio-sdk";
import React, { useCallback, useEffect, useState } from "react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { StudioButton, StudioPage, StudioPageHeader, StudioResultState } from "@elsa-workflows/studio-ui";
import { actor, basicProvider, createForm, forms, message, type FormVersion, type TaskActor, type TaskField, type TaskForm } from "./taskApi";
const initial = (): TaskForm => ({ id: "", version: "1", name: "", provider: basicProvider, fields: [] });
export function FormsPage({ api }: { api: ElsaStudioModuleApi }) {
  const [versions, setVersions] = useState<FormVersion[]>([]); const [nextPage, setNextPage] = useState<string | null>(null); const [identity, setIdentity] = useState<TaskActor>();
  const [form, setForm] = useState(initial); const [error, setError] = useState(""); const [notice, setNotice] = useState(""); const [busy, setBusy] = useState(false);
  const refresh = useCallback(async () => { try { const [who, all] = await Promise.all([actor(api), forms(api)]); setIdentity(who); setVersions(all.items); setNextPage(all.next ?? null); setError(""); } catch (failure) { setError(message(failure)); } }, [api]);
  useEffect(() => { void refresh(); }, [refresh]);
  const field = (index: number, update: Partial<TaskField>) => setForm(value => ({ ...value, fields: value.fields.map((entry, i) => i === index ? { ...entry, ...update } : entry) }));
  const save = async () => {
    if (busy) return; setBusy(true); setNotice("");
    try { await createForm(api, form); setNotice("The immutable form version is available to new tasks."); await refresh(); }
    catch (failure) { setError(message(failure)); } finally { setBusy(false); }
  };
  const valid = form.id.trim() && form.version.trim() && form.name.trim() && form.fields.every(item => item.key.trim() && item.label.trim()) && new Set(form.fields.map(item => item.key)).size === form.fields.length;
  return <StudioPage className="user-tasks"><StudioPageHeader title="Reusable task forms" description="Publish a field definition once and select its exact version in a User Task." actions={<a href={qualifyStudioBackendPath("/user-tasks")}>Task inbox</a>} />
    {error ? <div role="alert"><StudioResultState tone="danger" title="Forms unavailable" description={error} actions={<StudioButton onClick={() => void refresh()}>Retry</StudioButton>} /></div> : null}
    {notice ? <p role="status">{notice}</p> : null}
    <ul>{versions.map(version => <li key={`${version.form.id}/${version.form.version}`}><strong>{version.form.name}</strong> · {version.form.id} · version {version.form.version}
      {identity?.canManageForms && version.form.provider === basicProvider ? <StudioButton onClick={() => setForm({ ...structuredClone(version.form), version: "" })}>Create next version</StudioButton> : null}</li>)}</ul>
    {nextPage ? <StudioButton onClick={() => { void forms(api, nextPage).then(page => { setVersions(previous => [...previous, ...page.items]); setNextPage(page.next ?? null); }, failure => setError(message(failure))); }}>More form versions</StudioButton> : null}
    {identity?.canManageForms ? <fieldset disabled={busy}><legend>Publish a basic form version</legend><div className="user-task-fields">
      <label>Form ID<input value={form.id} onChange={event => setForm({ ...form, id: event.target.value })} /></label>
      <label>Version<input value={form.version} onChange={event => setForm({ ...form, version: event.target.value })} /></label>
      <label>Name<input value={form.name} onChange={event => setForm({ ...form, name: event.target.value })} /></label></div>
      {form.fields.map((item, index) => <fieldset key={index} className="user-task-field-author"><legend>Field {index + 1}</legend>
        <label>Key<input value={item.key} onChange={event => field(index, { key: event.target.value })} /></label>
        <label>Label<input value={item.label} onChange={event => field(index, { label: event.target.value })} /></label>
        <label>Type<select value={item.kind} onChange={event => field(index, { kind: event.target.value, minimum: undefined, maximum: undefined, maxLength: undefined, choices: event.target.value === "choice" ? ["Option 1"] : undefined })}>
          {["text", "multiline", "number", "boolean", "date", "choice"].map(kind => <option key={kind}>{kind}</option>)}</select></label>
        <label><input type="checkbox" checked={item.required} onChange={event => field(index, { required: event.target.checked })} /> Required</label>
        <label><input type="checkbox" checked={item.readOnly} onChange={event => field(index, { readOnly: event.target.checked })} /> Read only</label>
        <label>Help text<input value={item.help ?? ""} onChange={event => field(index, { help: event.target.value })} /></label>
        {item.kind === "choice" ? <label>Choices, one per line<textarea value={item.choices?.join("\n") ?? ""} onChange={event => field(index, { choices: event.target.value.split("\n") })} /></label> : null}
        {item.kind === "number" ? <><label>Minimum<input type="number" value={item.minimum ?? ""} onChange={event => field(index, { minimum: event.target.value === "" ? undefined : Number(event.target.value) })} /></label>
          <label>Maximum<input type="number" value={item.maximum ?? ""} onChange={event => field(index, { maximum: event.target.value === "" ? undefined : Number(event.target.value) })} /></label></> : null}
        {item.kind === "text" || item.kind === "multiline" ? <label>Maximum characters<input type="number" min="1" value={item.maxLength ?? ""} onChange={event => field(index, { maxLength: event.target.value === "" ? undefined : Number(event.target.value) })} /></label> : null}
        <StudioButton onClick={() => setForm({ ...form, fields: form.fields.filter((_, i) => i !== index) })}>Remove field {index + 1}</StudioButton>
      </fieldset>)}
      <div className="user-task-actions"><StudioButton disabled={form.fields.length >= 100} onClick={() => setForm({ ...form, fields: [...form.fields, { key: "", label: "", kind: "text", required: false, readOnly: false }] })}>Add field</StudioButton>
        <StudioButton disabled={!valid || busy} onClick={() => void save()}>Publish form version</StudioButton></div>
      <p>Existing tasks keep their original form. Publishing a changed form requires a new version.</p></fieldset> : null}
  </StudioPage>;
}
