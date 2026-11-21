import { qualifyStudioBackendPath } from "@elsa-workflows/studio-sdk";
import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { studioRowInteraction, SkeletonRows, StatusChip, StudioAlert, StudioButton, StudioCollectionEmptyState, StudioCollectionFooter, StudioPage, StudioPageHeader, StudioResultState, StudioTableContainer, StudioVersionHistory } from "@elsa-workflows/studio-ui";
import { FormDesigner } from "./FormDesigner";
import { validateTaskForm } from "./formDesignerModel";
import { actor, basicProvider, createForm, forms, formsPageMode, formsPageModeFromSession, message, statusOf, type FormVersion, type FormsPageMode, type TaskActor, type TaskForm } from "./taskApi";
import { FormLifecyclePage } from "./FormsPageLifecycle";
import { FormWorkspaceTabs, PublishedFormSnapshot, type FormWorkspaceTab } from "./FormVersionWorkspace";
const initial = (): TaskForm => ({ id: "", version: "1", name: "", provider: basicProvider, fields: [] });
const versionNumber = (version: string) => /^\d+$/.test(version) ? Number(version) : 0;
const formIdentity = (version: FormVersion) => `${version.tenantId}/${version.form.id}`;
/** The highest loaded version of each form; only that one appears in the collection. */
function latestVersions(versions: FormVersion[]) {
  const latest = new Map<string, FormVersion>();
  for (const item of versions) {
    const key = formIdentity(item);
    const current = latest.get(key);
    if (!current || versionNumber(item.form.version) > versionNumber(current.form.version)) latest.set(key, item);
  }
  return latest;
}
function appendUniqueVersions(current: FormVersion[], incoming: FormVersion[]) {
  const known = new Set(current.map(item => `${item.tenantId}/${item.form.id}/${item.form.version}`));
  return [...current, ...incoming.filter(item => {
    const key = `${item.tenantId}/${item.form.id}/${item.form.version}`;
    if (known.has(key)) return false;
    known.add(key);
    return true;
  })];
}
export function FormsPage({ api }: { api: ElsaStudioModuleApi }) {
  const [discovery, setDiscovery] = useState<{ api: ElsaStudioModuleApi; attempt: number; mode: "loading" | FormsPageMode; error: string }>();
  const [attempt, setAttempt] = useState(0);
  const sessionMode = formsPageModeFromSession(api);
  useEffect(() => {
    if (sessionMode !== null) return;
    let active = true;
    setDiscovery({ api, attempt, mode: "loading", error: "" });
    void formsPageMode(api).then(value => { if (active) setDiscovery({ api, attempt, mode: value, error: "" }); }).catch(failure => {
      if (active) setDiscovery({ api, attempt, mode: "unavailable", error: message(failure) });
    });
    return () => { active = false; };
  }, [api, attempt, sessionMode]);
  const currentDiscovery = discovery?.api === api && discovery.attempt === attempt ? discovery : undefined;
  const mode = sessionMode ?? currentDiscovery?.mode ?? "loading";
  const error = sessionMode === null ? currentDiscovery?.error ?? "" : "";
  if (mode === "lifecycle") return <FormLifecyclePage api={api} />;
  if (mode === "legacy") return <LegacyFormsPage api={api} />;
  return <StudioPage className="user-tasks" aria-label="Reusable task forms management">
    <StudioPageHeader title="Reusable task forms" description="Manage reusable task forms and their published versions."
      actions={<a className="studio-button" data-variant="ghost" data-size="md" href={qualifyStudioBackendPath("/user-tasks")}>Task inbox</a>} />
    {mode === "loading" ? <div role="status">Loading forms…</div> : <StudioResultState tone="danger" title="Forms unavailable"
      description={error || "This backend does not advertise the required User Task operation."}
      actions={<StudioButton onClick={() => setAttempt(value => value + 1)}>Retry</StudioButton>} />}
  </StudioPage>;
}

