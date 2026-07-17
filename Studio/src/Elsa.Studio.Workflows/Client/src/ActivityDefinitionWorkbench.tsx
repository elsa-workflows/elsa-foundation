import { useEffect, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import { AlertTriangle, ArrowLeft, ChevronLeft, ChevronRight, GitBranch, Layers3, RefreshCw } from "lucide-react";
import { useQueryClient } from "@tanstack/react-query";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { ActivityDefinitionDraftManagementView, ActivityDefinitionManagementView, ActivityDefinitionVersionManagementView } from "./activityDefinitionTypes";
import { activityDesignKeys, classifyActivityDefinitionReadFailure, redactActivityDefinitionChildCache, redactActivityDefinitionManagementCache, useActivityDefinition, useActivityDefinitionDraft, useActivityDefinitionDrafts, useActivityDefinitionVersion, useActivityDefinitionVersions } from "./api/activityDesign";
import { observeActivityDefinitions } from "./activityDefinitionObservability";

type Section = "overview" | "drafts" | "versions" | "relationships";
type RouteState = { definitionId: string | null; section: string | null; draftId: string | null; versionId: string | null };

export function ActivityDefinitionWorkbench({ context, definitionId, section, selectedDraftId, selectedVersionId, onNavigate }: {
  context: StudioEndpointContext;
  definitionId: string;
  section: string | null;
  selectedDraftId: string | null;
  selectedVersionId: string | null;
  onNavigate(next: RouteState): void;
}) {
  const queryClient = useQueryClient();
  const activeSection = selectedDraftId ? "drafts" : selectedVersionId ? "versions" : normalizeSection(section);
  const [draftCursorStack, setDraftCursorStack] = useState<(string | null)[]>([null]);
  const [draftCursorIndex, setDraftCursorIndex] = useState(0);
  const [versionCursorStack, setVersionCursorStack] = useState<(string | null)[]>([null]);
  const [versionCursorIndex, setVersionCursorIndex] = useState(0);
  const [privacyFailure, setPrivacyFailure] = useState<"unavailable" | "forbidden" | "not-found" | null>(null);
  const [draftPrivacyFailure, setDraftPrivacyFailure] = useState<"unavailable" | "forbidden" | "not-found" | null>(null);
  const [versionPrivacyFailure, setVersionPrivacyFailure] = useState<"unavailable" | "forbidden" | "not-found" | null>(null);
  const [draftSelectionFailure, setDraftSelectionFailure] = useState<{ id: string; kind: "unavailable" | "forbidden" | "not-found" } | null>(null);
  const [versionSelectionFailure, setVersionSelectionFailure] = useState<{ id: string; kind: "unavailable" | "forbidden" | "not-found" } | null>(null);
  const detailQuery = useActivityDefinition(context, definitionId);
  const directFailure = detailQuery.error ? classifyActivityDefinitionReadFailure(detailQuery.error) : null;
  const detailFailure = privacyFailure ?? directFailure;
  const mustDiscardData = detailFailure === "forbidden" || detailFailure === "unavailable" || detailFailure === "not-found";
  const detail = mustDiscardData ? undefined : detailQuery.data;
  const childEnabled = Boolean(detail && !detailQuery.error);
  const draftRequest = { definitionId, limit: 10, cursor: draftCursorStack[draftCursorIndex] };
  const versionRequest = { definitionId, limit: 10, cursor: versionCursorStack[versionCursorIndex] };
  const draftsQuery = useActivityDefinitionDrafts(context, draftRequest, childEnabled && activeSection === "drafts");
  const versionsQuery = useActivityDefinitionVersions(context, versionRequest, childEnabled && activeSection === "versions");
  const selectedDraftIsOnPage = Boolean(selectedDraftId && draftsQuery.data?.items.some(item => item.draft.draftId === selectedDraftId));
  const selectedVersionIsOnPage = Boolean(selectedVersionId && versionsQuery.data?.items.some(item => item.version.versionId === selectedVersionId));
  const exactDraftQuery = useActivityDefinitionDraft(context, definitionId, selectedDraftId, childEnabled && activeSection === "drafts" && !selectedDraftIsOnPage);
  const exactVersionQuery = useActivityDefinitionVersion(context, definitionId, selectedVersionId, childEnabled && activeSection === "versions" && !selectedVersionIsOnPage);
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
    { id: "drafts", label: "Drafts" },
    { id: "versions", label: "Versions" },
    { id: "relationships", label: "Dependencies & usage" }
  ];

  return <main className="ad-page ad-workbench" aria-labelledby="activity-definition-title">
    <button type="button" className="ad-back" onClick={() => onNavigate({ definitionId: null, section: null, draftId: null, versionId: null })}><ArrowLeft size={16} aria-hidden /> Activity Definitions</button>
    <header className="ad-workbench-header"><div><span className="ad-kicker">Stable Activity Definition</span><h1 id="activity-definition-title">{detail.definition.displayName}</h1><p>{detail.definition.description || "No description provided."}</p></div><button type="button" className="ad-primary-action" onClick={() => { setPrivacyFailure(null); observeActivityDefinitions({ event: "refresh", surface: "workbench" }); void detailQuery.refetch(); }} disabled={detailQuery.isFetching}><RefreshCw size={16} aria-hidden /> {detailQuery.isFetching ? "Refreshing" : "Refresh"}</button></header>
    {detailQuery.isError ? <div className="ad-stale-warning" role="alert"><AlertTriangle size={18} aria-hidden /><div><strong>Refresh failed; showing retained definition evidence</strong><span>Retry before relying on lifecycle changes.</span></div></div> : null}
    <dl className="ad-identity-strip">
      <div><dt>Activity Type Key</dt><dd><code>{detail.definition.activityTypeKey}</code></dd></div>
      <div><dt>Definition identity</dt><dd><code>{detail.definition.definitionId}</code></dd></div>
      <div><dt>Authority</dt><dd>{detail.definition.contentAuthority.kind === "Design" ? "Design owned" : "Source owned"}</dd></div>
      <div><dt>Provider</dt><dd>{provider}</dd></div>
      <div><dt>Recommended version</dt><dd>{detail.lifecycle.recommendation ? `${detail.lifecycle.recommendation.version} · ${detail.lifecycle.recommendation.lifecycle}` : "Not recommended"}</dd></div>
    </dl>
    <div className="ad-tabs" role="tablist" aria-label="Activity Definition sections" onKeyDown={event => handleTabKey(event, tabs, activeSection, tabRefs.current, navigateSection)}>
      {tabs.map((tab, index) => <button key={tab.id} ref={element => { tabRefs.current[index] = element; }} type="button" role="tab" aria-selected={activeSection === tab.id} tabIndex={activeSection === tab.id ? 0 : -1} onClick={() => navigateSection(tab.id)}>{tab.label}</button>)}
    </div>
    <section className="ad-tab-panel" role="tabpanel" aria-label={tabs.find(tab => tab.id === activeSection)?.label}>
      {activeSection === "overview" ? <Overview detail={detail} provider={provider} onOpen={navigateSection} /> : null}
      {activeSection === "drafts" ? <DraftsSection query={draftsQuery} exactQuery={exactDraftQuery} exactFailure={effectiveDraftSelectionFailure} privacyFailure={draftPrivacyFailure} selectedDraftId={selectedDraftId} page={draftCursorIndex + 1} onPrevious={() => setDraftCursorIndex(current => Math.max(0, current - 1))} onNext={() => { const continuation = draftsQuery.data?.continuation; if (!continuation) return; setDraftCursorStack(current => [...current.slice(0, draftCursorIndex + 1), continuation]); setDraftCursorIndex(current => current + 1); }} onRetry={() => { setDraftPrivacyFailure(null); void draftsQuery.refetch(); }} onSelect={draftId => navigateSection("drafts", draftId, null)} /> : null}
      {activeSection === "versions" ? <VersionsSection query={versionsQuery} exactQuery={exactVersionQuery} exactFailure={effectiveVersionSelectionFailure} privacyFailure={versionPrivacyFailure} selectedVersionId={selectedVersionId} page={versionCursorIndex + 1} onPrevious={() => setVersionCursorIndex(current => Math.max(0, current - 1))} onNext={() => { const continuation = versionsQuery.data?.continuation; if (!continuation) return; setVersionCursorStack(current => [...current.slice(0, versionCursorIndex + 1), continuation]); setVersionCursorIndex(current => current + 1); }} onRetry={() => { setVersionPrivacyFailure(null); void versionsQuery.refetch(); }} onSelect={versionId => navigateSection("versions", null, versionId)} /> : null}
      {activeSection === "relationships" ? <RelationshipsSection onOpen={navigateSection} /> : null}
    </section>
  </main>;
}

