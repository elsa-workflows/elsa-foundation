import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { notifyStudioNavigationGuardReleased, qualifyStudioBackendPath, studioNavigationRequestedEvent, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { OverflowMenu, studioRowInteraction, StatusChip, StudioAlert, StudioButton, StudioCollectionEmptyState, StudioCollectionFooter, StudioPage, StudioPageHeader, StudioResultState, StudioTableContainer, StudioVersionHistory, type OverflowMenuItem } from "@elsa-workflows/studio-ui";
import { FormDesigner } from "./FormDesigner";
import { validateTaskForm } from "./formDesignerModel";
import { actor, basicProvider, deleteForm, discardFormDraft, formDefinition, formDefinitions as listFormDefinitions, formVersions, message, publishForm, saveFormDraft, statusOf, supportsFormDeletion, type FormDefinition, type FormVersion, type TaskActor, type TaskForm } from "./taskApi";
import { PublishedFormSnapshot } from "./FormVersionWorkspace";

const initialForm = (): TaskForm => ({ id: "", version: "1", name: "", provider: basicProvider, fields: [] });
const cloneForm = (form: TaskForm) => structuredClone(form);
const autosaveDelayMs = 1200;
const formSignature = (form: TaskForm) => JSON.stringify(form);
const appendVersions = (current: FormVersion[], incoming: FormVersion[]) => {
  const known = new Set(current.map(item => `${item.tenantId}/${item.form.id}/${item.form.version}`));
  return [...current, ...incoming.filter(item => {
    const key = `${item.tenantId}/${item.form.id}/${item.form.version}`;
    if (known.has(key)) return false;
    known.add(key);
    return true;
  })];
};

export function FormLifecyclePage({ api }: { api: ElsaStudioModuleApi }) {
  const owner = useMemo(() => ({ api }), [api]);
  const fieldControls = api.userTaskFieldControls;
  const currentOwner = useRef(owner); currentOwner.current = owner;
  const listRequest = useRef(0); const editorRequest = useRef(0); const historyRequest = useRef(0);
  const currentIdRef = useRef<string | null>(null);
  const persistedIdRef = useRef<string | null>(null);
  const createButton = useRef<HTMLButtonElement>(null); const editorElement = useRef<HTMLElement>(null);
  const rowButtons = useRef(new Map<string, HTMLButtonElement>());
  const deletingRef = useRef<string | null>(null);
  const [identity, setIdentity] = useState<TaskActor>();
  const [definitions, setDefinitions] = useState<FormDefinition[]>([]);
  const definitionsRef = useRef(definitions); definitionsRef.current = definitions;
  const [nextPage, setNextPage] = useState<string | null>(null);
  const [deleteAvailable, setDeleteAvailable] = useState(false);
  const [loading, setLoading] = useState(true); const [busy, setBusy] = useState(false);
  const [savingDraft, setSavingDraft] = useState(false); const [autosaveEnabled, setAutosaveEnabled] = useState(true);
  const [autosavePaused, setAutosavePaused] = useState(false); const [autosaveStatus, setAutosaveStatus] = useState<"idle" | "pending" | "saving" | "saved" | "error">("idle");
  const [error, setError] = useState(""); const [deleteError, setDeleteError] = useState(""); const [formError, setFormError] = useState(""); const [conflict, setConflict] = useState(false);
  const [editing, setEditing] = useState(false); const [editorLoading, setEditorLoading] = useState(false);
  const [currentId, setCurrentId] = useState<string | null>(null); const [isNew, setIsNew] = useState(false); const [readOnly, setReadOnly] = useState(false);
  const [form, setForm] = useState<TaskForm>(initialForm);
  const [baseForm, setBaseForm] = useState<TaskForm | null>(null); const [savedForm, setSavedForm] = useState<TaskForm | null>(null);
  const [currentDefinition, setCurrentDefinition] = useState<FormDefinition | null>(null);
  const [designerGeneration, setDesignerGeneration] = useState(0);
  const [showValidation, setShowValidation] = useState(false);
  const [history, setHistory] = useState<FormVersion[]>([]); const [historyNext, setHistoryNext] = useState<string | null>(null);
  const [historyLoaded, setHistoryLoaded] = useState(false); const [historyLoading, setHistoryLoading] = useState(false);
  const [historyError, setHistoryError] = useState(""); const [selectedHistoryVersion, setSelectedHistoryVersion] = useState<string | null>(null);
  const [hasUnappliedSource, setHasUnappliedSource] = useState(false);
  const unappliedSourceRef = useRef(false);
  unappliedSourceRef.current = hasUnappliedSource;
  const formRef = useRef(form); formRef.current = form;
  const revisionRef = useRef(0);
  const savedFormRef = useRef(savedForm); savedFormRef.current = savedForm;
  const baseFormRef = useRef(baseForm); baseFormRef.current = baseForm;
  const autosaveTimer = useRef<number | null>(null);
  const saveQueue = useRef<Promise<void>>(Promise.resolve());
  const queuedSaves = useRef(0);
  const lastRequestedSignature = useRef("");
  const autosavePausedRef = useRef(false);
  autosavePausedRef.current = autosavePaused;
  const visibilityGeneration = useRef(0);
  const invalidateVisibilityGeneration = useCallback(() => { visibilityGeneration.current++; }, []);

  useEffect(() => {
    return () => {
      invalidateVisibilityGeneration();
      if (autosaveTimer.current !== null) window.clearTimeout(autosaveTimer.current);
    };
  }, [api, invalidateVisibilityGeneration]);

  const refresh = useCallback(async () => {
    const request = ++listRequest.current;
    try {
      const [who, page, canDelete] = await Promise.all([actor(api), listFormDefinitions(api), supportsFormDeletion(api)]);
      if (currentOwner.current !== owner || request !== listRequest.current) return;
      if (!Array.isArray(page.items)) throw new Error("The form definition list response is invalid.");
      setIdentity(who); setDefinitions(page.items); setNextPage(page.next ?? null); setDeleteAvailable(canDelete); setError(""); setDeleteError("");
    } catch (failure) {
      if (currentOwner.current === owner && request === listRequest.current) setError(message(failure));
    } finally {
      if (currentOwner.current === owner && request === listRequest.current) setLoading(false);
    }
  }, [api, owner]);

  useEffect(() => { void refresh(); }, [refresh]);

  const upsertDefinition = (definition: FormDefinition) => setDefinitions(previous =>
    [...previous.filter(item => item.id !== definition.id), definition].sort((left, right) => left.id.localeCompare(right.id)));

  const removeDefinition = async (definition: FormDefinition) => {
    if (busy || deletingRef.current || !deleteAvailable || !identity?.canManageForms) return;
    const name = definition.draft?.form?.name || definition.latestPublished?.form.name || definition.id;
    const confirmed = api.dialogs?.confirm ? await api.dialogs.confirm({
      title: "Delete reusable form",
      message: `Delete “${name}”? Published versions remain available to workflows that already reference them.`,
      tone: "danger"
    }) : false;
    if (!confirmed || currentOwner.current !== owner) return;
    const latest = definitionsRef.current.find(item => item.id === definition.id);
    if (!latest || latest.revision !== definition.revision) {
      setDeleteError("This form changed. Refresh the list and try again.");
      return;
    }
    deletingRef.current = latest.id;
    setBusy(true); setDeleteError("");
    try {
      await deleteForm(api, latest.id, latest.revision);
      if (currentOwner.current !== owner) return;
      const focusId = definitionsRef.current.find(item => item.id !== latest.id)?.id;
      setDefinitions(previous => previous.filter(item => item.id !== latest.id));
      requestAnimationFrame(() => {
        if (focusId) rowButtons.current.get(focusId)?.focus(); else createButton.current?.focus();
      });
    } catch (failure) {
      if (currentOwner.current === owner) setDeleteError(message(failure));
    } finally {
      if (currentOwner.current === owner) {
        deletingRef.current = null;
        setBusy(false);
      }
    }
  };

  const clearHistory = () => {
    historyRequest.current++;
    setHistory([]); setHistoryNext(null); setHistoryLoaded(false); setHistoryLoading(false); setHistoryError("");
    setSelectedHistoryVersion(null);
  };

  const openDefinition = async (id: string) => {
    if (busy) return;
    const request = ++editorRequest.current;
    currentIdRef.current = id;
    persistedIdRef.current = id;
    setHasUnappliedSource(false);
    unappliedSourceRef.current = false;
    clearHistory(); setEditing(true); setEditorLoading(true); setFormError(""); setConflict(false); setShowValidation(false);
    try {
      const definition = await formDefinition(api, id);
      if (currentOwner.current !== owner || currentIdRef.current !== id || request !== editorRequest.current && !editorElement.current?.isConnected) return;
      const source = definition.draft?.form ?? definition.latestPublished?.form;
      if (!source) throw new Error("This form has no saved content to open.");
      const openedForm = cloneForm(source);
      currentIdRef.current = id;
      setCurrentDefinition(definition); setCurrentId(id); setIsNew(false); setReadOnly(!identity?.canManageForms);
      setForm(openedForm); setBaseForm(openedForm); setSavedForm(definition.draft?.form ? cloneForm(definition.draft.form) : null);
      baseFormRef.current = openedForm; savedFormRef.current = definition.draft?.form ? cloneForm(definition.draft.form) : null;
      revisionRef.current = definition.revision; setDesignerGeneration(value => value + 1);
      lastRequestedSignature.current = formSignature(openedForm);
      autosavePausedRef.current = false; setAutosavePaused(false); setAutosaveStatus("idle"); setError("");
    } catch (failure) {
      if (currentOwner.current === owner && currentIdRef.current === id && (request === editorRequest.current || editorElement.current?.isConnected)) { setFormError(message(failure)); setEditing(false); }
    } finally {
      if (currentOwner.current === owner && currentIdRef.current === id && (request === editorRequest.current || editorElement.current?.isConnected)) setEditorLoading(false);
    }
  };

  const createForm = () => {
    editorRequest.current++; currentIdRef.current = null; persistedIdRef.current = null; clearHistory(); setCurrentDefinition(null); setCurrentId(null); setIsNew(true); setReadOnly(false);
    setHasUnappliedSource(false);
    unappliedSourceRef.current = false;
    const emptyForm = initialForm();
    setForm(emptyForm); setBaseForm(emptyForm); setSavedForm(null); baseFormRef.current = emptyForm; savedFormRef.current = null;
    revisionRef.current = 0; setDesignerGeneration(value => value + 1);
    lastRequestedSignature.current = formSignature(emptyForm);
    autosavePausedRef.current = false; setAutosavePaused(false); setAutosaveStatus("idle");
    setFormError(""); setConflict(false); setShowValidation(false); setEditorLoading(false); setEditing(true);
  };

  const closeEditor = (restoreFocus = true) => {
    const focused = document.activeElement;
    const shouldRestoreFocus = restoreFocus && (focused === document.body || Boolean(editorElement.current?.contains(focused)));
    const id = currentId;
    if (autosaveTimer.current !== null) { window.clearTimeout(autosaveTimer.current); autosaveTimer.current = null; }
    currentIdRef.current = null;
    persistedIdRef.current = null;
    editorRequest.current++; clearHistory(); setEditing(false); setEditorLoading(false); setBusy(false); setFormError(""); setConflict(false); setShowValidation(false);
    setSavingDraft(false); setAutosaveStatus("idle");
    setHasUnappliedSource(false);
    unappliedSourceRef.current = false;
    requestAnimationFrame(() => {
      if (!shouldRestoreFocus || (document.activeElement !== document.body && document.activeElement !== focused)) return;
      if (id) rowButtons.current.get(id)?.focus(); else createButton.current?.focus();
    });
  };
  const loadHistory = async (id: string, after?: string | null, append = false) => {
    const request = ++historyRequest.current;
    setHistoryLoading(true); setHistoryError("");
    try {
      const page = await formVersions(api, id, after);
      if (currentOwner.current !== owner || request !== historyRequest.current) return;
      setHistory(current => append ? appendVersions(current, page.items) : page.items);
      setHistoryNext(page.next ?? null); setHistoryLoaded(true);
      if (!selectedHistoryVersion && currentDefinition?.latestPublished) setSelectedHistoryVersion(currentDefinition.latestPublished.form.version);
    } catch (failure) {
      if (currentOwner.current === owner && request === historyRequest.current) setHistoryError(message(failure));
    } finally {
      if (currentOwner.current === owner && request === historyRequest.current) setHistoryLoading(false);
    }
  };

  const selectVersions = () => {
    if (hasUnappliedSource) return false;
    if (currentId && !historyLoaded && !historyLoading) void loadHistory(currentId);
    return true;
  };

  const loadMoreDefinitions = async () => {
    if (!nextPage || busy) return;
    const cursor = nextPage; const request = ++listRequest.current;
    setBusy(true); setError("");
    try {
      const page = await listFormDefinitions(api, cursor);
      if (currentOwner.current !== owner || request !== listRequest.current) return;
      if (!Array.isArray(page.items)) throw new Error("The form definition list response is invalid.");
      setDefinitions(previous => [...previous, ...page.items.filter(item => !previous.some(existing => existing.id === item.id))]);
      setNextPage(page.next ?? null);
    } catch (failure) {
      if (currentOwner.current === owner && request === listRequest.current) setError(message(failure));
    } finally {
      if (currentOwner.current === owner && request === listRequest.current) setBusy(false);
    }
  };

  const validation = validateTaskForm(form, fieldControls);
  const hasChanges = isNew || JSON.stringify(form) !== JSON.stringify(savedForm ?? baseForm ?? form);
  const hasDirtyContent = JSON.stringify(form) !== JSON.stringify(savedForm ?? baseForm ?? form);
  const canSave = !hasUnappliedSource && Boolean(form.id.trim()) && (isNew || !savedForm || hasChanges);
  const versionLabel = savedForm
    ? `Draft · ${currentDefinition?.latestPublished ? `based on version ${currentDefinition.latestPublished.form.version}` : "not yet published"}`
    : currentDefinition?.latestPublished ? `Published · version ${currentDefinition.latestPublished.form.version}` : "Unpublished working copy";

  const persistDraft = useCallback((snapshot: TaskForm, source: "manual" | "autosave" | "flush" = "manual"): Promise<boolean> => {
    const id = currentIdRef.current ?? snapshot.id.trim();
    if (!id) {
      setFormError("Enter a form identifier before saving a draft.");
      return Promise.resolve(false);
    }
    const request = editorRequest.current;
    const notificationGeneration = visibilityGeneration.current;
    const draftForm = { ...cloneForm(snapshot), id };
    currentIdRef.current = id;
    lastRequestedSignature.current = formSignature(draftForm);
    if (source !== "autosave") {
      autosavePausedRef.current = false; setAutosavePaused(false); setFormError(""); setConflict(false);
    }
    queuedSaves.current++;
    setSavingDraft(true);
    const operation = saveQueue.current.then(async () => {
      try {
        if (source === "autosave" && autosavePausedRef.current) return false;
        if (currentOwner.current !== owner || request !== editorRequest.current || currentIdRef.current !== id) return false;
        setAutosaveStatus("saving");
        const definition = await saveFormDraft(api, id, draftForm, revisionRef.current);
        if (currentOwner.current !== owner || request !== editorRequest.current || currentIdRef.current !== id) return false;
        const saved = definition.draft?.form ? cloneForm(definition.draft.form) : draftForm;
        revisionRef.current = definition.revision;
        currentIdRef.current = id; persistedIdRef.current = id;
        baseFormRef.current = saved; savedFormRef.current = saved;
        setCurrentId(id); setIsNew(false); setCurrentDefinition(definition); setBaseForm(saved); setSavedForm(saved);
        setForm(current => current.id === id ? current : { ...current, id });
        setAutosaveStatus("saved");
        upsertDefinition(definition);
        if (source === "manual" && notificationGeneration === visibilityGeneration.current) api.notifications?.notify({ level: "success", title: "Draft saved", message: "You can reopen and continue editing this draft." });
        return true;
      } catch (failure) {
        if (currentOwner.current === owner && request === editorRequest.current && currentIdRef.current === id) {
          setFormError(message(failure)); setConflict(statusOf(failure) === 409);
          autosavePausedRef.current = true; setAutosavePaused(true); setAutosaveStatus("error");
          if (persistedIdRef.current !== id) currentIdRef.current = null;
        }
        return false;
      } finally {
        queuedSaves.current--;
        if (currentOwner.current === owner) setSavingDraft(queuedSaves.current > 0);
      }
    });
    saveQueue.current = operation.then(() => undefined, () => undefined);
    return operation;
  }, [api, owner]);

  const saveDraft = async () => {
    if (busy || readOnly || hasUnappliedSource || !canSave) return;
    if (autosaveTimer.current !== null) { window.clearTimeout(autosaveTimer.current); autosaveTimer.current = null; }
    await persistDraft(formRef.current, "manual");
  };

  const flushPendingDraft = async () => {
    if (autosaveTimer.current !== null) { window.clearTimeout(autosaveTimer.current); autosaveTimer.current = null; }
    await saveQueue.current;
    const live = formRef.current;
    const baseline = savedFormRef.current ?? baseFormRef.current ?? live;
    if (autosaveEnabled && !unappliedSourceRef.current && !readOnly && formSignature(live) !== formSignature(baseline)) {
      if (!currentIdRef.current && !live.id.trim()) { setFormError("Enter a form identifier before leaving so the draft can be saved."); return false; }
      if (!await persistDraft(live, "flush")) return false;
    }
    await saveQueue.current;
    return queuedSaves.current === 0;
  };

  const leaveEditor = async (restoreFocus = true) => {
    const live = formRef.current;
    const baseline = savedFormRef.current ?? baseFormRef.current ?? live;
    const dirty = formSignature(live) !== formSignature(baseline);
    const unappliedSource = unappliedSourceRef.current;
    if (autosaveEnabled && dirty && !unappliedSource) {
      setBusy(true);
      if (!await flushPendingDraft()) { setBusy(false); return; }
    } else if (dirty || unappliedSource) {
      const confirmed = api.dialogs?.confirm ? await api.dialogs.confirm({ message: "Discard unsaved form changes?" }) : false;
      if (!confirmed || currentOwner.current !== owner) return;
    }
    const id = currentId;
    closeEditor(false);
    if (!restoreFocus) return;
    requestAnimationFrame(() => { if (id) rowButtons.current.get(id)?.focus(); else createButton.current?.focus(); });
  };

  const backToForms = () => { void leaveEditor(true); };

  useEffect(() => {
    if (!editing || editorLoading || !autosaveEnabled || autosavePaused || busy || readOnly || hasUnappliedSource || !form.id.trim() || !hasDirtyContent || formSignature(form) === lastRequestedSignature.current) {
      if (autosaveStatus === "pending") setAutosaveStatus(savedForm ? "saved" : "idle");
      return;
    }
    setAutosaveStatus("pending");
    const timer = window.setTimeout(() => {
      if (autosaveTimer.current === timer) autosaveTimer.current = null;
      void persistDraft(formRef.current, "autosave");
    }, autosaveDelayMs);
    autosaveTimer.current = timer;
    return () => {
      window.clearTimeout(timer);
      if (autosaveTimer.current === timer) autosaveTimer.current = null;
    };
  }, [editing, editorLoading, autosaveEnabled, autosavePaused, busy, readOnly, hasUnappliedSource, form, hasDirtyContent, autosaveStatus, savedForm, persistDraft]);

  useEffect(() => {
    const dirty = editing && (hasDirtyContent || hasUnappliedSource || queuedSaves.current > 0 || autosaveTimer.current !== null);
    const preventNavigation = (event: Event) => {
      if (!dirty) return;
      event.preventDefault();
      if (event.type === "beforeunload") (event as BeforeUnloadEvent).returnValue = "";
    };
    window.addEventListener(studioNavigationRequestedEvent, preventNavigation);
    window.addEventListener("beforeunload", preventNavigation);
    if (!dirty) notifyStudioNavigationGuardReleased();
    return () => {
      window.removeEventListener(studioNavigationRequestedEvent, preventNavigation);
      window.removeEventListener("beforeunload", preventNavigation);
    };
  }, [editing, hasDirtyContent, hasUnappliedSource, savingDraft]);

  const publish = async () => {
    if (busy || readOnly || unappliedSourceRef.current) return;
    const liveForm = formRef.current;
    if (validateTaskForm(liveForm, fieldControls).length) { setShowValidation(true); return; }
    const id = currentIdRef.current ?? liveForm.id.trim();
    if (!id) { setShowValidation(true); return; }
    currentIdRef.current = id;
    if (autosaveTimer.current !== null) { window.clearTimeout(autosaveTimer.current); autosaveTimer.current = null; }
    setBusy(true); setFormError(""); setConflict(false);
    const request = editorRequest.current;
    const notificationGeneration = visibilityGeneration.current;
    try {
      await saveQueue.current;
      if (currentOwner.current !== owner || request !== editorRequest.current || currentIdRef.current !== id) return;
      const publishSnapshot = { ...cloneForm(formRef.current), id };
      if (validateTaskForm(publishSnapshot, fieldControls).length) { setShowValidation(true); return; }
      const publication = await publishForm(api, id, publishSnapshot, revisionRef.current);
      if (currentOwner.current !== owner || currentIdRef.current !== id) return;
      const definition: FormDefinition = { tenantId: publication.tenantId, id, latestPublished: publication, draft: null, revision: revisionRef.current + 1 };
      const publishedForm = cloneForm(publication.form);
      formRef.current = publishedForm;
      baseFormRef.current = publishedForm; savedFormRef.current = null;
      revisionRef.current = definition.revision; currentIdRef.current = id; persistedIdRef.current = id;
      setCurrentDefinition(definition); setCurrentId(id); setForm(publishedForm); setBaseForm(publishedForm); setSavedForm(null); setIsNew(false);
      lastRequestedSignature.current = formSignature(publishedForm); setDesignerGeneration(value => value + 1);
      autosavePausedRef.current = false; setAutosavePaused(false); setAutosaveStatus("idle");
      upsertDefinition(definition); setHistory(current => appendVersions(current, [publication])); setSelectedHistoryVersion(publication.form.version);
      if (request === editorRequest.current && notificationGeneration === visibilityGeneration.current) api.notifications?.notify({ level: "success", title: "Form published", message: "The new immutable version is available to new tasks." });
    } catch (failure) {
      if (currentOwner.current === owner && request === editorRequest.current) {
        setFormError(message(failure)); setConflict(statusOf(failure) === 409);
        if (persistedIdRef.current !== id && currentIdRef.current === id) currentIdRef.current = null;
      }
    } finally {
      if (currentOwner.current === owner && request === editorRequest.current) setBusy(false);
    }
  };

  const discardDraft = async () => {
    if (busy || readOnly || unappliedSourceRef.current || !currentId || !savedForm) return;
    const confirmed = api.dialogs?.confirm ? await api.dialogs.confirm({ message: "Discard the saved form draft? This cannot be undone." }) : true;
    if (!confirmed || currentOwner.current !== owner) return;
    if (autosaveTimer.current !== null) { window.clearTimeout(autosaveTimer.current); autosaveTimer.current = null; }
    setBusy(true); setFormError(""); setConflict(false);
    const request = editorRequest.current;
    const notificationGeneration = visibilityGeneration.current;
    try {
      await saveQueue.current;
      if (currentOwner.current !== owner || request !== editorRequest.current || currentIdRef.current !== currentId) return;
      const definition = await discardFormDraft(api, currentId, revisionRef.current);
      if (currentOwner.current !== owner || currentIdRef.current !== currentId) return;
      revisionRef.current = definition.revision; setCurrentDefinition(definition); setSavedForm(null); upsertDefinition(definition);
      if (definition.latestPublished) {
        const published = cloneForm(definition.latestPublished.form);
        formRef.current = published;
        baseFormRef.current = published; savedFormRef.current = null;
        setForm(published); setBaseForm(published); setIsNew(false); lastRequestedSignature.current = formSignature(published); setDesignerGeneration(value => value + 1);
        setAutosaveStatus("idle"); autosavePausedRef.current = false; setAutosavePaused(false);
      } else {
        await refresh();
        if (currentOwner.current === owner && request === editorRequest.current) closeEditor();
      }
      if (request === editorRequest.current && notificationGeneration === visibilityGeneration.current) api.notifications?.notify({ level: "success", title: "Draft discarded" });
    } catch (failure) {
      if (currentOwner.current === owner && request === editorRequest.current) { setFormError(message(failure)); setConflict(statusOf(failure) === 409); }
    } finally {
      if (currentOwner.current === owner && currentIdRef.current === currentId) setBusy(false);
    }
  };

  const selectedHistory = history.find(item => item.form.version === selectedHistoryVersion)
    ?? history.find(item => item.form.version === currentDefinition?.latestPublished?.form.version)
    ?? history[0];
  const versionsPanel = !historyLoaded ? historyLoading ? <p role="status">Loading published versions…</p> : historyError ? <div><StudioAlert tone="danger">{historyError}</StudioAlert><StudioButton onClick={() => currentId && void loadHistory(currentId)}>Retry version history</StudioButton></div> : <p role="status">Published versions have not been loaded.</p> : <StudioVersionHistory label="Published form versions"
    items={history.map(version => ({
      id: version.form.version, ariaLabel: `Select version ${version.form.version}`, title: `Version ${version.form.version}`,
      subtitle: <time dateTime={version.createdAt}>{new Date(version.createdAt).toLocaleString()}</time>,
      status: version.form.version === currentDefinition?.latestPublished?.form.version ? <StatusChip tone="success">Latest</StatusChip> : undefined
    }))}
    selectedId={selectedHistory?.form.version ?? null}
    onSelect={version => setSelectedHistoryVersion(version)}
    emptyState="No published form versions are available."
    detail={selectedHistory ? <PublishedFormSnapshot form={selectedHistory.form} createdAt={selectedHistory.createdAt} latestVersion={currentDefinition?.latestPublished?.form.version ?? undefined} /> : null}
    listFooter={<>
      {historyError ? <StudioAlert tone="danger">{historyError}</StudioAlert> : null}
      {historyNext ? <StudioButton disabled={historyLoading || busy} onClick={() => currentId && void loadHistory(currentId, historyNext, true)}>{historyLoading ? "Loading versions…" : historyError ? "Retry older versions" : "Load older versions"}</StudioButton> : null}
    </>}
  />;
  const title = editing ? (isNew && !form.name ? "Design the form" : form.name || "Untitled form") : "Task forms";
  const hasFormAccess = !identity || identity.canReadForms || identity.canManageForms;
  const saveStatusLabel = hasUnappliedSource ? "Unapplied source"
    : autosaveStatus === "saving" ? "Saving draft"
      : autosaveStatus === "pending" ? "Saving automatically…"
        : autosaveStatus === "error" ? "Save failed"
          : hasChanges ? "Unsaved changes" : savedForm ? "Draft saved" : "No draft saved";
  const saveStatusTone: "danger" | "neutral" | "success" | "warning" = autosaveStatus === "error" ? "danger"
    : hasUnappliedSource || hasChanges || autosaveStatus === "pending" ? "warning" : savedForm ? "success" : "neutral";
  const formToolbarActions = readOnly ? null : <>
    {hasUnappliedSource ? <span className="user-task-form-source-pending" role="status">Unapplied source edits · Apply or reset before saving.</span> : null}
    <label className="user-task-form-autosave"><input type="checkbox" aria-label="Auto-save" checked={autosaveEnabled} onChange={event => {
      const enabled = event.target.checked;
      setAutosaveEnabled(enabled);
      if (!enabled && autosaveTimer.current !== null) {
        window.clearTimeout(autosaveTimer.current); autosaveTimer.current = null;
        setAutosaveStatus(hasDirtyContent ? "idle" : savedForm ? "saved" : "idle");
      }
    }} />Auto-save</label>
    <StatusChip tone={saveStatusTone}>{saveStatusLabel}</StatusChip>
    <StudioButton disabled={busy || !canSave} onClick={() => void saveDraft()}>{savingDraft ? "Saving…" : "Save"}</StudioButton>
    {savedForm ? <StudioButton disabled={busy || hasUnappliedSource} onClick={() => void discardDraft()}>Discard draft</StudioButton> : null}
    <StudioButton variant="primary" disabled={busy || hasUnappliedSource} onClick={() => void publish()}>{busy ? "Publishing…" : "Publish"}</StudioButton>
    <StudioButton disabled={busy} onClick={() => void leaveEditor(true)}>Cancel</StudioButton>
  </>;

  return <StudioPage className={`user-tasks${editing ? " user-tasks--editing" : ""}`} aria-label="Reusable task forms management">
    <StudioPageHeader className={editing ? "user-task-form-page-header--editing" : undefined} title={title}
      description={editing ? undefined : "Manage each form once. Drafts remain editable until you publish an immutable version."}
      actions={<>
        {!editing && identity?.canManageForms ? <StudioButton ref={createButton} variant="primary" disabled={busy} onClick={createForm}>New form</StudioButton> : null}
        {editing ? <StudioButton disabled={busy} onClick={backToForms}>Back to forms</StudioButton> : null}
        <a className="studio-button" data-variant="ghost" data-size="md" href={qualifyStudioBackendPath("/user-tasks")}>Task inbox</a>
      </>} />
    {error ? <StudioResultState tone="danger" title="Forms unavailable" description={error} actions={<StudioButton onClick={() => void refresh()}>Retry</StudioButton>} /> : null}

    {!editing ? <>
      {!loading && !error && !hasFormAccess ? <StudioResultState tone="danger" title="Form access unavailable" description="Your account does not have permission to view reusable forms." /> : null}
      {deleteError ? <StudioAlert tone="danger">{deleteError}</StudioAlert> : null}
      {loading ? <StudioTableContainer role="region" aria-label="Reusable forms" tabIndex={0}><div role="status">Loading forms…</div></StudioTableContainer> : definitions.length ? <StudioTableContainer role="region" aria-label="Reusable forms" tabIndex={0}><table className="studio-table user-task-table user-task-forms-table" aria-label="Task forms">
          <thead><tr><th scope="col">Form</th><th scope="col">Identifier</th><th scope="col">Published version</th><th scope="col">Status</th><th scope="col">Actions</th></tr></thead>
          <tbody>{definitions.map(definition => {
            const name = definition.draft?.form?.name || definition.latestPublished?.form.name || definition.id;
            const status = definition.draft ? definition.latestPublished ? "Published · Draft" : "Draft" : definition.latestPublished ? `Published · v${definition.latestPublished.form.version}` : "Draft";
            const menuItems: OverflowMenuItem[] = [
              { id: "open", label: identity?.canManageForms ? "Edit" : "View", onSelect: () => void openDefinition(definition.id) },
              ...(deleteAvailable && identity?.canManageForms ? [{ id: "delete", label: "Delete", destructive: true, onSelect: () => void removeDefinition(definition) }] : [])
            ];
            return <tr key={definition.id} aria-label={`Open reusable task form ${name}`} {...studioRowInteraction<HTMLTableRowElement>(() => void openDefinition(definition.id))}>
              <td data-label="Form"><StudioButton className="user-task-link" disabled={busy} ref={button => { if (button) rowButtons.current.set(definition.id, button); else rowButtons.current.delete(definition.id); }} onClick={() => void openDefinition(definition.id)}>{name}</StudioButton></td><td data-label="Identifier">{definition.id}</td>
              <td data-label="Published version">{definition.latestPublished?.form.version ?? "—"}</td>
              <td data-label="Status"><StatusChip tone={definition.draft ? "warning" : "success"}>{status}</StatusChip></td>
              <td data-label="Actions" onClick={event => event.stopPropagation()}><OverflowMenu items={menuItems} ariaLabel={`Actions for ${name}`} /></td>
            </tr>;
          })}</tbody>
        </table></StudioTableContainer> : !error && hasFormAccess ? <StudioCollectionEmptyState title="No reusable forms yet" description="Create a form once, save a draft as you work, then publish it for new tasks."
          actions={identity?.canManageForms ? <StudioButton variant="primary" disabled={busy} onClick={createForm}>New form</StudioButton> : undefined} /> : null}
      {!loading && !error && hasFormAccess ? <StudioCollectionFooter><span>{definitions.length} {definitions.length === 1 ? "form" : "forms"} shown</span>
        {nextPage ? <StudioButton disabled={busy} onClick={() => void loadMoreDefinitions()}>{busy ? "Loading forms…" : "More forms"}</StudioButton> : null}
      </StudioCollectionFooter> : null}
    </> : null}

    {editing ? <section ref={editorElement} className="user-task-form-editor" aria-label="Form editor">
      {editorLoading ? <div role="status">Loading form…</div> : null}
      {formError ? <StudioAlert tone="danger">{formError}{conflict && currentId ? <StudioButton disabled={busy} onClick={() => void openDefinition(currentId)}>Reload latest draft</StudioButton> : null}</StudioAlert> : null}
      {showValidation && validation.length ? <StudioAlert tone="danger">{validation.join(" ")}</StudioAlert> : null}
      {!editorLoading && (!readOnly || identity?.canReadForms) ? <>
        {currentId && identity?.canReadForms ? <FormDesigner key={designerGeneration} form={form} versionLabel={versionLabel} fieldControls={fieldControls} formIdReadOnly={!isNew || savingDraft} compactHeader
          onFormChange={update => setForm(value => ({ ...value, ...update }))} fields={form.fields}
          onFieldsChange={fields => setForm(value => ({ ...value, fields }))}
          description="Save work as a draft; only publishing makes an immutable version available to new tasks. Existing tasks remain pinned."
          disabled={busy || readOnly}
          versionsPanel={versionsPanel}
          onVersionsSelect={selectVersions}
          onUnappliedSourceChange={value => { unappliedSourceRef.current = value; setHasUnappliedSource(value); }}
          toolbarActions={formToolbarActions} /> : <FormDesigner key={designerGeneration} form={form} versionLabel={versionLabel} fieldControls={fieldControls} formIdReadOnly={!isNew || savingDraft}
          onFormChange={update => setForm(value => ({ ...value, ...update }))} fields={form.fields}
          onFieldsChange={fields => setForm(value => ({ ...value, fields }))}
          description="Save work as a draft; only publishing makes an immutable version available to new tasks. Existing tasks remain pinned."
          disabled={busy || readOnly}
          onUnappliedSourceChange={value => { unappliedSourceRef.current = value; setHasUnappliedSource(value); }}
          toolbarActions={formToolbarActions} />}
      </> : null}
    </section> : null}
  </StudioPage>;
}