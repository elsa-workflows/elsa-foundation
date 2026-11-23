import React, { useCallback, useEffect, useId, useLayoutEffect, useMemo, useRef, useState } from "react";
import { AlertTriangle, Check, CheckCircle2, Download, LoaderCircle, MoreHorizontal, Plus, RotateCcw, Save, Send, Upload } from "lucide-react";
import { notifyStudioNavigationGuardReleased, qualifyStudioBackendPath, studioNavigationRequestedEvent, useStudioWorkspaceTitle, type ElsaStudioModuleApi, type StudioNavigationRequest } from "@elsa-workflows/studio-sdk";
import { StudioNewResourceDialog, StatusChip, PeerViewSwitch, studioRowInteraction, SkeletonRows, StudioAlert, StudioButton, StudioCollectionEmptyState, StudioCollectionToolbar, StudioPage, StudioPageHeader, StudioResultState, StudioSearchInput, StudioTableContainer, StudioField, OverflowMenu, StudioTabs, StudioTabPanel, StudioTimestamp, StudioShellBreadcrumbs, type OverflowMenuItem } from "@elsa-workflows/studio-ui";
import { can, cloneVersion, createDecision, exportDecision, failureMessage, getDecision, isConflict, listDecisions, listRecycleBin, publishDecision, purgeDecision, recycleDecision, restoreDecision, saveDecision, unpublishDecision, validateDecision, version, versions, type DecisionAsset, type DecisionVersion, type Diagnostic } from "./decisionsApi";
import { decodeDecisionXml, newDecisionXml, parseTable } from "./decisionXml";
import { TableEditor } from "./TableEditor";
import { CasePanel } from "./CasePanel";
import { EvaluationPanel } from "./EvaluationPanel";
import { DecisionDiagnostics } from "./DecisionDiagnostics";
import { completeNewDecisionHandoff } from "./newDecisionHandoff";

const autosaveDelayMs = 1200;
type SaveSource = "manual" | "autosave" | "flush";
type AutosaveState = "idle" | "pending" | "saving" | "saved" | "error";
interface DraftSnapshot { name: string; description: string; xml: string; isSensitive: boolean }
const draftSignature = (value: DraftSnapshot) => JSON.stringify([value.name, value.description, value.xml, value.isSensitive]);
const assetMatchesVersion = (current: DecisionAsset, candidate: DecisionVersion) =>
  current.sourceHash === candidate.sourceHash
  && current.name === candidate.name
  && (candidate.isSensitive === undefined || current.isSensitive === candidate.isSensitive);