function Overview({ detail, provider, onOpen }: { detail: ActivityDefinitionManagementView; provider: string; onOpen(section: Section): void }) {
  return <div className="ad-overview-grid">
    <article><span>Mutable work</span><strong>{detail.lifecycle.draftCount} drafts</strong><p>Drafts are independent mutable authoring states. Opening a draft keeps its exact identity in the URL.</p><button type="button" onClick={() => onOpen("drafts")}>View drafts</button></article>
    <article><span>Immutable history</span><strong>{detail.lifecycle.versionCount} versions</strong><p>Published versions are immutable and remain separate from the definition's recommendation.</p><button type="button" onClick={() => onOpen("versions")}>View versions</button></article>
    <article><span>Implementation provider</span><strong>{provider}</strong><p>The workbench does not load provider editors or interpret provider manifests.</p></article>
    <article><span>Relationships</span><strong>Dependencies & usage</strong><p>Enter the relationship surface from the stable definition, never from an arbitrary draft.</p><button type="button" onClick={() => onOpen("relationships")}>Explore relationships</button></article>
  </div>;
}

function DraftsSection({ query, exactQuery, exactFailure, privacyFailure, selectedDraftId, page, onPrevious, onNext, onRetry, onSelect }: { query: ReturnType<typeof useActivityDefinitionDrafts>; exactQuery: ReturnType<typeof useActivityDefinitionDraft>; exactFailure: ReturnType<typeof classifyActivityDefinitionReadFailure> | null; privacyFailure: "unavailable" | "forbidden" | "not-found" | null; selectedDraftId: string | null; page: number; onPrevious(): void; onNext(): void; onRetry(): void; onSelect(draftId: string): void }) {
  const data = privacyFailure ? undefined : query.data;
  const selected = privacyFailure || exactFailure ? undefined : data?.items.find(item => item.draft.draftId === selectedDraftId) ?? exactQuery.data;
  return <div><SectionHeading icon={<GitBranch size={18} />} title="Drafts" description="Mutable drafts are listed by exact identity. Presentation labels are optional and need not be unique." />{selected ? <SelectedDraft item={selected} /> : <ExactSelectionState kind="draft" selected={Boolean(selectedDraftId)} query={exactQuery} failure={exactFailure} />}<ChildCollectionState query={query} privacyFailure={privacyFailure} empty="No authorized drafts are available for this definition." onRetry={onRetry} />{data?.items.length ? <div className="ad-child-list" role="group" aria-label="Activity Definition drafts">{data.items.map(item => <button key={item.draft.draftId} type="button" className={item.draft.draftId === selectedDraftId ? "is-selected" : ""} onClick={() => onSelect(item.draft.draftId)}><span><strong>{draftLabel(item)}</strong><small>Revision {item.draft.revision} · {item.draft.status}</small></span><span><strong>{item.draft.providerKey}</strong><small>{formatDate(item.draft.updatedAt)}</small></span></button>)}</div> : null}<CursorPager page={page} data={data} onPrevious={onPrevious} onNext={onNext} /></div>;
}

