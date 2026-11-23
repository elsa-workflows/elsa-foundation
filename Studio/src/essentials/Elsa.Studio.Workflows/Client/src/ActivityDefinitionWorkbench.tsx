import { lazy, Suspense, useEffect, useRef, useState, useId, type ReactNode } from "react";
import { AlertTriangle, ArrowRight, ChevronDown, ChevronLeft, ChevronRight, GitBranch, Layers3, Pencil } from "lucide-react";
import { useQueryClient } from "@tanstack/react-query";
import type { StudioActivityDefinitionImplementationEditorContribution, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { StatusChip, StudioTabs, tabElementIds, StudioLoadingStatus, StudioResultState, StudioShellBreadcrumbs, StudioPageHeader, StudioButton } from "@elsa-workflows/studio-ui";
import {
  isActivityDefinitionEnumValue,
  type ActivityDefinitionDraftManagementView,
  type ActivityDefinitionManagementView,
  type ActivityDefinitionVersionLifecycleAction,
  type ActivityDefinitionVersionManagementView
} from "./activityDefinitionTypes";
import { activityDesignKeys, classifyActivityDefinitionReadFailure, redactActivityDefinitionChildCache, redactActivityDefinitionManagementCache, useActivityAuthoringCapabilities, useActivityDefinition, useActivityDefinitionDraft, useActivityDefinitionDrafts, useActivityDefinitionVersion, useActivityDefinitionVersions } from "./api/activityDesign";
import { observeActivityDefinitions } from "./activityDefinitionObservability";
import { ActivityDefinitionDraftDialog, isActionAllowed } from "./ActivityDefinitionDraftManagementDialogs";
import "./activityDefinitionOverview.css";
import { ActivityDefinitionForkDialog } from "./ActivityDefinitionForkDialog";
import { displayActivityDefinitionVersion, hasMigrationProviderChoice } from "./activityDefinitionDraftManagementShared";
import { formatActivityDefinitionDate as formatDate } from "./workflowFormatting";
import { StudioLink } from "./StudioLink";

const ActivityDefinitionVersionLifecycleDialog = lazy(async () => {
  const module = await import("./ActivityDefinitionVersionLifecycleDialog");
  return { default: module.ActivityDefinitionVersionLifecycleDialog };
});

const ActivityDefinitionDependencyExplorer = lazy(async () => {
  const module = await import("./ActivityDefinitionDependencyExplorer");
  return { default: module.ActivityDefinitionDependencyExplorer };
});

type Section = "overview" | "drafts" | "versions" | "relationships";
type RouteState = { definitionId: string | null; section: string | null; draftId: string | null; versionId: string | null; originDraftId?: string | null; originVersionId?: string | null; returnPlanId?: string | null; draftActionVersionId?: string | null; publicationReview?: boolean };
type DefinitionPrivacyFailure = { definitionId: string; kind: "unavailable" | "forbidden" | "not-found" };

export function ActivityDefinitionWorkbench({ agentWorkspace = false, context, definitionId, section, selectedDraftId, selectedVersionId, originDraftId, originVersionId, returnPlanId, requestedDraftActionVersionId, requestedPublicationReview = false, activityEditors, onNavigate, onPlanUpgrade, onOpenWorkflows }: {
  agentWorkspace?: boolean;
  context: StudioEndpointContext;
  definitionId: string;
  section: string | null;
  selectedDraftId: string | null;
  selectedVersionId: string | null;
  originDraftId?: string | null;
  originVersionId?: string | null;
  returnPlanId?: string | null;
  requestedDraftActionVersionId?: string | null;
  requestedPublicationReview?: boolean;
  activityEditors: StudioActivityDefinitionImplementationEditorContribution[];
  onOpenWorkflows?(): void;
  onNavigate(next: RouteState, replace?: boolean): void;
  onPlanUpgrade(kind: "ActivityDraft" | "ActivityVersion", id: string): void;
}) {
  const queryClient = useQueryClient();
  const requestedSection = selectedDraftId ? "drafts" : selectedVersionId ? "versions" : normalizeSection(section);
  const [draftCursorStack, setDraftCursorStack] = useState<(string | null)[]>([null]);
  const [draftCursorIndex, setDraftCursorIndex] = useState(0);
  const [versionCursorStack, setVersionCursorStack] = useState<(string | null)[]>([null]);
  const [versionCursorIndex, setVersionCursorIndex] = useState(0);
  const [privacyFailure, setPrivacyFailure] = useState<DefinitionPrivacyFailure | null>(null);
  const [draftPrivacyFailure, setDraftPrivacyFailure] = useState<DefinitionPrivacyFailure | null>(null);
  const [versionPrivacyFailure, setVersionPrivacyFailure] = useState<DefinitionPrivacyFailure | null>(null);
  const [draftSelectionFailure, setDraftSelectionFailure] = useState<{ id: string; kind: "unavailable" | "forbidden" | "not-found" } | null>(null);
  const [versionSelectionFailure, setVersionSelectionFailure] = useState<{ id: string; kind: "unavailable" | "forbidden" | "not-found" } | null>(null);
  const [draftDialogSource, setDraftDialogSource] = useState<string | null | false>(false);
  const [forkDialogOpen, setForkDialogOpen] = useState(false);
  const [lifecycleDialog, setLifecycleDialog] = useState<{ action: ActivityDefinitionVersionLifecycleAction; version: ActivityDefinitionVersionManagementView } | null>(null);
  const requestedActionHandled = useRef(false);
  const publicationReviewRequestHandled = useRef(false);
  const detailQuery = useActivityDefinition(context, definitionId);
  const directFailure = detailQuery.error ? classifyActivityDefinitionReadFailure(detailQuery.error) : null;
  const detailFailure = (privacyFailure?.definitionId === definitionId ? privacyFailure.kind : null) ?? directFailure;
  const mustDiscardData = detailFailure === "forbidden" || detailFailure === "unavailable" || detailFailure === "not-found";
  const detail = mustDiscardData ? undefined : detailQuery.data;
  const designOwned = isActivityDefinitionEnumValue(detail?.definition.contentAuthority.kind, "Design");
  const activeSection = detail && !designOwned && requestedSection === "drafts" ? "overview" : requestedSection;
  const forkAllowed = !designOwned && isActionAllowed(detail?.actions ?? [], "fork-definition");
  const recommendation = detail?.lifecycle.recommendation;
  const capabilitiesQuery = useActivityAuthoringCapabilities(context, forkAllowed && isActivityDefinitionEnumValue(recommendation?.lifecycle, "Active"));
  const childEnabled = Boolean(detail && !detailQuery.error);
  const overview = activeSection === "overview" && designOwned;
  const draftRequest = { definitionId, limit: 10, cursor: overview ? null : draftCursorStack[draftCursorIndex] };
  const versionRequest = { definitionId, limit: 10, cursor: overview ? null : versionCursorStack[versionCursorIndex] };
  const draftsQuery = useActivityDefinitionDrafts(context, draftRequest, childEnabled && (activeSection === "drafts" || overview));
  const versionsQuery = useActivityDefinitionVersions(context, versionRequest, childEnabled && (activeSection === "versions" || overview));
  const selectedDraftIsOnPage = Boolean(selectedDraftId && draftsQuery.data?.items.some(item => item.draft.draftId === selectedDraftId));
  const exactDraftQuery = useActivityDefinitionDraft(context, definitionId, selectedDraftId, childEnabled && activeSection === "drafts" && !selectedDraftIsOnPage);
  const overviewVersionId = !designOwned && activeSection === "overview" && isActivityDefinitionEnumValue(recommendation?.lifecycle, "Active") ? detail?.lifecycle.recommendation?.versionId ?? null : null;
  const exactVersionQuery = useActivityDefinitionVersion(context, definitionId, selectedVersionId ?? overviewVersionId, childEnabled && (activeSection === "versions" || Boolean(overviewVersionId)));
  const draftDirectFailure = draftsQuery.error ? classifyActivityDefinitionReadFailure(draftsQuery.error) : null;
  const versionDirectFailure = versionsQuery.error ? classifyActivityDefinitionReadFailure(versionsQuery.error) : null;
  const effectiveDraftPrivacyFailure = (draftPrivacyFailure?.definitionId === definitionId ? draftPrivacyFailure.kind : null)
    ?? (draftDirectFailure === "forbidden" || draftDirectFailure === "unavailable" || draftDirectFailure === "not-found" ? draftDirectFailure : null);
  const effectiveVersionPrivacyFailure = (versionPrivacyFailure?.definitionId === definitionId ? versionPrivacyFailure.kind : null)
    ?? (versionDirectFailure === "forbidden" || versionDirectFailure === "unavailable" || versionDirectFailure === "not-found" ? versionDirectFailure : null);
  const exactDraftFailure = exactDraftQuery.error ? classifyActivityDefinitionReadFailure(exactDraftQuery.error) : null;
  const exactVersionFailure = exactVersionQuery.error ? classifyActivityDefinitionReadFailure(exactVersionQuery.error) : null;
  const effectiveDraftSelectionFailure = draftSelectionFailure?.id === selectedDraftId ? draftSelectionFailure.kind : exactDraftFailure;
  const effectiveVersionSelectionFailure = versionSelectionFailure?.id === selectedVersionId ? versionSelectionFailure.kind : exactVersionFailure;
  const tabBaseId = useId();

  useEffect(() => {
    if (!mustDiscardData) return;
    void redactActivityDefinitionManagementCache(queryClient);
  }, [mustDiscardData, queryClient]);

  useEffect(() => {
    if (directFailure === "forbidden" || directFailure === "unavailable" || directFailure === "not-found") setPrivacyFailure({ definitionId, kind: directFailure });
  }, [definitionId, directFailure]);

  useEffect(() => {
    if (draftDirectFailure !== "forbidden" && draftDirectFailure !== "unavailable" && draftDirectFailure !== "not-found") return;
    setDraftPrivacyFailure({ definitionId, kind: draftDirectFailure });
    void redactActivityDefinitionChildCache(queryClient, definitionId, "draft");
  }, [definitionId, draftDirectFailure, queryClient]);

  useEffect(() => {
    if (versionDirectFailure !== "forbidden" && versionDirectFailure !== "unavailable" && versionDirectFailure !== "not-found") return;
    setVersionPrivacyFailure({ definitionId, kind: versionDirectFailure });
    void redactActivityDefinitionChildCache(queryClient, definitionId, "version");
  }, [definitionId, queryClient, versionDirectFailure]);

  useEffect(() => {
    if (!selectedDraftId || (exactDraftFailure !== "forbidden" && exactDraftFailure !== "unavailable" && exactDraftFailure !== "not-found")) return;
    setDraftSelectionFailure({ id: selectedDraftId, kind: exactDraftFailure });
    queryClient.setQueryData(activityDesignKeys.definitionDraft(definitionId, selectedDraftId), null);
  }, [definitionId, exactDraftFailure, queryClient, selectedDraftId]);

  useEffect(() => {
    if (!selectedVersionId || (exactVersionFailure !== "forbidden" && exactVersionFailure !== "unavailable" && exactVersionFailure !== "not-found")) return;
    setVersionSelectionFailure({ id: selectedVersionId, kind: exactVersionFailure });
    queryClient.setQueryData(activityDesignKeys.definitionVersion(definitionId, selectedVersionId), null);
  }, [definitionId, exactVersionFailure, queryClient, selectedVersionId]);

  useEffect(() => {
    if (detailQuery.error) observeActivityDefinitions({ event: "query-failure", surface: "workbench", outcome: detailFailure ?? "failed" });
  }, [detailFailure, detailQuery.error]);

  useEffect(() => {
    if (!detail || requestedActionHandled.current || !requestedDraftActionVersionId) return;
    requestedActionHandled.current = true;
    const designOwned = isActivityDefinitionEnumValue(detail.definition.contentAuthority.kind, "Design");
    if (designOwned && isActionAllowed(detail.actions, "create-draft")) setDraftDialogSource(requestedDraftActionVersionId);
  }, [detail, requestedDraftActionVersionId]);

  useEffect(() => {
    if (!requestedPublicationReview || publicationReviewRequestHandled.current || !detail || detailQuery.error) return;
    if (!designOwned) {
      publicationReviewRequestHandled.current = true;
      if (section !== "overview") onNavigate({ definitionId, section: "overview", draftId: null, versionId: null }, true);
      return;
    }
    if (effectiveDraftPrivacyFailure || draftsQuery.error || draftsQuery.isFetching || !draftsQuery.data) return;
    const completeDraftCollection = !draftsQuery.data.hasMore && !draftsQuery.data.continuation;
    const editableActiveDrafts = completeDraftCollection
      ? draftsQuery.data.items.filter(item => isActivityDefinitionEnumValue(item.draft.status, "Active") && isActionAllowed(item.actions, "edit-draft"))
      : [];
    if (completeDraftCollection && editableActiveDrafts.length === 1) {
      publicationReviewRequestHandled.current = true;
      onNavigate({ definitionId, section: "editor", draftId: editableActiveDrafts[0].draft.draftId, versionId: null, publicationReview: true }, true);
      return;
    }
    if (section !== "drafts") onNavigate({ definitionId, section: "drafts", draftId: null, versionId: null, publicationReview: true }, true);
  }, [definitionId, detail, detailQuery.error, designOwned, draftsQuery.data, draftsQuery.error, draftsQuery.isFetching, effectiveDraftPrivacyFailure, onNavigate, requestedPublicationReview, section]);

  useEffect(() => {
    if (section !== null || !detail) return;
    if (!designOwned) {
      const recommendedVersion = detail.lifecycle.recommendation;
      if (recommendedVersion && isActivityDefinitionEnumValue(recommendedVersion.lifecycle, "Active")) {
        onNavigate({ definitionId, section: "editor", draftId: null, versionId: recommendedVersion.versionId }, true);
      }
      return;
    }
    if (!overview || effectiveDraftPrivacyFailure || draftsQuery.error || draftsQuery.isFetching || !draftsQuery.data || draftsQuery.data.hasMore || draftsQuery.data.continuation) return;
    const activeDrafts = draftsQuery.data.items.filter(item => isActivityDefinitionEnumValue(item.draft.status, "Active"));
    if (activeDrafts.length === 1 && isActionAllowed(activeDrafts[0].actions, "edit-draft")) {
      onNavigate({ definitionId, section: "editor", draftId: activeDrafts[0].draft.draftId, versionId: null }, true);
      return;
    }
    if (activeDrafts.length !== 0 || versionsQuery.error || versionsQuery.isFetching || !versionsQuery.data || !detail.definition.headVersionId) return;
    const head = versionsQuery.data.items.find(item => item.version.versionId === detail.definition.headVersionId);
    if (head) onNavigate({ definitionId, section: "editor", draftId: null, versionId: head.version.versionId }, true);
  }, [definitionId, designOwned, detail, draftsQuery.data, draftsQuery.error, draftsQuery.isFetching, effectiveDraftPrivacyFailure, onNavigate, overview, section, versionsQuery.data, versionsQuery.error, versionsQuery.isFetching]);

  const navigateSection = (nextSection: Section, draftId: string | null = null, versionId: string | null = null) => onNavigate({ definitionId, section: nextSection, draftId, versionId, publicationReview: nextSection === "drafts" && requestedPublicationReview ? true : undefined });
  const returnToDraftId = originDraftId && !originVersionId ? originDraftId : null;
  const returnToVersionId = originVersionId && !originDraftId ? originVersionId : null;
  const returnDesignerHref = returnToDraftId || returnToVersionId
    ? activityDefinitionEditorHref(definitionId, returnToDraftId, returnToVersionId, returnPlanId)
    : null;
  const returnToDesigner = () => {
    if (!returnToDraftId && !returnToVersionId) return;
    onNavigate({ definitionId, section: "editor", draftId: returnToDraftId, versionId: returnToVersionId, returnPlanId });
  };

  if (!detail && detailQuery.isPending && !detailFailure) return <main className="ad-page ad-workbench" aria-busy="true"><div className="ad-skeleton" role="status">Loading the Activity Definition workbench…</div></main>;
  if (!detail && detailFailure) {
    return <main className="ad-page ad-workbench">
      <StudioShellBreadcrumbs items={[{ label: agentWorkspace ? "Agents" : "Activity definitions", onClick: () => onNavigate({ definitionId: null, section: null, draftId: null, versionId: null }) }]} current="Activity unavailable" />
      <StudioResultState tone="danger" headingLevel={1} icon={<AlertTriangle size={22} aria-hidden />} title={detailFailure === "unavailable" ? "Activity definitions unavailable" : "Activity Definition unavailable"} description={detailFailure === "unavailable" ? "This backend does not support activity definition management." : "Studio could not confirm an authorized Activity Definition for this address. No identity or count is shown."} actions={<><StudioButton type="button" onClick={() => { setPrivacyFailure(current => current?.definitionId === definitionId ? null : current); void detailQuery.refetch(); }}>Retry</StudioButton></>} />
    </main>;
  }
  if (!detail) return null;

  const provider = detail.lifecycle.recommendation?.providerKey ?? detail.lifecycle.head?.providerKey ?? "Not established";
  const tabs: { id: Section; label: string }[] = [
    { id: "overview", label: "Overview" },
    ...(designOwned ? [{ id: "drafts" as const, label: "Drafts" }] : []),
    { id: "versions", label: "Versions" },
    { id: "relationships", label: designOwned ? "Dependencies & usage" : "Usage" }
  ];

  const canCreateDraft = designOwned && isActionAllowed(detail.actions, "create-draft");
  const recommendedActiveVersion = isActivityDefinitionEnumValue(detail.lifecycle.recommendation?.lifecycle, "Active")
    ? detail.lifecycle.recommendation
    : null;
  const canFork = forkAllowed && Boolean(recommendedActiveVersion && hasMigrationProviderChoice(capabilitiesQuery.data, activityEditors, recommendedActiveVersion.providerKey, recommendedActiveVersion.providerSchemaVersion));
  const confirmedDrafts = draftRequest.cursor === null && !effectiveDraftPrivacyFailure && !draftsQuery.error && !draftsQuery.isFetching && draftsQuery.data && !draftsQuery.data.hasMore && !draftsQuery.data.continuation
    ? draftsQuery.data.items : null;
  const activeDrafts = confirmedDrafts?.filter(item => isActivityDefinitionEnumValue(item.draft.status, "Active"));
  const editableDraft = activeDrafts?.length === 1 && isActionAllowed(activeDrafts[0].actions, "edit-draft") ? activeDrafts[0] : null;
  const head = !effectiveVersionPrivacyFailure && !versionsQuery.error && !versionsQuery.isFetching
    ? versionsQuery.data?.items.find(item => item.version.versionId === detail.definition.headVersionId) : undefined;
  const canStartFromHead = canCreateDraft && activeDrafts?.length === 0 && head && isActionAllowed(head.actions, "clone-draft");
  const editLabel = editableDraft || canStartFromHead ? "Edit" : activeDrafts?.length === 0 && canCreateDraft && detail.definition.headVersionId === null ? "Create draft" : "Open drafts";
  const openEditor = (draftId: string) => onNavigate({ definitionId, section: "editor", draftId, versionId: null });
  const openVersionEditor = (versionId: string) => onNavigate({ definitionId, section: "editor", draftId: null, versionId });
  const startEditing = () => {
    if (detailQuery.error) return;
    if (editableDraft) { openEditor(editableDraft.draft.draftId); return; }
    if (canStartFromHead && head) { openVersionEditor(head.version.versionId); return; }
    if (editLabel === "Create draft") setDraftDialogSource(null);
    else navigateSection("drafts");
  };

  return <>
  <main className="ad-page ad-workbench" aria-labelledby="activity-definition-title">
    <StudioShellBreadcrumbs items={[
      { href: agentWorkspace ? "/agents" : "/workflows/activity-definitions", label: agentWorkspace ? "Agents" : "Activity definitions" },
      ...(returnDesignerHref ? [{ href: returnDesignerHref, label: "Designer" }] : [])
    ]} current={detail.definition.displayName} renderLink={item =>
      <StudioLink href={item.href} navigateToStudioPath={item.href === returnDesignerHref ? returnToDesigner :
        () => onNavigate({ definitionId: null, section: null, draftId: null, versionId: null })}>{item.label}</StudioLink>
    } />
    <StudioPageHeader compact headingLevel={2} title={detail.definition.displayName} titleId="activity-definition-title"
      description={detail.definition.description} refresh={{
        id: "activity-definition-workbench", label: "Refresh activity definition", refreshing: detailQuery.isFetching,
        onRefresh: async () => {
          setPrivacyFailure(current => current?.definitionId === definitionId ? null : current);
          observeActivityDefinitions({ event: "refresh", surface: "workbench" });
          await detailQuery.refetch();
        }
      }} actions={<>
        {designOwned ? <StudioButton type="button" variant="primary" disabled={Boolean(detailQuery.error) || (overview && draftsQuery.isPending)} onClick={startEditing}>
          <Pencil size={16} aria-hidden />{editLabel}
        </StudioButton> : null}
        {canFork ? <StudioButton type="button" onClick={() => setForkDialogOpen(true)}>Fork recommended version</StudioButton> : null}
      </>} />
    {detailQuery.isError ? <div className="ad-stale-warning" role="alert"><AlertTriangle size={18} aria-hidden /><div><strong>Refresh failed; showing retained definition evidence</strong><span>Retry before relying on lifecycle changes.</span></div></div> : null}
    {!designOwned ? <p className="ad-source-note">Provided by an installed module. Read-only in Studio.</p> : null}
    {!designOwned ? <details className="ad-technical-details ad-technical-disclosure"><summary>Technical details</summary><IdentityDetails detail={detail} provider={provider} /></details> : null}
    <StudioTabs compact baseId={tabBaseId} ariaLabel="Activity Definition sections"
      tabs={tabs.map(tab => ({ ...tab, panelIndex: 0 }))} activeTab={activeSection} onSelect={id => navigateSection(normalizeSection(id))} />
    <section className="ad-tab-panel" role="tabpanel" id={tabElementIds(tabBaseId, 0).panelId}
      aria-labelledby={tabElementIds(tabBaseId, tabs.findIndex(tab => tab.id === activeSection)).tabId}>
      {activeSection === "overview" ? designOwned ? <DesignedOverview detail={detail} activeDrafts={activeDrafts?.length ?? null} onOpen={navigateSection} onOpenWorkflows={onOpenWorkflows} /> : <Overview detail={detail} provider={provider} designOwned={false} versionQuery={exactVersionQuery} onOpen={navigateSection} /> : null}
      {activeSection === "drafts" ? <DraftsSection query={draftsQuery} exactQuery={exactDraftQuery} exactFailure={effectiveDraftSelectionFailure} privacyFailure={effectiveDraftPrivacyFailure} selectedDraftId={selectedDraftId} page={draftCursorIndex + 1} designOwned={designOwned} canCreateDraft={canCreateDraft} publicationReviewRequested={requestedPublicationReview} onCreate={() => setDraftDialogSource(null)} onPrevious={() => setDraftCursorIndex(current => Math.max(0, current - 1))} onNext={() => { const continuation = draftsQuery.data?.continuation; if (!continuation) return; setDraftCursorStack(current => [...current.slice(0, draftCursorIndex + 1), continuation]); setDraftCursorIndex(current => current + 1); }} onRetry={() => { setDraftPrivacyFailure(current => current?.definitionId === definitionId ? null : current); void draftsQuery.refetch(); }} onSelect={draftId => navigateSection("drafts", draftId, null)} onEdit={draftId => onNavigate({ definitionId, section: "editor", draftId, versionId: null })} onReviewPublication={draftId => onNavigate({ definitionId, section: "editor", draftId, versionId: null, publicationReview: true })} onPlanUpgrade={draftId => onPlanUpgrade("ActivityDraft", draftId)} /> : null}
      {activeSection === "versions" ? <VersionsSection query={versionsQuery} exactQuery={exactVersionQuery} exactFailure={effectiveVersionSelectionFailure} privacyFailure={effectiveVersionPrivacyFailure} selectedVersionId={selectedVersionId} page={versionCursorIndex + 1} designOwned={designOwned} onEditVersion={openVersionEditor} onManage={(action, version) => setLifecycleDialog({ action, version })} onPrevious={() => setVersionCursorIndex(current => Math.max(0, current - 1))} onNext={() => { const continuation = versionsQuery.data?.continuation; if (!continuation) return; setVersionCursorStack(current => [...current.slice(0, versionCursorIndex + 1), continuation]); setVersionCursorIndex(current => current + 1); }} onRetry={() => { setVersionPrivacyFailure(current => current?.definitionId === definitionId ? null : current); void versionsQuery.refetch(); }} onSelect={versionId => navigateSection("versions", null, versionId)} onPlanUpgrade={versionId => onPlanUpgrade("ActivityVersion", versionId)} /> : null}
      {activeSection === "relationships" ? <Suspense fallback={<StudioLoadingStatus>Loading dependency explorer…</StudioLoadingStatus>}><ActivityDefinitionDependencyExplorer context={context} definitionId={definitionId} initialVersionId={!designOwned ? detail.lifecycle.recommendation?.versionId : undefined} initialDirection={!designOwned ? "inbound" : undefined} /></Suspense> : null}
    </section>
    {designOwned ? <details className="ad-technical-disclosure"><summary>Advanced</summary><IdentityDetails detail={detail} provider={provider} /></details> : null}
  </main>
  {draftDialogSource !== false ? <ActivityDefinitionDraftDialog context={context} definitionId={definitionId} activityEditors={activityEditors} initialSourceVersionId={draftDialogSource} onClose={() => setDraftDialogSource(false)} onCreated={draft => { setDraftDialogSource(false); void queryClient.invalidateQueries({ queryKey: activityDesignKeys.definitionResources }); onNavigate({ definitionId, section: "editor", draftId: draft.draftId, versionId: null }); }} /> : null}
  {forkDialogOpen && recommendedActiveVersion ? <ActivityDefinitionForkDialog context={context} definition={detail} activityEditors={activityEditors} onClose={() => setForkDialogOpen(false)} onApplied={receipt => { setForkDialogOpen(false); void queryClient.invalidateQueries({ queryKey: activityDesignKeys.all }); onNavigate({ definitionId: receipt.definition.definitionId, section: "editor", draftId: receipt.draft.draftId, versionId: null }); }} /> : null}
  {lifecycleDialog ? <Suspense fallback={<div className="ad-dialog-backdrop" role="presentation"><div className="ad-dialog" role="status">Loading exact lifecycle review…</div></div>}><ActivityDefinitionVersionLifecycleDialog context={context} definition={detail} version={lifecycleDialog.version} action={lifecycleDialog.action} onClose={() => setLifecycleDialog(null)} onApplied={() => { setLifecycleDialog(null); void queryClient.invalidateQueries({ queryKey: activityDesignKeys.all }); }} /></Suspense> : null}
  </>;
}

function IdentityDetails({ detail, provider }: { detail: ActivityDefinitionManagementView; provider: string }) {
  return <dl className="ad-identity-strip">
    {isActivityDefinitionEnumValue(detail.definition.contentAuthority.kind, "Design") && detail.definition.category ? <div><dt>Category</dt><dd>{detail.definition.category}</dd></div> : null}
    <div><dt>Activity Type Key</dt><dd><code>{detail.definition.activityTypeKey}</code></dd></div>
    <div><dt>Definition identity</dt><dd><code>{detail.definition.definitionId}</code></dd></div>
    <div><dt>Authority</dt><dd>{isActivityDefinitionEnumValue(detail.definition.contentAuthority.kind, "Design") ? "Design owned" : "Source owned"}</dd></div>
    <div><dt>Authority key</dt><dd><code>{detail.definition.contentAuthority.authorityKey}</code></dd></div>
    {!isActivityDefinitionEnumValue(detail.definition.contentAuthority.kind, "Design") ? <div><dt>Source identity</dt><dd><code>{detail.definition.contentAuthority.sourceId ?? "Not disclosed"}</code></dd></div> : null}
    <div><dt>Provider</dt><dd>{provider}</dd></div>
    <div><dt>Recommended version</dt><dd>{detail.lifecycle.recommendation ? `${detail.lifecycle.recommendation.version} · ${detail.lifecycle.recommendation.lifecycle}` : "Not recommended"}</dd></div>
  </dl>;
}

function DesignedOverview({ detail, activeDrafts, onOpen, onOpenWorkflows }: { detail: ActivityDefinitionManagementView; activeDrafts: number | null; onOpen(section: Section): void; onOpenWorkflows?(): void }) {
  const recommendation = detail.lifecycle.recommendation;
  const available = isActivityDefinitionEnumValue(recommendation?.lifecycle, "Active");
  return <div className="ad-designed-overview">
    <section className="ad-authoring-summary"><div><span className="ad-kicker">Author</span><h2>Build your activity</h2><p>Define inputs, outputs and outcomes, then connect activities in the designer. Test your changes before publishing.</p></div><div className="ad-authoring-state"><span className="ad-state-badge">{activeDrafts === null ? "Drafts" : activeDrafts === 0 ? "No open drafts" : `${activeDrafts} open ${activeDrafts === 1 ? "draft" : "drafts"}`}</span><button type="button" onClick={() => onOpen("drafts")}>Manage drafts <ArrowRight size={14} aria-hidden /></button></div></section>
    <section className="ad-authoring-summary"><div><span className="ad-kicker">Reuse</span><h2>{available && recommendation ? `Version ${recommendation.version.split("+")[0]}` : "Not ready for reuse"}</h2><p>{available ? `The recommended version appears in the workflow activity palette under ${detail.definition.category || "its category"}. Existing workflows keep the version they already use.` : "Publish and recommend a version to make this activity available in the workflow activity palette."}</p></div><div className="ad-authoring-state">{available ? <span className="ad-state-badge">Recommended</span> : null}{onOpenWorkflows && available ? <button type="button" onClick={onOpenWorkflows}>Open workflows <ArrowRight size={14} aria-hidden /></button> : null}<button type="button" onClick={() => onOpen("versions")}>Version history ({detail.lifecycle.versionCount})</button></div></section>
  </div>;
}

function Overview({ detail, provider, designOwned, versionQuery, onOpen }: { detail: ActivityDefinitionManagementView; provider: string; designOwned: boolean; versionQuery: ReturnType<typeof useActivityDefinitionVersion>; onOpen(section: Section): void }) {
  if (!designOwned) return <div className="ad-source-overview">
    <dl className="ad-source-summary">
      <div><dt>Category</dt><dd>{detail.definition.category}</dd></div>
      <div><dt>Recommended version</dt><dd>{detail.lifecycle.recommendation ? displayActivityDefinitionVersion(detail.lifecycle.recommendation.version) : "None"}</dd></div>
    </dl>
    {isActivityDefinitionEnumValue(detail.lifecycle.recommendation?.lifecycle, "Active") ? versionQuery.isError
      ? <div className="ad-inline-error" role="alert">Activity details could not be loaded. <button type="button" onClick={() => void versionQuery.refetch()}>Retry</button></div>
      : versionQuery.data?.contract ? <HistoricalContract contract={versionQuery.data.contract} compact />
      : <StudioLoadingStatus>Loading activity details…</StudioLoadingStatus>
      : <p>No recommended active version is available. Choose a version to inspect its inputs and outputs.</p>}
  </div>;

  return <div className="ad-overview-grid">
    <article><span>Drafts</span><strong>{detail.lifecycle.draftCount} drafts</strong><p>Each draft can be edited independently and has its own link.</p><button type="button" onClick={() => onOpen("drafts")}>View drafts</button></article>
    <article><span>Version history</span><strong>{detail.lifecycle.versionCount} versions</strong><p>Published versions are read-only. You can choose which version to recommend.</p><button type="button" onClick={() => onOpen("versions")}>View versions</button></article>
    <article><span>Implementation provider</span><strong>{provider}</strong><p>The workbench does not load provider editors or interpret provider manifests.</p></article>
    <article><span>Relationships</span><strong>Dependencies & usage</strong><p>Enter the relationship surface from the stable definition, never from an arbitrary draft.</p><button type="button" onClick={() => onOpen("relationships")}>Explore relationships</button></article>
  </div>;
}

function DraftsSection({ query, exactQuery, exactFailure, privacyFailure, selectedDraftId, page, designOwned, canCreateDraft, publicationReviewRequested, onCreate, onPrevious, onNext, onRetry, onSelect, onEdit, onReviewPublication, onPlanUpgrade }: { query: ReturnType<typeof useActivityDefinitionDrafts>; exactQuery: ReturnType<typeof useActivityDefinitionDraft>; exactFailure: ReturnType<typeof classifyActivityDefinitionReadFailure> | null; privacyFailure: "unavailable" | "forbidden" | "not-found" | null; selectedDraftId: string | null; page: number; designOwned: boolean; canCreateDraft: boolean; publicationReviewRequested: boolean; onCreate(): void; onPrevious(): void; onNext(): void; onRetry(): void; onSelect(draftId: string): void; onEdit(draftId: string): void; onReviewPublication(draftId: string): void; onPlanUpgrade(draftId: string): void }) {
  const data = privacyFailure ? undefined : query.data;
  const selected = privacyFailure || exactFailure ? undefined : data?.items.find(item => item.draft.draftId === selectedDraftId) ?? exactQuery.data;
  const editableDrafts = data && !data.hasMore && !data.continuation
    ? data.items.filter(item => isActivityDefinitionEnumValue(item.draft.status, "Active") && isActionAllowed(item.actions, "edit-draft"))
    : [];
  return <div><div className="ad-section-heading-row"><SectionHeading icon={<GitBranch size={18} />} title="Drafts" description="Choose a draft to edit. Drafts with the same name have different IDs." />{editableDrafts.length === 1 ? <button type="button" onClick={() => onEdit(editableDrafts[0].draft.draftId)}>Open editor</button> : null}{canCreateDraft ? <StudioButton onClick={onCreate}>Create parallel draft</StudioButton> : null}</div>{publicationReviewRequested && !selectedDraftId && !privacyFailure && !query.error ? <p className="ad-inline-status" role="status">Select an active draft to review for publication.</p> : null}{!designOwned ? <div className="ad-unavailable-note" role="status"><strong>This source has no editable drafts</strong><span>Choose a published version to create your own editable copy.</span></div> : null}{selected ? <SelectedDraft item={query.error ? { ...selected, actions: [] } : selected} onEdit={() => onEdit(selected.draft.draftId)} showPublicationReview={publicationReviewRequested} onReviewPublication={() => onReviewPublication(selected.draft.draftId)} onPlanUpgrade={() => onPlanUpgrade(selected.draft.draftId)} /> : <ExactSelectionState kind="draft" selected={Boolean(selectedDraftId)} query={exactQuery} failure={exactFailure} />}<ChildCollectionState query={query} privacyFailure={privacyFailure} empty="No authorized drafts are available for this definition." onRetry={onRetry} />{data?.items.length ? <div className="ad-child-list" role="group" aria-label="Activity Definition drafts">{data.items.map(item => <button key={item.draft.draftId} type="button" className={item.draft.draftId === selectedDraftId ? "is-selected" : ""} onClick={() => onSelect(item.draft.draftId)}><span><strong>{draftLabel(item)}</strong><small>Revision {item.draft.revision} · {item.draft.status}</small></span><span><strong>{item.draft.providerKey}</strong><small>{formatDate(item.draft.updatedAt)}</small></span></button>)}</div> : null}<CursorPager page={page} data={data} onPrevious={onPrevious} onNext={onNext} /></div>;
}

function VersionsSection({ query, exactQuery, exactFailure, privacyFailure, selectedVersionId, page, designOwned, onEditVersion, onManage, onPrevious, onNext, onRetry, onSelect, onPlanUpgrade }: { query: ReturnType<typeof useActivityDefinitionVersions>; exactQuery: ReturnType<typeof useActivityDefinitionVersion>; exactFailure: ReturnType<typeof classifyActivityDefinitionReadFailure> | null; privacyFailure: "unavailable" | "forbidden" | "not-found" | null; selectedVersionId: string | null; page: number; designOwned: boolean; onEditVersion(versionId: string): void; onManage(action: ActivityDefinitionVersionLifecycleAction, version: ActivityDefinitionVersionManagementView): void; onPrevious(): void; onNext(): void; onRetry(): void; onSelect(versionId: string | null): void; onPlanUpgrade(versionId: string): void }) {
  const data = privacyFailure ? undefined : query.data;
  const listedSelection = data?.items.find(item => item.version.versionId === selectedVersionId);
  const selected = privacyFailure || exactFailure
    ? undefined
    : exactQuery.data
      ? { ...exactQuery.data, actions: listedSelection?.actions ?? exactQuery.data.actions }
      : listedSelection;
  const actionAllowed = Boolean(designOwned && listedSelection && isActionAllowed(listedSelection.actions, "clone-draft"));
  return <div className="ad-version-history">
    <SectionHeading icon={<Layers3 size={18} />} title="Versions" description={designOwned ? "Every row is an immutable semantic version. Recommendation and lifecycle are reviewed exact-version decisions." : "Versions supplied by the module."} />
    {selected && !listedSelection ? <SelectedVersion item={selected} compact={!designOwned} standalone actionLabel="Edit this version" actionAllowed={actionAllowed} onCreate={() => onEditVersion(selected.version.versionId)} onPlanUpgrade={() => onPlanUpgrade(selected.version.versionId)} onManage={action => onManage(action, selected)} /> : null}
    {!selected ? <ExactSelectionState kind="version" selected={Boolean(selectedVersionId)} query={exactQuery} failure={exactFailure} /> : null}
    <ChildCollectionState query={query} privacyFailure={privacyFailure} empty="No authorized versions are available for this definition." onRetry={onRetry} />
    {data?.items.length ? <div className="ad-child-list ad-version-list" role="group" aria-label="Activity Definition versions">{data.items.map(item => {
      const isSelected = item.version.versionId === selectedVersionId;
      const selectedItem = isSelected && selected ? selected : null;
      const detailsId = `activity-version-details-${encodeURIComponent(item.version.versionId)}`;
      return <div key={item.version.versionId} className="ad-version-row">
        <button type="button" className={isSelected ? "is-selected" : ""} aria-expanded={Boolean(selectedItem)} aria-controls={selectedItem ? detailsId : undefined} onClick={() => onSelect(isSelected ? null : item.version.versionId)}>
          <span className="ad-version-primary"><strong>{designOwned ? item.version.version : displayActivityDefinitionVersion(item.version.version)}</strong><small>{formatDate(item.version.publishedAt)}</small></span>
          <span className="ad-version-badges"><StatusChip tone={isActivityDefinitionEnumValue(item.version.lifecycle, "Active") ? "success" : "neutral"}>{item.version.lifecycle}</StatusChip>{item.isRecommended ? <span className="ad-version-recommended">Recommended</span> : null}</span>
          <span className="ad-version-disclosure"><span>{selectedItem ? "Hide details" : "Show details"}</span><ChevronDown size={16} aria-hidden /></span>
        </button>
        {selectedItem ? <div id={detailsId} className="ad-version-expanded"><SelectedVersion item={selectedItem} compact={!designOwned} actionLabel="Edit this version" actionAllowed={actionAllowed} onCreate={() => onEditVersion(selectedItem.version.versionId)} onPlanUpgrade={() => onPlanUpgrade(selectedItem.version.versionId)} onManage={action => onManage(action, selectedItem)} /></div> : null}
      </div>;
    })}</div> : null}
    <CursorPager page={page} data={data} onPrevious={onPrevious} onNext={onNext} />
  </div>;
}

function ChildCollectionState({ query, privacyFailure, empty, onRetry }: { query: ReturnType<typeof useActivityDefinitionDrafts> | ReturnType<typeof useActivityDefinitionVersions>; privacyFailure: "unavailable" | "forbidden" | "not-found" | null; empty: string; onRetry(): void }) {
  if (privacyFailure) return <div className="ad-inline-error" role="alert"><span>{privacyFailure === "unavailable" ? "This backend does not advertise this supported management collection. No fallback is used." : privacyFailure === "not-found" ? "This collection is no longer available. No identities or counts are shown." : "This account cannot open this collection. No identities or counts are shown."}</span> <button type="button" onClick={onRetry}>Retry</button></div>;
  if (query.isPending) return <StudioLoadingStatus>Loading authorized rows…</StudioLoadingStatus>;
  if (query.isError && !query.data) return <div className="ad-inline-error" role="alert">This collection could not be confirmed. No unconfirmed rows are shown.</div>;
  if (query.isError && query.data) return <div className="ad-inline-error" role="alert">Refresh failed; retained rows remain visible as stale evidence.</div>;
  if (query.data?.items.length === 0) return <div className="ad-inline-status" role="status">{empty}</div>;
  return null;
}

function ExactSelectionState({ kind, selected, query, failure }: { kind: "draft" | "version"; selected: boolean; query: ReturnType<typeof useActivityDefinitionDraft> | ReturnType<typeof useActivityDefinitionVersion>; failure: ReturnType<typeof classifyActivityDefinitionReadFailure> | null }) {
  if (!selected) return null;
  if (query.isPending) return <StudioLoadingStatus>Loading the exact selected {kind}…</StudioLoadingStatus>;
  if (failure) return <div className="ad-inline-error" role="alert">The exact selected {kind} could not be confirmed. No unconfirmed identity is shown.</div>;
  return null;
}

function CursorPager({ page, data, onPrevious, onNext }: { page: number; data: { hasMore: boolean; continuation?: string | null } | undefined; onPrevious(): void; onNext(): void }) {
  if (!data || (page <= 1 && !data.hasMore)) return null;
  return <div className="ad-child-pager"><button type="button" onClick={onPrevious} disabled={page <= 1}><ChevronLeft size={15} aria-hidden /> Previous</button><span>Page {page}</span><button type="button" onClick={onNext} disabled={!data.hasMore || !data.continuation}>Next <ChevronRight size={15} aria-hidden /></button></div>;
}

function SectionHeading({ icon, title, description }: { icon: ReactNode; title: string; description: string }) { return <header className="ad-section-heading"><span aria-hidden>{icon}</span><div><h2>{title}</h2><p>{description}</p></div></header>; }
function SelectedDraft({ item, onEdit, showPublicationReview, onReviewPublication, onPlanUpgrade }: { item: ActivityDefinitionDraftManagementView; onEdit(): void; showPublicationReview: boolean; onReviewPublication(): void; onPlanUpgrade(): void }) {
  const canEdit = isActivityDefinitionEnumValue(item.draft.status, "Active") && isActionAllowed(item.actions, "edit-draft");
  return <div className="ad-selected-record" aria-label="Selected exact draft">
    <strong>{draftLabel(item)}</strong>
    <span><code>{item.draft.draftId}</code> · revision {item.draft.revision} · {item.draft.providerKey}</span>
    {!canEdit ? <span className="ad-selected-record-note">This draft is read-only or editing is unavailable for this account.</span> : null}
    <div className="ad-header-actions">
      {canEdit ? <button type="button" onClick={onEdit}>Open editor</button> : null}
      {showPublicationReview && canEdit ? <button type="button" onClick={onReviewPublication}>Review publication</button> : null}
      <button type="button" onClick={onPlanUpgrade}>Plan broad upgrade from this exact draft</button>
    </div>
  </div>;
}
function SelectedVersion({ item, compact, standalone = false, actionLabel, actionAllowed, onCreate, onPlanUpgrade, onManage }: { item: ActivityDefinitionVersionManagementView; compact: boolean; standalone?: boolean; actionLabel: string; actionAllowed: boolean; onCreate(): void; onPlanUpgrade(): void; onManage(action: ActivityDefinitionVersionLifecycleAction): void }) {
  const lifecycleActions: Array<{ action: ActivityDefinitionVersionLifecycleAction; availability: string; label: string }> = [
    { action: "recommend", availability: "set-recommendation", label: "Make recommended" },
    { action: "retire", availability: "retire-version", label: "Retire" },
    { action: "restore", availability: "restore-version", label: "Restore" },
    { action: "revoke", availability: "revoke-version", label: "Revoke" }
  ];
  const available = lifecycleActions.filter(action =>
    isActionAllowed(item.actions, action.availability) &&
    (action.action !== "recommend" || !item.isRecommended)
  );
  const actions = actionAllowed || available.length ? <div className="ad-header-actions">{actionAllowed ? <button type="button" onClick={onCreate}>{actionLabel}</button> : null}{available.map(action => <button key={action.action} type="button" className={action.action === "revoke" ? "ad-danger-action" : ""} onClick={() => onManage(action.action)}>{action.label}</button>)}</div> : null;
  return <section className="ad-version-detail" aria-label="Selected exact version">
    {standalone ? <header className="ad-version-detail-heading"><strong>Version {compact ? displayActivityDefinitionVersion(item.version.version) : item.version.version}{item.isRecommended ? " · Recommended" : ""}</strong><span>{item.version.lifecycle} · {formatDate(item.version.publishedAt)}</span></header> : null}
    {!compact ? actions : null}
    {item.contract ? <HistoricalContract contract={item.contract} compact={compact} /> : <StudioLoadingStatus>Loading activity details…</StudioLoadingStatus>}
    <details className="ad-technical-details ad-version-details"><summary>Version details</summary>
      {compact ? actions : null}
      <dl className="ad-version-identifiers">
        <div><dt>Version ID</dt><dd><code>{item.version.versionId}</code></dd></div>
        <div><dt>Provider</dt><dd>{item.providerKey}</dd></div>
        <div><dt>Provider schema</dt><dd>{item.providerSchemaVersion}</dd></div>
      </dl>
      <button type="button" onClick={onPlanUpgrade}>Plan broad upgrade from this exact version</button>
    </details>
  </section>;

}

function HistoricalContract({ contract, compact = false }: { contract: NonNullable<ActivityDefinitionVersionManagementView["contract"]>; compact?: boolean }) {
  return <section className="ad-historical-contract" aria-labelledby="historical-contract-title"><header>{compact ? <h2 id="historical-contract-title">Inputs, outputs &amp; outcomes</h2> : <h3 id="historical-contract-title">Published inputs, outputs and outcomes</h3>}{!compact ? <span>Schema {contract.contractSchemaVersion}</span> : null}</header>{!compact ? <p>Stored aliases remain visible even when they are no longer available in the current authoring catalog.</p> : null}
    <div className="ad-historical-contract-groups">
      <HistoricalMembers compact={compact} title="Inputs" members={contract.inputs.map(member => ({
        key: member.referenceKey,
        name: member.displayName || member.name,
        detail: `${member.type.alias} · ${member.type.collectionKind} · ${member.isRequired ? "Required effective value" : "Optional effective value"} · ${member.isNullable ? "Allows null" : "Non-null"}`,
        compactDetail: compactContractType(member.type.alias, member.type.collectionKind, member.isRequired ? "Required" : "Optional", member.isNullable)
      }))} />
      <HistoricalMembers compact={compact} title="Outputs" members={contract.outputs.map(member => ({
        key: member.referenceKey,
        name: member.displayName || member.name,
        detail: `${member.type.alias} · ${member.type.collectionKind} · ${member.isRequired ? "Must be produced" : "Optional production"} · ${member.isNullable ? "Allows null" : "Non-null"}`,
        compactDetail: compactContractType(member.type.alias, member.type.collectionKind, member.isRequired ? "Must be produced" : "Optional", member.isNullable)
      }))} />
      <HistoricalMembers compact={compact} title="Outcomes" members={contract.outcomes.map(member => ({
        key: member.referenceKey,
        name: member.name,
        detail: member.isEmitted ? "Emitted" : "Not emitted",
        compactDetail: member.isEmitted ? "Emitted" : "Not emitted"
      }))} />
    </div>
  </section>;
}

function HistoricalMembers({ title, members, compact }: { title: string; compact: boolean; members: Array<{ key: string; name: string; detail: string; compactDetail: string }> }) {
  return <section>{compact ? <h3>{title}</h3> : <h4>{title}</h4>}{members.length ? <ul>{members.map(member => <li key={member.key}><strong>{member.name}</strong>{compact ? <span>{member.compactDetail}</span> : <><code>{member.key}</code><span>{member.detail}</span></>}</li>)}</ul> : <p>None</p>}</section>;
}

function activityDefinitionEditorHref(definitionId: string, draftId: string | null, versionId: string | null, returnPlanId?: string | null) {
  const parameters = new URLSearchParams({ definition: definitionId, section: "editor" });
  if (draftId) parameters.set("draft", draftId);
  if (versionId) parameters.set("version", versionId);
  if (returnPlanId) parameters.set("returnPlan", returnPlanId);
  return `/workflows/activity-definitions?${parameters.toString()}`;
}

function compactContractType(alias: string, collectionKind: string, requirement: string, isNullable: boolean) {
  return [alias, collectionKind === "Scalar" ? null : collectionKind, requirement, isNullable ? "Allows null" : "Non-null"]
    .filter(Boolean)
    .join(" · ");
}
function draftLabel(item: ActivityDefinitionDraftManagementView) { return item.draft.presentationLabel?.trim() || `Draft ${item.draft.draftId}`; }
function normalizeSection(value: string | null): Section { return value === "drafts" || value === "versions" || value === "relationships" ? value : "overview"; }
