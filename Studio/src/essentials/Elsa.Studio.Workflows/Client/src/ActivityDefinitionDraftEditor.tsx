import { activityDiagnosticSeverity } from "./activityDiagnosticSeverity";
import { getActivityGraphOutputSetters } from "./activityGraphOutputSources";
import { activityGraphDocumentAdapter } from "./activityGraphDocumentAdapter";
import { NativeBlockAuthorityProvider } from "./graph-authoring/nativeBlockAuthority";
import { validateActivityDefinitionCandidate } from "./api/activityDesign";
import { Component, lazy, Suspense, useCallback, useEffect, useId, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { createPortal, flushSync } from "react-dom";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { StudioButton, tabElementIds, StudioAlert, StudioResultState, StudioShellBreadcrumbs, StudioField, StudioTabs, type StudioTabItem } from "@elsa-workflows/studio-ui";
import { AlertTriangle, ArrowLeft, CheckCircle2, Code2, CopyPlus, ListTree, LogIn, LogOut, Play, RefreshCw, Variable, Workflow as WorkflowIcon } from "lucide-react";
import {
  studioNavigationRequestedEvent,
  authSessionStartedEvent,
  authSessionEndedEvent,
  StudioHttpError,
  useStudioWorkspaceTitle,
  useStudioWorkspaceWhenActive,
  type StudioActivityDefinitionImplementationEditorContribution,
  type StudioActivityDefinitionImplementationState,
  type StudioActivityDefinitionRecoverySettings,
  type StudioActivityDiagnostic,
  type StudioActivityDiagnosticFocusResult,
  type StudioActivityPropertyEditorContribution,
  type StudioEndpointContext,
  type StudioExpressionEditorContribution,
  type StudioRuntimeIdentity,
  type StudioWorkflowDesignerPanelContribution,
  type StudioWorkflowRunInputEditorContribution
} from "@elsa-workflows/studio-sdk";
import type { ActivityDefinitionDraftView, ActivityDefinitionVersionView } from "./activityDefinitionTypes";
import { activityDesignKeys, createActivityDefinitionDraft, createActivityDefinitionConflictCopy, getActivityDefinitionVersion, replaceActivityDefinitionDraft, useActivityAuthoringCapabilities, useActivityContractExpressionDescriptors, useActivityDefinition, useFullActivityDefinitionDraft, useFullActivityDefinitionVersion, validateActivityDefinitionDraft } from "./api/activityDesign";
import { createActivityDefinitionRecoveryStore, type ActivityDefinitionRecoverySnapshot } from "./activityDefinitionRecovery";
import { observeActivityDefinitions } from "./activityDefinitionObservability";
import { ActivityDefinitionContractEditor, type ActivityDefinitionContractEditorHandle } from "./ActivityDefinitionContractEditor";
import { ActivityDefinitionDiagnosticsPanel, type ActivityDraftValidationFailure } from "./ActivityDefinitionDiagnosticsPanel";
import { ActivityDefinitionPublicationReview } from "./ActivityDefinitionPublicationReview";
import { ActivityDefinitionContractProposalReview } from "./ActivityDefinitionContractProposalReview";
import { ActivityDefinitionProviderMigrationDialog } from "./ActivityDefinitionDraftManagementDialogs";
import { ApiCapabilityUnavailableError, ApiCapabilityVersionMismatchError } from "./api/capabilities";
import type { ActivityPublicationOutcome } from "./api/publishing";
import type { PreparedActivityTestRunRevision } from "./ActivityDefinitionTestRunDialog";
import {
  ActivityDefinitionDraftCodeView,
  type ActivityDefinitionDraftCodeBufferState
} from "./ActivityDefinitionDraftCodeView";
import { isWorkflowChunkLoadFailure } from "./WorkflowLazyBoundary";
import "./activityDefinitionAuthoring.css";
import { ActivityDefinitionAuthoringTabsHostContext } from "./activityDefinitionAuthoringTabsHost";
import { getNotifications, useNotificationGuard } from "./workflow-editor/notifications";
import { WorkflowCanvasToolsHostContext, WorkflowEditorToolbarFrame } from "./workflow-editor/WorkflowEditorToolbar";
import { StudioLink } from "./StudioLink";
import {
  canRedo,
  canUndo,
  createHistory,
  pushSnapshot,
  redo,
  undo,
  type HistoryState
} from "./workflowHistory";

type SaveStatus = "saved" | "pending" | "saving" | "offline" | "conflict" | "failed";
type ActivityDefinitionAuthoringView = "designer" | "inputs" | "outputs" | "outcomes" | "variables" | "code";

const ActivityDefinitionTestRunDialog = lazy(() => import("./ActivityDefinitionTestRunDialog").then(module => ({ default: module.ActivityDefinitionTestRunDialog })));
const ActivityGraphOutputValueEditor = lazy(() => import("./ActivityGraphOutputValueEditor").then(module => ({ default: module.ActivityGraphOutputValueEditor })));
const ActivityGraphVariablesEditor = lazy(() => import("./ActivityGraphImplementationEditor").then(module => ({ default: module.ActivityGraphVariablesEditor })));

type ManagementSection = "drafts" | "versions" | "overview" | "relationships";

export function ActivityDefinitionDraftEditor({ agentWorkspace = false, context, definitionId, draftId, versionId, allowStartEditing = false, readOnly = false, openPublicationReviewOnMount = false, onManage, onDraftStarted, activityEditors, propertyEditors = [], expressionEditors = [], graphAuthoringPanels = [], inputEditors = [], recoverySettings, identity, onNavigationGuardChange, onBack, onOpenDraft, onOpenVersion, onOpenStudioPath }: {
  agentWorkspace?: boolean;
  context: StudioEndpointContext;
  definitionId: string;
  draftId?: string;
  versionId?: string;
  allowStartEditing?: boolean;
  readOnly?: boolean;
  openPublicationReviewOnMount?: boolean;
  onManage?(section: ManagementSection): void;
  onDraftStarted?(draft: ActivityDefinitionDraftView): void;
  activityEditors: StudioActivityDefinitionImplementationEditorContribution[];
  propertyEditors?: StudioActivityPropertyEditorContribution[];
  expressionEditors?: StudioExpressionEditorContribution[];
  graphAuthoringPanels?: StudioWorkflowDesignerPanelContribution[];
  inputEditors?: StudioWorkflowRunInputEditorContribution[];
  recoverySettings?: StudioActivityDefinitionRecoverySettings;
  identity?: StudioRuntimeIdentity;
  onNavigationGuardChange(blocked: boolean): void;
  onBack(force?: boolean): void;
  onOpenDraft(definitionId: string, draftId: string): void;
  onOpenVersion(definitionId: string, versionId: string): void;
  onOpenStudioPath(path: string): void;
}) {
  const queryClient = useQueryClient();
  const [authorityRevision, setAuthorityRevision] = useState(0);
  useEffect(() => {
    const changed = () => setAuthorityRevision(revision => revision + 1);
    window.addEventListener(authSessionStartedEvent, changed);
    window.addEventListener(authSessionEndedEvent, changed);
    return () => {
      window.removeEventListener(authSessionStartedEvent, changed);
      window.removeEventListener(authSessionEndedEvent, changed);
    };
  }, []);
  const scope = JSON.stringify([context.baseUrl, identity?.subject, identity?.tenantId, authorityRevision]);
  const [started, setStarted] = useState<{ draft: ActivityDefinitionDraftView; version: ActivityDefinitionVersionView; scope: string } | null>(null);
  const query = useFullActivityDefinitionDraft(context, draftId ?? null);
  const versionQuery = useQuery({
    queryKey: [...activityDesignKeys.fullDefinitionVersion(versionId ?? ""), scope],
    queryFn: ({ signal }) => getActivityDefinitionVersion(context, versionId!, signal),
    enabled: !draftId && Boolean(versionId),
    refetchOnMount: "always",
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
    retry: false
  });
  const definitionQuery = useActivityDefinition(context, definitionId);
  const ownClone = started?.scope === scope && started.draft.definitionId === definitionId && (draftId === started.draft.draftId || !draftId && versionId === started.version.versionId) ? started : null;
  if (started && !ownClone) setStarted(null);
  const version = ownClone?.version ?? (!draftId ? versionQuery.data : undefined);
  const initialDraft = ownClone?.draft ?? (draftId ? query.data : version ? publishedEditorDocument(version) : undefined);
  const loading = !ownClone && (draftId ? query.isPending || query.isFetching && !query.isFetchedAfterMount : versionQuery.isPending || versionQuery.isFetching && !versionQuery.isFetchedAfterMount);
  const failed = !ownClone && (draftId ? query.isError : versionQuery.isError);

  if (loading) return <main className="ad-page ad-draft-editor" aria-busy="true"><div className="ad-skeleton" role="status">Loading the exact Activity Definition {draftId ? "draft" : "version"}…</div></main>;
  if (failed || !initialDraft || initialDraft.definitionId !== definitionId || version && version.versionId !== (ownClone?.version.versionId ?? versionId)) return <main className="ad-page ad-draft-editor"><button type="button" className="ad-back" onClick={() => onBack()}><ArrowLeft size={16} /> {onManage ? "Activity Definitions" : "Activity Definition"}</button><StudioResultState tone="danger" headingLevel={1} icon={<AlertTriangle size={22} aria-hidden />} title={<>Activity {draftId ? "draft" : "version"} unavailable</>} description={<>Studio could not confirm the exact authorized {draftId ? "draft" : "version"}. No provider state is shown.</>} actions={<><StudioButton type="button" onClick={() => void (draftId ? query.refetch() : versionQuery.refetch())}>Retry</StudioButton></>} /></main>;

  const designAuthoringAllowed = !definitionQuery.isError && definitionQuery.data?.definition.contentAuthority.kind.toLowerCase() === "design" && definitionQuery.data?.actions.some(action => action.action === "create-draft" && action.allowed) === true;
  return <LoadedActivityDefinitionDraftEditor agentWorkspace={agentWorkspace} key={`${scope}:${definitionId}:${ownClone || !draftId ? `version:${version?.versionId}` : `draft:${draftId}`}`} context={context} initialDraft={initialDraft} initialVersion={version} readOnly={readOnly} allowStartEditing={allowStartEditing && designAuthoringAllowed && !readOnly} openPublicationReviewOnMount={openPublicationReviewOnMount} onManage={onManage} onDraftStarted={draft => {
    if (!version) return;
    queryClient.setQueryData(activityDesignKeys.fullDefinitionDraft(draft.draftId), draft);
    setStarted({ draft, version, scope });
    onDraftStarted?.(draft);
    onOpenDraft(definitionId, draft.draftId);
  }} blockAuthoringAllowed={designAuthoringAllowed} definitionDisplayName={definitionQuery.data?.definition.displayName?.trim() || version?.definition.displayName?.trim() || undefined} definitionLabel={definitionQuery.data?.definition.displayName?.trim() || version?.definition.displayName?.trim() || initialDraft.presentationLabel?.trim() || generatedDraftLabel(initialDraft)} activityEditors={activityEditors} propertyEditors={propertyEditors} expressionEditors={expressionEditors} graphAuthoringPanels={graphAuthoringPanels} inputEditors={inputEditors} recoverySettings={recoverySettings} identity={identity} onNavigationGuardChange={onNavigationGuardChange} onBack={onBack} onOpenDraft={onOpenDraft} onOpenVersion={onOpenVersion} onOpenStudioPath={onOpenStudioPath} />;
}

function LoadedActivityDefinitionDraftEditor({ agentWorkspace, context, initialDraft, initialVersion, allowStartEditing, readOnly, openPublicationReviewOnMount, onManage, onDraftStarted, blockAuthoringAllowed, definitionDisplayName, definitionLabel, activityEditors, propertyEditors, expressionEditors, graphAuthoringPanels, inputEditors, recoverySettings, identity, onNavigationGuardChange, onBack, onOpenDraft, onOpenVersion, onOpenStudioPath }: {
  agentWorkspace: boolean;
  context: StudioEndpointContext;
  initialDraft: ActivityDefinitionDraftView;
  initialVersion?: ActivityDefinitionVersionView;
  allowStartEditing: boolean;
  readOnly: boolean;
  openPublicationReviewOnMount: boolean;
  onManage?(section: ManagementSection): void;
  onDraftStarted(draft: ActivityDefinitionDraftView): void;
  blockAuthoringAllowed: boolean;
  definitionDisplayName?: string;
  definitionLabel: string;
  activityEditors: StudioActivityDefinitionImplementationEditorContribution[];
  propertyEditors: StudioActivityPropertyEditorContribution[];
  expressionEditors: StudioExpressionEditorContribution[];
  graphAuthoringPanels: StudioWorkflowDesignerPanelContribution[];
  inputEditors: StudioWorkflowRunInputEditorContribution[];
  recoverySettings?: StudioActivityDefinitionRecoverySettings;
  identity?: StudioRuntimeIdentity;
  onNavigationGuardChange(blocked: boolean): void;
  onBack(force?: boolean): void;
  onOpenDraft(definitionId: string, draftId: string): void;
  onOpenVersion(definitionId: string, versionId: string): void;
  onOpenStudioPath(path: string): void;
}) {
  const initialDraftIsActive = isActiveDraft(initialDraft.status);
  const [draft, setDraft] = useState(initialDraft);
  const [publishedVersion, setPublishedVersion] = useState(initialVersion && !initialDraftIsActive ? initialVersion.version : null);
  const sourceVersionRef = useRef(initialDraft.draftId ? null : initialVersion);
  const startAllowedRef = useRef(allowStartEditing);
  startAllowedRef.current = allowStartEditing;
  const readOnlyRef = useRef(readOnly);
  readOnlyRef.current = readOnly;
  const startingRef = useRef(false);
  const [starting, setStarting] = useState(false);
  const [startFailure, setStartFailure] = useState(false);
  const layoutEditedRef = useRef(false);
  const mountedRef = useRef(true);
  const whenActive = useStudioWorkspaceWhenActive();
  const captureNotificationGuard = useNotificationGuard(context);
  const historyIdentity = useRef(initialVersion ? `version:${initialVersion.versionId}` : initialDraft.draftId).current;
  useEffect(() => { mountedRef.current = true; return () => { mountedRef.current = false; }; }, []);
  useStudioWorkspaceTitle(definitionLabel, isActiveDraft(draft.status) ? "Draft" : "Published");
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
  const [proposalApplying, setProposalApplying] = useState(false);
  const [publicationPending, setPublicationPending] = useState(false);
  const [migrationOpen, setMigrationOpen] = useState(false);
  const testRunTriggerRef = useRef<HTMLButtonElement>(null);
  const [testRunOpen, setTestRunOpen] = useState(false);
  const [boundaryMappingsRequested, setBoundaryMappingsRequested] = useState(false);
  const [activeView, setActiveView] = useState<ActivityDefinitionAuthoringView>("designer");
  const [canvasToolsHost, setCanvasToolsHost] = useState<HTMLElement | null>(null);
  const [authoringTabsHost, setAuthoringTabsHost] = useState<HTMLDivElement | null>(null);
  const [authoringViewHost, setAuthoringViewHost] = useState<HTMLDivElement | null>(null);
  const authoringTabFocusTransferRef = useRef(false);
  const authoringTabsId = useId();
  const draftDiagnosticsId = useId();
  const [codeBufferState, setCodeBufferState] = useState<ActivityDefinitionDraftCodeBufferState>({
    dirty: false,
    valid: true
  });
  const capabilitiesQuery = useActivityAuthoringCapabilities(context);
  const expressionsQuery = useActivityContractExpressionDescriptors(context);
  const baselineQuery = useFullActivityDefinitionVersion(context, initialVersion ? null : initialDraft.sourceVersionId ?? null);
  const recoveryStore = useMemo(() => createActivityDefinitionRecoveryStore(recoverySettings, identity), [identity, recoverySettings]);
  const [recovery, setRecovery] = useState(() => initialDraftIsActive ? recoveryStore?.read(initialDraft) ?? null : null);
  const currentRef = useRef(draft);
  const publicationPendingRef = useRef(false);
  const revisionRef = useRef(draft.revision);
  const savedSignatureRef = useRef(editableSignature(draft));
  const autosavePausedRef = useRef(!initialDraftIsActive);
  const queueRef = useRef(Promise.resolve());
  const timerRef = useRef<number | null>(null);
  const codeApplyHistoryRef = useRef<HistoryState<ActivityDefinitionDraftView>>(createHistory());
  const [, setCodeApplyHistoryVersion] = useState(0);
  const contractEditorRef = useRef<HTMLDivElement | null>(null);
  const contractEditorHandle = useRef<ActivityDefinitionContractEditorHandle | null>(null);
  const providerEditorRef = useRef<HTMLElement | null>(null);
  const variablesEditorRef = useRef<HTMLElement | null>(null);
  const diagnosticsPanelRef = useRef<HTMLElement | null>(null);
  const providerPublicInterfaceRef = useRef<HTMLElement | null>(null);
  const diagnosticReturnRef = useRef<HTMLButtonElement | null>(null);
  const publicationToolbarRef = useRef<HTMLButtonElement | null>(null);
  const publicationNotificationGuardRef = useRef<(() => boolean) | null>(null);
  const diagnosticFocusRequestRef = useRef(0);
  const setPublicationPendingLock = useCallback((pending: boolean) => {
    publicationPendingRef.current = pending;
    setPublicationPending(pending);
    publicationNotificationGuardRef.current = pending ? captureNotificationGuard() : null;
  }, [captureNotificationGuard]);
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
    if (isActiveDraft(draft.status)) return;
    if (timerRef.current !== null) window.clearTimeout(timerRef.current);
    timerRef.current = null;
    autosavePausedRef.current = true;
    if (draft.draftId) recoveryStore?.clear(draft);
    setRecovery(null);
  }, [draft, recoveryStore]);

  useEffect(() => {
    setRecoveryReviewed(false);
  }, [recovery]);

  useEffect(() => {
    const blocked = publicationPending || status !== "saved" || !contractLocallyValid || proposalApplying || codeBufferState.dirty;
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
  }, [codeBufferState.dirty, contractLocallyValid, draft.status, onNavigationGuardChange, publicationPending, proposalApplying, status]);

  useEffect(() => () => {
    if (timerRef.current !== null) window.clearTimeout(timerRef.current);
    diagnosticFocusRequestRef.current += 1;
  }, []);

  const resetCodeApplyHistory = () => {
    if (!canUndo(codeApplyHistoryRef.current) && !canRedo(codeApplyHistoryRef.current)) return;
    codeApplyHistoryRef.current = createHistory();
    setCodeApplyHistoryVersion(version => version + 1);
  };

  const startDraft = async () => {
    const source = sourceVersionRef.current;
    if (readOnlyRef.current || !source || !startAllowedRef.current || startingRef.current || currentRef.current.draftId) return;
    startingRef.current = true;
    setStarting(true);
    setStartFailure(false);
    setStatus("saving");
    try {
      const created = await createActivityDefinitionDraft(context, source.definition.definitionId, { sourceVersionId: source.versionId, presentationLabel: null });
      await new Promise<void>(resolve => whenActive(resolve));
      if (!mountedRef.current) return;
      if (!created.draftId || created.definitionId !== source.definition.definitionId || created.sourceVersionId !== source.versionId || !isActiveDraft(created.status) || created.provider.providerKey !== source.provider.providerKey || created.provider.schemaVersion !== source.provider.schemaVersion) throw new Error("The exact source clone could not be confirmed.");
      const local = currentRef.current;
      const next = {
        ...created,
        contract: local.contract,
        provider: { ...created.provider, payload: local.provider.payload },
        layout: layoutEditedRef.current ? local.layout : created.layout,
        activityPresentation: local.activityPresentation ?? created.activityPresentation ?? [],
        presentationLabel: local.presentationLabel ?? null
      };
      if (!layoutEditedRef.current) {
        const bindLayout = (snapshot: ActivityDefinitionDraftView) => ({ ...snapshot, layout: created.layout });
        codeApplyHistoryRef.current = {
          past: codeApplyHistoryRef.current.past.map(bindLayout),
          future: codeApplyHistoryRef.current.future.map(bindLayout)
        };
      }
      sourceVersionRef.current = null;
      revisionRef.current = created.revision;
      savedSignatureRef.current = editableSignature(created);
      autosavePausedRef.current = false;
      currentRef.current = next;
      setDraft(next);
      setStatus("pending");
      recoveryStore?.write(next);
      enqueueSave(next);
      onDraftStarted(created);
    } catch {
      await new Promise<void>(resolve => whenActive(resolve));
      if (!mountedRef.current) return;
      setStartFailure(true);
      setStatus("failed");
    } finally {
      startingRef.current = false;
      if (mountedRef.current) setStarting(false);
    }
  };

  const scheduleSave = (
    next: ActivityDefinitionDraftView,
    options: { preserveCodeApplyHistory?: boolean } = {}
  ) => {
    if (readOnlyRef.current || publicationPendingRef.current) return;
    const current = currentRef.current;
    const unstarted = !current.draftId && Boolean(sourceVersionRef.current);
    if (unstarted ? !startAllowedRef.current : autosavePausedRef.current || !isActiveDraft(current.status)) return;
    next = { ...current, contract: next.contract, provider: { ...current.provider, payload: next.provider.payload }, layout: next.layout, activityPresentation: next.activityPresentation, presentationLabel: next.presentationLabel };
    if (editableSignature(next) === editableSignature(current)) return;
    if (unstarted && JSON.stringify(next.layout) !== JSON.stringify(current.layout)) layoutEditedRef.current = true;
    if (!options.preserveCodeApplyHistory) resetCodeApplyHistory();
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
    if (unstarted) {
      void startDraft();
      return;
    }
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
      if (readOnlyRef.current || publicationPendingRef.current || autosavePausedRef.current || !snapshot.draftId || !isActiveDraft(currentRef.current.status)) return;
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
          activityPresentation: snapshot.activityPresentation ?? [],
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

  const updateImplementation = (value: StudioActivityDefinitionImplementationState) => {
    if (readOnlyRef.current || publicationPendingRef.current || proposalApplying) return;
    scheduleSave({
      ...currentRef.current,
      provider: { ...currentRef.current.provider, payload: value.payload },
      layout: value.layout,
      activityPresentation: value.activityPresentation ?? currentRef.current.activityPresentation ?? []
    });
  };
  const updateContract = (contract: ActivityDefinitionDraftView["contract"]) => {
    if (readOnlyRef.current || publicationPendingRef.current || proposalApplying) return;
    scheduleSave({
      ...currentRef.current,
      contract
    });
  };
  const updateLocalContractValidity = useCallback((valid: boolean) => setContractLocallyValid(valid), []);
  const acceptAppliedProposal = useCallback((saved: ActivityDefinitionDraftView) => {
    if (readOnlyRef.current || publicationPendingRef.current || !isActiveDraft(currentRef.current.status)) return;
    revisionRef.current = saved.revision;
    savedSignatureRef.current = editableSignature(saved);
    currentRef.current = saved;
    autosavePausedRef.current = false;
    setDraft(saved);
    setStatus("saved");
    setValidationFailure(null);
    setFocusAnnouncement(null);
    codeApplyHistoryRef.current = createHistory();
    setCodeApplyHistoryVersion(version => version + 1);
    recoveryStore?.clear(saved);
    setRecovery(null);
  }, [recoveryStore]);

  const saveNow = () => {
    if (!currentRef.current.draftId) { void startDraft(); return; }
    if (readOnlyRef.current || publicationPendingRef.current || !isActiveDraft(currentRef.current.status)) return;
    if (timerRef.current !== null) window.clearTimeout(timerRef.current);
    timerRef.current = null;
    enqueueSave(currentRef.current);
  };

  const createConflictCopy = async () => {
    if (readOnlyRef.current || status !== "conflict" || conflictRevision === null || copying) return;
    setCopying(true);
    setCopyError(false);
    const local = currentRef.current;
    try {
      const copy = await createActivityDefinitionConflictCopy(context, local.draftId, {
        expectedSourceRevision: conflictRevision,
        contract: local.contract,
        provider: { providerKey: local.provider.providerKey, schemaVersion: local.provider.schemaVersion, payload: local.provider.payload },
        layout: local.layout,
        activityPresentation: local.activityPresentation ?? [],
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
    if (readOnlyRef.current || !recovery || copying) return;
    setCopyError(false);
    const restored = {
      ...currentRef.current,
      contract: recovery.contract,
      provider: { ...currentRef.current.provider, payload: recovery.payload },
      layout: recovery.layout,
      activityPresentation: recovery.activityPresentation ?? [],
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
        activityPresentation: recovery.activityPresentation ?? [],
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
    if (readOnlyRef.current || publicationPendingRef.current || !isActiveDraft(currentRef.current.status) || status === "conflict" || !contractLocallyValid || validating) return;
    diagnosticFocusRequestRef.current += 1;
    diagnosticReturnRef.current = null;
    setValidating(true);
    setValidationFailure(null);
    setFocusAnnouncement(null);
    setDraft(current => ({ ...current, validation: null }));
    try {
      await requestExactDraftValidation();
    } finally {
      setValidating(false);
    }
  };

  const flushExactSavedRevision = async () => {
    if (readOnlyRef.current || publicationPendingRef.current || !isActiveDraft(currentRef.current.status)) return null;
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

  const requestExactDraftValidation = async (
    onSaved?: (revision: number) => void
  ) => {
    if (readOnlyRef.current || publicationPendingRef.current) return null;
    let validationRevision: number | null = null;
    let validationSignature: string | null = null;
    try {
      const saved = await flushExactSavedRevision();
      if (!saved) {
        setValidationFailure("transport");
        return null;
      }
      validationRevision = saved.revision;
      validationSignature = editableSignature(currentRef.current);
      onSaved?.(validationRevision);
      const validation = await validateActivityDefinitionDraft(
        context,
        currentRef.current.draftId,
        validationRevision);
      if (
        revisionRef.current !== validationRevision ||
        editableSignature(currentRef.current) !== validationSignature
      ) return null;
      setDraft(current => ({ ...current, validation }));
      return validation;
    } catch (error) {
      if (validationRevision !== null && (
        revisionRef.current !== validationRevision ||
        editableSignature(currentRef.current) !== validationSignature
      )) return null;
      if (isStaleRevision(error)) {
        autosavePausedRef.current = true;
        setConflictRevision(readCurrentRevision(error) ?? revisionRef.current);
        setStatus("conflict");
        setValidationFailure("rejected");
      } else {
        setValidationFailure(classifyValidationFailure(error));
      }
      return null;
    }
  };

  const prepareExactTestRunRevision = async (
    onPhase: (phase: "saving" | "validating") => void
  ): Promise<PreparedActivityTestRunRevision | null> => {
    if (readOnlyRef.current || publicationPendingRef.current || !isActiveDraft(currentRef.current.status) || status === "conflict" || !contractLocallyValid || validating || proposalApplying || codeBufferState.dirty) return null;
    diagnosticFocusRequestRef.current += 1;
    diagnosticReturnRef.current = null;
    setValidating(true);
    setValidationFailure(null);
    setFocusAnnouncement(null);
    setDraft(current => ({ ...current, validation: null }));
    onPhase("saving");
    try {
      const validation = await requestExactDraftValidation(() => onPhase("validating"));
      return validation ? { revision: validation.revision, validation } : null;
    } finally {
      setValidating(false);
    }
  };

  const [outputNavigationFailure, setOutputNavigationFailure] = useState<string | null>(null);
  const revealOutputStep = async (nodeId: string): Promise<StudioActivityDiagnosticFocusResult> => {
    setOutputNavigationFailure(null);
    setActiveView("designer");
    await waitForAuthoringView();
    let result = unsupportedDiagnosticFocus();
    try {
      if (providerEditorRef.current && contribution?.focusDiagnosticLocation) result = await contribution.focusDiagnosticLocation({
        location: { providerKey: draft.provider.providerKey, jsonPointer: "/rootActivity/inputs", referenceKey: nodeId },
        subject: { kind: "ActivityDraft", id: draft.draftId }, editorElement: providerEditorRef.current
      });
    } catch { /* Preserve the graph and explain unsupported or stale navigation. */ }
    if (result.kind === "focused") {
      setFocusAnnouncement("Opened the Set Output step in the designer.");
    } else {
      setActiveView("outputs");
      setOutputNavigationFailure("This step could not be opened. The graph is unchanged. Open Designer to find the step or reload this draft if it changed elsewhere.");
    }
    return result;
  };

  const focusDiagnostic = async (diagnostic: StudioActivityDiagnostic, trigger: HTMLButtonElement): Promise<StudioActivityDiagnosticFocusResult> => {
    const request = ++diagnosticFocusRequestRef.current;
    diagnosticReturnRef.current = trigger;
    const location = diagnostic.location;
    let result: StudioActivityDiagnosticFocusResult;
    if (!location) {
      result = unsupportedDiagnosticFocus();
    } else if (isContractDiagnostic(location.providerKey, location.jsonPointer, location.referenceKey, draft)) {
      setActiveView(contractDiagnosticView(location.jsonPointer, location.referenceKey, draft.contract));
      await waitForAuthoringView();
      if (request !== diagnosticFocusRequestRef.current) return unsupportedDiagnosticFocus();
      result = focusContractDiagnostic(contractEditorRef.current, location.jsonPointer, location.referenceKey, contractEditorHandle.current);
    } else if (location.providerKey === draft.provider.providerKey && location.jsonPointer?.startsWith("/variables")) {
      setActiveView("variables");
      await waitForAuthoringView();
      if (request !== diagnosticFocusRequestRef.current) return unsupportedDiagnosticFocus();
      const index = /^\/variables\/(0|[1-9][0-9]{0,3})(?:\/|$)/.exec(location.jsonPointer)?.[1];
      const target = await waitForSettingTarget(() => index === undefined ? variablesEditorRef.current : variablesEditorRef.current?.querySelector<HTMLElement>(`[data-variable-index="${index}"] [data-variable-default]`) ?? null,
        () => request === diagnosticFocusRequestRef.current);
      if (request !== diagnosticFocusRequestRef.current) return unsupportedDiagnosticFocus();
      result = focusSettingControl(target, "variable initial value");
    } else if (
      location.providerKey === draft.provider.providerKey &&
      isBoundaryMappingPointer(location.jsonPointer)
    ) {
      setBoundaryMappingsRequested(true);
      setActiveView(location.jsonPointer?.startsWith("/outputMappings") ? "outputs" : "outcomes");
      await waitForAuthoringView();
      if (request !== diagnosticFocusRequestRef.current) return unsupportedDiagnosticFocus();
      if (draft.provider.providerKey === "elsa.activity-graph" && location.jsonPointer?.startsWith("/outputMappings")) {
        const index = Number(location.jsonPointer.split("/")[2]);
        const mappings = (draft.provider.payload as { outputMappings?: { outputReferenceKey: string }[] }).outputMappings;
        let key = location.referenceKey || (Number.isInteger(index) ? mappings?.[index]?.outputReferenceKey : null);
        if (!key && diagnostic.code === "activity.graph.set-output-mapping-conflict") {
          const root = activityGraphDocumentAdapter.readRoot({ payload: draft.provider.payload, layout: draft.layout });
          const setters = getActivityGraphOutputSetters(root, new Map());
          const conflicts = draft.contract.outputs.filter(output => mappings?.some(mapping => mapping.outputReferenceKey === output.referenceKey) && setters.some(setter => setter.outputName === output.name));
          if (conflicts.length === 1) key = conflicts[0].referenceKey;
        }
        if (key) contractEditorHandle.current?.revealMember("output", key);
        await waitForAuthoringView();
        const target = key ? [...(contractEditorRef.current?.querySelectorAll<HTMLElement>(".ad-output-value-source") ?? [])].find(element => element.dataset.outputReferenceKey === key) : contractEditorRef.current;
        if (target) { target.tabIndex = -1; target.focus(); target.scrollIntoView?.({ block: "center" }); result = { kind: "focused", announcement: key ? "Focused the output value source." : "Focused Outputs. Select an output to review its value source; this diagnostic does not identify a single output." }; }
        else result = unsupportedDiagnosticFocus();
      } else {
        await waitForSettingTarget(() => providerPublicInterfaceRef.current?.querySelector<HTMLElement>("[data-output-source], .ad-graph-outcome-mappings") ?? null, () => request === diagnosticFocusRequestRef.current);
        if (request !== diagnosticFocusRequestRef.current) return unsupportedDiagnosticFocus();
        result = focusBoundaryMappingDiagnostic(providerPublicInterfaceRef.current, location.jsonPointer, draft.provider.payload);
      }
    } else if (
      location.providerKey === draft.provider.providerKey &&
      contribution?.focusDiagnosticLocation
    ) {
      setActiveView("designer");
      await waitForAuthoringView();
      if (request !== diagnosticFocusRequestRef.current) return unsupportedDiagnosticFocus();
      try {
        result = providerEditorRef.current
          ? await contribution.focusDiagnosticLocation({
              location,
              subject: diagnostic.subject,
              editorElement: providerEditorRef.current
            })
          : unsupportedDiagnosticFocus();
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
    for (let parent = target?.parentElement; parent; parent = parent.parentElement) {
      if (parent instanceof HTMLDetailsElement) parent.open = true;
    }
    target?.focus();
  };

  const Editor = contribution?.component;
  const PublicInterfaceEditor = contribution?.publicInterfaceComponent;
  const draftIsActive = isActiveDraft(draft.status);
  const canStartEditing = !draft.draftId && Boolean(sourceVersionRef.current) && allowStartEditing && hasPayload && Boolean(contribution && Editor) && !providerEditorFailed;
  const documentEditable = !readOnly && (draftIsActive || canStartEditing);
  const providerReadOnly = !documentEditable || publicationPending || status === "conflict" || proposalApplying;
  const canEditProvider = !readOnly && draftIsActive && !publicationPending && hasPayload && Boolean(contribution && Editor) && !providerEditorFailed;
  const revisionSensitiveActionsBlocked = publicationPending || status !== "saved" || !contractLocallyValid || proposalApplying || codeBufferState.dirty;
  const dependentActionsBlocked = readOnly || publicationPending || Boolean(recovery) || !draftIsActive || !contractLocallyValid || status === "conflict" || validating || proposalApplying || codeBufferState.dirty;
  const graphDesignerViewActive = activeView === "designer" && draft.provider.providerKey === "elsa.activity-graph";
  const hasGraphVariables = hasPayload && Boolean(contribution) && draft.provider.providerKey === "elsa.activity-graph" && ["1", "2"].includes(draft.provider.schemaVersion);
  const authoringViewTabIndices: Record<ActivityDefinitionAuthoringView, number> = {
    designer: 0,
    inputs: 1,
    outputs: 2,
    outcomes: 3,
    variables: 4,
    code: hasGraphVariables ? 5 : 4
  };
  const activeViewTabIndex = authoringViewTabIndices[activeView];
  const authoringTabs: StudioTabItem[] = [
    { id: "designer", label: "Designer", icon: <WorkflowIcon size={14} />, disabled: !contractLocallyValid && activeView !== "designer" },
    { id: "inputs", label: `Inputs (${draft.contract.inputs.length})`, icon: <LogIn size={14} />, panelIndex: 1 },
    { id: "outputs", label: `Outputs (${draft.contract.outputs.length})`, icon: <LogOut size={14} />, panelIndex: 1 },
    { id: "outcomes", label: `Outcomes (${draft.contract.outcomes.length})`, icon: <ListTree size={14} />, panelIndex: 1 },
    ...(hasGraphVariables ? [{ id: "variables", label: "Variables", icon: <Variable size={14} />, disabled: !contractLocallyValid && activeView !== "variables", panelIndex: authoringViewTabIndices.variables }] : []),
    ...(activeView === "code" ? [{ id: "code", label: "Code", icon: <Code2 size={14} />, panelIndex: authoringViewTabIndices.code }] : [])
  ];
  const graphEditorAvailable = hasPayload && Boolean(contribution && Editor) && draft.provider.providerKey === "elsa.activity-graph" && !providerEditorFailed;
  const authoringTabsShouldBeHosted = graphEditorAvailable && Boolean(authoringTabsHost && authoringViewHost);
  const graphWorkbenchActive = graphDesignerViewActive || authoringTabsShouldBeHosted;
  const shouldPortalAuthoringTabs = authoringTabsShouldBeHosted && Boolean(authoringTabsHost);
  const authoringTabsHostContext = useMemo(() => ({
    activeView,
    designerPanelId: tabElementIds(authoringTabsId, authoringViewTabIndices.designer).panelId,
    designerTabId: tabElementIds(authoringTabsId, authoringViewTabIndices.designer).tabId,
    setTablistHost: setAuthoringTabsHost,
    setCenterViewHost: setAuthoringViewHost
  }), [activeView, authoringTabsId, authoringViewTabIndices.designer]);
  const selectAuthoringView = (id: string) => {
    authoringTabFocusTransferRef.current = true;
    setActiveView(id as ActivityDefinitionAuthoringView);
  };
  const authoringViewTablist = <StudioTabs
    tabs={authoringTabs}
    activeTab={activeView}
    onSelect={selectAuthoringView}
    ariaLabel="Activity Definition authoring views"
    baseId={authoringTabsId}
    className="ad-authoring-view-tabs"
    compact
  />;
  useLayoutEffect(() => {
    if (!authoringTabFocusTransferRef.current || (graphEditorAvailable && (!authoringTabsHost || !authoringViewHost))) return;
    const tab = document.getElementById(tabElementIds(authoringTabsId, activeViewTabIndex).tabId);
    if (!tab) return;
    tab.focus();
    authoringTabFocusTransferRef.current = false;
  }, [activeView, activeViewTabIndex, authoringTabsHost, authoringTabsId, authoringViewHost, graphEditorAvailable]);
  const updateCodeBufferState = useCallback((next: ActivityDefinitionDraftCodeBufferState) => {
    setCodeBufferState(current => current.dirty === next.dirty && current.valid === next.valid ? current : next);
  }, []);
  const applyCodeDraft = (next: ActivityDefinitionDraftView) => {
    if (readOnlyRef.current || publicationPendingRef.current || !documentEditable) return;
    codeApplyHistoryRef.current = pushSnapshot(
      codeApplyHistoryRef.current,
      structuredClone(currentRef.current)
    );
    setCodeApplyHistoryVersion(version => version + 1);
    scheduleSave(next, { preserveCodeApplyHistory: true });
  };
  const undoCodeApply = () => {
    if (readOnlyRef.current || publicationPendingRef.current || !documentEditable) return;
    const step = undo(codeApplyHistoryRef.current, structuredClone(currentRef.current));
    if (!step) return;
    codeApplyHistoryRef.current = step.history;
    setCodeApplyHistoryVersion(version => version + 1);
    scheduleSave(step.snapshot, { preserveCodeApplyHistory: true });
  };
  const redoCodeApply = () => {
    if (readOnlyRef.current || publicationPendingRef.current || !documentEditable) return;
    const step = redo(codeApplyHistoryRef.current, structuredClone(currentRef.current));
    if (!step) return;
    codeApplyHistoryRef.current = step.history;
    setCodeApplyHistoryVersion(version => version + 1);
    scheduleSave(step.snapshot, { preserveCodeApplyHistory: true });
  };
  const discardLocalChanges = () => {
    if (readOnlyRef.current || publicationPendingRef.current || startingRef.current) return;
    if (timerRef.current !== null) window.clearTimeout(timerRef.current);
    timerRef.current = null;
    autosavePausedRef.current = true;
    if (currentRef.current.draftId) recoveryStore?.clear(currentRef.current);
    onNavigationGuardChange(false);
    onBack(true);
  };

  const markDraftPublished = (outcome: ActivityPublicationOutcome) => {
    const current = currentRef.current;
    if (!isActiveDraft(current.status)) return;
    autosavePausedRef.current = true;
    const published = { ...current, status: "published" };
    currentRef.current = published;
    setDraft(published);
    setPublishedVersion(outcome.version);
    setStatus("saved");
    setMigrationOpen(false);
    setTestRunOpen(false);
    recoveryStore?.clear(published);
    setRecovery(null);
    onNavigationGuardChange(false);
    const canNotify = publicationNotificationGuardRef.current;
    publicationNotificationGuardRef.current = null;
    if (canNotify?.()) getNotifications().notify({
      level: "success",
      title: `Published ${definitionLabel} ${outcome.version}`,
      link: {
        label: "View version",
        path: `/workflows/activity-definitions?definition=${encodeURIComponent(current.definitionId)}&section=versions&version=${encodeURIComponent(outcome.definitionVersionId)}`
      }
    });
  };

  const historyResetKey = `${historyIdentity}:${draft.provider.providerKey}:${draft.provider.schemaVersion}:${status === "conflict" ? `conflict-${conflictRevision ?? "unknown"}` : documentEditable ? "active" : "published"}`;
  const saveStatusNeedsAttention = status === "offline" || status === "failed" || !contractLocallyValid;
  const saveStatusDetail = draftIsActive || status !== "saved" ? <div className={`ad-save-state is-${contractLocallyValid ? status : "failed"}`} role={status === "failed" || status === "conflict" || !contractLocallyValid || status === "offline" ? "alert" : "status"} aria-live="polite"><strong>{startFailure ? "Your changes are still here" : contractLocallyValid ? saveStatusLabel(status, draft.revision) : "An input needs attention"}</strong><span>{startFailure ? "A draft could not be confirmed. Retry to save your changes; keep this editor open." : contractLocallyValid ? saveStatusDescription(status, revisionSensitiveActionsBlocked) : "Apply or correct the highlighted default value before leaving this editor, testing, or publishing."}</span>{startFailure ? <button type="button" onClick={saveNow} disabled={starting || !allowStartEditing}>Retry saving changes</button> : null}</div> : null;
  const visibleSaveStatus = saveStatusNeedsAttention ? saveStatusDetail : null;
  const advancedSaveStatus = saveStatusNeedsAttention || status === "conflict" ? null : saveStatusDetail;

  const viewDraftDiagnostics = () => {
    const panel = diagnosticsPanelRef.current;
    const details = panel?.querySelector<HTMLDetailsElement>("details");
    const summary = details?.querySelector<HTMLElement>("summary");
    if (details && !details.open) summary?.click();
    const first = panel?.querySelector<HTMLElement>(".ad-diagnostic-item[tabindex]");
    const target = first ?? panel;
    target?.focus();
    target?.scrollIntoView?.({ block: "center" });
  };
  const draftErrorCount = draft.validation?.diagnostics.filter(item => activityDiagnosticSeverity(item.severity) === "Error").length ?? 0;
  const diagnosticsLabel = codeBufferState.dirty ? "View draft diagnostics" : validationFailure ? "View validation problem" : draftErrorCount ? `View ${draftErrorCount} draft error${draftErrorCount === 1 ? "" : "s"}` : "View draft diagnostics";
  const saveChip = <span className={`ad-save-chip is-${draftIsActive && !contractLocallyValid ? "failed" : draftIsActive ? status : "published"}`}>{draftIsActive ? contractLocallyValid ? saveStatusLabel(status, draft.revision, false) : "Finish editing" : "Published"}</span>;
  const authoringViewPanels = <>
    <section id={tabElementIds(authoringTabsId, 1).panelId} className="ad-public-interface-view" role="tabpanel" aria-label={activeView === "outputs" ? "Outputs" : activeView === "outcomes" ? "Outcomes" : "Inputs"} aria-labelledby={tabElementIds(authoringTabsId, isContractView(activeView) ? authoringViewTabIndices[activeView] : 1).tabId} hidden={!isContractView(activeView)}>
      {activeView === "outputs" && outputNavigationFailure ? <p role="alert">{outputNavigationFailure}</p> : null}
      {capabilitiesQuery.isPending ? <div className="ad-inline-status" role="status">Loading available input and output types…</div> : <div ref={contractEditorRef}><ActivityDefinitionContractEditor
        ref={contractEditorHandle}
        memberKind={activeView === "outputs" ? "output" : activeView === "outcomes" ? "outcome" : "input"}
        contract={draft.contract}
        baselineContract={initialVersion?.contract ?? (baselineQuery.data?.definition.definitionId === draft.definitionId ? baselineQuery.data.contract : undefined)}
        capabilities={capabilitiesQuery.data}
        expressions={expressionsQuery.data ?? []}
        providerRequiredOutcomes={capabilitiesQuery.data?.providers.find(provider => provider.providerKey === draft.provider.providerKey)?.requiredOutcomes ?? []}
        sourceVersionId={draft.sourceVersionId}
        baselineUnavailable={baselineQuery.isError}
        readOnly={providerReadOnly}
        capabilitiesUnavailable={capabilitiesQuery.isError || !capabilitiesQuery.data}
        renderOutputValue={draft.provider.providerKey === "elsa.activity-graph" ? output => <ProviderEditorBoundary label="Output value editor" onFailure={() => setProviderEditorFailed(true)}><Suspense fallback={<p role="status">Loading value source…</p>}><ActivityGraphOutputValueEditor output={output} onRevealStep={revealOutputStep} context={context} definitionId={draft.definitionId} draftId={draft.draftId} revision={draft.revision} providerKey={draft.provider.providerKey} providerSchemaVersion={draft.provider.schemaVersion} manifestFingerprint={draft.provider.manifestFingerprint} contract={draft.contract} expressionEditors={expressionEditors} value={{ payload: draft.provider.payload, layout: draft.layout, activityPresentation: draft.activityPresentation ?? [] }} readOnly={providerReadOnly} onChange={updateImplementation} /></Suspense></ProviderEditorBoundary> : undefined}
        onChange={updateContract}
        onLocalValidityChange={updateLocalContractValidity}
      /></div>}
      {PublicInterfaceEditor && !(draft.provider.providerKey === "elsa.activity-graph" && activeView === "outputs") && (boundaryMappingsRequested || hasBoundaryMappings(draft.provider.payload) || draft.contract.outputs.length > 0 || draft.contract.outcomes.some(outcome => outcome.isEmitted)) ? <section ref={providerPublicInterfaceRef} className="ad-provider-public-interface" aria-label="Provider boundary mappings" hidden={activeView !== "outputs" && activeView !== "outcomes"}>
        <ProviderEditorBoundary label="Provider boundary mappings" onFailure={() => setProviderEditorFailed(true)}>
          <Suspense fallback={<div className="ad-inline-status" role="status" data-boundary-mappings-loading>Loading provider boundary mappings…</div>}>
            <PublicInterfaceEditor
              contractSection={activeView === "outputs" ? "outputs" : "outcomes"}
              context={context}
              definitionId={draft.definitionId}
              draftId={draft.draftId}
              revision={draft.revision}
              providerKey={draft.provider.providerKey}
              providerSchemaVersion={draft.provider.schemaVersion}
              manifestFingerprint={draft.provider.manifestFingerprint}
              contract={draft.contract}
              propertyEditors={propertyEditors}
              expressionEditors={expressionEditors}
              graphAuthoringPanels={graphAuthoringPanels}
              historyResetKey={historyResetKey}
              value={{ payload: draft.provider.payload, layout: draft.layout, activityPresentation: draft.activityPresentation ?? [] }}
              readOnly={providerReadOnly}
              onChange={updateImplementation}
            />
          </Suspense>
        </ProviderEditorBoundary>
      </section> : null}
      {draft.draftId ? <details className="ad-interface-suggestions"><summary>Suggestions from the designer</summary>
        <ActivityDefinitionContractProposalReview
          context={context}
          draft={draft}
          enabled={!readOnly && draftIsActive && !publicationPending && status === "saved" && contractLocallyValid && !validating && !proposalApplying && !codeBufferState.dirty}
          onApplyingChange={setProposalApplying}
          onApplied={acceptAppliedProposal}
          onFocusDiagnostic={focusDiagnostic}
        />
      </details> : null}
    </section>
    {hasGraphVariables ? <section ref={variablesEditorRef} id={tabElementIds(authoringTabsId, authoringViewTabIndices.variables).panelId} className="ad-variables-view" role="tabpanel" aria-label="Variables" aria-labelledby={tabElementIds(authoringTabsId, authoringViewTabIndices.variables).tabId} hidden={activeView !== "variables"}>
      {activeView === "variables" ? <>
        <p className="wf-muted">Private to each execution of this activity and available throughout its designer.</p>
        <ProviderEditorBoundary label="Variables editor" onFailure={() => setProviderEditorFailed(true)}>
          <Suspense fallback={<p role="status">Loading variables…</p>}>
            <ActivityGraphVariablesEditor context={context} value={{ payload: draft.provider.payload, layout: draft.layout, activityPresentation: draft.activityPresentation ?? [] }} readOnly={providerReadOnly} onChange={updateImplementation} />
          </Suspense>
        </ProviderEditorBoundary>
      </> : null}
    </section> : null}
    <section id={tabElementIds(authoringTabsId, authoringViewTabIndices.code).panelId} role="tabpanel" aria-label="Code" aria-labelledby={activeView === "code" ? tabElementIds(authoringTabsId, authoringViewTabIndices.code).tabId : undefined} hidden={activeView !== "code"}>
      <ActivityDefinitionDraftCodeView
        draft={draft}
        readOnly={providerReadOnly}
        canUndo={canUndo(codeApplyHistoryRef.current)}
        canRedo={canRedo(codeApplyHistoryRef.current)}
        onApply={applyCodeDraft}
        onUndo={undoCodeApply}
        onRedo={redoCodeApply}
        onBufferStateChange={updateCodeBufferState}
      />
    </section>
  </>;

  return <main className={`ad-page ad-draft-editor${graphWorkbenchActive ? " is-graph-designer" : ""}`} aria-labelledby="activity-draft-title">
    <div className="ad-draft-editor-toolbar">
      <WorkflowEditorToolbarFrame
        className="ad-workbench-header"
        identity={<><StudioShellBreadcrumbs
          items={[{ href: agentWorkspace ? "/agents" : "/workflows/activity-definitions", label: agentWorkspace ? "Agents" : onManage ? "Activity Definitions" : "Activity Definition" }]}
          current={draftHeaderTitle(definitionDisplayName, draft)}
          renderLink={item => <StudioLink href={item.href} aria-disabled={revisionSensitiveActionsBlocked || undefined} tabIndex={revisionSensitiveActionsBlocked ? -1 : undefined} onClick={event => {
            if (revisionSensitiveActionsBlocked) { event.preventDefault(); return; }
            if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
            event.preventDefault();
            onBack();
          }}>{item.label}</StudioLink>} />
          <span className="wf-editor-title"><strong id="activity-draft-title" role="heading" aria-level={1} title={draftHeaderTitle(definitionDisplayName, draft)}>{draftHeaderTitle(definitionDisplayName, draft)}</strong><span className="wf-chip">{draftIsActive ? "Draft" : "Published"}</span></span></>}
        metadata={<div className="ad-draft-context">
          {publishedVersion && !draftIsActive ? <><span>Version {publishedVersion}</span><span aria-hidden>·</span></> : !onManage || draftIsActive || status !== "saved" || codeBufferState.dirty ? <><span>{onManage ? "Unpublished changes" : draftIsActive ? "Draft" : "Published draft"}</span><span aria-hidden>·</span></> : null}
          {draft.validation || validationFailure || codeBufferState.dirty ? <button type="button" className={`ad-draft-diagnostics-link${draftErrorCount || validationFailure || codeBufferState.dirty ? " needs-attention" : ""}`} aria-label={diagnosticsLabel} title={diagnosticsLabel} aria-controls={draftDiagnosticsId} onClick={viewDraftDiagnostics}>
            {saveChip}{draftErrorCount || validationFailure || codeBufferState.dirty ? <><AlertTriangle size={14} aria-hidden /><span>{codeBufferState.dirty ? "Diagnostics" : validationFailure ? "Validation problem" : `${draftErrorCount} error${draftErrorCount === 1 ? "" : "s"}`}</span></> : null}
          </button> : draftIsActive ? saveChip : null}
        </div>}
        actions={<div className="ad-header-actions ad-authoring-toolbar-actions">
          {graphWorkbenchActive ? <span ref={setCanvasToolsHost} className="wf-canvas-command-host" /> : null}
          {draft.draftId ? <ActivityDefinitionPublicationReview
            context={context}
            definitionId={draft.definitionId}
            draftId={draft.draftId}
            openOnMount={openPublicationReviewOnMount}
            currentRevision={draft.revision}
            currentSignature={editableSignature(draft)}
            disabled={dependentActionsBlocked}
            flushExactSavedRevision={flushExactSavedRevision}
            onPublicationPending={setPublicationPendingLock}
            onPublished={markDraftPublished}
            onFocusDiagnostic={(diagnostic, trigger) => focusDiagnostic(diagnostic, publicationToolbarRef.current ?? trigger)}
            onOpenVersion={versionId => onOpenVersion(draft.definitionId, versionId)}
            onOpenWorkflows={() => onOpenStudioPath("/workflows/definitions")}
            mode="toolbar"
            triggerRef={publicationToolbarRef}
          /> : null}
          <StudioButton size="sm" ref={testRunTriggerRef} type="button" onClick={event => {
            event.currentTarget.closest(".ad-draft-editor-toolbar")?.querySelectorAll<HTMLDetailsElement>("details[open]").forEach(disclosure => {
              disclosure.open = false;
            });
            setTestRunOpen(true);
          }} disabled={dependentActionsBlocked}><Play size={16} /> Test Run</StudioButton>
          <StudioButton variant="primary" onClick={saveNow} disabled={!documentEditable || publicationPending || !contractLocallyValid || status === "saved" || status === "saving" || status === "conflict" || proposalApplying || codeBufferState.dirty}><RefreshCw size={16} /> Save now</StudioButton>
          {onManage ? <details className="ad-authoring-history" onKeyDown={event => {
            if (event.key !== "Escape") return;
            event.preventDefault();
            event.currentTarget.open = false;
            event.currentTarget.querySelector("summary")?.focus();
          }} onBlur={event => { if (!event.currentTarget.contains(event.relatedTarget as Node | null)) event.currentTarget.open = false; }}>
            <summary>History</summary>
            <div className="ad-authoring-history-panel">
              {([["drafts", "Drafts"], ["versions", "Versions"], ["relationships", "Used by"], ["overview", "Details"]] as const).map(([section, label]) => <button key={section} type="button" disabled={revisionSensitiveActionsBlocked} onClick={event => {
                const disclosure = event.currentTarget.closest("details");
                if (disclosure) { disclosure.open = false; disclosure.querySelector("summary")?.focus(); }
                onManage(section);
              }}>{label}</button>)}
            </div>
          </details> : null}
          <details className="ad-authoring-advanced">
            <summary>Advanced</summary>
            <div className="ad-authoring-advanced-panel">
              <dl className="ad-authoring-identity">
                {draft.draftId ? <div><dt>Draft ID</dt><dd><code>{draft.draftId}</code></dd></div> : <div><dt>Version ID</dt><dd><code>{initialVersion?.versionId}</code></dd></div>}
                <div><dt>Definition ID</dt><dd><code>{draft.definitionId}</code></dd></div>
                <div><dt>Provider</dt><dd><code>{draft.provider.providerKey}</code></dd></div>
                <div><dt>Provider schema</dt><dd>{draft.provider.schemaVersion}</dd></div>
                {draft.draftId ? <div><dt>Saved revision</dt><dd>{draft.revision}</dd></div> : <div><dt>Version</dt><dd>{initialVersion?.version}</dd></div>}
              </dl>
              <StudioField className="ad-draft-label" label={<><span>Draft label </span></>}><input value={draft.presentationLabel ?? ""} maxLength={200} disabled={readOnly || !draftIsActive || publicationPending || status === "conflict"} placeholder={generatedDraftLabel(draft)} onChange={event => scheduleSave({ ...currentRef.current, presentationLabel: event.target.value || null })} /><small>The generated fallback is derived from the stable draft identity.</small><small>Optional · need not be unique</small></StudioField>
              {advancedSaveStatus}
              <div className="ad-authoring-advanced-actions">
                <button type="button" onClick={async event => { setActiveView("code"); event.currentTarget.closest("details")?.removeAttribute("open"); await waitForAuthoringView(); document.getElementById(tabElementIds(authoringTabsId, authoringViewTabIndices.code).tabId)?.focus(); }} disabled={!contractLocallyValid}>Edit code</button>
                <button type="button" onClick={() => setMigrationOpen(true)} disabled={readOnly || !draftIsActive || revisionSensitiveActionsBlocked}>Migrate provider</button>
                <button type="button" onClick={() => void validateSavedRevision()} disabled={dependentActionsBlocked}><CheckCircle2 size={16} /> {validating ? "Saving & validating…" : "Validate saved revision"}</button>
              </div>
            </div>
          </details>
        </div>}
      />
    </div>
    {visibleSaveStatus}
    {!draftIsActive && !canStartEditing && status === "saved" ? <section className="ad-save-state is-published" role="status"><strong>{initialVersion ? `Version ${initialVersion.version} is read-only.` : `Published revision ${draft.revision} is read-only.`}</strong><span>{onManage ? "Open History to review versions and details." : "Use Activity Definition to review immutable versions or create another draft."}</span></section> : null}
    {shouldPortalAuthoringTabs && authoringTabsHost ? createPortal(authoringViewTablist, authoringTabsHost) : authoringViewTablist}
    {testRunOpen && !readOnly ? <Suspense fallback={<div className="ad-inline-status" role="status">Loading the Activity Definition Test Run workbench…</div>}><ActivityDefinitionTestRunDialog
      context={context}
      draft={draft}
      definitionLabel={definitionLabel}
      inputEditors={inputEditors}
      expressionEditors={expressionEditors}
      prepareExactRevision={prepareExactTestRunRevision}
      onFocusDiagnostic={focusDiagnostic}
      onClose={() => setTestRunOpen(false)}
      restoreFocus={() => testRunTriggerRef.current?.focus()}
      onOpenRun={onOpenStudioPath}
    /></Suspense> : null}
    {recovery ? <StudioAlert tone="warning"><div className="ad-recovery-card"><div><h2>Unsaved local recovery available</h2><p>Captured {formatRecoveryTime(recovery.capturedAt)} from saved revision {recovery.baseRevision}; it expires {formatRecoveryTime(recovery.expiresAt)}. {!canEditProvider ? "The exact provider editor is unavailable, so Studio will preserve the server draft and will not apply this opaque state." : recovery.baseRevision === draft.revision ? "Review the recovered content before applying; Studio never restores it silently." : `The server is now at revision ${draft.revision}, so recovery creates a parallel draft and never overwrites it.`}</p><details onToggle={event => { if (event.currentTarget.open) setRecoveryReviewed(true); }}><summary>Review recovered content</summary><pre>{formatRecoveryPreview(recovery)}</pre></details></div><div>{canEditProvider ? <StudioButton type="button" onClick={() => void applyRecovery()} disabled={copying || !recoveryReviewed}>{copying ? "Creating recovery draft…" : !recoveryReviewed ? "Review content before recovery" : recovery.baseRevision === draft.revision ? "Apply local recovery" : "Create recovery draft"}</StudioButton> : null}<StudioButton variant="danger" onClick={() => { recoveryStore?.clear(initialDraft); setRecovery(null); }} disabled={copying}>Discard recovery</StudioButton>{copyError ? <span>Recovery could not be confirmed. The local snapshot remains available.</span> : null}</div></div></StudioAlert> : null}
    {status === "conflict" ? <StudioAlert tone="warning"><div className="ad-conflict-card"><AlertTriangle size={20} /><div><h2>Local work preserved</h2><p>The server draft advanced to revision {conflictRevision}. Studio paused autosave and will not force overwrite or merge opaque provider state.</p><StudioButton type="button" onClick={() => void createConflictCopy()} disabled={readOnly || copying}><CopyPlus size={16} /> {copying ? "Creating recovery draft…" : "Create parallel recovery draft"}</StudioButton>{copyError ? <span>Recovery could not be confirmed. The local work remains in this editor.</span> : null}</div></div></StudioAlert> : null}
    {authoringTabsShouldBeHosted && authoringViewHost ? createPortal(authoringViewPanels, authoringViewHost) : authoringViewPanels}
    <section
      id={authoringTabsShouldBeHosted ? undefined : tabElementIds(authoringTabsId, 0).panelId}
      className="ad-designer-view"
      role={authoringTabsShouldBeHosted ? undefined : "tabpanel"}
      aria-label={authoringTabsShouldBeHosted ? undefined : "Designer"}
      aria-labelledby={authoringTabsShouldBeHosted ? undefined : tabElementIds(authoringTabsId, 0).tabId}
      hidden={!authoringTabsShouldBeHosted && activeView !== "designer"}
    >
    {!hasPayload ? <StudioResultState tone="danger" headingLevel={2} icon={<AlertTriangle size={22} aria-hidden />} title={<>Implementation payload unavailable</>} description={<>The exact provider payload was not disclosed. Studio preserves the server draft and does not invent provider state.</>} actions={<></>} /> : !contribution || !Editor ? <StudioResultState tone="danger" headingLevel={2} icon={<AlertTriangle size={22} aria-hidden />} title={<>Implementation editor unavailable</>} description={<>No exact Studio contribution is available for <code>{draft.provider.providerKey}</code> schema {draft.provider.schemaVersion}. The server draft is preserved and no fallback editor is invoked.</>} actions={<></>} /> : <section ref={providerEditorRef} className="ad-implementation-shell" aria-label="Provider implementation editor"><NativeBlockAuthorityProvider value={{
      sourceKey: JSON.stringify([draft, blockAuthoringAllowed, providerReadOnly, proposalApplying, publicationPending]),
      canEdit: Boolean(draft.draftId) && blockAuthoringAllowed && !providerReadOnly,
      ...(canStartEditing && !providerReadOnly ? { startEditing: () => { void startDraft(); }, startingEditing: starting } : {}),
      canApply: () => Boolean(currentRef.current.draftId) && blockAuthoringAllowed && !providerReadOnly && !publicationPendingRef.current && isActiveDraft(currentRef.current.status) && JSON.stringify(currentRef.current) === JSON.stringify(draft),
      async validate(candidate) {
        const source = currentRef.current;
        const signature = JSON.stringify(source);
        if (!blockAuthoringAllowed || providerReadOnly || proposalApplying || publicationPendingRef.current || !isActiveDraft(source.status)) return { state: "denied", messages: [] };
        const result = await validateActivityDefinitionCandidate(context, source.draftId, {
          expectedRevision: revisionRef.current, contract: source.contract,
          provider: { providerKey: source.provider.providerKey, schemaVersion: source.provider.schemaVersion, payload: candidate.payload },
          layout: candidate.layout, activityPresentation: candidate.activityPresentation ?? []
        });
        if (signature !== JSON.stringify(currentRef.current) || !isActiveDraft(currentRef.current.status)) return { state: "stale", messages: [] };
        return { state: result.state, messages: result.validation?.diagnostics.map(item => `${item.code}: ${item.message}`) ?? [] };
      }
    }}><WorkflowCanvasToolsHostContext.Provider value={graphWorkbenchActive ? canvasToolsHost : null}><ActivityDefinitionAuthoringTabsHostContext.Provider value={authoringTabsHostContext}><ProviderEditorBoundary key={`${historyIdentity}:${draft.provider.providerKey}:${draft.provider.schemaVersion}`} label="Implementation editor" onFailure={() => setProviderEditorFailed(true)}><Suspense fallback={<div className="ad-inline-status" role="status">Loading the exact provider editor…</div>}><Editor context={context} definitionId={draft.definitionId} draftId={draft.draftId} revision={draft.revision} providerKey={draft.provider.providerKey} providerSchemaVersion={draft.provider.schemaVersion} manifestFingerprint={draft.provider.manifestFingerprint} contract={draft.contract} propertyEditors={propertyEditors} expressionEditors={expressionEditors} graphAuthoringPanels={graphAuthoringPanels} historyResetKey={historyResetKey} value={{ payload: draft.provider.payload, layout: draft.layout, activityPresentation: draft.activityPresentation ?? [] }} readOnly={providerReadOnly} onChange={updateImplementation} /></Suspense></ProviderEditorBoundary></ActivityDefinitionAuthoringTabsHostContext.Provider></WorkflowCanvasToolsHostContext.Provider></NativeBlockAuthorityProvider></section>}
    </section>
    <ActivityDefinitionDiagnosticsPanel
      panelRef={diagnosticsPanelRef}
      id={draftDiagnosticsId}
      validation={draft.validation}
      failure={validationFailure}
      localDiagnostics={codeBufferState.dirty ? [{
        area: "JSON",
        severity: codeBufferState.valid ? "warning" : "error",
        message: codeBufferState.valid
          ? "The valid JSON buffer is unapplied. Apply or Reset it before validation, testing, migration, publication, or navigation."
          : "The JSON buffer is invalid and remains local. Correct or Reset it before validation, testing, migration, publication, or navigation."
      }] : []}
      focusAnnouncement={focusAnnouncement}
      canReturn={Boolean(diagnosticReturnRef.current)}
      onFocus={focusDiagnostic}
      onReturn={returnToDiagnostic}
    />
    {revisionSensitiveActionsBlocked && contractLocallyValid && (status === "failed" || status === "offline" || status === "conflict" || codeBufferState.dirty) ? <div className="ad-revision-gate" role="status"><span>Revision-sensitive lifecycle actions and navigation are paused until this exact draft revision is saved.</span><StudioButton variant="danger" onClick={discardLocalChanges} disabled={status === "pending" || status === "saving"}>{status === "pending" || status === "saving" ? "Waiting for save before navigation" : "Discard local changes and return"}</StudioButton></div> : null}
    {migrationOpen && !readOnly ? <ActivityDefinitionProviderMigrationDialog context={context} draft={draft} activityEditors={activityEditors} onClose={() => setMigrationOpen(false)} onCreated={created => { setMigrationOpen(false); onOpenDraft(created.definitionId, created.draftId); }} /> : null}
  </main>;
}

class ProviderEditorBoundary extends Component<{ children: ReactNode; label: string; onFailure(): void }, { error: Error | null }> {
  state = { error: null as Error | null };

  static getDerivedStateFromError(error: unknown) {
    return { error: error instanceof Error ? error : new Error(String(error)) };
  }

  componentDidCatch() {
    this.props.onFailure();
  }

  render() {
    if (this.state.error) {
      const assetFailure = isWorkflowChunkLoadFailure(this.state.error);
      return <StudioResultState tone="danger" headingLevel={2} icon={<AlertTriangle size={22} aria-hidden />} title={<>{this.props.label} unavailable</>} description={assetFailure ? "The provider editor could not load its Studio assets. Reload this draft to fetch the latest assets; unsaved work remains protected by the save and navigation guard." : "The exact provider editor failed to render. Studio kept the draft shell and any local recovery state intact."} actions={<>{assetFailure ? <StudioButton type="button" onClick={() => window.location.reload()}>Reload draft</StudioButton> : null}</>} />;
    }
    return this.props.children;
  }
}

function publishedEditorDocument(version: ActivityDefinitionVersionView): ActivityDefinitionDraftView {
  return {
    draftId: "",
    definitionId: version.definition.definitionId,
    tenantId: version.definition.tenantId,
    revision: 0,
    sourceVersionId: version.versionId,
    status: "Published",
    contract: version.contract,
    provider: version.provider,
    layout: [],
    activityPresentation: version.activityPresentation ?? [],
    createdAt: version.publishedAt,
    updatedAt: version.publishedAt
  };
}

function editableSignature(draft: ActivityDefinitionDraftView) {
  return JSON.stringify({ contract: draft.contract, provider: { providerKey: draft.provider.providerKey, schemaVersion: draft.provider.schemaVersion, payload: draft.provider.payload }, layout: draft.layout, activityPresentation: draft.activityPresentation ?? [], presentationLabel: draft.presentationLabel ?? null });
}

function isActiveDraft(status: string) {
  return status.toLowerCase() === "active";
}

function generatedDraftLabel(draft: Pick<ActivityDefinitionDraftView, "draftId">) {
  return `Draft ${draft.draftId}`;
}

/**
 * Keep operational identities out of the page title; they remain available in Advanced details.
 */
function draftHeaderTitle(definitionDisplayName: string | undefined, draft: ActivityDefinitionDraftView) {
  const draftLabel = draft.presentationLabel?.trim();
  const name = definitionDisplayName?.trim();
  if (name && draftLabel && name !== draftLabel) return `${name} — ${draftLabel}`;
  return name || draftLabel || "Activity Definition draft";
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

function isContractView(view: ActivityDefinitionAuthoringView): view is "inputs" | "outputs" | "outcomes" {
  return view === "inputs" || view === "outputs" || view === "outcomes";
}

function contractDiagnosticView(pointer: string | null | undefined, referenceKey: string | null | undefined, contract: ActivityDefinitionDraftView["contract"]): "inputs" | "outputs" | "outcomes" {
  const section = /^\/contract\/(inputs|outputs|outcomes)(?:\/|$)/.exec(pointer ?? "")?.[1];
  if (section === "inputs" || section === "outputs" || section === "outcomes") return section;
  if (referenceKey) {
    if (contract.inputs.some(member => member.referenceKey === referenceKey)) return "inputs";
    if (contract.outputs.some(member => member.referenceKey === referenceKey)) return "outputs";
    if (contract.outcomes.some(member => member.referenceKey === referenceKey)) return "outcomes";
  }
  return "inputs";
}

function focusContractDiagnostic(
  editor: HTMLElement | null,
  jsonPointer: string | null | undefined,
  referenceKey: string | null | undefined,
  handle: ActivityDefinitionContractEditorHandle | null
): StudioActivityDiagnosticFocusResult {
  if (!editor) return unsupportedDiagnosticFocus();
  const members = [...editor.querySelectorAll<HTMLElement>("[data-contract-index][data-contract-reference-key]")];
  const pointedMember = contractMemberAtPointer(members, jsonPointer);
  const pointerKind = /^\/contract\/(inputs|outputs|outcomes)(?:\/|$)/.exec(jsonPointer ?? "")?.[1].replace(/s$/, "");
  const referenced = referenceKey
    ? members.find(element => element.dataset.contractReferenceKey === referenceKey && (!pointerKind || element.dataset.contractKind === pointerKind))
    : null;
  const member = pointedMember && (!referenceKey || pointedMember.dataset.contractReferenceKey === referenceKey)
    ? pointedMember
    : referenced ?? pointedMember;
  if (member?.dataset.contractKind && member.dataset.contractReferenceKey) {
    flushSync(() => handle?.revealMember(member.dataset.contractKind as "input" | "output" | "outcome", member.dataset.contractReferenceKey!));
  }
  const field = contractFieldAtPointer(jsonPointer)
    ?? (member && referenceKey && /^\/contract\/(?:inputs|outputs|outcomes)$/.test(jsonPointer ?? "") ? "referenceKey" : null);
  const exact = member && field
    ? [...member.querySelectorAll<HTMLElement>("[data-contract-field]")]
      .find(element => element.dataset.contractField === field)
    : null;
  const target = exact ?? member ?? editor.querySelector<HTMLElement>("[data-contract-schema]");
  if (!target) return unsupportedDiagnosticFocus();
  const memberPanel = target.closest<HTMLElement>(".ad-member-settings > [role='tabpanel']");
  if (memberPanel?.hidden && member) {
    const tab = [...member.querySelectorAll<HTMLButtonElement>("[role='tab']")].find(button => button.getAttribute("aria-controls") === memberPanel.id);
    flushSync(() => tab?.click());
  }
  const fold = target.closest(".studio-disclosure-fold") ?? target.querySelector(".studio-disclosure-fold");
  const trigger = fold?.querySelector<HTMLButtonElement>(":scope > button[aria-expanded='false']");
  if (trigger) flushSync(() => trigger.click());
  const disabledContext = target.matches(":disabled")
    ? target.closest<HTMLElement>(".studio-form-control, label") ?? member
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

function isBoundaryMappingPointer(pointer?: string | null) {
  return pointer?.startsWith("/outcomeMappings") || pointer?.startsWith("/outputMappings");
}

function focusBoundaryMappingDiagnostic(
  root: HTMLElement | null,
  pointer?: string | null,
  payload?: unknown
): StudioActivityDiagnosticFocusResult {
  const mappingIndex = /^\/outputMappings\/(0|[1-9][0-9]{0,3})(?:\/|$)/.exec(pointer ?? "")?.[1];
  if (mappingIndex !== undefined && payload && typeof payload === "object" && "outputMappings" in payload && Array.isArray(payload.outputMappings)) {
    const mapping = payload.outputMappings[Number(mappingIndex)] as { outputReferenceKey?: unknown } | undefined;
    const target = [...root?.querySelectorAll<HTMLElement>("[data-output-reference-key]") ?? []]
      .find(item => item.dataset.outputReferenceKey === mapping?.outputReferenceKey);
    return focusSettingControl(target?.querySelector<HTMLElement>("[data-output-source]") ?? null, "output value source");
  }
  const exactTarget = root?.querySelector<HTMLElement>(
    pointer?.startsWith("/outputMappings")
      ? ".ad-graph-output-mappings"
      : ".ad-graph-outcome-mappings"
  );
  const target = exactTarget ?? (root?.querySelector("[data-boundary-mappings-loading]") ? root : null);
  if (!target) return unsupportedDiagnosticFocus();
  target.tabIndex = -1;
  target.focus();
  target.scrollIntoView?.({ block: "center" });
  return {
    kind: "focused",
    announcement: !exactTarget
      ? "Focused the boundary mappings editor while its controls load. Use Focus location again when loading finishes."
      : pointer?.startsWith("/outputMappings")
      ? "Focused the boundary output mappings."
      : "Focused the boundary outcome mappings."
  };
}

async function waitForSettingTarget(read: () => HTMLElement | null, isCurrent: () => boolean) {
  for (let attempt = 0; attempt < 80 && isCurrent(); attempt++) {
    const target = read();
    if (target) return target;
    await new Promise<void>(resolve => window.setTimeout(resolve, 25));
  }
  return null;
}

function focusSettingControl(target: HTMLElement | null, label: string): StudioActivityDiagnosticFocusResult {
  if (!target) return unsupportedDiagnosticFocus();
  const control = target.querySelector<HTMLElement>("input:not(:disabled),select:not(:disabled),textarea:not(:disabled),button:not(:disabled)");
  const focused = control ?? target;
  if (!control) focused.tabIndex = -1;
  focused.focus();
  focused.scrollIntoView?.({ block: "center" });
  return { kind: "focused", announcement: `Focused the ${label}${control ? " control" : " settings context"}. Use Return to diagnostic to restore the diagnostics context.` };
}

function waitForAuthoringView() {
  return new Promise<void>(resolve => {
    if (typeof window.requestAnimationFrame === "function") {
      window.requestAnimationFrame(() => resolve());
      return;
    }
    window.setTimeout(resolve, 0);
  });
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

function saveStatusLabel(status: SaveStatus, revision: number, includeRevision = true) {
  if (status === "saved") return includeRevision ? `Saved revision ${revision}` : "Saved";
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
    layout: recovery.layout,
    activityPresentation: recovery.activityPresentation ?? []
  }, null, 2);
}

function hasBoundaryMappings(payload: unknown): boolean {
  if (!payload || typeof payload !== "object") return false;
  const mappings = payload as { outputMappings?: unknown; outcomeMappings?: unknown };
  return [mappings.outputMappings, mappings.outcomeMappings].some(value => Array.isArray(value) && value.length > 0);
}