function VersionsSection({ query, exactQuery, exactFailure, privacyFailure, selectedVersionId, page, onPrevious, onNext, onRetry, onSelect }: { query: ReturnType<typeof useActivityDefinitionVersions>; exactQuery: ReturnType<typeof useActivityDefinitionVersion>; exactFailure: ReturnType<typeof classifyActivityDefinitionReadFailure> | null; privacyFailure: "unavailable" | "forbidden" | "not-found" | null; selectedVersionId: string | null; page: number; onPrevious(): void; onNext(): void; onRetry(): void; onSelect(versionId: string): void }) {
  const data = privacyFailure ? undefined : query.data;
  const selected = privacyFailure || exactFailure ? undefined : data?.items.find(item => item.version.versionId === selectedVersionId) ?? exactQuery.data;
  return <div><SectionHeading icon={<Layers3 size={18} />} title="Versions" description="Every row is an immutable semantic version. Recommendation remains an explicit definition-level decision." />{selected ? <SelectedVersion item={selected} /> : <ExactSelectionState kind="version" selected={Boolean(selectedVersionId)} query={exactQuery} failure={exactFailure} />}<ChildCollectionState query={query} privacyFailure={privacyFailure} empty="No authorized versions are available for this definition." onRetry={onRetry} />{data?.items.length ? <div className="ad-child-list" role="group" aria-label="Activity Definition versions">{data.items.map(item => <button key={item.version.versionId} type="button" className={item.version.versionId === selectedVersionId ? "is-selected" : ""} onClick={() => onSelect(item.version.versionId)}><span><strong>{item.version.version}{item.isRecommended ? " · Recommended" : ""}</strong><small>{item.version.lifecycle} · {formatDate(item.version.publishedAt)}</small></span><span><strong>{item.providerKey}</strong><small>Schema {item.providerSchemaVersion}</small></span></button>)}</div> : null}<CursorPager page={page} data={data} onPrevious={onPrevious} onNext={onNext} /></div>;
}

