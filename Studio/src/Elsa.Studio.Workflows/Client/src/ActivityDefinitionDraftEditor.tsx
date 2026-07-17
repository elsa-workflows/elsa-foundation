import { Component, Suspense, useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { AlertTriangle, ArrowLeft, CheckCircle2, CopyPlus, RefreshCw } from "lucide-react";
import { studioNavigationRequestedEvent, StudioHttpError, type StudioActivityDefinitionImplementationEditorContribution, type StudioActivityDefinitionImplementationState, type StudioActivityDefinitionRecoverySettings, type StudioActivityDiagnostic, type StudioActivityDiagnosticFocusResult, type StudioEndpointContext, type StudioRuntimeIdentity } from "@elsa-workflows/studio-sdk";
import type { ActivityDefinitionDraftView } from "./activityDefinitionTypes";
import { createActivityDefinitionConflictCopy, replaceActivityDefinitionDraft, useActivityAuthoringCapabilities, useActivityContractExpressionDescriptors, useFullActivityDefinitionDraft, useFullActivityDefinitionVersion, validateActivityDefinitionDraft } from "./api/activityDesign";
import { createActivityDefinitionRecoveryStore, type ActivityDefinitionRecoverySnapshot } from "./activityDefinitionRecovery";
import { observeActivityDefinitions } from "./activityDefinitionObservability";
import { ActivityDefinitionContractEditor } from "./ActivityDefinitionContractEditor";
import { ActivityDefinitionDiagnosticsPanel, type ActivityDraftValidationFailure } from "./ActivityDefinitionDiagnosticsPanel";
import { ActivityDefinitionPublicationReview } from "./ActivityDefinitionPublicationReview";
import { ActivityDefinitionProviderMigrationDialog } from "./ActivityDefinitionDraftManagementDialogs";
import { ApiCapabilityUnavailableError, ApiCapabilityVersionMismatchError } from "./api/capabilities";

type SaveStatus = "saved" | "pending" | "saving" | "offline" | "conflict" | "failed";

export function ActivityDefinitionDraftEditor({ context, definitionId, draftId, activityEditors, recoverySettings, identity, onNavigationGuardChange, onBack, onOpenDraft, onOpenVersion }: {
  context: StudioEndpointContext;
  definitionId: string;
  draftId: string;
  activityEditors: StudioActivityDefinitionImplementationEditorContribution[];
  recoverySettings?: StudioActivityDefinitionRecoverySettings;
  identity?: StudioRuntimeIdentity;
  onNavigationGuardChange(blocked: boolean): void;
  onBack(force?: boolean): void;
  onOpenDraft(definitionId: string, draftId: string): void;
  onOpenVersion(definitionId: string, versionId: string): void;
}) {
  const query = useFullActivityDefinitionDraft(context, draftId);

  if (query.isPending || query.isFetching && !query.isFetchedAfterMount) return <main className="ad-page ad-draft-editor" aria-busy="true"><div className="ad-skeleton" role="status">Loading the exact Activity Definition draft…</div></main>;
  if (query.isError || !query.data || query.data.definitionId !== definitionId) return <main className="ad-page ad-draft-editor"><button type="button" className="ad-back" onClick={() => onBack()}><ArrowLeft size={16} /> Activity Definition</button><section className="ad-failure" role="alert"><AlertTriangle size={22} /><h1>Activity draft unavailable</h1><p>Studio could not confirm the exact authorized draft. No provider state is shown.</p><button type="button" onClick={() => void query.refetch()}>Try again</button></section></main>;

  return <LoadedActivityDefinitionDraftEditor context={context} initialDraft={query.data} activityEditors={activityEditors} recoverySettings={recoverySettings} identity={identity} onNavigationGuardChange={onNavigationGuardChange} onBack={onBack} onOpenDraft={onOpenDraft} onOpenVersion={onOpenVersion} />;
}

function LoadedActivityDefinitionDraftEditor({ context, initialDraft, activityEditors, recoverySettings, identity, onNavigationGuardChange, onBack, onOpenDraft, onOpenVersion }: {
  context: StudioEndpointContext;
  initialDraft: ActivityDefinitionDraftView;
  activityEditors: StudioActivityDefinitionImplementationEditorContribution[];
  recoverySettings?: StudioActivityDefinitionRecoverySettings;
  identity?: StudioRuntimeIdentity;
  onNavigationGuardChange(blocked: boolean): void;
  onBack(force?: boolean): void;
  onOpenDraft(definitionId: string, draftId: string): void;
  onOpenVersion(definitionId: string, versionId: string): void;
}) {
  const [draft, setDraft] = useState(initialDraft);
  const [status, setStatus] = useState<SaveStatus>("saved");
  const [conflictRevision, setConflictRevision] = useState<number | null>(null);
  const [copying, setCopying] = useState(false);
  const [copyError, setCopyError] = useState(false);
  const [validating, setValidating] = useState(false);
  const [validationFailure, setValidationFailure] = useState<ActivityDraftValidationFailure | null>(null);
  const [focusAnnouncement, setFocusAnnouncement] = useState<string | null>(null);
  const [recoveryReviewed, setRecoveryReviewed] = useState(false);
  const [providerEditorFailed, setProviderEditorFailed] = useState(false);
  const [contractLocallyValid, setContractLocallyValid] = useState(true);
  const [migrationOpen, setMigrationOpen] = useState(false);
  const capabilitiesQuery = useActivityAuthoringCapabilities(context);
  const expressionsQuery = useActivityContractExpressionDescriptors(context);
  const baselineQuery = useFullActivityDefinitionVersion(context, initialDraft.sourceVersionId ?? null);
  const recoveryStore = useMemo(() => createActivityDefinitionRecoveryStore(recoverySettings, identity), [identity, recoverySettings]);
  const [recovery, setRecovery] = useState(() => recoveryStore?.read(initialDraft) ?? null);
  const currentRef = useRef(draft);
  const revisionRef = useRef(draft.revision);
  const savedSignatureRef = useRef(editableSignature(draft));
  const autosavePausedRef = useRef(false);
  const queueRef = useRef(Promise.resolve());
  const timerRef = useRef<number | null>(null);
  const contractEditorRef = useRef<HTMLDivElement | null>(null);
  const providerEditorRef = useRef<HTMLElement | null>(null);
  const diagnosticReturnRef = useRef<HTMLButtonElement | null>(null);
  const diagnosticFocusRequestRef = useRef(0);
  const contribution = useMemo(() => activityEditors.find(item => item.providerKey === draft.provider.providerKey && item.providerSchemaVersion === draft.provider.schemaVersion), [activityEditors, draft.provider.providerKey, draft.provider.schemaVersion]);
  const hasPayload = Object.prototype.hasOwnProperty.call(draft.provider, "payload");

  useEffect(() => {
    observeActivityDefinitions({ event: "autosave", surface: "editor", outcome: status, providerKey: draft.provider.providerKey, providerSchemaVersion: draft.provider.schemaVersion });
  }, [draft.provider.providerKey, draft.provider.schemaVersion, status]);

  useEffect(() => {
    observeActivityDefinitions({ event: "provider-editor", surface: "editor", outcome: hasPayload && contribution && !providerEditorFailed ? "ready" : "unavailable", providerKey: draft.provider.providerKey, providerSchemaVersion: draft.provider.schemaVersion });
  }, [contribution, draft.provider.providerKey, draft.provider.schemaVersion, hasPayload, providerEditorFailed]);

  useEffect(() => {
    currentRef.current = draft;
  }, [draft]);

  useEffect(() => {
    setRecoveryReviewed(false);
  }, [recovery]);

  useEffect(() => {
    const blocked = status !== "saved" || !contractLocallyValid;
    onNavigationGuardChange(blocked);
    if (!blocked) return () => onNavigationGuardChange(false);
    const preventUnload = (event: BeforeUnloadEvent) => event.preventDefault();
    const preventStudioNavigation = (event: Event) => event.preventDefault();
    window.addEventListener("beforeunload", preventUnload);
    window.addEventListener(studioNavigationRequestedEvent, preventStudioNavigation);
    return () => {
      window.removeEventListener("beforeunload", preventUnload);
      window.removeEventListener(studioNavigationRequestedEvent, preventStudioNavigation);
      onNavigationGuardChange(false);
    };
  }, [contractLocallyValid, onNavigationGuardChange, status]);

  useEffect(() => () => {
    if (timerRef.current !== null) window.clearTimeout(timerRef.current);
    diagnosticFocusRequestRef.current += 1;
  }, []);

  const scheduleSave = (next: ActivityDefinitionDraftView) => {
    if (autosavePausedRef.current) return;
    const unvalidated = { ...next, validation: null };
    currentRef.current = unvalidated;
    setDraft(unvalidated);
    setValidationFailure(null);
    setFocusAnnouncement(null);
    diagnosticFocusRequestRef.current += 1;
    diagnosticReturnRef.current = null;
    setStatus("pending");
    setConflictRevision(null);
    setCopyError(false);
    recoveryStore?.write(unvalidated);
    if (timerRef.current !== null) window.clearTimeout(timerRef.current);
    timerRef.current = window.setTimeout(() => {
      timerRef.current = null;
      enqueueSave(unvalidated);
    }, 800);
  };

  const enqueueSave = (snapshot: ActivityDefinitionDraftView) => {
    const signature = editableSignature(snapshot);
    queueRef.current = queueRef.current.then(async () => {
      if (autosavePausedRef.current) return;
      if (signature === savedSignatureRef.current) {
        if (editableSignature(currentRef.current) === signature) setStatus("saved");
        return;
      }
      setStatus("saving");
      try {
        const saved = await replaceActivityDefinitionDraft(context, snapshot.draftId, {
          expectedRevision: revisionRef.current,
          contract: snapshot.contract,
          provider: {
            providerKey: snapshot.provider.providerKey,
            schemaVersion: snapshot.provider.schemaVersion,
            payload: snapshot.provider.payload
          },
          layout: snapshot.layout,
          presentationLabel: snapshot.presentationLabel ?? null
        });
        revisionRef.current = saved.revision;
        savedSignatureRef.current = signature;
        const latest = currentRef.current;
        if (editableSignature(latest) === signature) {
          currentRef.current = saved;
          setDraft(saved);
          setStatus("saved");
          recoveryStore?.clear(saved);
          setRecovery(null);
        } else {
          const rebased = { ...latest, revision: saved.revision, updatedAt: saved.updatedAt };
          currentRef.current = rebased;
          setDraft(rebased);
          setStatus("pending");
          enqueueSave(rebased);
        }
      } catch (error) {
        if (isStaleRevision(error)) {
          autosavePausedRef.current = true;
          setConflictRevision(readCurrentRevision(error) ?? revisionRef.current);
          setStatus("conflict");
        } else if (isOfflineFailure(error)) {
          setStatus("offline");
        } else {
          setStatus("failed");
        }
      }
    });
    return queueRef.current;
  };

  const updateImplementation = (value: StudioActivityDefinitionImplementationState) => scheduleSave({
    ...currentRef.current,
    provider: { ...currentRef.current.provider, payload: value.payload },
    layout: value.layout
  });
  const updateContract = (contract: ActivityDefinitionDraftView["contract"]) => scheduleSave({
    ...currentRef.current,
    contract
  });
  const updateLocalContractValidity = useCallback((valid: boolean) => setContractLocallyValid(valid), []);

  const saveNow = () => {
    if (timerRef.current !== null) window.clearTimeout(timerRef.current);
    timerRef.current = null;
    enqueueSave(currentRef.current);
  };

  const createConflictCopy = async () => {
    if (status !== "conflict" || conflictRevision === null || copying) return;
    setCopying(true);
    setCopyError(false);
    const local = currentRef.current;
    try {
      const copy = await createActivityDefinitionConflictCopy(context, local.draftId, {
        expectedSourceRevision: conflictRevision,
        contract: local.contract,
        provider: { providerKey: local.provider.providerKey, schemaVersion: local.provider.schemaVersion, payload: local.provider.payload },
        layout: local.layout,
        presentationLabel: local.presentationLabel ?? null
      });
      recoveryStore?.clear(local);
      setRecovery(null);
      onOpenDraft(copy.definitionId, copy.draftId);
    } catch {
      setCopyError(true);
    } finally {
      setCopying(false);
    }
  };

  const applyRecovery = async () => {
    if (!recovery || copying) return;
    setCopyError(false);
    const restored = {
      ...currentRef.current,
      contract: recovery.contract,
      provider: { ...currentRef.current.provider, payload: recovery.payload },
      layout: recovery.layout,
      presentationLabel: recovery.presentationLabel ?? null
    };
    if (recovery.baseRevision === currentRef.current.revision) {
      setRecovery(null);
      scheduleSave(restored);
      return;
    }

    setCopying(true);
    try {
      const copy = await createActivityDefinitionConflictCopy(context, currentRef.current.draftId, {
        expectedSourceRevision: currentRef.current.revision,
        contract: recovery.contract,
        provider: {
          providerKey: recovery.providerKey,
          schemaVersion: recovery.providerSchemaVersion,
          payload: recovery.payload
        },
        layout: recovery.layout,
        presentationLabel: recovery.presentationLabel ?? null
      });
      recoveryStore?.clear(currentRef.current);
      setRecovery(null);
      onOpenDraft(copy.definitionId, copy.draftId);
    } catch {
      setCopyError(true);
    } finally {
      setCopying(false);
    }
  };

  const validateSavedRevision = async () => {
    if (status === "conflict" || !contractLocallyValid || validating) return;
    let validationRevision: number | null = null;
    let validationSignature: string | null = null;
    diagnosticFocusRequestRef.current += 1;
    diagnosticReturnRef.current = null;
    setValidating(true);
    setValidationFailure(null);
    setFocusAnnouncement(null);
    setDraft(current => ({ ...current, validation: null }));
    try {
      const saved = await flushExactSavedRevision();
      if (!saved) {
        setValidationFailure("transport");
        return;
      }
      validationRevision = saved.revision;
      validationSignature = editableSignature(currentRef.current);
      const validation = await validateActivityDefinitionDraft(context, draft.draftId, validationRevision);
      if (revisionRef.current !== validationRevision || editableSignature(currentRef.current) !== validationSignature) return;
      setDraft(current => ({ ...current, validation }));
    } catch (error) {
      if (validationRevision !== null && (
        revisionRef.current !== validationRevision ||
        editableSignature(currentRef.current) !== validationSignature
      )) return;
      if (isStaleRevision(error)) {
        autosavePausedRef.current = true;
        setConflictRevision(readCurrentRevision(error) ?? revisionRef.current);
        setStatus("conflict");
        setValidationFailure("rejected");
      } else {
        setValidationFailure(classifyValidationFailure(error));
      }
    } finally {
      setValidating(false);
    }
  };

  const flushExactSavedRevision = async () => {
    if (timerRef.current !== null) window.clearTimeout(timerRef.current);
    timerRef.current = null;
    enqueueSave(currentRef.current);
    while (true) {
      const active = queueRef.current;
      await active;
      if (active !== queueRef.current) continue;
      return !autosavePausedRef.current && savedSignatureRef.current === editableSignature(currentRef.current)
        ? { revision: revisionRef.current, signature: savedSignatureRef.current }
        : null;
    }
  };

  const focusDiagnostic = async (diagnostic: StudioActivityDiagnostic, trigger: HTMLButtonElement): Promise<StudioActivityDiagnosticFocusResult> => {
    const request = ++diagnosticFocusRequestRef.current;
    diagnosticReturnRef.current = trigger;
    const location = diagnostic.location;
    let result: StudioActivityDiagnosticFocusResult;
    if (!location) {
      result = unsupportedDiagnosticFocus();
    } else if (isContractDiagnostic(location.providerKey, location.jsonPointer, location.referenceKey, draft)) {
      result = focusContractDiagnostic(contractEditorRef.current, location.jsonPointer, location.referenceKey);
    } else if (
      location.providerKey === draft.provider.providerKey &&
      contribution?.focusDiagnosticLocation &&
      providerEditorRef.current
    ) {
      try {
        result = await contribution.focusDiagnosticLocation({
          location,
          subject: diagnostic.subject,
          editorElement: providerEditorRef.current
        });
      } catch {
        result = unsupportedDiagnosticFocus();
      }
    } else {
      result = unsupportedDiagnosticFocus();
    }
    if (request !== diagnosticFocusRequestRef.current) return result;
    setFocusAnnouncement(result.announcement);
    if (result.kind === "unsupported") diagnosticReturnRef.current = null;
    return result;
  };

  const returnToDiagnostic = () => {
    diagnosticFocusRequestRef.current += 1;
    const target = diagnosticReturnRef.current;
    diagnosticReturnRef.current = null;
    setFocusAnnouncement("Returned to the selected diagnostic.");
    target?.focus();
  };

  const Editor = contribution?.component;
  const canEditProvider = hasPayload && Boolean(contribution && Editor) && !providerEditorFailed;
  const revisionSensitiveActionsBlocked = status !== "saved" || !contractLocallyValid;
  const discardLocalChanges = () => {
    if (timerRef.current !== null) window.clearTimeout(timerRef.current);
    timerRef.current = null;
    autosavePausedRef.current = true;
    recoveryStore?.clear(currentRef.current);
    onNavigationGuardChange(false);
    onBack(true);
  };

  return <main className="ad-page ad-draft-editor" aria-labelledby="activity-draft-title">
    <button type="button" className="ad-back" onClick={() => onBack()} disabled={revisionSensitiveActionsBlocked}><ArrowLeft size={16} /> Activity Definition</button>
    <header className="ad-workbench-header"><div><span className="ad-kicker">Exact mutable draft</span><h1 id="activity-draft-title">{draft.presentationLabel?.trim() || generatedDraftLabel(draft)}</h1><p><code>{draft.draftId}</code> · {draft.provider.providerKey} · schema {draft.provider.schemaVersion}</p></div><div className="ad-header-actions"><button type="button" onClick={() => setMigrationOpen(true)} disabled={revisionSensitiveActionsBlocked}>Migrate provider</button><button type="button" onClick={() => void validateSavedRevision()} disabled={!contractLocallyValid || status === "conflict" || validating}><CheckCircle2 size={16} /> {validating ? "Saving & validating…" : "Validate saved revision"}</button><button type="button" className="ad-primary-action" onClick={saveNow} disabled={!contractLocallyValid || status === "saved" || status === "saving" || status === "conflict"}><RefreshCw size={16} /> Save now</button></div></header>
    <label className="ad-draft-label"><span>Draft label <small>Optional · need not be unique</small></span><input value={draft.presentationLabel ?? ""} maxLength={200} disabled={status === "conflict"} placeholder={generatedDraftLabel(draft)} onChange={event => scheduleSave({ ...currentRef.current, presentationLabel: event.target.value || null })} /><small>The generated fallback is derived from the stable draft identity.</small></label>
    <div className={`ad-save-state is-${contractLocallyValid ? status : "failed"}`} role={status === "failed" || status === "conflict" || !contractLocallyValid ? "alert" : "status"} aria-live="polite"><strong>{contractLocallyValid ? saveStatusLabel(status, draft.revision) : "Contract correction required"}</strong><span>{contractLocallyValid ? saveStatusDescription(status, revisionSensitiveActionsBlocked) : `Server revision ${draft.revision} is saved, but a visible literal is not valid contract data and has not been added to the autosave queue. Correct it or explicitly discard local changes.`}</span></div>
    <ActivityDefinitionDiagnosticsPanel
      validation={draft.validation}
      failure={validationFailure}
      focusAnnouncement={focusAnnouncement}
      canReturn={Boolean(diagnosticReturnRef.current)}
      onFocus={focusDiagnostic}
      onReturn={returnToDiagnostic}
    />
    <ActivityDefinitionPublicationReview
      context={context}
      definitionId={draft.definitionId}
      draftId={draft.draftId}
      currentRevision={draft.revision}
      currentSignature={editableSignature(draft)}
      disabled={status === "conflict" || !contractLocallyValid || validating}
      flushExactSavedRevision={flushExactSavedRevision}
      onFocusDiagnostic={focusDiagnostic}
      onOpenVersion={versionId => onOpenVersion(draft.definitionId, versionId)}
    />
    {recovery ? <section className="ad-recovery-card" role="alert"><div><h2>Unsaved local recovery available</h2><p>Captured {formatRecoveryTime(recovery.capturedAt)} from revision {recovery.baseRevision} for {recovery.providerKey} schema {recovery.providerSchemaVersion}. It expires {formatRecoveryTime(recovery.expiresAt)}. {!canEditProvider ? "The exact provider editor is unavailable, so Studio will preserve the server draft and will not apply this opaque state." : recovery.baseRevision === draft.revision ? "Review the recovered content before applying; Studio never restores it silently." : `The server is now at revision ${draft.revision}, so recovery creates a parallel draft and never overwrites it.`}</p><details onToggle={event => { if (event.currentTarget.open) setRecoveryReviewed(true); }}><summary>Review recovered content</summary><pre>{formatRecoveryPreview(recovery)}</pre></details></div><div>{canEditProvider ? <button type="button" onClick={() => void applyRecovery()} disabled={copying || !recoveryReviewed}>{copying ? "Creating recovery draft…" : !recoveryReviewed ? "Review content before recovery" : recovery.baseRevision === draft.revision ? "Apply local recovery" : "Create recovery draft"}</button> : null}<button type="button" onClick={() => { recoveryStore?.clear(initialDraft); setRecovery(null); }} disabled={copying}>Discard recovery</button>{copyError ? <span>Recovery could not be confirmed. The local snapshot remains available.</span> : null}</div></section> : null}
    {status === "conflict" ? <section className="ad-conflict-card" role="alert"><AlertTriangle size={20} /><div><h2>Local work preserved</h2><p>The server draft advanced to revision {conflictRevision}. Studio paused autosave and will not force overwrite or merge opaque provider state.</p><button type="button" onClick={() => void createConflictCopy()} disabled={copying}><CopyPlus size={16} /> {copying ? "Creating recovery draft…" : "Create parallel recovery draft"}</button>{copyError ? <span>Recovery could not be confirmed. The local work remains in this editor.</span> : null}</div></section> : null}
    {capabilitiesQuery.isPending ? <div className="ad-inline-status" role="status">Loading the authorized contract capability catalog…</div> : <div ref={contractEditorRef}><ActivityDefinitionContractEditor
      contract={draft.contract}
      baselineContract={baselineQuery.data?.definition.definitionId === draft.definitionId ? baselineQuery.data.contract : undefined}
      capabilities={capabilitiesQuery.data}
      expressions={expressionsQuery.data ?? []}
      providerRequiredOutcomes={capabilitiesQuery.data?.providers.find(provider => provider.providerKey === draft.provider.providerKey)?.requiredOutcomes ?? []}
      sourceVersionId={draft.sourceVersionId}
      baselineUnavailable={baselineQuery.isError}
      readOnly={status === "conflict"}
      capabilitiesUnavailable={capabilitiesQuery.isError || !capabilitiesQuery.data}
      onChange={updateContract}
      onLocalValidityChange={updateLocalContractValidity}
    /></div>}
    {!hasPayload ? <section className="ad-failure" role="alert"><AlertTriangle size={22} /><h2>Implementation payload unavailable</h2><p>The exact provider payload was not disclosed. Studio preserves the server draft and does not invent provider state.</p></section> : !contribution || !Editor ? <section className="ad-failure" role="alert"><AlertTriangle size={22} /><h2>Implementation editor unavailable</h2><p>No exact Studio contribution is available for <code>{draft.provider.providerKey}</code> schema {draft.provider.schemaVersion}. The server draft is preserved and no fallback editor is invoked.</p></section> : <section ref={providerEditorRef} className="ad-implementation-shell" aria-label="Provider implementation editor"><ProviderEditorBoundary key={`${draft.draftId}:${draft.provider.providerKey}:${draft.provider.schemaVersion}`} onFailure={() => setProviderEditorFailed(true)}><Suspense fallback={<div className="ad-inline-status" role="status">Loading the exact provider editor…</div>}><Editor context={context} definitionId={draft.definitionId} draftId={draft.draftId} revision={draft.revision} providerKey={draft.provider.providerKey} providerSchemaVersion={draft.provider.schemaVersion} manifestFingerprint={draft.provider.manifestFingerprint} value={{ payload: draft.provider.payload, layout: draft.layout }} readOnly={status === "conflict"} onChange={updateImplementation} /></Suspense></ProviderEditorBoundary></section>}
    {revisionSensitiveActionsBlocked ? <div className="ad-revision-gate" role="status"><span>Revision-sensitive lifecycle actions and navigation are paused until this exact draft revision is saved.</span><button type="button" onClick={discardLocalChanges} disabled={status === "pending" || status === "saving"}>{status === "pending" || status === "saving" ? "Waiting for save before navigation" : "Discard local changes and return"}</button></div> : null}
    {migrationOpen ? <ActivityDefinitionProviderMigrationDialog context={context} draft={draft} activityEditors={activityEditors} onClose={() => setMigrationOpen(false)} onCreated={created => { setMigrationOpen(false); onOpenDraft(created.definitionId, created.draftId); }} /> : null}
  </main>;
}

class ProviderEditorBoundary extends Component<{ children: ReactNode; onFailure(): void }, { failed: boolean }> {
  state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  componentDidCatch() {
    this.props.onFailure();
  }

  render() {
    if (this.state.failed) return <div className="ad-failure" role="alert"><AlertTriangle size={22} /><h2>Implementation editor unavailable</h2><p>The exact provider editor failed to load. Studio kept the draft shell and any local recovery state intact.</p></div>;
    return this.props.children;
  }
}

function editableSignature(draft: ActivityDefinitionDraftView) {
  return JSON.stringify({ contract: draft.contract, provider: { providerKey: draft.provider.providerKey, schemaVersion: draft.provider.schemaVersion, payload: draft.provider.payload }, layout: draft.layout, presentationLabel: draft.presentationLabel ?? null });
}

function generatedDraftLabel(draft: Pick<ActivityDefinitionDraftView, "draftId">) {
  return `Draft ${draft.draftId}`;
}

function isStaleRevision(error: unknown) {
  return error instanceof StudioHttpError && error.status === 409 && (error.payload as { errorCode?: string } | null)?.errorCode === "activity.draft.stale-revision";
}

function readCurrentRevision(error: unknown) {
  if (!(error instanceof StudioHttpError)) return null;
  const value = (error.payload as { recovery?: { currentRevision?: unknown } } | null)?.recovery?.currentRevision;
  return typeof value === "number" && Number.isFinite(value) ? value : null;
}

function isOfflineFailure(error: unknown) {
  return (typeof navigator !== "undefined" && navigator.onLine === false) || error instanceof TypeError;
}

function classifyValidationFailure(error: unknown): ActivityDraftValidationFailure {
  if (error instanceof ApiCapabilityUnavailableError || error instanceof ApiCapabilityVersionMismatchError) return "unavailable";
  if (error instanceof StudioHttpError && error.status === 403) return "forbidden";
  if (error instanceof StudioHttpError && error.status === 404) return "not-found";
  if (error instanceof StudioHttpError && (error.status === 501 || error.status === 503)) return "unavailable";
  return "transport";
}

function isContractDiagnostic(
  providerKey: string | null | undefined,
  jsonPointer: string | null | undefined,
  referenceKey: string | null | undefined,
  draft: ActivityDefinitionDraftView
) {
  if (providerKey) return false;
  if (jsonPointer?.startsWith("/contract")) return true;
  if (!referenceKey) return false;
  return [...draft.contract.inputs, ...draft.contract.outputs, ...draft.contract.outcomes]
    .some(member => member.referenceKey === referenceKey);
}

function focusContractDiagnostic(
  editor: HTMLElement | null,
  jsonPointer: string | null | undefined,
  referenceKey: string | null | undefined
): StudioActivityDiagnosticFocusResult {
  if (!editor) return unsupportedDiagnosticFocus();
  const members = [...editor.querySelectorAll<HTMLElement>("[data-contract-reference-key]")];
  const referenced = referenceKey
    ? members.find(element => element.dataset.contractReferenceKey === referenceKey)
    : null;
  const member = referenced ?? contractMemberAtPointer(members, jsonPointer);
  const field = contractFieldAtPointer(jsonPointer)
    ?? (member && referenceKey && /^\/contract\/(?:inputs|outputs|outcomes)$/.test(jsonPointer ?? "") ? "referenceKey" : null);
  const exact = member && field
    ? [...member.querySelectorAll<HTMLElement>("[data-contract-field]")]
      .find(element => element.dataset.contractField === field)
    : null;
  const target = exact ?? member ?? editor.querySelector<HTMLElement>("[data-contract-schema]");
  if (!target) return unsupportedDiagnosticFocus();
  target.closest("details")?.setAttribute("open", "");
  member?.querySelector("details")?.setAttribute("open", "");
  const disabledContext = target.matches(":disabled")
    ? target.closest<HTMLElement>("label") ?? member
    : null;
  const control = disabledContext ?? target;
  if (disabledContext) disabledContext.tabIndex = -1;
  control.focus();
  control.scrollIntoView?.({ block: "center" });
  return {
    kind: "focused",
    announcement: disabledContext
      ? "Focused the exact accessible context for a disabled provider-neutral contract control. Use Return to diagnostic to restore the diagnostics context."
      : "Focused the exact provider-neutral contract location. Use Return to diagnostic to restore the diagnostics context."
  };
}

function contractMemberAtPointer(members: HTMLElement[], jsonPointer: string | null | undefined) {
  const match = /^\/contract\/(inputs|outputs|outcomes)\/(\d+)(?:\/|$)/.exec(jsonPointer ?? "");
  if (!match) return null;
  const kind = match[1]?.slice(0, -1);
  const index = Number(match[2]);
  return members.find(element => element.dataset.contractKind === kind && Number(element.dataset.contractIndex) === index) ?? null;
}

function contractFieldAtPointer(jsonPointer: string | null | undefined) {
  const match = /^\/contract\/(?:inputs|outputs|outcomes)\/\d+\/(.+)$/.exec(jsonPointer ?? "");
  if (!match?.[1]) return null;
  return match[1].split("/").join(".");
}

function unsupportedDiagnosticFocus(): StudioActivityDiagnosticFocusResult {
  return {
    kind: "unsupported",
    announcement: "The exact diagnostic location is unavailable in the current authorized editor. The diagnostic remains visible without disclosing a hidden provider or resource identity."
  };
}

function saveStatusLabel(status: SaveStatus, revision: number) {
  if (status === "saved") return `Saved revision ${revision}`;
  if (status === "pending") return "Saving queued";
  if (status === "saving") return "Saving";
  if (status === "offline") return "Offline";
  if (status === "conflict") return "Conflict";
  return "Save failed";
}

function saveStatusDescription(status: SaveStatus, blocked: boolean) {
  if (status === "saved") return "The exact server revision includes the current implementation state.";
  if (status === "pending") return "Local changes are waiting for serialized autosave.";
  if (status === "saving") return "Studio is replacing the full draft against its exact expected revision.";
  if (status === "offline") return "Local work is preserved, but the server revision is not current.";
  if (status === "conflict") return "Autosave is paused because the server revision changed.";
  return blocked ? "Local work is preserved. Retry before using revision-sensitive lifecycle actions." : "The save could not be confirmed.";
}

function formatRecoveryTime(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "at an unknown time" : date.toLocaleString();
}

function formatRecoveryPreview(recovery: ActivityDefinitionRecoverySnapshot) {
  return JSON.stringify({
    presentationLabel: recovery.presentationLabel ?? null,
    contract: recovery.contract,
    provider: {
      providerKey: recovery.providerKey,
      schemaVersion: recovery.providerSchemaVersion,
      payload: recovery.payload
    },
    layout: recovery.layout
  }, null, 2);
}