export function LegacyFormsPage({ api }: { api: ElsaStudioModuleApi }) {
  const fieldControls = api.userTaskFieldControls;
  const owner = useMemo(() => ({ api }), [api]);
  const currentOwner = useRef(owner); currentOwner.current = owner;
  const generation = useRef(0);
  const pageRequest = useRef(0);
  useEffect(() => () => { generation.current++; pageRequest.current++; }, [api]);
  const [versions, setVersions] = useState<FormVersion[]>([]); const [nextPage, setNextPage] = useState<string | null>(null); const [identity, setIdentity] = useState<TaskActor>();
  const [formError, setFormError] = useState(""); const [versionConflict, setVersionConflict] = useState(false);
  const [showValidation, setShowValidation] = useState(false);
  const [hasUnappliedSource, setHasUnappliedSource] = useState(false);
  const returnKey = useRef<string>(""); const returnButtons = useRef(new Map<string, HTMLButtonElement>());
  const [opened, setOpened] = useState<FormVersion | null>(null);
  const [workspaceTab, setWorkspaceTab] = useState<FormWorkspaceTab>("form");
  const [historyLoading, setHistoryLoading] = useState(false); const [historyError, setHistoryError] = useState("");
  const [editing, setEditing] = useState(false); const [loading, setLoading] = useState(true);
  const createButton = useRef<HTMLButtonElement>(null); const editor = useRef<HTMLDivElement>(null);
  useEffect(() => { if (editing) editor.current?.querySelector<HTMLInputElement>("input")?.focus(); }, [editing]);
  const closeEditor = (allowFocus = true) => {
    const focused = document.activeElement;
    const restoreFocus = allowFocus && (focused === document.body || Boolean(editor.current?.contains(focused)));
    setFormError(""); setShowValidation(false); setHasUnappliedSource(false); setEditing(false); setForm(initial());
    requestAnimationFrame(() => {
      if (restoreFocus && (document.activeElement === document.body || document.activeElement === focused)) {
        if (returnKey.current) returnButtons.current.get(returnKey.current)?.focus(); else createButton.current?.focus();
      }
    });
  };
  const [form, setForm] = useState(initial); const [error, setError] = useState(""); const [busy, setBusy] = useState(false);
  const refresh = useCallback(async () => {
    const request = ++pageRequest.current;
    setHistoryLoading(false); setHistoryError("");
    try {
      const [who, all] = await Promise.all([actor(api), forms(api)]);
      if (currentOwner.current !== owner || request !== pageRequest.current) return;
      setIdentity(who); setVersions(all.items); setNextPage(all.next ?? null); setError("");
    } catch (failure) { if (currentOwner.current === owner && request === pageRequest.current) setError(message(failure)); }
    finally { if (currentOwner.current === owner && request === pageRequest.current) setLoading(false); }
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
      setForm({ ...structuredClone(version.form), version: next }); setHasUnappliedSource(false); setOpened(null); setWorkspaceTab("form"); setEditing(true);
    } catch (failure) { setError(message(failure)); } finally { setBusy(false); }
  };
  const loadMoreHistory = async () => {
    if (!nextPage || historyLoading) return;
    const cursor = nextPage;
    const request = ++pageRequest.current;
    setHistoryLoading(true); setHistoryError("");
    try {
      const page = await forms(api, cursor);
      if (currentOwner.current !== owner || request !== pageRequest.current) return;
      setVersions(previous => appendUniqueVersions(previous, page.items));
      setNextPage(page.next ?? null);
    } catch (failure) {
      if (currentOwner.current === owner && request === pageRequest.current) setHistoryError(message(failure));
    } finally {
      if (currentOwner.current === owner && request === pageRequest.current) setHistoryLoading(false);
    }
  };
  const save = async () => {
    if (busy || hasUnappliedSource) return;
    if (!valid) { setShowValidation(true); return; }
    setBusy(true); setFormError("");
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
  const hasFormAccess = !identity || identity.canReadForms || identity.canManageForms;
  const isLatest = (version: FormVersion) => latest.get(formIdentity(version))?.form.version === version.form.version;
  const canVersion = (version: FormVersion) => Boolean(identity?.canManageForms) && version.form.provider === basicProvider && isLatest(version);
  const validation = validateTaskForm(form, fieldControls);
  const valid = validation.length === 0;
  const backToForms = () => {
    setOpened(null); setWorkspaceTab("form");
    requestAnimationFrame(() => returnButtons.current.get(returnKey.current)?.focus());
  };
  return <StudioPage className={`user-tasks${editing ? " user-tasks--editing" : ""}`} aria-label="Reusable task forms management"><StudioPageHeader className={editing ? "user-task-form-page-header--editing" : opened ? "user-task-form-page-header--workspace" : undefined} title={editing ? "Design the form" : opened ? opened.form.name : "Reusable task forms"} description={editing || opened ? undefined : "Manage published task forms. Selecting a form in a workflow pins its current version automatically."} actions={<>
      {!editing && !opened && identity?.canManageForms && <StudioButton ref={createButton} variant="primary" disabled={busy} onClick={() => { returnKey.current = ""; setFormError(""); setShowValidation(false); setHasUnappliedSource(false); setForm(initial()); setEditing(true); }}>Create form version</StudioButton>}
      {!editing && opened && <StudioButton disabled={busy} onClick={backToForms}>Back to forms</StudioButton>}
      {editing && <StudioButton disabled={busy} onClick={() => closeEditor()}>Back to forms</StudioButton>}
      <a className="studio-button" data-variant="ghost" data-size="md" href={qualifyStudioBackendPath("/user-tasks")}>Task inbox</a></>} />
    {error ? <StudioResultState tone="danger" title="Forms unavailable" description={error} actions={<StudioButton onClick={() => void refresh()}>Retry</StudioButton>} /> : null}
    {formError && editing ? <StudioAlert tone="danger">{formError}{versionConflict && <StudioButton disabled={busy} onClick={() => void recoverVersion()}>Use next available version</StudioButton>}</StudioAlert> : null}
    {!editing && !opened && <>
      {!loading && !error && !hasFormAccess ? <StudioResultState tone="danger" title="Form access unavailable" description="Your account does not have permission to view reusable forms." /> : loading ? <StudioTableContainer role="region" aria-label="Reusable forms" tabIndex={0}><SkeletonRows rows={3} label="Loading forms" /></StudioTableContainer> : latest.size ? <StudioTableContainer role="region" aria-label="Reusable forms" tabIndex={0}><table className="studio-table user-task-table user-task-forms-table" aria-label="Reusable task forms">
          <thead><tr><th scope="col">Form</th><th scope="col">Identifier</th><th scope="col">Latest version</th>{identity?.canManageForms && <th scope="col">Actions</th>}</tr></thead>
          <tbody>{[...latest.values()].map(version => {
            const key = formIdentity(version);
            const open = () => {
              returnKey.current = key;
              setOpened(version);
              setWorkspaceTab("form");
            };
            return <tr key={key} {...studioRowInteraction<HTMLTableRowElement>(busy ? undefined : open)}>
              <td data-label="Form"><button type="button" className="user-task-link studio-row-link" aria-label={`Open form ${version.form.id}`} disabled={busy} ref={node => { if (node) returnButtons.current.set(key, node); else returnButtons.current.delete(key); }} onClick={open}>{version.form.name}</button></td>
              <td data-label="Identifier">{version.form.id}</td>
              <td data-label="Latest version">{version.form.version}</td>
              {identity?.canManageForms && <td data-label="Actions" data-empty={!canVersion(version) || undefined}>{canVersion(version) && <StudioButton disabled={busy} onClick={() => { returnKey.current = key; void nextVersion(version); }}>Create next version</StudioButton>}</td>}
            </tr>;
          })}</tbody>
        </table></StudioTableContainer> : !error && hasFormAccess ? <StudioCollectionEmptyState title="No reusable forms yet" description="Reusable forms define the fields people fill in when responding to a task."
          actions={identity?.canManageForms ? <StudioButton variant="primary" disabled={busy} onClick={() => { returnKey.current = ""; setFormError(""); setShowValidation(false); setHasUnappliedSource(false); setForm(initial()); setEditing(true); }}>Create form version</StudioButton> : undefined} /> : null}
      {!loading && !error && hasFormAccess && <StudioCollectionFooter><span>{latest.size} {latest.size === 1 ? "form" : "forms"} shown</span>
        {nextPage && <StudioButton disabled={busy} onClick={() => { const cursor = nextPage; const request = ++pageRequest.current; setBusy(true); void forms(api, cursor).then(page => { if (currentOwner.current === owner && request === pageRequest.current) { setVersions(previous => appendUniqueVersions(previous, page.items)); setNextPage(page.next ?? null); } }, failure => { if (currentOwner.current === owner && request === pageRequest.current) setError(message(failure)); }).finally(() => { if (currentOwner.current === owner && request === pageRequest.current) setBusy(false); }); }}>More forms</StudioButton>}
      </StudioCollectionFooter>}
    </>}
    {opened && !editing && <section className="user-task-form-preview" aria-label="Published form">
      <FormWorkspaceTabs primaryLabel="Form" activeTab={workspaceTab} onSelect={setWorkspaceTab}
        primary={<PublishedFormSnapshot form={opened.form} createdAt={opened.createdAt} latestVersion={latest.get(formIdentity(opened))?.form.version}
          actions={canVersion(opened) ? <StudioButton variant="primary" disabled={busy} onClick={() => void nextVersion(opened)}>Create next version</StudioButton> : null} />}
        versions={<StudioVersionHistory
          label="Published form versions"
          items={(() => {
            const related = versions.filter(version => version.tenantId === opened.tenantId && version.form.id === opened.form.id);
            const formVersions = related.some(version => version.form.version === opened.form.version) ? related : [opened, ...related];
            return formVersions.sort((left, right) => versionNumber(right.form.version) - versionNumber(left.form.version)).map(version => ({
              id: version.form.version,
              ariaLabel: `Select version ${version.form.version}`,
              title: `Version ${version.form.version}`,
              subtitle: <time dateTime={version.createdAt}>{new Date(version.createdAt).toLocaleString()}</time>,
              status: isLatest(version) ? <StatusChip tone="success">Latest</StatusChip> : undefined
            }));
          })()}
          selectedId={opened.form.version}
          onSelect={version => {
            const selected = versions.find(item => item.tenantId === opened.tenantId && item.form.id === opened.form.id && item.form.version === version);
            if (selected) setOpened(selected);
          }}
          emptyState="No published form versions are available."
          detail={<PublishedFormSnapshot form={opened.form} createdAt={opened.createdAt} latestVersion={latest.get(formIdentity(opened))?.form.version}
            actions={canVersion(opened) ? <StudioButton variant="primary" disabled={busy} onClick={() => void nextVersion(opened)}>Create next version</StudioButton> : null} />}
          listFooter={<>
            {historyError ? <StudioAlert tone="danger">{historyError}</StudioAlert> : null}
            {nextPage ? <StudioButton disabled={busy || historyLoading} onClick={() => void loadMoreHistory()}>{historyLoading ? "Loading more form versions…" : historyError ? "Retry older versions" : "Load older versions for this form"}</StudioButton> : null}
          </>}
        />}
      />
    </section>}
    {editing && identity?.canManageForms ? <div ref={editor} className="user-task-form-editor" role="region" aria-label="Design and publish a form version">
      {showValidation && validation.length ? <StudioAlert tone="danger">{validation.join(" ")}</StudioAlert> : null}
      <FormDesigner
        form={form}
        onFormChange={update => setForm(value => ({ ...value, ...update }))}
        fields={form.fields}
        onFieldsChange={fields => setForm(value => ({ ...value, fields }))}
        fieldControls={fieldControls}
        description="Existing tasks keep this version. Publishing changes makes them available to new tasks."
        onUnappliedSourceChange={setHasUnappliedSource}
        toolbarActions={<>{hasUnappliedSource ? <span className="user-task-form-source-pending" role="status">Unapplied source edits · Apply or reset before publishing.</span> : null}<StudioButton variant="primary" disabled={busy || hasUnappliedSource} onClick={() => void save()}>Publish form version</StudioButton><StudioButton disabled={busy} onClick={() => closeEditor()}>Cancel</StudioButton></>}
        disabled={busy}
      />
    </div> : null}
  </StudioPage>;
}