function RelationshipsSection({ onOpen }: { onOpen(section: Section): void }) {
  return <div><SectionHeading icon={<Layers3 size={18} />} title="Dependencies & usage" description="Relationship exploration starts from this stable Activity Definition identity." /><div className="ad-relationship-entrypoints"><article><h2>Mutable relationships</h2><p>Review the definition's independently mutable work before opening dependency evidence.</p><button type="button" onClick={() => onOpen("drafts")}>Open drafts</button></article><article><h2>Immutable relationships</h2><p>Review exact published versions before opening usage or path evidence.</p><button type="button" onClick={() => onOpen("versions")}>Open versions</button></article></div><div className="ad-unavailable-note" role="status"><strong>Relationship contribution unavailable</strong><span>This backend does not advertise dependency or usage management relations yet. Studio does not infer them from workflow-as-activity data.</span></div></div>;
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
  if (!data) return null;
  return <div className="ad-child-pager"><button type="button" onClick={onPrevious} disabled={page <= 1}><ChevronLeft size={15} aria-hidden /> Previous</button><span>Page {page}</span><button type="button" onClick={onNext} disabled={!data.hasMore || !data.continuation}>Next <ChevronRight size={15} aria-hidden /></button></div>;
}

function SectionHeading({ icon, title, description }: { icon: ReactNode; title: string; description: string }) { return <header className="ad-section-heading"><span aria-hidden>{icon}</span><div><h2>{title}</h2><p>{description}</p></div></header>; }
function SelectedDraft({ item }: { item: ActivityDefinitionDraftManagementView }) { return <div className="ad-selected-record" aria-label="Selected exact draft"><strong>{draftLabel(item)}</strong><span><code>{item.draft.draftId}</code> · revision {item.draft.revision} · {item.draft.providerKey}</span></div>; }
function SelectedVersion({ item }: { item: ActivityDefinitionVersionManagementView }) { return <div className="ad-selected-record" aria-label="Selected exact version"><strong>Version {item.version.version}</strong><span><code>{item.version.versionId}</code> · {item.version.lifecycle} · {item.providerKey}</span></div>; }
function draftLabel(item: ActivityDefinitionDraftManagementView) { return item.draft.presentationLabel?.trim() || `Draft updated ${formatDate(item.draft.updatedAt)}`; }
function normalizeSection(value: string | null): Section { return value === "drafts" || value === "versions" || value === "relationships" ? value : "overview"; }

function handleTabKey(event: KeyboardEvent, tabs: { id: Section }[], active: Section, elements: (HTMLButtonElement | null)[], navigate: (section: Section) => void) {
  if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
  event.preventDefault();
  const current = tabs.findIndex(tab => tab.id === active);
  const next = event.key === "Home" ? 0 : event.key === "End" ? tabs.length - 1 : (current + (event.key === "ArrowRight" ? 1 : -1) + tabs.length) % tabs.length;
  navigate(tabs[next].id);
  window.setTimeout(() => elements[next]?.focus());
}

function formatDate(value: string | null | undefined) { if (!value) return "—"; const date = new Date(value); return Number.isNaN(date.getTime()) ? "—" : date.toLocaleDateString(); }