export interface DecisionsPageProps { api: ElsaStudioModuleApi; navigate(path: string): void; assetId?: string | null; versionId?: string | null; isNew?: boolean; /** Set when an Evaluate Decision picker opened this editor and waits for the decision it creates. */ handoff?: string | null }
export function DecisionsPage({ api, navigate, assetId, versionId, isNew = false, handoff }: DecisionsPageProps) {
  const [assets, setAssets] = useState<DecisionAsset[]>([]), [asset, setAsset] = useState<DecisionAsset | null>(null), [published, setPublished] = useState<DecisionVersion | undefined>(), [availableVersions, setAvailableVersions] = useState<DecisionVersion[]>([]);
  const [metadataEntered, setMetadataEntered] = useState(false);
  const [name, setName] = useState(""), [description, setDescription] = useState(""), [xml, setXml] = useState(""), [sensitive, setSensitive] = useState(false);
  const [loading, setLoading] = useState(true), [busy, setBusy] = useState(false), [error, setError] = useState(""), [conflict, setConflict] = useState(false), [diagnostics, setDiagnostics] = useState<Diagnostic[]>([]), [search, setSearch] = useState("");
  const [autosaveEnabled, setAutosaveEnabled] = useState(true), [autosavePaused, setAutosavePaused] = useState(false), [autosaveState, setAutosaveState] = useState<AutosaveState>("idle");
  const [saving, setSaving] = useState(false), [savingSource, setSavingSource] = useState<SaveSource | null>(null), [lastSaveSource, setLastSaveSource] = useState<"manual" | "autosave" | null>(null);
  const [recycleBin, setRecycleBin] = useState(false), [editorView, setEditorView] = useState<"rules" | "tests" | "settings">("rules");
  const [caseDirty, setCaseDirty] = useState(false), [caseBusy, setCaseBusy] = useState(false), casePending = useRef(false), caseDirtyRef = useRef(false);
  const reportCaseState = useCallback((changed: boolean, pending: boolean) => { setCaseDirty(changed); setCaseBusy(pending); caseDirtyRef.current = changed; casePending.current = pending; }, []);
  const generation = useRef(0), writing = useRef(false), initialXml = useRef(""), importFile = useRef<HTMLInputElement>(null), heading = useRef<HTMLHeadingElement>(null), collectionCreateButton = useRef<HTMLButtonElement>(null), focusCollectionOnLoad = useRef(false), reviewFocus = useRef<Diagnostic | null>(null);
  const assetRef = useRef<DecisionAsset | null>(null);
  const latestDraftRef = useRef<DraftSnapshot>({ name: "", description: "", xml: "", isSensitive: false });
  latestDraftRef.current = { name, description, xml, isSensitive: sensitive };
  const autosaveEnabledRef = useRef(true); autosaveEnabledRef.current = autosaveEnabled;
  const autosavePausedRef = useRef(false); autosavePausedRef.current = autosavePaused;
  const autosaveTimerRef = useRef<number | null>(null);
  const saveQueueRef = useRef<Promise<unknown>>(Promise.resolve());
  const queuedSavesRef = useRef(0);
  const lastRequestedSignatureRef = useRef("");
  const cancelAutosaveTimer = useCallback(() => {
    if (autosaveTimerRef.current !== null) { window.clearTimeout(autosaveTimerRef.current); autosaveTimerRef.current = null; }
  }, []);
  const collectionTabsId = useId();
  const editorTabsId = useId();
  const editor = !!assetId || isNew;
  const canEdit = can(api, "decisions:manage") && (isNew || (!!asset && asset.xml !== null && (!published || published.xml !== null)));
  const readOnly = !canEdit;
  const diagnosticTable = useMemo(() => { try { return xml ? parseTable(xml) : null; } catch { return null; } }, [xml]);
  const latestPublishedVersion = useMemo(() => asset?.publishedVersionId ? availableVersions.find(item => item.id === asset.publishedVersionId) : undefined, [asset?.publishedVersionId, availableVersions]);
  const savedDraftMatchesPublished = !!asset && !!latestPublishedVersion && assetMatchesVersion(asset, latestPublishedVersion);
  const hasDivergedSavedDraft = !!asset && !(
    (latestPublishedVersion && assetMatchesVersion(asset, latestPublishedVersion))
    || (published && assetMatchesVersion(asset, published))
    || availableVersions.some(item => assetMatchesVersion(asset, item))
  );
  const versionDiffersFromDraft = !!asset && !!published && !assetMatchesVersion(asset, published);
  const versionDirty = editor && !!published && !!asset && (
    name !== asset.name
    || description !== (asset.description ?? "")
    || xml !== (published.xml ?? "")
    || sensitive !== (published.isSensitive ?? asset.isSensitive)
  );
  const draftDirty = editor && !published && (isNew
    ? !!name || !!description || sensitive || xml !== "" && xml !== initialXml.current
    : !!asset && (name !== asset.name || description !== (asset.description ?? "") || xml !== (asset.xml ?? "") || sensitive !== asset.isSensitive));
  const dirty = published ? versionDirty : draftDirty;
  const currentDraftSignature = draftSignature({ name, description, xml, isSensitive: sensitive });
  useLayoutEffect(() => { if (isNew && metadataEntered) heading.current?.focus(); }, [isNew, metadataEntered]);
  const dirtyRef = useRef(false); dirtyRef.current = dirty || caseDirty;
  // The tab names only what this route shows: the host keeps its route label while another decision or version is still loading,
  // and "Decision" marks one whose own load finished without it.
  const route = `${assetId ?? ""}|${versionId ?? ""}|${isNew ? "new" : "existing"}`, settledRoute = useRef<string | null>(null), shown = asset && asset.id === assetId && (published?.id ?? null) === (versionId ?? null) ? asset : null;
  const routePending = loading || settledRoute.current !== route;
  const editorTitle = isNew ? "New decision" : shown?.name ?? (routePending ? "Loading decision" : "Decision unavailable");
  const editorBadge = routePending || (!isNew && !shown) ? ""
    : published
      ? (versionDirty ? `Editing from v${published.number}` : readOnly ? `Version ${published.number} · Read only` : `Version ${published.number}`)
      : !dirty && savedDraftMatchesPublished && latestPublishedVersion
        ? `Published · v${latestPublishedVersion.number}`
        : latestPublishedVersion
          ? "Draft · Unpublished changes"
          : "Draft";
  const editorBadgeTone: "neutral" | "success" | "warning" = published
    ? (versionDirty ? "warning" : "neutral")
    : !dirty && savedDraftMatchesPublished && latestPublishedVersion
      ? "success"
      : "warning";
  useEffect(() => { setEditorView("rules"); }, [route]);
  useLayoutEffect(() => {
    if (editorView !== "rules" || !reviewFocus.current) return;
    const item = reviewFocus.current;
    const target = item.ruleId && item.clauseId ? document.getElementById(`cell-${item.ruleId}-${item.clauseId}`) : null;
    if (target) { target.focus(); reviewFocus.current = null; }
  }, [editorView, diagnostics]);
  const workspaceTabDetail = shown && !isNew
    ? published
      ? `Version ${published.number}`
      : (!dirty && savedDraftMatchesPublished && latestPublishedVersion ? `Published · v${latestPublishedVersion.number}` : "Draft")
    : null;
  useStudioWorkspaceTitle(!editor ? "Decisions" : isNew ? "New decision" : shown ? shown.name : !loading && settledRoute.current === route ? "Decision" : null, workspaceTabDetail);
  useEffect(() => { if (!dirty && !caseDirty && !caseBusy && !saving && !busy) notifyStudioNavigationGuardReleased(); }, [dirty, caseDirty, caseBusy, saving, busy]);
  const apply = useCallback((next: DecisionAsset, candidate?: DecisionVersion) => {
    const nextDescription = next.description ?? "";
    const nextXml = candidate?.xml ?? (candidate ? "" : next.xml ?? "");
    const nextSensitive = candidate?.isSensitive ?? next.isSensitive;
    assetRef.current = next;
    lastRequestedSignatureRef.current = draftSignature({ name: next.name, description: nextDescription, xml: nextXml, isSensitive: nextSensitive });
    setAsset(next); setPublished(candidate); setName(next.name); setDescription(nextDescription); setSensitive(nextSensitive); setXml(nextXml); setConflict(false); setDiagnostics([]);
  }, []);
  const load = useCallback(async () => {
    const ticket = ++generation.current; cancelAutosaveTimer(); writing.current = false; assetRef.current = null; lastRequestedSignatureRef.current = ""; autosavePausedRef.current = false;
    setBusy(false); setSaving(false); setSavingSource(null); setAutosavePaused(false); setAutosaveState("idle"); setLastSaveSource(null); setLoading(true); setError(""); setConflict(false); setAsset(null); setPublished(undefined); setAvailableVersions([]); setDiagnostics([]);
    try {
      if (isNew) { setMetadataEntered(false); const initial = newDecisionXml(); initialXml.current = initial; setName(""); setDescription(""); setSensitive(false); setXml(initial); }
      else if (assetId) {
        const next = await getDecision(api, assetId);
        const candidate = versionId ? await version(api, assetId, versionId) : undefined;
        const choices = await versions(api, assetId);
        if (ticket !== generation.current) return;
        apply(next, candidate); setAvailableVersions(choices);
      } else {
        const items = recycleBin ? await listRecycleBin(api) : await listDecisions(api); if (ticket !== generation.current) return; setAssets(items); setXml(""); setName("");
      }
    } catch (failure) { if (ticket === generation.current) setError(failureMessage(failure)); }
    finally {
      if (ticket === generation.current) {
        settledRoute.current = route;
        const previousFocus = document.activeElement;
        setLoading(false);
        const focusCollection = focusCollectionOnLoad.current && !editor;
        if (focusCollection) focusCollectionOnLoad.current = false;
        requestAnimationFrame(() => {
          if (ticket !== generation.current) return;
          const target = editor ? heading.current : focusCollection ? collectionCreateButton.current : null;
          if (!target?.isConnected) return;
          const active = target.ownerDocument.activeElement;
          // A later field selection belongs to the author, even before this frame runs.
          if (active && active !== target.ownerDocument.body && active !== previousFocus) return;
          target.focus();
        });
      }
    }
  }, [api, assetId, versionId, isNew, apply, cancelAutosaveTimer, editor, recycleBin, route]);
  const invalidate = useCallback(() => { generation.current += 1; cancelAutosaveTimer(); }, [cancelAutosaveTimer]);
  // Retire old completions before the host hands notifications to the next visit.
  useLayoutEffect(() => invalidate, [load, invalidate]);
  useEffect(() => { void load(); }, [load]);
  const path = useCallback((id?: string, selectedVersion?: string) => qualifyStudioBackendPath(`/decisions${id ? `?decision=${encodeURIComponent(id)}${selectedVersion ? `&version=${encodeURIComponent(selectedVersion)}` : ""}` : ""}`), []);
  const savedNavigation = useCallback((target: string) => { cancelAutosaveTimer(); writing.current = false; dirtyRef.current = false; notifyStudioNavigationGuardReleased(); navigate(target); }, [cancelAutosaveTimer, navigate]);
  const leave = useCallback(async (target: string, onLeaving?: () => void) => {
    if (writing.current || queuedSavesRef.current > 0 || casePending.current) return false;
    if (dirtyRef.current && !await api.dialogs.confirm({ title: "Unsaved decision", message: "Discard your unsaved changes and leave this decision?", confirmLabel: "Discard changes" })) return false;
    cancelAutosaveTimer(); dirtyRef.current = false; notifyStudioNavigationGuardReleased(); onLeaving?.(); navigate(target); return true;
  }, [api, cancelAutosaveTimer, navigate]);
  useEffect(() => {
    const beforeUnload = (event: BeforeUnloadEvent) => { if (dirtyRef.current || writing.current || queuedSavesRef.current > 0 || casePending.current) { event.preventDefault(); event.returnValue = ""; } };
    const guard = (event: Event) => {
      const request = event as CustomEvent<StudioNavigationRequest>;
      if (!dirtyRef.current && !writing.current && queuedSavesRef.current === 0 && !casePending.current) return;
      request.preventDefault();
      if (!request.detail || request.detail.intent === "probe" || request.detail.intent === "close") return;
      if (!writing.current && queuedSavesRef.current === 0 && !casePending.current) void leave(request.detail.toPath);
    };
    window.addEventListener("beforeunload", beforeUnload); window.addEventListener(studioNavigationRequestedEvent, guard);
    return () => { window.removeEventListener("beforeunload", beforeUnload); window.removeEventListener(studioNavigationRequestedEvent, guard); };
  }, [leave]);
  const work = useCallback(async (action: (ticket: number) => Promise<void>) => {
    if (writing.current) return; const ticket = generation.current; writing.current = true; setBusy(true); setError("");
    try { await action(ticket); } catch (failure) { if (ticket === generation.current) { setError(failureMessage(failure)); setConflict(editor && isConflict(failure)); } }
    finally { if (ticket === generation.current) { writing.current = false; setBusy(false); notifyStudioNavigationGuardReleased(); } }
  }, [editor]);
  const persistDraft = useCallback((source: SaveSource): Promise<DecisionAsset | null> => {
    const ticket = generation.current;
    const raw = latestDraftRef.current;
    const input = { name: raw.name.trim(), description: raw.description, xml: raw.xml, isSensitive: raw.isSensitive };
    if (!input.name || !input.xml) return Promise.resolve(null);
    const rawSig = draftSignature(raw);
    if (source !== "autosave") {
      cancelAutosaveTimer();
      autosavePausedRef.current = false; setAutosavePaused(false); setError(""); setConflict(false);
    }
    lastRequestedSignatureRef.current = rawSig;
    queuedSavesRef.current += 1;
    setSaving(true);
    setSavingSource(source);
    const run = async (): Promise<DecisionAsset | null> => {
      try {
        if (ticket !== generation.current) return null;
        if (source === "autosave" && (autosavePausedRef.current || !autosaveEnabledRef.current || casePending.current || caseDirtyRef.current)) return null;
        const current = assetRef.current;
        if (source === "autosave" && !current) return null;
        if (current && input.name === current.name && input.description === (current.description ?? "") && input.xml === (current.xml ?? "") && input.isSensitive === current.isSensitive) {
          setAutosaveState("saved");
          return current;
        }
        setAutosaveState("saving");
        const next = current ? await saveDecision(api, current, input) : await createDecision(api, input);
        // The decision exists once the create returns, even if this tab was hidden meanwhile (which invalidates the ticket).
        if (!current && isNew && handoff) completeNewDecisionHandoff(handoff, { id: next.id, name: next.name });
        if (ticket !== generation.current) return null;
        assetRef.current = next;
        setAsset(next);
        setConflict(false);
        setDiagnostics([]);
        const liveSig = draftSignature(latestDraftRef.current);
        if (liveSig === rawSig) {
          const nextDescription = next.description ?? "";
          const nextXml = next.xml ?? "";
          setName(next.name); setDescription(nextDescription); setSensitive(next.isSensitive); setXml(nextXml);
          lastRequestedSignatureRef.current = draftSignature({ name: next.name, description: nextDescription, xml: nextXml, isSensitive: next.isSensitive });
          dirtyRef.current = caseDirtyRef.current;
          setAutosaveState("saved");
          setLastSaveSource(source === "autosave" ? "autosave" : "manual");
        } else {
          lastRequestedSignatureRef.current = "";
          setAutosaveState("idle");
        }
        if (source === "manual") api.notifications?.notify({ level: "success", title: "Decision saved" });
        if (!current) savedNavigation(path(next.id));
        return next;
      } catch (failure) {
        if (ticket === generation.current) {
          setError(failureMessage(failure));
          setConflict(editor && isConflict(failure));
          autosavePausedRef.current = true; setAutosavePaused(true); setAutosaveState("error");
          lastRequestedSignatureRef.current = "";
        }
        return null;
      } finally {
        queuedSavesRef.current -= 1;
        if (ticket === generation.current && queuedSavesRef.current === 0) {
          setSaving(false);
          setSavingSource(null);
          if (!dirtyRef.current && !writing.current && !casePending.current) notifyStudioNavigationGuardReleased();
        }
      }
    };
    const operation = saveQueueRef.current.then(run, run);
    saveQueueRef.current = operation.then(() => undefined, () => undefined);
    return operation;
  }, [api, cancelAutosaveTimer, editor, handoff, isNew, path, savedNavigation]);
  const saveFromPublished = useCallback(() => work(async ticket => {
    const current = assetRef.current;
    if (!current || !published) return;
    const raw = latestDraftRef.current;
    const input = { name: raw.name.trim(), description: raw.description, xml: raw.xml, isSensitive: raw.isSensitive };
    if (!input.name || !input.xml) return;
    if (hasDivergedSavedDraft) {
      const confirmed = await api.dialogs.confirm({
        title: "Replace current draft?",
        message: `Your current draft has unpublished changes. Save these edits from Version ${published.number} as the new draft and overwrite those changes? Version ${published.number} remains immutable.`,
        confirmLabel: "Replace draft"
      });
      if (!confirmed || ticket !== generation.current) return;
    }
    const next = await saveDecision(api, current, input);
    if (ticket !== generation.current) return;
    apply(next);
    setLastSaveSource("manual");
    setAutosaveState("saved");
    api.notifications?.notify({ level: "success", title: "Decision saved" });
    savedNavigation(path(next.id));
  }), [api, apply, hasDivergedSavedDraft, path, published, savedNavigation, work]);
  const save = useCallback(() => {
    if (writing.current || casePending.current || caseDirtyRef.current || readOnly) return;
    if (published) { void saveFromPublished(); return; }
    void persistDraft("manual");
  }, [persistDraft, published, readOnly, saveFromPublished]);
  const discardVersionEdits = useCallback(() => {
    if (!asset || !published) return;
    apply(asset, published);
    setError("");
    setConflict(false);
  }, [apply, asset, published]);
  const autosaveScheduled = editor && !routePending && !published && !readOnly && !!asset && autosaveEnabled && !autosavePaused && !busy && !conflict && !caseBusy && !caseDirty && dirty && !!name.trim() && !!xml;
  useEffect(() => {
    if (!autosaveScheduled || currentDraftSignature === lastRequestedSignatureRef.current) {
      if (autosaveState === "pending") setAutosaveState(asset && !dirty ? "saved" : "idle");
      return;
    }
    setAutosaveState("pending");
    const timer = window.setTimeout(() => {
      if (autosaveTimerRef.current === timer) autosaveTimerRef.current = null;
      void persistDraft("autosave");
    }, autosaveDelayMs);
    autosaveTimerRef.current = timer;
    return () => {
      window.clearTimeout(timer);
      if (autosaveTimerRef.current === timer) autosaveTimerRef.current = null;
    };
  }, [autosaveScheduled, asset, dirty, currentDraftSignature, autosaveState, persistDraft]);
  const canSave = !busy && !saving && !caseBusy && !caseDirty && !readOnly && !!name.trim() && !!xml && (dirty || !asset);
  useEffect(() => {
    if (!editor || routePending || readOnly) return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (!(event.metaKey || event.ctrlKey) || event.altKey || event.shiftKey || event.key.toLowerCase() !== "s") return;
      event.preventDefault();
      if (canSave) save();
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [editor, routePending, readOnly, canSave, save]);
  const flushDraftForAction = async (ticket: number): Promise<DecisionAsset | null> => {
    cancelAutosaveTimer();
    await saveQueueRef.current;
    if (ticket !== generation.current) return null;
    const current = assetRef.current;
    if (!current) return null;
    const live = latestDraftRef.current;
    const needsSave = live.name !== current.name || live.description !== (current.description ?? "") || live.xml !== (current.xml ?? "") || live.isSensitive !== current.isSensitive;
    if (!needsSave) return current;
    return persistDraft("flush");
  };
  const importDocument = async (file: File) => {
    if (file.size > 1024 * 1024) { setError("Decision documents must be at most 1 MiB."); return; }
    const importedName = await api.dialogs.prompt({ title: "Import decision", message: "Name this imported decision. Unsupported documents can be retained without becoming executable.", defaultValue: file.name.replace(/\.dmn$/i, ""), confirmLabel: "Import" });
    if (!importedName?.trim()) return;
    await work(async ticket => { const importedXml = decodeDecisionXml(await file.arrayBuffer()); const next = await createDecision(api, { name: importedName.trim(), xml: importedXml, isSensitive: false }); if (ticket === generation.current) savedNavigation(path(next.id)); });
  };
  const download = () => work(async ticket => {
    if (!asset) return; const source = await exportDecision(api, asset.id, published?.id); if (ticket !== generation.current) return;
    const url = URL.createObjectURL(new Blob([source], { type: "application/xml" })); const anchor = document.createElement("a"); anchor.href = url; anchor.download = `${asset.name.replace(/[^a-zA-Z0-9_-]/g, "-") || "decision"}.dmn`; anchor.click(); URL.revokeObjectURL(url);
  });
  const validate = () => work(async ticket => {
    const target = await flushDraftForAction(ticket);
    if (!target || ticket !== generation.current) return;
    const result = await validateDecision(api, target);
    if (ticket === generation.current) { setDiagnostics(result.diagnostics); api.notifications?.notify({ level: result.success ? "success" : "warning", title: result.success ? "The saved draft is valid for the supported DMN profile." : "Validation found unsupported or invalid content." }); }
  });
  const publish = () => work(async ticket => {
    let target: DecisionAsset | null = null;
    if (published) {
      const current = assetRef.current;
      const raw = latestDraftRef.current;
      const input = { name: raw.name.trim(), description: raw.description, xml: raw.xml, isSensitive: raw.isSensitive };
      if (!current || !versionDirty || !input.name || !input.xml) return;
      const draftAlreadyMatchesEdits = input.name === current.name
        && input.description === (current.description ?? "")
        && input.xml === (current.xml ?? "")
        && input.isSensitive === current.isSensitive;
      if (draftAlreadyMatchesEdits) {
        target = current;
      } else {
        if (hasDivergedSavedDraft) {
          const confirmed = await api.dialogs.confirm({
            title: "Replace current draft and publish?",
            message: `Your current draft has unpublished changes. Save these edits from Version ${published.number} to the draft and publish a new immutable version? Version ${published.number} remains immutable.`,
            confirmLabel: "Replace and publish"
          });
          if (!confirmed || ticket !== generation.current) return;
        }
        target = await saveDecision(api, current, input);
        if (ticket !== generation.current) return;
        assetRef.current = target;
        setAsset(target);
      }
    } else {
      target = await flushDraftForAction(ticket);
    }
    if (!target || ticket !== generation.current) return;
    const result = await publishDecision(api, target);
    if (ticket !== generation.current) return;
    const normalizedVersion: DecisionVersion = {
      ...result,
      assetId: result.assetId ?? target.id,
      name: result.name ?? target.name,
      xml: result.xml !== undefined ? result.xml : target.xml,
      sourceHash: result.sourceHash ?? target.sourceHash,
      publishedAt: result.publishedAt ?? target.updatedAt,
      isSensitive: result.isSensitive ?? target.isSensitive
    };
    const [refreshedAsset, refreshedVersions] = await Promise.all([
      getDecision(api, target.id).catch(() => null),
      versions(api, target.id).catch(() => null)
    ]);
    if (ticket !== generation.current) return;
    const nextAsset: DecisionAsset = refreshedAsset
      && refreshedAsset.publishedVersionId === normalizedVersion.id
      && refreshedAsset.xml === target.xml
      && refreshedAsset.name === target.name
      ? refreshedAsset
      : { ...target, publishedVersionId: normalizedVersion.id };
    const nextVersions = refreshedVersions && refreshedVersions.length
      ? (refreshedVersions.some(item => item.id === normalizedVersion.id) ? refreshedVersions : [normalizedVersion, ...refreshedVersions])
      : [normalizedVersion, ...availableVersions.filter(item => item.id !== normalizedVersion.id)];
    apply(nextAsset);
    setAvailableVersions(nextVersions);
    setLastSaveSource(null);
    setAutosaveState("saved");
    api.notifications?.notify({ level: "success", title: `Version ${normalizedVersion.number} published` });
    if (published) savedNavigation(path(nextAsset.id));
  });
  const restorePublished = () => work(async ticket => {
    if (!asset || !published) return;
    if (hasDivergedSavedDraft) {
      const confirmed = await api.dialogs.confirm({
        title: "Restore version to draft",
        message: `Your current draft has unpublished changes. Replace the saved draft with Version ${published.number}? Version ${published.number} remains immutable.`,
        confirmLabel: "Restore to draft"
      });
      if (!confirmed || ticket !== generation.current) return;
    }
    const next = await cloneVersion(api, asset, published.id);
    if (ticket === generation.current) {
      apply(next);
      setLastSaveSource(null);
      setAutosaveState("idle");
      api.notifications?.notify({ level: "success", title: "Version restored to the draft" });
      savedNavigation(path(next.id));
    }
  });
  const unpublish = () => work(async ticket => {
    if (!asset || dirty || saving || caseDirty || caseBusy || !await api.dialogs.confirm({ title: "Unpublish decision", message: "This removes the decision from new evaluations and workflow bindings. Existing published versions and already-frozen workflow executions remain unchanged.", confirmLabel: "Unpublish" })) return;
    const result = await unpublishDecision(api, asset);
    if (ticket !== generation.current) return;
    const nextAsset: DecisionAsset = { ...asset, revision: result?.revision ?? asset.revision, publishedVersionId: null };
    apply(nextAsset);
    setLastSaveSource(null);
    setAutosaveState("saved");
    api.notifications?.notify({ level: "success", title: "Decision unpublished" });
    if (published) savedNavigation(path(asset.id));
  });
  const recycle = (target: DecisionAsset | null = asset, fromCollection = false) => work(async ticket => {
    if (!target || (!fromCollection && (dirty || saving || caseDirty || caseBusy))) return;
    const confirmed = await api.dialogs.confirm({ title: "Move decision to recycle bin", message: `${fromCollection ? `Move ${target.name} to the recycle bin? ` : ""}The decision will no longer be available for new use. Its versions, saved cases and test history will be retained until permanent deletion. Existing frozen workflow executions remain unchanged.`, confirmLabel: "Move to recycle bin", tone: "danger" });
    if (!confirmed || ticket !== generation.current) return;
    if (fromCollection && (recycleBin || !assets.some(item => item.id === target.id && item.revision === target.revision))) return;
    await recycleDecision(api, target);
    if (ticket !== generation.current) return;
    if (fromCollection) {
      const items = await listDecisions(api);
      if (ticket !== generation.current) return;
      setAssets(items);
      api.notifications?.notify({ level: "success", title: "Decision moved to recycle bin" });
    } else {
      api.notifications?.notify({ level: "success", title: "Decision moved to recycle bin" }); savedNavigation(path());
    }
  });
  const restore = (item: DecisionAsset) => work(async ticket => {
    await restoreDecision(api, item);
    const items = await listRecycleBin(api);
    if (ticket === generation.current) { setAssets(items); api.notifications?.notify({ level: "success", title: `${item.name} was restored as an unpublished decision.` }); }
  });
  const purge = (item: DecisionAsset) => work(async ticket => {
    if (!await api.dialogs.confirm({ title: "Permanently delete decision", message: `This permanently deletes ${item.name}, its versions, saved cases and test history. Already-frozen workflow executions remain unchanged. This cannot be undone.`, confirmLabel: "Delete permanently", tone: "danger" })) return;
    await purgeDecision(api, item);
    const items = await listRecycleBin(api);
    if (ticket === generation.current) { setAssets(items); api.notifications?.notify({ level: "success", title: `${item.name} was permanently deleted.` }); }
  });
  const toggleAutosave = () => {
    const next = !autosaveEnabled;
    autosaveEnabledRef.current = next;
    setAutosaveEnabled(next);
    if (!next) {
      cancelAutosaveTimer();
      setAutosaveState(current => current === "pending" ? (asset && !dirty ? "saved" : "idle") : current);
    } else {
      autosavePausedRef.current = false;
      setAutosavePaused(false);
      lastRequestedSignatureRef.current = "";
    }
  };
  const showSaveStatus = !routePending && (isNew || !!shown) && (!published || versionDirty);
  const savePending = showSaveStatus && (!published ? (saving || autosaveState === "pending" || autosaveState === "saving" || autosaveScheduled) : busy);
  const saveFailed = showSaveStatus && !savePending && (autosaveState === "error" || conflict);
  const saveAttention = showSaveStatus && !savePending && !saveFailed && (!asset || dirty);
  const saveSuccess = showSaveStatus && !savePending && !saveFailed && !saveAttention && !!asset;
  const saveStatusText = !showSaveStatus ? ""
    : savePending ? (published || savingSource === "manual" ? "Saving…" : "Autosaving…")
      : saveFailed ? (conflict ? "Save conflict" : "Save failed")
        : !asset ? "Unsaved decision"
          : dirty ? "Unsaved changes"
            : savedDraftMatchesPublished ? "Up to date"
              : lastSaveSource === "autosave" ? "Autosaved" : "Saved draft";
  const hasUnpublishedDraftChanges = !savedDraftMatchesPublished || dirty;
  const canPublish = !busy && !saving && !caseBusy && !caseDirty && !conflict && !!asset && can(api, "decisions:publish") && (
    published
      ? (versionDirty && !readOnly && !!name.trim() && !!xml)
      : (hasUnpublishedDraftChanges && (!dirty || (!readOnly && !!name.trim() && !!xml)))
  );
  const publishTitle = !asset
    ? "Save this decision before publishing"
    : published
      ? (versionDirty ? "Save edits to draft and publish a new immutable version" : "Edit this version to publish a new immutable version")
      : (!hasUnpublishedDraftChanges && latestPublishedVersion ? `No unpublished changes since Version ${latestPublishedVersion.number}` : "Publish a new immutable version");
  const canRestorePublished = !!published && !versionDirty && versionDiffersFromDraft && !busy && !caseBusy && !caseDirty && !readOnly && !!published.xml;
  const evaluationVersion = !dirty && asset && xml && can(api, "decisions:evaluate")
    ? (published ?? (savedDraftMatchesPublished ? latestPublishedVersion : undefined))
    : undefined;
  const moreDecisionItems: OverflowMenuItem[] = [
    ...(!published && !readOnly ? [{
      id: "autosave",
      group: "Saving",
      label: "Autosave",
      detail: autosaveEnabled ? "On" : "Off",
      selection: { type: "checkbox" as const, checked: autosaveEnabled },
      onSelect: toggleAutosave
    }] : []),
    { id: "validate", group: "Decision", label: "Validate", icon: <CheckCircle2 size={15} aria-hidden="true" />, disabled: busy || saving || !asset || !!published || !can(api, "decisions:manage") || (dirty && (readOnly || !name.trim() || !xml || conflict)), onSelect: validate },
    { id: "export", group: "Decision", label: "Export DMN", icon: <Download size={15} aria-hidden="true" />, disabled: busy || !asset || !can(api, "decisions:source"), onSelect: download }
  ];
  const filtered = assets.filter(item => `${item.name} ${item.description ?? ""}`.toLocaleLowerCase().includes(search.trim().toLocaleLowerCase()));
  if (isNew && !metadataEntered) return <StudioPage className="decisions-page" aria-label="New decision">
    {can(api, "decisions:manage") ? <StudioNewResourceDialog title="New decision" onCancel={() => { focusCollectionOnLoad.current = true; navigate(path()); }}
      onCreate={metadata => { setName(metadata.name); setDescription(metadata.description); setMetadataEntered(true); }} />
      : <StudioResultState title="Decision creation unavailable" description="Your account does not have permission to create decisions." />}
  </StudioPage>;
  return <StudioPage className={`decisions-page${editor ? " studio-authoring-workspace" : ""}`} aria-labelledby={editor ? "decision-title" : "decisions-title"}>
    {!editor ? <StudioPageHeader title="Decisions" titleId="decisions-title"
      description="Reusable business policies with versioned decision tables, saved cases and explanations."
      refresh={{ id: "decisions", label: "Refresh decisions", refreshing: loading, disabled: busy, autoRefresh: true, onRefresh: load }}
      actions={<>
        <StudioButton ref={collectionCreateButton} variant="primary" disabled={busy || !can(api, "decisions:manage")} onClick={() => navigate(qualifyStudioBackendPath("/decisions?new=1"))}><Plus size={15} /> New decision</StudioButton>
        <StudioButton disabled={busy || !can(api, "decisions:manage")} onClick={() => importFile.current?.click()}><Upload size={15} /> Import DMN</StudioButton>
      </>} /> : null}
    <input ref={importFile} hidden type="file" accept=".dmn,.xml,application/xml,text/xml" aria-label="Import DMN file" onChange={event => { const file = event.target.files?.[0]; event.target.value = ""; if (file) void importDocument(file); }} />
    {error ? <StudioAlert tone="danger">{error}</StudioAlert> : null}
    {conflict ? <div className="decision-conflict"><p>Your unsaved work is still here. Reload the current server draft or save your work as a separate decision.</p><div className="decision-actions"><StudioButton onClick={() => void api.dialogs.confirm({ title: "Reload decision", message: "Discard your unsaved changes and load the server draft?", confirmLabel: "Reload" }).then(confirmed => { if (confirmed) void load(); })}>Reload server draft</StudioButton><StudioButton disabled={busy} onClick={() => void work(async ticket => { const copy = await createDecision(api, { name: `${name.trim()} copy`, description, xml, isSensitive: sensitive }); if (ticket === generation.current) savedNavigation(path(copy.id)); })}>Save as a copy</StudioButton></div></div> : null}
    {!editor ? <>
      <div className="decision-collection-toolbar">
        <StudioCollectionToolbar><StudioSearchInput aria-label="Search decisions" value={search} onChange={event => setSearch(event.target.value)} placeholder="Search decisions" /></StudioCollectionToolbar>
        <PeerViewSwitch baseId={collectionTabsId} ariaLabel="Decision collections" views={[{ id: "decisions", label: "Decisions" }, { id: "recycle-bin", label: "Recycle bin" }]} value={recycleBin ? "recycle-bin" : "decisions"} onChange={id => { setSearch(""); setRecycleBin(id === "recycle-bin"); }} />
      </div>
      <StudioTabPanel baseId={collectionTabsId} index={0} hidden={recycleBin}>
        {loading ? <SkeletonRows label="Loading decisions" /> : !recycleBin ? !assets.length ? error ? <StudioResultState tone="danger" title="Decisions could not be loaded" description="Refresh after the backend or your access is restored." />
          : <StudioCollectionEmptyState title="No decisions yet" description="Create a table for eligibility, approval requirements, discounts or another reusable policy."
            actions={can(api, "decisions:manage") ? <StudioButton variant="primary" onClick={() => navigate(qualifyStudioBackendPath("/decisions?new=1"))}><Plus size={15} /> New decision</StudioButton> : undefined} />
          : !filtered.length ? error ? null : <StudioCollectionEmptyState title="No matching decisions" description="Try another name or clear your search." actions={<StudioButton onClick={() => setSearch("")}>Clear search</StudioButton>} />
            : <StudioTableContainer role="region" aria-label="Decisions table" tabIndex={0}>
              <table className="studio-table decision-assets"><thead><tr><th scope="col">Decision</th><th scope="col">Status</th><th scope="col">Updated</th><th scope="col">Actions</th></tr></thead>
                <tbody>{filtered.map(item => <tr key={item.id} {...studioRowInteraction<HTMLTableRowElement>(() => navigate(path(item.id)))}><th scope="row"><button className="decision-link studio-row-link" onClick={() => navigate(path(item.id))}>{item.name}</button>{item.description ? <p className="decision-muted">{item.description}</p> : null}</th>
                  <td>{item.publishedVersionId ? "Published · Draft available" : "Unpublished"}{item.isSensitive ? " · Protected" : ""}</td><td><StudioTimestamp value={item.updatedAt} fallback="—" invalidFallback="Invalid Date" /></td>
                  <td><OverflowMenu ariaLabel={`Actions for ${item.name}`} items={[{ id: "delete", label: "Move to recycle bin", destructive: true, disabled: busy || loading || !can(api, "decisions:manage"), onSelect: () => void recycle(item, true) }]} /></td></tr>)}</tbody>
              </table>
            </StudioTableContainer> : null}
      </StudioTabPanel>
      <StudioTabPanel baseId={collectionTabsId} index={1} hidden={!recycleBin}>
        {loading ? <SkeletonRows label="Loading recycled decisions" /> : recycleBin ? !assets.length ? error ? <StudioResultState tone="danger" title="Decisions could not be loaded" description="Refresh after the backend or your access is restored." />
          : <StudioCollectionEmptyState title="Recycle bin is empty" description="Recycled decisions stay here until restored or permanently deleted." />
          : !filtered.length ? error ? null : <StudioCollectionEmptyState title="No matching decisions" description="Try another name or clear your search." actions={<StudioButton onClick={() => setSearch("")}>Clear search</StudioButton>} />
            : <StudioTableContainer role="region" aria-label="Recycled decisions table" tabIndex={0}>
              <table className="studio-table decision-assets"><thead><tr><th scope="col">Decision</th><th scope="col">Recycled</th><th scope="col">Actions</th></tr></thead>
                <tbody>{filtered.map(item => <tr key={item.id}><th scope="row">{item.name}{item.description ? <p className="decision-muted">{item.description}</p> : null}</th>
                  <td><StudioTimestamp value={item.deletedAt} fallback="Recycled" invalidFallback="Invalid Date" /></td>
                  <td><OverflowMenu ariaLabel={`Actions for ${item.name}`} items={[
                    { id: "restore", label: "Restore", disabled: busy || !can(api, "decisions:manage"), onSelect: () => void restore(item) },
                    { id: "purge", label: "Delete permanently", destructive: true, disabled: busy || !can(api, "decisions:manage"), onSelect: () => void purge(item) }
                  ]} /></td></tr>)}</tbody>
              </table>
            </StudioTableContainer> : null}
      </StudioTabPanel>
    </> : <div className="decision-editor-workspace">
      <header className="decision-draft-bar decision-editor-header">
        <div className="decision-draft-context">
          <><StudioShellBreadcrumbs items={[{ href: path(), label: "Decisions" }]} current={editorTitle}
          renderLink={item => <a href={item.href} aria-disabled={busy || saving || caseBusy || undefined} onClick={event => {
            if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
            event.preventDefault();
            void leave(item.href, () => { focusCollectionOnLoad.current = true; });
          }}>{item.label}</a>} />
          <span className="decision-editor-title">
            <h1 id="decision-title" ref={heading} tabIndex={-1} title={editorTitle}>{editorTitle}</h1>
            {editorBadge ? <StatusChip tone={editorBadgeTone}>{editorBadge}</StatusChip> : null}
          </span></>
          {shown && availableVersions.length > 0 ? <fieldset className="decision-version-picker" disabled={busy || saving} aria-label="Decision version">
            <PeerViewSwitch ariaLabel="Decision version" value={published?.id ?? "draft"} views={[
              { id: "draft", label: "Current draft" }, ...availableVersions.map(item => ({ id: item.id, label: `Version ${item.number}` }))
            ]} onChange={version => void leave(path(shown.id, version === "draft" ? undefined : version))} />
          </fieldset> : null}
        </div>
        <div className="decision-editor-metadata">
          {saveStatusText ? <span className="decision-save-status" role="status" aria-live="polite" aria-atomic="true" title={saveStatusText}
            data-pending={savePending || undefined} data-success={saveSuccess || undefined} data-attention={saveAttention || undefined} data-error={saveFailed || undefined}>
            {savePending ? <LoaderCircle size={12} aria-hidden="true" className="decision-save-spinner" /> : saveSuccess ? <Check size={12} aria-hidden="true" /> : saveFailed ? <AlertTriangle size={12} aria-hidden="true" /> : null}
            <span>{saveStatusText}</span>
          </span> : null}
        </div>
        {!routePending && (isNew || shown) ? <div className="decision-actions">
          {published && !versionDirty && versionDiffersFromDraft && !readOnly ? <StudioButton size="sm" disabled={!canRestorePublished} onClick={() => void restorePublished()}>
            <RotateCcw size={15} aria-hidden="true" />
            Restore to draft
          </StudioButton> : null}
          <StudioButton size="sm" className="decision-icon-button decision-save-button" title={published ? "Save edits to draft" : "Save decision"} disabled={!canSave} onClick={save}>
            <Save size={16} aria-hidden="true" />
            <span className="decision-visually-hidden">Save</span>
          </StudioButton>
          <StudioButton size="sm" variant="primary" className="decision-publish-button" title={publishTitle} disabled={!canPublish} onClick={() => void publish()}>
            <Send size={15} aria-hidden="true" />
            Publish
          </StudioButton>
          <OverflowMenu ariaLabel="More decision actions" triggerIcon={<MoreHorizontal size={18} aria-hidden="true" />} minWidth={220} items={moreDecisionItems} />
        </div> : null}
      </header>
      {!routePending && shown && published && versionDirty ? <div className="decision-version-edit-banner" role="status">
        <p>{hasDivergedSavedDraft
          ? `Editing from Version ${published.number}. Saving will replace your current draft's unpublished changes.`
          : `Editing from Version ${published.number}. Save to update the draft with these changes.`}</p>
        <StudioButton size="sm" disabled={busy} onClick={discardVersionEdits}>Discard changes</StudioButton>
      </div> : null}
      {routePending ? <SkeletonRows label="Loading decision" /> : !isNew && !shown ? <StudioResultState title="Unable to open this decision" description="The resource could not be loaded. It may have been recycled or your account may not have access." /> : <>
        <StudioTabs tabs={[{ id: "rules", label: "Rules" }, { id: "tests", label: "Tests" }, { id: "settings", label: "Settings" }]} activeTab={editorView} onSelect={tabId => setEditorView(tabId as "rules" | "tests" | "settings")} baseId={editorTabsId} ariaLabel="Decision views" className="decision-editor-tabs" />
        <StudioTabPanel baseId={editorTabsId} index={0} hidden={editorView !== "rules"} className="decision-editor-panel decision-rules-panel">
          {diagnostics.length ? <DecisionDiagnostics diagnostics={diagnostics} table={diagnosticTable} label="Compatibility and validation" onReview={item => { const target = item.ruleId && item.clauseId ? document.getElementById(`cell-${item.ruleId}-${item.clauseId}`) : null; target?.focus(); }} /> : null}
          {xml ? <TableEditor key={`${asset?.id ?? "new"}:${published?.id ?? "draft"}`} xml={xml} readOnly={readOnly || busy} diagnostics={diagnostics} onChange={value => { setXml(value); setError(""); setDiagnostics([]); }} onError={setError} /> : <StudioResultState title="Decision source is protected" description="Source access and any required sensitive-value grant are needed to inspect or edit this table." />}
        </StudioTabPanel>
        <StudioTabPanel baseId={editorTabsId} index={1} hidden={editorView !== "tests"} className="decision-editor-panel decision-tests-panel">
          {asset && can(api, "decisions:test") ? <CasePanel key={`${asset.id}:${published?.id ?? "draft"}`} api={api} asset={asset} candidate={published} xml={xml} dirty={dirty || saving} onDraftStateChange={reportCaseState} onReviewRules={items => {
            setDiagnostics(items); reviewFocus.current = items.find(item => item.ruleId && item.clauseId) ?? null; setEditorView("rules");
          }} outputSupplement={asset && evaluationVersion && xml ? <EvaluationPanel key={evaluationVersion.id} api={api} id={asset.id} versionId={evaluationVersion.id} xml={xml} /> : null} /> : <StudioResultState title="Testing is unavailable" description="Testing is not available with your current permissions." />}
        </StudioTabPanel>
        <StudioTabPanel baseId={editorTabsId} index={2} hidden={editorView !== "settings"} className="decision-editor-panel decision-settings-panel">
          <fieldset disabled={readOnly || busy} className="decision-metadata"><legend className="decision-visually-hidden">Decision details</legend>
            <StudioField label="Name"><input value={name} maxLength={200} onChange={event => setName(event.target.value)} /></StudioField>
            <StudioField label="Description"><input value={description} maxLength={2048} onChange={event => setDescription(event.target.value)} /></StudioField>
            <StudioField className="decision-checkbox" label="Protect decision source and values"><input type="checkbox" checked={sensitive} onChange={event => setSensitive(event.target.checked)} /></StudioField>
          </fieldset>
          {asset ? <section className="decision-lifecycle-settings" aria-labelledby="decision-availability-title"><h2 id="decision-availability-title">Availability</h2>
            <p className="decision-muted">{asset.publishedVersionId ? "Published versions are immutable. Unpublishing only affects new evaluations and workflow bindings." : "This decision is not available for new workflow bindings until you publish a version."}</p>
            <div className="decision-actions">
              {asset.publishedVersionId ? <StudioButton disabled={busy || saving || dirty || caseBusy || caseDirty || !can(api, "decisions:publish")} onClick={() => void unpublish()}>Unpublish</StudioButton> : null}
              <StudioButton disabled={busy || saving || dirty || caseBusy || caseDirty || !can(api, "decisions:manage")} onClick={() => void recycle()}>Move to recycle bin</StudioButton>
            </div>
          </section> : null}
        </StudioTabPanel>
      </>}
    </div>}
  </StudioPage>;
}
