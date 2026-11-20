import { qualifyStudioBackendPath } from "@elsa-workflows/studio-sdk";
import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { studioRowInteraction, SkeletonRows, StudioAlert, StudioButton, StudioCollectionFooter, StudioField, StudioPage, StudioPageHeader, StudioResultState, StudioTableContainer } from "@elsa-workflows/studio-ui";
import { FormDesigner } from "./FormDesigner";
import { validateTaskForm } from "./formDesignerModel";
import { actor, basicProvider, createForm, forms, message, statusOf, type FormVersion, type TaskActor, type TaskForm } from "./taskApi";
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
  const validation = validateTaskForm(form);
  const valid = validation.length === 0;
  return <StudioPage className="user-tasks" aria-label="Reusable task forms management"><StudioPageHeader title={editing ? "New form version" : opened ? opened.form.name : "Reusable task forms"} description="Manage published task forms. Selecting a form in a workflow pins its current version automatically." actions={<>
      {!editing && !opened && identity?.canManageForms && <StudioButton ref={createButton} variant="primary" disabled={busy} onClick={() => { setFormError(""); setForm(initial()); setEditing(true); }}>Create form version</StudioButton>}
      <a className="studio-button" data-variant="ghost" data-size="md" href={qualifyStudioBackendPath("/user-tasks")}>Task inbox</a></>} />
    {error ? <StudioResultState tone="danger" title="Forms unavailable" description={error} actions={<StudioButton onClick={() => void refresh()}>Retry</StudioButton>} /> : null}
    {formError && editing ? <StudioAlert tone="danger">{formError}{versionConflict && <StudioButton disabled={busy} onClick={() => void recoverVersion()}>Use next available version</StudioButton>}</StudioAlert> : null}
    {!editing && !opened && <>
      <StudioTableContainer role="region" aria-label="Reusable form versions" tabIndex={0}>
        {loading ? <SkeletonRows rows={3} label="Loading form versions" /> : versions.length ? <table className="studio-table user-task-table user-task-forms-table" aria-label="Reusable task forms">
          <thead><tr><th scope="col">Form</th><th scope="col">Identifier</th><th scope="col">Version</th>{identity?.canManageForms && <th scope="col">Actions</th>}</tr></thead>
          <tbody>{versions.map(version => {
            const key = `${version.form.id}/${version.form.version}`;
            const open = () => {
              returnKey.current = key;
              setOpened(version);
            };
            return <tr key={key} {...studioRowInteraction<HTMLTableRowElement>(busy ? undefined : open)}>
              <td data-label="Form"><button type="button" className="user-task-link studio-row-link" disabled={busy} ref={node => { if (returnKey.current === key) returnButton.current = node; }} onClick={open}>{version.form.name}</button></td>
              <td data-label="Identifier">{version.form.id}</td>
              <td data-label="Version">{version.form.version}</td>
              {identity?.canManageForms && <td data-label="Actions" data-empty={!canVersion(version) || undefined}>{canVersion(version) && <StudioButton disabled={busy} onClick={() => void nextVersion(version)}>Create next version</StudioButton>}</td>}
            </tr>;
          })}</tbody>
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
    {editing && identity?.canManageForms ? <fieldset ref={editor} disabled={busy} className="user-task-form-editor"><legend>Design and publish a form version</legend><div className="user-task-fields">
      <StudioField label="Form ID"><input value={form.id} aria-invalid={!form.id.trim()} onChange={event => setForm({ ...form, id: event.target.value })} /></StudioField>
      <StudioField label="Version" description="Assigned automatically for a new form version."><output className="user-task-version-value">{form.version}</output></StudioField>
      <StudioField label="Name"><input value={form.name} aria-invalid={!form.name.trim()} onChange={event => setForm({ ...form, name: event.target.value })} /></StudioField></div>
      {validation.length ? <StudioAlert tone="danger">{validation.join(" ")}</StudioAlert> : null}
      <FormDesigner fields={form.fields} onFieldsChange={fields => setForm(value => ({ ...value, fields }))} disabled={busy} />
      <div className="user-task-actions"><StudioButton variant="primary" disabled={!valid || busy} onClick={() => void save()}>Publish form version</StudioButton><StudioButton onClick={() => closeEditor()}>Cancel</StudioButton></div>
      <p>Existing tasks keep their original form. Publishing a changed form requires a new version.</p></fieldset> : null}
  </StudioPage>;
}
