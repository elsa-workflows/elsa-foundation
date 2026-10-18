import { lazy, Suspense, useEffect, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import { AlertTriangle, ArrowLeft, ChevronLeft, ChevronRight, GitBranch, Layers3, RefreshCw } from "lucide-react";
import { useQueryClient } from "@tanstack/react-query";
import type { StudioActivityDefinitionImplementationEditorContribution, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
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
import { ActivityDefinitionForkDialog } from "./ActivityDefinitionForkDialog";
import { displayActivityDefinitionVersion, hasMigrationProviderChoice } from "./activityDefinitionDraftManagementShared";
import { formatActivityDefinitionDate as formatDate } from "./workflowFormatting";

const ActivityDefinitionVersionLifecycleDialog = lazy(async () => {
  const module = await import("./ActivityDefinitionVersionLifecycleDialog");
  return { default: module.ActivityDefinitionVersionLifecycleDialog };
});

const ActivityDefinitionDependencyExplorer = lazy(async () => {
  const module = await import("./ActivityDefinitionDependencyExplorer");
  return { default: module.ActivityDefinitionDependencyExplorer };
});

type Section = "overview" | "drafts" | "versions" | "relationships";
type RouteState = { definitionId: string | null; section: string | null; draftId: string | null; versionId: string | null; draftActionVersionId?: string | null };

export function ActivityDefinitionWorkbench({ context, definitionId, section, selectedDraftId, selectedVersionId, requestedDraftActionVersionId, activityEditors, onNavigate, onPlanUpgrade }: {
  context: StudioEndpointContext;
  definitionId: string;
  section: string | null;
  selectedDraftId: string | null;
  selectedVersionId: string | null;
  requestedDraftActionVersionId?: string | null;
  activityEditors: StudioActivityDefinitionImplementationEditorContribution[];
  onNavigate(next: RouteState): void;
  onPlanUpgrade(kind: "ActivityDraft" | "ActivityVersion", id: string): void;
}) {
  const queryClient = useQueryClient();
  const requestedSection = selectedDraftId ? "drafts" : selectedVersionId ? "versions" : normalizeSection(section);
  const [draftCursorStack, setDraftCursorStack] = useState<(string | null)[]>([null]);
  const [draftCursorIndex, setDraftCursorIndex] = useState(0);
  const [versionCursorStack, setVersionCursorStack] = useState<(string | null)[]>([null]);
  const [versionCursorIndex, setVersionCursorIndex] = useState(0);
  const [privacyFailure, setPrivacyFailure] = useState<"unavailable" | "forbidden" | "not-found" | null>(null);
  const [draftPrivacyFailure, setDraftPrivacyFailure] = useState<"unavailable" | "forbidden" | "not-found" | null>(null);
  const [versionPrivacyFailure, setVersionPrivacyFailure] = useState<"unavailable" | "forbidden" | "not-found" | null>(null);
  const [draftSelectionFailure, setDraftSelectionFailure] = useState<{ id: string; kind: "unavailable" | "forbidden" | "not-found" } | null>(null);
  const [versionSelectionFailure, setVersionSelectionFailure] = useState<{ id: string; kind: "unavailable" | "forbidden" | "not-found" } | null>(null);
  const [draftDialogSource, setDraftDialogSource] = useState<string | null | false>(false);
  const [forkDialogOpen, setForkDialogOpen] = useState(false);
  const [lifecycleDialog, setLifecycleDialog] = useState<{ action: ActivityDefinitionVersionLifecycleAction; version: ActivityDefinitionVersionManagementView } | null>(null);
  const requestedActionHandled = useRef(false);
  const detailQuery = useActivityDefinition(context, definitionId);
  const directFailure = detailQuery.error ? classifyActivityDefinitionReadFailure(detailQuery.error) : null;
  const detailFailure = privacyFailure ?? directFailure;
  const mustDiscardData = detailFailure === "forbidden" || detailFailure === "unavailable" || detailFailure === "not-found";
  const detail = mustDiscardData ? undefined : detailQuery.data;
  const designOwned = isActivityDefinitionEnumValue(detail?.definition.contentAuthority.kind, "Design");
  const activeSection = detail && !designOwned && requestedSection === "drafts" ? "overview" : requestedSection;
  const forkAllowed = !designOwned && isActionAllowed(detail?.actions ?? [], "fork-definition");
  const recommendation = detail?.lifecycle.recommendation;
  const capabilitiesQuery = useActivityAuthoringCapabilities(context, forkAllowed && isActivityDefinitionEnumValue(recommendation?.lifecycle, "Active"));
  const childEnabled = Boolean(detail && !detailQuery.error);
  const draftRequest = { definitionId, limit: 10, cursor: draftCursorStack[draftCursorIndex] };
  const versionRequest = { definitionId, limit: 10, cursor: versionCursorStack[versionCursorIndex] };
  const draftsQuery = useActivityDefinitionDrafts(context, draftRequest, childEnabled && activeSection === "drafts");
  const versionsQuery = useActivityDefinitionVersions(context, versionRequest, childEnabled && activeSection === "versions");
  const selectedDraftIsOnPage = Boolean(selectedDraftId && draftsQuery.data?.items.some(item => item.draft.draftId === selectedDraftId));
  const exactDraftQuery = useActivityDefinitionDraft(context, definitionId, selectedDraftId, childEnabled && activeSection === "drafts" && !selectedDraftIsOnPage);
  const overviewVersionId = !designOwned && activeSection === "overview" && isActivityDefinitionEnumValue(recommendation?.lifecycle, "Active") ? detail?.lifecycle.recommendation?.versionId ?? null : null;
  const exactVersionQuery = useActivityDefinitionVersion(context, definitionId, selectedVersionId ?? overviewVersionId, childEnabled && (activeSection === "versions" || Boolean(overviewVersionId)));
  const draftDirectFailure = draftsQuery.error ? classifyActivityDefinitionReadFailure(draftsQuery.error) : null;
  const versionDirectFailure = versionsQuery.error ? classifyActivityDefinitionReadFailure(versionsQuery.error) : null;
  const exactDraftFailure = exactDraftQuery.error ? classifyActivityDefinitionReadFailure(exactDraftQuery.error) : null;
  const exactVersionFailure = exactVersionQuery.error ? classifyActivityDefinitionReadFailure(exactVersionQuery.error) : null;
  const effectiveDraftSelectionFailure = draftSelectionFailure?.id === selectedDraftId ? draftSelectionFailure.kind : exactDraftFailure;
  const effectiveVersionSelectionFailure = versionSelectionFailure?.id === selectedVersionId ? versionSelectionFailure.kind : exactVersionFailure;
  const tabRefs = useRef<(HTMLButtonElement | null)[]>([]);

  useEffect(() => {
    if (!mustDiscardData) return;
    void redactActivityDefinitionManagementCache(queryClient);
  }, [mustDiscardData, queryClient]);

  useEffect(() => {
    if (directFailure === "forbidden" || directFailure === "unavailable" || directFailure === "not-found") setPrivacyFailure(directFailure);
  }, [directFailure]);

  useEffect(() => {
    if (draftDirectFailure !== "forbidden" && draftDirectFailure !== "unavailable" && draftDirectFailure !== "not-found") return;
    setDraftPrivacyFailure(draftDirectFailure);
    void redactActivityDefinitionChildCache(queryClient, definitionId, "draft");
  }, [definitionId, draftDirectFailure, queryClient]);

  useEffect(() => {
    if (versionDirectFailure !== "forbidden" && versionDirectFailure !== "unavailable" && versionDirectFailure !== "not-found") return;
    setVersionPrivacyFailure(versionDirectFailure);
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

  const navigateSection = (nextSection: Section, draftId: string | null = null, versionId: string | null = null) => onNavigate({ definitionId, section: nextSection, draftId, versionId });

  if (!detail && detailQuery.isPending && !detailFailure) return <main className="ad-page ad-workbench" aria-busy="true"><div className="ad-skeleton" role="status">Loading the Activity Definition workbench…</div></main>;
  if (!detail && detailFailure) {
    return <main className="ad-page ad-workbench">
      <button type="button" className="ad-back" onClick={() => onNavigate({ definitionId: null, section: null, draftId: null, versionId: null })}><ArrowLeft size={16} aria-hidden /> Activity Definitions</button>
      <section className="ad-failure" role="alert"><AlertTriangle size={22} aria-hidden /><h1>{detailFailure === "unavailable" ? "Unavailable contribution" : "Activity Definition unavailable"}</h1><p>{detailFailure === "unavailable" ? "This backend does not advertise the supported Activity Definition management capability. No legacy fallback is used." : "Studio could not confirm an authorized Activity Definition for this address. No identity or count is shown."}</p><button type="button" onClick={() => { setPrivacyFailure(null); void detailQuery.refetch(); }}>Try again</button></section>
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

  return <>
  <main className="ad-page ad-workbench" aria-labelledby="activity-definition-title">
    <button type="button" className="ad-back" onClick={() => onNavigate({ definitionId: null, section: null, draftId: null, versionId: null })}><ArrowLeft size={16} aria-hidden /> Activity Definitions</button>
    <header className="ad-workbench-header"><div><span className="ad-kicker">{designOwned ? "Stable Activity Definition" : "Module activity"}</span><h1 id="activity-definition-title">{detail.definition.displayName}</h1>{detail.definition.description ? <p>{detail.definition.description}</p> : null}</div><div className="ad-header-actions">{canCreateDraft ? <button type="button" onClick={() => setDraftDialogSource(null)}>Create draft</button> : null}{canFork ? <button type="button" onClick={() => setForkDialogOpen(true)}>Fork recommended version</button> : null}<button type="button" className="ad-primary-action" onClick={() => { setPrivacyFailure(null); observeActivityDefinitions({ event: "refresh", surface: "workbench" }); void detailQuery.refetch(); }} disabled={detailQuery.isFetching}><RefreshCw size={16} aria-hidden /> {detailQuery.isFetching ? "Refreshing" : "Refresh"}</button></div></header>
    {detailQuery.isError ? <div className="ad-stale-warning" role="alert"><AlertTriangle size={18} aria-hidden /><div><strong>Refresh failed; showing retained definition evidence</strong><span>Retry before relying on lifecycle changes.</span></div></div> : null}
    {!designOwned ? <p className="ad-source-note">Provided by an installed module. Read-only in Studio.</p> : null}
    <details className="ad-technical-details"><summary>Technical details</summary><dl className="ad-identity-strip">
      <div><dt>Activity Type Key</dt><dd><code>{detail.definition.activityTypeKey}</code></dd></div>
      <div><dt>Definition identity</dt><dd><code>{detail.definition.definitionId}</code></dd></div>
      <div><dt>Authority</dt><dd>{isActivityDefinitionEnumValue(detail.definition.contentAuthority.kind, "Design") ? "Design owned" : "Source owned"}</dd></div>
      <div><dt>Authority key</dt><dd><code>{detail.definition.contentAuthority.authorityKey}</code></dd></div>
      {!designOwned ? <div><dt>Source identity</dt><dd><code>{detail.definition.contentAuthority.sourceId ?? "Not disclosed"}</code></dd></div> : null}
      <div><dt>Provider</dt><dd>{provider}</dd></div>
      <div><dt>Recommended version</dt><dd>{detail.lifecycle.recommendation ? `${detail.lifecycle.recommendation.version} · ${detail.lifecycle.recommendation.lifecycle}` : "Not recommended"}</dd></div>
    </dl></details>
    <div className="ad-tabs" role="tablist" aria-label="Activity Definition sections" onKeyDown={event => handleTabKey(event, tabs, activeSection, tabRefs.current, navigateSection)}>
      {tabs.map((tab, index) => <button key={tab.id} ref={element => { tabRefs.current[index] = element; }} type="button" role="tab" aria-selected={activeSection === tab.id} tabIndex={activeSection === tab.id ? 0 : -1} onClick={() => navigateSection(tab.id)}>{tab.label}</button>)}
    </div>
    <section className="ad-tab-panel" role="tabpanel" aria-label={tabs.find(tab => tab.id === activeSection)?.label}>
      {activeSection === "overview" ? <Overview detail={detail} provider={provider} designOwned={designOwned} versionQuery={exactVersionQuery} onOpen={navigateSection} /> : null}
      {activeSection === "drafts" ? <DraftsSection query={draftsQuery} exactQuery={exactDraftQuery} exactFailure={effectiveDraftSelectionFailure} privacyFailure={draftPrivacyFailure} selectedDraftId={selectedDraftId} page={draftCursorIndex + 1} designOwned={designOwned} canCreateDraft={canCreateDraft} onCreate={() => setDraftDialogSource(null)} onPrevious={() => setDraftCursorIndex(current => Math.max(0, current - 1))} onNext={() => { const continuation = draftsQuery.data?.continuation; if (!continuation) return; setDraftCursorStack(current => [...current.slice(0, draftCursorIndex + 1), continuation]); setDraftCursorIndex(current => current + 1); }} onRetry={() => { setDraftPrivacyFailure(null); void draftsQuery.refetch(); }} onSelect={draftId => navigateSection("drafts", draftId, null)} onEdit={draftId => onNavigate({ definitionId, section: "editor", draftId, versionId: null })} onPlanUpgrade={draftId => onPlanUpgrade("ActivityDraft", draftId)} /> : null}
      {activeSection === "versions" ? <VersionsSection query={versionsQuery} exactQuery={exactVersionQuery} exactFailure={effectiveVersionSelectionFailure} privacyFailure={versionPrivacyFailure} selectedVersionId={selectedVersionId} page={versionCursorIndex + 1} designOwned={designOwned} onCreateFromVersion={versionId => setDraftDialogSource(versionId)} onManage={(action, version) => setLifecycleDialog({ action, version })} onPrevious={() => setVersionCursorIndex(current => Math.max(0, current - 1))} onNext={() => { const continuation = versionsQuery.data?.continuation; if (!continuation) return; setVersionCursorStack(current => [...current.slice(0, versionCursorIndex + 1), continuation]); setVersionCursorIndex(current => current + 1); }} onRetry={() => { setVersionPrivacyFailure(null); void versionsQuery.refetch(); }} onSelect={versionId => navigateSection("versions", null, versionId)} onPlanUpgrade={versionId => onPlanUpgrade("ActivityVersion", versionId)} /> : null}
      {activeSection === "relationships" ? <Suspense fallback={<div className="ad-inline-status" role="status">Loading dependency explorer…</div>}><ActivityDefinitionDependencyExplorer context={context} definitionId={definitionId} initialVersionId={!designOwned ? detail.lifecycle.recommendation?.versionId : undefined} initialDirection={!designOwned ? "inbound" : undefined} /></Suspense> : null}
    </section>
  </main>
  {draftDialogSource !== false ? <ActivityDefinitionDraftDialog context={context} definitionId={definitionId} activityEditors={activityEditors} initialSourceVersionId={draftDialogSource} onClose={() => setDraftDialogSource(false)} onCreated={draft => { setDraftDialogSource(false); void queryClient.invalidateQueries({ queryKey: activityDesignKeys.definitionResources }); onNavigate({ definitionId, section: "editor", draftId: draft.draftId, versionId: null }); }} /> : null}
  {forkDialogOpen && recommendedActiveVersion ? <ActivityDefinitionForkDialog context={context} definition={detail} activityEditors={activityEditors} onClose={() => setForkDialogOpen(false)} onApplied={receipt => { setForkDialogOpen(false); void queryClient.invalidateQueries({ queryKey: activityDesignKeys.all }); onNavigate({ definitionId: receipt.definition.definitionId, section: "editor", draftId: receipt.draft.draftId, versionId: null }); }} /> : null}
  {lifecycleDialog ? <Suspense fallback={<div className="ad-dialog-backdrop" role="presentation"><div className="ad-dialog" role="status">Loading exact lifecycle review…</div></div>}><ActivityDefinitionVersionLifecycleDialog context={context} definition={detail} version={lifecycleDialog.version} action={lifecycleDialog.action} onClose={() => setLifecycleDialog(null)} onApplied={() => { setLifecycleDialog(null); void queryClient.invalidateQueries({ queryKey: activityDesignKeys.all }); }} /></Suspense> : null}
  </>;
}

function Overview({ detail, provider, designOwned, versionQuery, onOpen }: { detail: ActivityDefinitionManagementView; provider: string; designOwned: boolean; versionQuery: ReturnType<typeof useActivityDefinitionVersion>; onOpen(section: Section): void }) {
  if (!designOwned) return <div className="ad-source-overview">
    <dl className="ad-source-summary">
      <div><dt>Category</dt><dd>{detail.definition.category}</dd></div>
      <div><dt>Recommended version</dt><dd>{detail.lifecycle.recommendation ? displayActivityDefinitionVersion(detail.lifecycle.recommendation.version) : "None"}</dd></div>
    </dl>
    {isActivityDefinitionEnumValue(detail.lifecycle.recommendation?.lifecycle, "Active") ? versionQuery.isError
      ? <div className="ad-inline-error" role="alert">Activity details could not be loaded. <button type="button" onClick={() => void versionQuery.refetch()}>Try again</button></div>
      : versionQuery.data?.contract ? <HistoricalContract contract={versionQuery.data.contract} compact />
      : <div className="ad-inline-status" role="status">Loading activity details…</div>
      : <p>No recommended active version is available. Choose a version to inspect its inputs and outputs.</p>}
  </div>;

  return <div className="ad-overview-grid">
    <article><span>Mutable work</span><strong>{detail.lifecycle.draftCount} drafts</strong><p>Drafts are independent mutable authoring states. Opening a draft keeps its exact identity in the URL.</p><button type="button" onClick={() => onOpen("drafts")}>View drafts</button></article>
    <article><span>Immutable history</span><strong>{detail.lifecycle.versionCount} versions</strong><p>Published versions are immutable and remain separate from the definition's recommendation.</p><button type="button" onClick={() => onOpen("versions")}>View versions</button></article>
    <article><span>Implementation provider</span><strong>{provider}</strong><p>The workbench does not load provider editors or interpret provider manifests.</p></article>
    <article><span>Relationships</span><strong>Dependencies & usage</strong><p>Enter the relationship surface from the stable definition, never from an arbitrary draft.</p><button type="button" onClick={() => onOpen("relationships")}>Explore relationships</button></article>
  </div>;
}

function DraftsSection({ query, exactQuery, exactFailure, privacyFailure, selectedDraftId, page, designOwned, canCreateDraft, onCreate, onPrevious, onNext, onRetry, onSelect, onEdit, onPlanUpgrade }: { query: ReturnType<typeof useActivityDefinitionDrafts>; exactQuery: ReturnType<typeof useActivityDefinitionDraft>; exactFailure: ReturnType<typeof classifyActivityDefinitionReadFailure> | null; privacyFailure: "unavailable" | "forbidden" | "not-found" | null; selectedDraftId: string | null; page: number; designOwned: boolean; canCreateDraft: boolean; onCreate(): void; onPrevious(): void; onNext(): void; onRetry(): void; onSelect(draftId: string): void; onEdit(draftId: string): void; onPlanUpgrade(draftId: string): void }) {
  const data = privacyFailure ? undefined : query.data;
  const selected = privacyFailure || exactFailure ? undefined : data?.items.find(item => item.draft.draftId === selectedDraftId) ?? exactQuery.data;
  return <div><div className="ad-section-heading-row"><SectionHeading icon={<GitBranch size={18} />} title="Drafts" description="Mutable drafts are listed by exact identity. Presentation labels are optional and need not be unique." />{canCreateDraft ? <button type="button" onClick={onCreate}>Create parallel draft</button> : null}</div>{!designOwned ? <div className="ad-unavailable-note" role="status"><strong>No mutable source-owned drafts</strong><span>Choose an immutable version and fork it into a new Design-owned identity.</span></div> : null}{selected ? <SelectedDraft item={selected} onEdit={() => onEdit(selected.draft.draftId)} onPlanUpgrade={() => onPlanUpgrade(selected.draft.draftId)} /> : <ExactSelectionState kind="draft" selected={Boolean(selectedDraftId)} query={exactQuery} failure={exactFailure} />}<ChildCollectionState query={query} privacyFailure={privacyFailure} empty="No authorized drafts are available for this definition." onRetry={onRetry} />{data?.items.length ? <div className="ad-child-list" role="group" aria-label="Activity Definition drafts">{data.items.map(item => <button key={item.draft.draftId} type="button" className={item.draft.draftId === selectedDraftId ? "is-selected" : ""} onClick={() => onSelect(item.draft.draftId)}><span><strong>{draftLabel(item)}</strong><small>Revision {item.draft.revision} · {item.draft.status}</small></span><span><strong>{item.draft.providerKey}</strong><small>{formatDate(item.draft.updatedAt)}</small></span></button>)}</div> : null}<CursorPager page={page} data={data} onPrevious={onPrevious} onNext={onNext} /></div>;
}

function VersionsSection({ query, exactQuery, exactFailure, privacyFailure, selectedVersionId, page, designOwned, onCreateFromVersion, onManage, onPrevious, onNext, onRetry, onSelect, onPlanUpgrade }: { query: ReturnType<typeof useActivityDefinitionVersions>; exactQuery: ReturnType<typeof useActivityDefinitionVersion>; exactFailure: ReturnType<typeof classifyActivityDefinitionReadFailure> | null; privacyFailure: "unavailable" | "forbidden" | "not-found" | null; selectedVersionId: string | null; page: number; designOwned: boolean; onCreateFromVersion(versionId: string): void; onManage(action: ActivityDefinitionVersionLifecycleAction, version: ActivityDefinitionVersionManagementView): void; onPrevious(): void; onNext(): void; onRetry(): void; onSelect(versionId: string): void; onPlanUpgrade(versionId: string): void }) {
  const data = privacyFailure ? undefined : query.data;
  const listedSelection = data?.items.find(item => item.version.versionId === selectedVersionId);
  const selected = privacyFailure || exactFailure
    ? undefined
    : exactQuery.data
      ? { ...exactQuery.data, actions: listedSelection?.actions ?? exactQuery.data.actions }
      : listedSelection;
  const actionAllowed = Boolean(designOwned && listedSelection && isActionAllowed(listedSelection.actions, "clone-draft"));
  return <div><SectionHeading icon={<Layers3 size={18} />} title="Versions" description={designOwned ? "Every row is an immutable semantic version. Recommendation and lifecycle are reviewed exact-version decisions." : "Versions supplied by the module."} />{selected ? <SelectedVersion item={selected} compact={!designOwned} actionLabel="Create draft from this exact version" actionAllowed={actionAllowed} onCreate={() => onCreateFromVersion(selected.version.versionId)} onPlanUpgrade={() => onPlanUpgrade(selected.version.versionId)} onManage={action => onManage(action, selected)} /> : <ExactSelectionState kind="version" selected={Boolean(selectedVersionId)} query={exactQuery} failure={exactFailure} />}<ChildCollectionState query={query} privacyFailure={privacyFailure} empty="No authorized versions are available for this definition." onRetry={onRetry} />{data?.items.length ? <div className="ad-child-list" role="group" aria-label="Activity Definition versions">{data.items.map(item => <button key={item.version.versionId} type="button" className={item.version.versionId === selectedVersionId ? "is-selected" : ""} onClick={() => onSelect(item.version.versionId)}><span><strong>{designOwned ? item.version.version : displayActivityDefinitionVersion(item.version.version)}{item.isRecommended ? " · Recommended" : ""}</strong><small>{item.version.lifecycle} · {formatDate(item.version.publishedAt)}</small></span>{designOwned ? <span><strong>{item.providerKey}</strong><small>Schema {item.providerSchemaVersion}</small></span> : null}</button>)}</div> : null}<CursorPager page={page} data={data} onPrevious={onPrevious} onNext={onNext} /></div>;
}

function ChildCollectionState({ query, privacyFailure, empty, onRetry }: { query: ReturnType<typeof useActivityDefinitionDrafts> | ReturnType<typeof useActivityDefinitionVersions>; privacyFailure: "unavailable" | "forbidden" | "not-found" | null; empty: string; onRetry(): void }) {
  if (privacyFailure) return <div className="ad-inline-error" role="alert"><span>{privacyFailure === "unavailable" ? "This backend does not advertise this supported management collection. No fallback is used." : privacyFailure === "not-found" ? "This collection is no longer available. No identities or counts are shown." : "This account cannot open this collection. No identities or counts are shown."}</span> <button type="button" onClick={onRetry}>Try again</button></div>;
  if (query.isPending) return <div className="ad-inline-status" role="status">Loading authorized rows…</div>;
  if (query.isError && !query.data) return <div className="ad-inline-error" role="alert">This collection could not be confirmed. No unconfirmed rows are shown.</div>;
  if (query.isError && query.data) return <div className="ad-inline-error" role="alert">Refresh failed; retained rows remain visible as stale evidence.</div>;
  if (query.data?.items.length === 0) return <div className="ad-inline-status" role="status">{empty}</div>;
  return null;
}

function ExactSelectionState({ kind, selected, query, failure }: { kind: "draft" | "version"; selected: boolean; query: ReturnType<typeof useActivityDefinitionDraft> | ReturnType<typeof useActivityDefinitionVersion>; failure: ReturnType<typeof classifyActivityDefinitionReadFailure> | null }) {
  if (!selected) return null;
  if (query.isPending) return <div className="ad-inline-status" role="status">Loading the exact selected {kind}…</div>;
  if (failure) return <div className="ad-inline-error" role="alert">The exact selected {kind} could not be confirmed. No unconfirmed identity is shown.</div>;
  return null;
}

function CursorPager({ page, data, onPrevious, onNext }: { page: number; data: { hasMore: boolean; continuation?: string | null } | undefined; onPrevious(): void; onNext(): void }) {
  if (!data || (page <= 1 && !data.hasMore)) return null;
  return <div className="ad-child-pager"><button type="button" onClick={onPrevious} disabled={page <= 1}><ChevronLeft size={15} aria-hidden /> Previous</button><span>Page {page}</span><button type="button" onClick={onNext} disabled={!data.hasMore || !data.continuation}>Next <ChevronRight size={15} aria-hidden /></button></div>;
}

function SectionHeading({ icon, title, description }: { icon: ReactNode; title: string; description: string }) { return <header className="ad-section-heading"><span aria-hidden>{icon}</span><div><h2>{title}</h2><p>{description}</p></div></header>; }
function SelectedDraft({ item, onEdit, onPlanUpgrade }: { item: ActivityDefinitionDraftManagementView; onEdit(): void; onPlanUpgrade(): void }) { return <div className="ad-selected-record" aria-label="Selected exact draft"><strong>{draftLabel(item)}</strong><span><code>{item.draft.draftId}</code> · revision {item.draft.revision} · {item.draft.providerKey}</span><div className="ad-header-actions"><button type="button" onClick={onEdit}>Open implementation editor</button><button type="button" onClick={onPlanUpgrade}>Plan broad upgrade from this exact draft</button></div></div>; }
function SelectedVersion({ item, compact, actionLabel, actionAllowed, onCreate, onPlanUpgrade, onManage }: { item: ActivityDefinitionVersionManagementView; compact: boolean; actionLabel: string; actionAllowed: boolean; onCreate(): void; onPlanUpgrade(): void; onManage(action: ActivityDefinitionVersionLifecycleAction): void }) {
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
  return <div className="ad-selected-version">
    <div className="ad-selected-record" aria-label="Selected exact version">
      <strong>Version {compact ? displayActivityDefinitionVersion(item.version.version) : item.version.version}{item.isRecommended ? " · Recommended" : ""}</strong>
      {!compact ? <span><code>{item.version.versionId}</code> · {item.version.lifecycle} · {item.providerKey} · schema {item.providerSchemaVersion}</span> : <span>{item.version.lifecycle}</span>}
      {!compact ? actions : null}
      <details className="ad-technical-details"><summary>Version details</summary>
        {compact ? actions : null}
        {compact ? <p><code>{item.version.versionId}</code> · {item.version.version} · {item.providerKey} · schema {item.providerSchemaVersion}</p> : null}
        <button type="button" onClick={onPlanUpgrade}>Plan broad upgrade from this exact version</button>
      </details>
    </div>
    {item.contract ? <HistoricalContract contract={item.contract} compact={compact} /> : <div className="ad-inline-status" role="status">Loading activity details…</div>}
  </div>;

}

function HistoricalContract({ contract, compact = false }: { contract: NonNullable<ActivityDefinitionVersionManagementView["contract"]>; compact?: boolean }) {
  return <section className="ad-historical-contract" aria-labelledby="historical-contract-title"><header>{compact ? <h2 id="historical-contract-title">Inputs, outputs &amp; outcomes</h2> : <h3 id="historical-contract-title">Immutable public contract</h3>}{!compact ? <span>Schema {contract.contractSchemaVersion}</span> : null}</header>{!compact ? <p>Stored aliases remain visible even when they are no longer available in the current authoring catalog.</p> : null}
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
function compactContractType(alias: string, collectionKind: string, requirement: string, isNullable: boolean) {
  return [alias, collectionKind === "Scalar" ? null : collectionKind, requirement, isNullable ? "Allows null" : "Non-null"]
    .filter(Boolean)
    .join(" · ");
}
function draftLabel(item: ActivityDefinitionDraftManagementView) { return item.draft.presentationLabel?.trim() || `Draft ${item.draft.draftId}`; }
function normalizeSection(value: string | null): Section { return value === "drafts" || value === "versions" || value === "relationships" ? value : "overview"; }

function handleTabKey(event: KeyboardEvent, tabs: { id: Section }[], active: Section, elements: (HTMLButtonElement | null)[], navigate: (section: Section) => void) {
  if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
  event.preventDefault();
  const current = tabs.findIndex(tab => tab.id === active);
  const next = event.key === "Home" ? 0 : event.key === "End" ? tabs.length - 1 : (current + (event.key === "ArrowRight" ? 1 : -1) + tabs.length) % tabs.length;
  navigate(tabs[next].id);
  window.setTimeout(() => elements[next]?.focus());
}
