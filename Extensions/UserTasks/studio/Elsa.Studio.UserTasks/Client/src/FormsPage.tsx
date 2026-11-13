import { qualifyStudioBackendPath } from "@elsa-workflows/studio-sdk";
import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { SkeletonRows, StudioAlert, StudioButton, StudioCollectionFooter, StudioField, StudioPage, StudioPageHeader, StudioResultState, StudioTableContainer } from "@elsa-workflows/studio-ui";
import { RemoveIcon } from "./PropertyEditors";
import { actor, basicProvider, createForm, forms, message, statusOf, type FormVersion, type TaskActor, type TaskField, type TaskForm } from "./taskApi";
const initial = (): TaskForm => ({ id: "", version: "1", name: "", provider: basicProvider, fields: [] });
const versionNumber = (version: string) => /^\d+$/.test(version) ? Number(version) : 0;
/** The highest loaded version of each form; only that one offers "Create next version". */
function latestVersions(versions: FormVersion[]) {
  const latest = new Map<string, number>();
  for (const item of versions) latest.set(item.form.id, Math.max(latest.get(item.form.id) ?? 0, versionNumber(item.form.version)));
  return latest;
}
export function FormsPage({ api }: { api: ElsaStudioModuleApi }) {
  const owner = useMemo(() => ({ api }), [api]);
  const currentOwner = useRef(owner); currentOwner.current = owner;
  const generation = useRef(0);
  useEffect(() => () => { generation.current++; }, [api]);
  const [versions, setVersions] = useState<FormVersion[]>([]); const [nextPage, setNextPage] = useState<string | null>(null); const [identity, setIdentity] = useState<TaskActor>();
  const [formError, setFormError] = useState(""); const [versionConflict, setVersionConflict] = useState(false);
  const returnKey = useRef<string>(""); const returnButton = useRef<HTMLButtonElement>(null);
  const [opened, setOpened] = useState<FormVersion | null>(null);
  const [editing, setEditing] = useState(false); const [loading, setLoading] = useState(true);
  const previewHeading = useRef<HTMLHeadingElement>(null);
  useEffect(() => { if (opened) previewHeading.current?.focus(); }, [opened]);
  const createButton = useRef<HTMLButtonElement>(null); const editor = useRef<HTMLFieldSetElement>(null);
  useEffect(() => { if (editing) editor.current?.querySelector<HTMLInputElement>("input")?.focus(); }, [editing]);
  const closeEditor = (allowFocus = true) => {
    const focused = document.activeElement;
    const restoreFocus = allowFocus && (focused === document.body || Boolean(editor.current?.contains(focused)));
    setFormError(""); setEditing(false); setForm(initial());
    requestAnimationFrame(() => {
      if (restoreFocus && (document.activeElement === document.body || document.activeElement === focused)) createButton.current?.focus();
    });
  };
  const [form, setForm] = useState(initial); const [error, setError] = useState(""); const [busy, setBusy] = useState(false);
  const refresh = useCallback(async () => {
    try {
      const [who, all] = await Promise.all([actor(api), forms(api)]);
      if (currentOwner.current !== owner) return;
      setIdentity(who); setVersions(all.items); setNextPage(all.next ?? null); setError("");
    } catch (failure) { if (currentOwner.current === owner) setError(message(failure)); }
    finally { if (currentOwner.current === owner) setLoading(false); }
  }, [api, owner]);
  useEffect(() => { void refresh(); }, [refresh]);
  const availableVersion = async (id: string) => {
    const all: FormVersion[] = []; let cursor: string | null = null; const seen = new Set<string>();
    do {
      const page = await forms(api, cursor); all.push(...page.items); cursor = page.next ?? null;
      if (cursor && seen.has(cursor)) throw new Error("Form paging did not advance. Refresh forms and try again.");
      if (cursor) seen.add(cursor);
    } while (cursor);
    const numbers = all.filter(item => item.form.id === id).map(item => versionNumber(item.form.version)).filter(Number.isSafeInteger);
    return String(Math.max(0, ...numbers) + 1);
  };
  const recoverVersion = async () => {
    if (busy) return; setBusy(true);
    try { const version = await availableVersion(form.id); setForm(current => ({ ...current, version })); setVersionConflict(false); setFormError(""); }
    catch (failure) { setFormError(message(failure)); } finally { setBusy(false); }
  };
  const nextVersion = async (version: FormVersion) => {
    if (busy) return; setBusy(true); setFormError("");
    try {
      const next = await availableVersion(version.form.id);
      setForm({ ...structuredClone(version.form), version: next }); setOpened(null); setEditing(true);
    } catch (failure) { setError(message(failure)); } finally { setBusy(false); }
  };
  const field = (index: number, update: Partial<TaskField>) => setForm(value => ({ ...value, fields: value.fields.map((entry, i) => i === index ? { ...entry, ...update } : entry) }));
  const save = async () => {
    if (busy) return; setBusy(true); setFormError("");
    const operationGeneration = generation.current;
    try {
      await createForm(api, form);
      if (currentOwner.current !== owner) return;
      if (generation.current === operationGeneration) api.notifications?.notify({ level: "success", title: "Form version published.", message: "The immutable form version is available to new tasks." });
      await refresh();
      if (currentOwner.current === owner) closeEditor(generation.current === operationGeneration);
    }
    catch (failure) { if (currentOwner.current === owner) { setFormError(message(failure)); setVersionConflict(statusOf(failure) === 409); } } finally { if (currentOwner.current === owner) setBusy(false); }
  };
  const latest = latestVersions(versions);
  const isLatest = (version: FormVersion) => versionNumber(version.form.version) >= (latest.get(version.form.id) ?? 0);
  const canVersion = (version: FormVersion) => Boolean(identity?.canManageForms) && version.form.provider === basicProvider && isLatest(version);
  const valid = form.id.trim() && form.version.trim() && form.name.trim() && form.fields.every(item => item.key.trim() && item.label.trim()) && new Set(form.fields.map(item => item.key)).size === form.fields.length;
  return <StudioPage className="user-tasks" aria-label="Reusable task forms management"><StudioPageHeader title={editing ? "New form version" : opened ? opened.form.name : "Reusable task forms"} description="Manage published task forms. Selecting a form in a workflow pins its current version automatically." actions={<>
      {!editing && !opened && identity?.canManageForms && <StudioButton ref={createButton} variant="primary" disabled={busy} onClick={() => { setFormError(""); setForm(initial()); setEditing(true); }}>Create form version</StudioButton>}
      <a className="studio-button" data-variant="ghost" data-size="md" href={qualifyStudioBackendPath("/user-tasks")}>Task inbox</a></>} />
    {error ? <StudioResultState tone="danger" title="Forms unavailable" description={error} actions={<StudioButton onClick={() => void refresh()}>Retry</StudioButton>} /> : null}
    {formError && editing ? <StudioAlert tone="danger">{formError}{versionConflict && <StudioButton disabled={busy} onClick={() => void recoverVersion()}>Use next available version</StudioButton>}</StudioAlert> : null}
    {!editing && !opened && <>
      <StudioTableContainer role="region" aria-label="Reusable form versions" tabIndex={0}>
        {loading ? <SkeletonRows rows={3} label="Loading form versions" /> : versions.length ? <table className="studio-table user-task-table user-task-forms-table" aria-label="Reusable task forms">
          <thead><tr><th scope="col">Form</th><th scope="col">Identifier</th><th scope="col">Version</th>{identity?.canManageForms && <th scope="col">Actions</th>}</tr></thead>
          <tbody>{versions.map(version => <tr key={`${version.form.id}/${version.form.version}`}><td data-label="Form"><button type="button" className="user-task-link" disabled={busy} ref={node => { if (returnKey.current === `${version.form.id}/${version.form.version}`) returnButton.current = node; }} onClick={() => { returnKey.current = `${version.form.id}/${version.form.version}`; setOpened(version); }}>{version.form.name}</button></td><td data-label="Identifier">{version.form.id}</td><td data-label="Version">{version.form.version}</td>
            {identity?.canManageForms && <td data-label="Actions" data-empty={!canVersion(version) || undefined}>{canVersion(version) && <StudioButton disabled={busy} onClick={() => void nextVersion(version)}>Create next version</StudioButton>}</td>}</tr>)}</tbody>
        </table> : !error && <StudioResultState title="No reusable forms yet" description="Reusable forms define the fields people fill in when responding to a task."
          actions={identity?.canManageForms ? <StudioButton variant="primary" disabled={busy} onClick={() => { setFormError(""); setForm(initial()); setEditing(true); }}>Create form version</StudioButton> : undefined} />}
      </StudioTableContainer>
      {!loading && !error && <StudioCollectionFooter><span>{versions.length} form {versions.length === 1 ? "version" : "versions"} shown</span>
        {nextPage && <StudioButton disabled={busy} onClick={() => { setBusy(true); void forms(api, nextPage).then(page => { setVersions(previous => [...previous, ...page.items]); setNextPage(page.next ?? null); }, failure => setError(message(failure))).finally(() => setBusy(false)); }}>More form versions</StudioButton>}
      </StudioCollectionFooter>}
    </>}
    {opened && !editing && <section className="user-task-form-preview" aria-label="Published form">
      <div className="user-task-detail-header"><div><h2 ref={previewHeading} tabIndex={-1}>Published version {opened.form.version}</h2><p>{opened.form.id}</p></div>
        <div className="user-task-actions">
          {canVersion(opened) && <StudioButton variant="primary" disabled={busy} onClick={() => void nextVersion(opened)}>Create next version</StudioButton>}
          <StudioButton disabled={busy} onClick={() => { setOpened(null); requestAnimationFrame(() => returnButton.current?.focus()); }}>Back to forms</StudioButton>
        </div>
      </div>
      <p>This published form is immutable. {isLatest(opened) ? "Create a new version to change its fields." : `Version ${latest.get(opened.form.id)} is the latest version of this form.`} Open tasks keep this version.</p>
      {opened.form.fields.length ? <dl className="user-task-form-fields">{opened.form.fields.map(item => <div key={item.key}><dt>{item.label}</dt><dd>{item.kind}{item.required ? " · Required" : ""}{item.readOnly ? " · Read only" : ""}</dd><dd><code>{item.key}</code>{item.help && <p>{item.help}</p>}
          {item.choices && <p>Choices: {item.choices.join(", ")}</p>}
          {item.minimum != null && <p>Minimum: {item.minimum}</p>}{item.maximum != null && <p>Maximum: {item.maximum}</p>}
          {item.maxLength != null && <p>Maximum characters: {item.maxLength}</p>}
        </dd></div>)}</dl> : <p>This form has no fields.</p>}
    </section>}
    {editing && identity?.canManageForms ? <fieldset ref={editor} disabled={busy} className="user-task-form-editor"><legend>Publish a basic form version</legend><div className="user-task-fields">
      <StudioField label="Form ID"><input value={form.id} onChange={event => setForm({ ...form, id: event.target.value })} /></StudioField>
      <StudioField label="Version" description="Assigned automatically for a new form version."><output className="user-task-version-value">{form.version}</output></StudioField>
      <StudioField label="Name"><input value={form.name} onChange={event => setForm({ ...form, name: event.target.value })} /></StudioField></div>
      {form.fields.map((item, index) => <fieldset key={index} className="user-task-field-author"><legend>Field {index + 1}</legend>
        <StudioField label="Key"><input value={item.key} onChange={event => field(index, { key: event.target.value })} /></StudioField>
        <StudioField label="Label"><input value={item.label} onChange={event => field(index, { label: event.target.value })} /></StudioField>
        <StudioField label="Type"><select value={item.kind} onChange={event => field(index, { kind: event.target.value, minimum: undefined, maximum: undefined, maxLength: undefined, choices: event.target.value === "choice" ? ["Option 1"] : undefined })}>
          {["text", "multiline", "number", "boolean", "date", "choice"].map(kind => <option key={kind}>{kind}</option>)}</select></StudioField>
        <label><input type="checkbox" checked={item.required} onChange={event => field(index, { required: event.target.checked })} /> Required</label>
        <label><input type="checkbox" checked={item.readOnly} onChange={event => field(index, { readOnly: event.target.checked })} /> Read only</label>
        <StudioField label="Help text"><input value={item.help ?? ""} onChange={event => field(index, { help: event.target.value })} /></StudioField>
        {item.kind === "choice" ? <StudioField label="Choices, one per line"><textarea value={item.choices?.join("\n") ?? ""} onChange={event => field(index, { choices: event.target.value.split("\n") })} /></StudioField> : null}
        {item.kind === "number" ? <><StudioField label="Minimum"><input type="number" value={item.minimum ?? ""} onChange={event => field(index, { minimum: event.target.value === "" ? undefined : Number(event.target.value) })} /></StudioField>
          <StudioField label="Maximum"><input type="number" value={item.maximum ?? ""} onChange={event => field(index, { maximum: event.target.value === "" ? undefined : Number(event.target.value) })} /></StudioField></> : null}
        {item.kind === "text" || item.kind === "multiline" ? <StudioField label="Maximum characters"><input type="number" min="1" value={item.maxLength ?? ""} onChange={event => field(index, { maxLength: event.target.value === "" ? undefined : Number(event.target.value) })} /></StudioField> : null}
        <StudioButton variant="ghost" aria-label={`Remove field ${index + 1}`} title={`Remove field ${index + 1}`} onClick={() => setForm({ ...form, fields: form.fields.filter((_, i) => i !== index) })}><RemoveIcon /></StudioButton>
      </fieldset>)}
      <div className="user-task-actions"><StudioButton disabled={form.fields.length >= 100} onClick={() => setForm({ ...form, fields: [...form.fields, { key: "", label: "", kind: "text", required: false, readOnly: false }] })}>Add field</StudioButton>
        <StudioButton variant="primary" disabled={!valid || busy} onClick={() => void save()}>Publish form version</StudioButton><StudioButton onClick={() => closeEditor()}>Cancel</StudioButton></div>
      <p>Existing tasks keep their original form. Publishing a changed form requires a new version.</p></fieldset> : null}
  </StudioPage>;
}
