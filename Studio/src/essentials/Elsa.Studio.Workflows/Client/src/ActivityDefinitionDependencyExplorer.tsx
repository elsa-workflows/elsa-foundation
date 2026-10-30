import { CopyableValue } from "@elsa-workflows/studio-ui";
import { useEffect, useRef, useState, type UIEvent } from "react";
import { useInfiniteQuery } from "@tanstack/react-query";
import { ArrowRight, RefreshCw } from "lucide-react";
import { StudioHttpError, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type {
  ActivityDefinitionDependencyItem,
  ActivityDefinitionDependencyReference,
  ActivityDefinitionUsageEvidence
} from "./activityDefinitionTypes";
import { activityDesignKeys, useActivityDefinitionVersion, useActivityDefinitionVersions } from "./api/activityDesign";
import { displayActivityDefinitionVersion } from "./activityDefinitionDraftManagementShared";
import { getActivityDefinitionDependencyPage } from "./api/activityDefinitionDependencies";
import { StudioLink } from "./StudioLink";
import dependencyExplorerStylesUrl from "./activityDefinitionDependencyExplorer.css?url&no-inline";

const dependencyPageSize = 100;
const dependencyRowHeight = 116;
const dependencyViewportHeight = 520;
const dependencyExplorerStylesHref = import.meta.env.PROD && dependencyExplorerStylesUrl.startsWith("/")
  ? new URL(dependencyExplorerStylesUrl.slice(1), import.meta.url).href
  : dependencyExplorerStylesUrl;

type DependencyDirection = "outbound" | "inbound";
type IncludedArtifact = "versions" | "drafts";

export function ActivityDefinitionDependencyExplorer({
  context,
  definitionId,
  initialVersionId,
  initialDirection
}: {
  context: StudioEndpointContext;
  definitionId: string;
  initialVersionId?: string;
  initialDirection?: DependencyDirection;
}) {
  const [versionCursors, setVersionCursors] = useState<(string | null)[]>([null]);
  const [versionPage, setVersionPage] = useState(0);
  const [rootVersionId, setRootVersionId] = useState("");
  const [direction, setDirection] = useState<DependencyDirection>(initialDirection ?? "outbound");
  const [transitive, setTransitive] = useState(false);
  const [included, setIncluded] = useState<IncludedArtifact[]>(["versions", "drafts"]);
  const [generation, setGeneration] = useState(0);
  const [selectedRelationshipId, setSelectedRelationshipId] = useState<string | null>(null);
  const [scrollTop, setScrollTop] = useState(0);
  const initializedVersionId = useRef<string | null>(null);
  const versionRequest = {
    definitionId,
    limit: 50,
    cursor: versionCursors[versionPage]
  };
  const versionsQuery = useActivityDefinitionVersions(context, versionRequest, true);
  const initialVersionQuery = useActivityDefinitionVersion(context, definitionId, initialVersionId ?? null, Boolean(initialVersionId));
  const listedRootVersion = !versionsQuery.isError
    ? versionsQuery.data?.items.find(item => item.version.definitionId === definitionId && item.version.versionId === rootVersionId) ?? null
    : null;
  const exactInitialVersion = initialVersionQuery.data;
  const confirmedInitialVersion = !initialVersionQuery.isError &&
    exactInitialVersion?.version?.definitionId === definitionId &&
    exactInitialVersion?.version?.versionId === initialVersionId
    ? exactInitialVersion
    : null;
  const rootVersion = listedRootVersion ?? (rootVersionId === initialVersionId ? confirmedInitialVersion : null);
  const versionOptions = !versionsQuery.isError ? versionsQuery.data?.items ?? [] : [];
  const showConfirmedInitialVersion = confirmedInitialVersion && !versionOptions.some(item => item.version.versionId === confirmedInitialVersion.version.versionId);
  const includeKey = [...included].sort().join(",");

  useEffect(() => {
    if (!initialVersionId || initialVersionQuery.isPending || initialVersionQuery.isError || !confirmedInitialVersion || initializedVersionId.current === initialVersionId) return;

    initializedVersionId.current = initialVersionId;
    setRootVersionId(current => current || initialVersionId);
  }, [confirmedInitialVersion, initialVersionId, initialVersionQuery.isError, initialVersionQuery.isPending]);

  const dependenciesQuery = useInfiniteQuery({
    queryKey: [
      ...activityDesignKeys.fullDefinitionVersion(rootVersionId || "unselected"),
      "dependencies",
      direction,
      transitive,
      includeKey,
      generation
    ],
    initialPageParam: null as string | null,
    queryFn: ({ pageParam, signal }) => getActivityDefinitionDependencyPage(context, {
      versionId: rootVersionId,
      direction,
      transitive,
      include: included,
      cursor: pageParam,
      limit: dependencyPageSize
    }, signal),
    getNextPageParam: lastPage => lastPage.nextCursor || undefined,
    enabled: Boolean(rootVersion && included.length),
    retry: false
  });

  const pages = dependenciesQuery.data?.pages ?? [];
  const firstPageMatches = pages[0]
    ? pageMatchesRequest(pages[0], rootVersionId, direction, transitive, included)
    : false;
  const binding = firstPageMatches ? dependencyBinding(pages[0]) : null;
  const mismatchIndex = binding
    ? pages.findIndex(page =>
      !pageMatchesRequest(page, rootVersionId, direction, transitive, included) ||
      dependencyBinding(page) !== binding)
    : pages.length ? 0 : -1;
  const validPages = binding ? pages.slice(0, mismatchIndex < 0 ? pages.length : mismatchIndex) : [];
  const responseBindingMismatch = validPages.length !== pages.length;
  const privacyFailure = isPrivateFailure(dependenciesQuery.error);
  const cursorFailure = dependencyCursorFailure(dependenciesQuery.error);
  const rows = !rootVersion || privacyFailure ? [] : validPages.flatMap(page => page.items);
  const selected = rows.find(item => item.relationshipId === selectedRelationshipId) ?? null;
  const snapshot = validPages[0]?.consistency ?? null;
  const start = Math.max(0, Math.floor(scrollTop / dependencyRowHeight) - 2);
  const end = Math.min(rows.length, Math.ceil((scrollTop + dependencyViewportHeight) / dependencyRowHeight) + 2);
  const visibleRows = rows.slice(start, end);
  const isStale = rows.length > 0 && (responseBindingMismatch || Boolean(cursorFailure));

  useEffect(() => {
    setSelectedRelationshipId(null);
    setScrollTop(0);
  }, [direction, generation, includeKey, rootVersionId, transitive]);

  const toggleIncluded = (kind: IncludedArtifact) => {
    setIncluded(current => current.includes(kind)
      ? current.filter(item => item !== kind)
      : [...current, kind]);
  };

  const restart = () => {
    setGeneration(current => current + 1);
  };

  const onScroll = (event: UIEvent<HTMLDivElement>) => {
    setScrollTop(event.currentTarget.scrollTop);
  };

  return <><link rel="stylesheet" href={dependencyExplorerStylesHref} /><div className="ad-dependency-explorer">
    <header className="ad-section-heading">
      <span aria-hidden><ArrowRight size={18} /></span>
      <div>
        <h2>{direction === "inbound" ? "Used by" : "Dependencies"}</h2>
        <p>{direction === "inbound"
          ? "See where this exact activity version is used."
          : "See the exact activity versions used by this one."}</p>
      </div>
    </header>

    <section className="ad-dependency-controls" aria-label="Usage query">
      <label>
        <span>Version</span>
        <select value={rootVersionId} onChange={event => setRootVersionId(event.target.value)}>
          <option value="">Choose a version</option>
          {showConfirmedInitialVersion ? <option value={confirmedInitialVersion.version.versionId} title={confirmedInitialVersion.version.version}>
            {displayActivityDefinitionVersion(confirmedInitialVersion.version.version)} · {confirmedInitialVersion.version.lifecycle}
          </option> : null}
          {versionOptions.map(item =>
            <option key={item.version.versionId} value={item.version.versionId} title={item.version.version}>
              {displayActivityDefinitionVersion(item.version.version)} · {item.version.lifecycle}
            </option>)}
        </select>
      </label>
      <fieldset className="ad-dependency-direction">
        <legend>Show</legend>
        <label><input type="radio" name="dependency-direction" checked={direction === "inbound"} onChange={() => setDirection("inbound")} /> Used by</label>
        <label><input type="radio" name="dependency-direction" checked={direction === "outbound"} onChange={() => setDirection("outbound")} /> Dependencies</label>
      </fieldset>
      <details className="ad-dependency-advanced">
        <summary>Advanced options</summary>
        <div className="ad-dependency-advanced-content">
          <fieldset>
            <legend>Search depth</legend>
            <label><input type="radio" name="dependency-depth" checked={!transitive} onChange={() => setTransitive(false)} /> Direct only</label>
            <label><input type="radio" name="dependency-depth" checked={transitive} onChange={() => setTransitive(true)} /> Include indirect</label>
          </fieldset>
          <fieldset>
            <legend>Include</legend>
            <label><input type="checkbox" checked={included.includes("versions")} onChange={() => toggleIncluded("versions")} /> Versions</label>
            <label><input type="checkbox" checked={included.includes("drafts")} onChange={() => toggleIncluded("drafts")} /> Drafts</label>
          </fieldset>
        </div>
      </details>
    </section>

    {versionPage > 0 || versionsQuery.data?.hasMore ? <div className="ad-child-pager" aria-label="Version pages">
      <button type="button" onClick={() => {
        setRootVersionId("");
        setVersionPage(current => Math.max(0, current - 1));
      }} disabled={versionPage === 0}>Previous</button>
      <span>Page {versionPage + 1}</span>
      <button type="button" onClick={() => {
        const cursor = versionsQuery.data?.continuation;
        if (!cursor) return;
        setRootVersionId("");
        setVersionCursors(current => [...current.slice(0, versionPage + 1), cursor]);
        setVersionPage(current => current + 1);
      }} disabled={!versionsQuery.data?.hasMore || !versionsQuery.data.continuation}>Next</button>
    </div> : null}

    {versionsQuery.isPending ? <div className="ad-inline-status" role="status">Loading versions…</div> : null}
    {versionsQuery.isError ? <div className="ad-inline-error" role="alert">Versions could not be confirmed. No identities or counts are shown.</div> : null}
    {!rootVersionId ? <div className="ad-inline-status" role="status">Choose a version to see matching evidence.</div> : null}
    {rootVersionId && !rootVersion ? <div className="ad-inline-error" role="alert">This version is not confirmed by an authorized read. Choose a listed version.</div> : null}
    {included.length === 0 ? <div className="ad-inline-error" role="alert">Select versions or drafts to continue.</div> : null}
    {rootVersion && included.length > 0 && dependenciesQuery.isPending ? <div className="ad-inline-status" role="status">Loading evidence…</div> : null}
    {privacyFailure ? <div className="ad-inline-error" role="alert">Evidence is unavailable for this account or version. No identities or counts are shown.</div> : null}
    {responseBindingMismatch && !rows.length ? <div className="ad-inline-error" role="alert">Results did not match this version or snapshot. No identities or counts are shown. <button type="button" onClick={restart}>Restart scan</button></div> : null}
    {dependenciesQuery.isError && !privacyFailure && !rows.length ? <div className="ad-inline-error" role="alert">Could not confirm these results. No unconfirmed rows are shown. <button type="button" onClick={restart}>Try again</button></div> : null}
    {isStale ? <div className="ad-stale-warning" role="alert"><div><strong>This scan is out of date</strong><span>{cursorFailure === "expired" ? "The snapshot is no longer available. Confirmed rows remain visible." : "The next page did not match this version, filters, or snapshot. Confirmed rows remain visible."} Pages from another snapshot were not combined.</span></div><button type="button" onClick={restart}><RefreshCw size={15} aria-hidden /> Restart scan</button></div> : null}

    {snapshot && rows.length ? <details className="ad-scan-details">
      <summary>Scan details</summary>
      <section className="ad-dependency-snapshot" aria-label="Evidence consistency">
        <div><span>Evidence</span><strong>{snapshot.kind}</strong></div>
        <div><span>Authority</span><strong>{snapshot.isAuthoritative ? "Authoritative direct facts" : "Derived projection"}</strong></div>
        <div><span>Freshness</span><strong>{snapshot.asOf ? formatTimestamp(snapshot.asOf) : "Publication truth"}</strong></div>
        <div><span>Loaded</span><strong>{rows.length} visible rows · {validPages.length} server pages</strong></div>
      </section>
    </details> : null}

    {dependenciesQuery.isSuccess && rootVersion && rows.length === 0 ? <div className="ad-inline-status" role="status">No evidence matches this version and filters.</div> : null}
    {rows.length ? <div className="ad-dependency-table-scroll" onScroll={onScroll}>
      <table className="ad-dependency-table" aria-label={direction === "outbound" ? "Dependency evidence" : "Used by evidence"}>
        <thead><tr><th>{direction === "inbound" ? "Used in" : "Version"}</th><th>Where</th><th>{direction === "inbound" ? "Activity" : "Uses"}</th><th>Evidence</th><th>Path</th></tr></thead>
        <tbody>
          {start > 0 ? <tr aria-hidden><td colSpan={5} style={{ height: start * dependencyRowHeight, padding: 0 }} /></tr> : null}
          {visibleRows.map(item => <DependencyRow
            key={item.relationshipId}
            item={item}
            direction={direction}
            selected={selectedRelationshipId === item.relationshipId}
            onSelect={() => setSelectedRelationshipId(item.relationshipId)}
          />)}
          {end < rows.length ? <tr aria-hidden><td colSpan={5} style={{ height: (rows.length - end) * dependencyRowHeight, padding: 0 }} /></tr> : null}
        </tbody>
      </table>
    </div> : null}

    {dependenciesQuery.hasNextPage && rootVersion && !isStale && !privacyFailure ? <div className="ad-child-pager">
      <span>Pages remain bound to this exact snapshot.</span>
      <button type="button" onClick={() => void dependenciesQuery.fetchNextPage()} disabled={dependenciesQuery.isFetchingNextPage}>
        {dependenciesQuery.isFetchingNextPage ? "Loading next page…" : "Load next page"}
      </button>
    </div> : null}

    {selected ? <DependencyPath item={selected} /> : null}
  </div></>;
}

function DependencyRow({
  item,
  direction,
  selected,
  onSelect
}: {
  item: ActivityDefinitionDependencyItem;
  direction: DependencyDirection;
  selected: boolean;
  onSelect(): void;
}) {
  const sourceHref = exactOwnerHref(item);
  return <tr className={selected ? "is-selected" : ""}>
    <td data-label={direction === "inbound" ? "Used in" : "Version"}><strong>{referenceLabel(item.owner)}</strong><code><CopyableValue label="Dependency owner identity" value={referenceIdentity(item.owner)} /></code></td>
    <td data-label="Where"><strong>{item.occurrence.occurrenceId}</strong><span>{item.occurrence.nodeOrigin.length} origin steps</span></td>
    <td data-label={direction === "inbound" ? "Activity" : "Uses"}><strong>{referenceLabel(item.dependency)}</strong><code><CopyableValue label="Dependency target identity" value={referenceIdentity(item.dependency)} /></code></td>
    <td data-label="Evidence"><strong>{item.isDirect ? "Direct" : `Transitive depth ${item.depth}`}</strong><span>{item.owner.kind}</span></td>
    <td data-label="Path"><button type="button" aria-pressed={selected} onClick={onSelect}>View path</button>{sourceHref ? <StudioLink href={sourceHref}>Open source</StudioLink> : <span>Source unavailable</span>}</td>
  </tr>;
}

function DependencyPath({ item }: { item: ActivityDefinitionDependencyItem }) {
  const path = item.path.length ? item.path : [item.owner, item.dependency];
  const cycle = path.length > 1 && referenceIdentity(path[0]) === referenceIdentity(path[path.length - 1]);
  return <section className="ad-dependency-path" aria-labelledby="dependency-path-title">
    <header><div><span>{cycle ? "Complete cycle path" : "Complete exact-version path"}</span><h3 id="dependency-path-title">{item.relationshipId}</h3></div><strong>{path.length} nodes</strong></header>
    <ol>{path.map((reference, index) => <li key={`${referenceIdentity(reference)}:${index}`}>
      <span>{index + 1}</span><div><strong>{referenceLabel(reference)}</strong><code><CopyableValue label="Dependency identity" value={referenceIdentity(reference)} /></code></div>{index < path.length - 1 ? <ArrowRight size={16} aria-hidden /> : null}
    </li>)}</ol>
  </section>;
}

function dependencyBinding(page: ActivityDefinitionUsageEvidence) {
  return JSON.stringify({
    root: referenceIdentity(page.root),
    direction: page.query.direction.toLowerCase(),
    transitive: page.query.transitive,
    include: [...page.query.include].map(item => item.toLowerCase()).sort(),
    consistency: page.consistency
  });
}

function pageMatchesRequest(
  page: ActivityDefinitionUsageEvidence,
  versionId: string,
  direction: DependencyDirection,
  transitive: boolean,
  included: IncludedArtifact[]
) {
  return page.root.versionId === versionId &&
    page.query.direction.toLowerCase() === direction &&
    page.query.transitive === transitive &&
    [...page.query.include].map(item => item.toLowerCase()).sort().join(",") === [...included].sort().join(",");
}

function dependencyCursorFailure(error: unknown): "mismatch" | "expired" | null {
  if (!(error instanceof StudioHttpError)) return null;
  if (error.status === 409) return "mismatch";
  if (error.status === 410) return "expired";
  return null;
}

function isPrivateFailure(error: unknown) {
  return error instanceof StudioHttpError && [401, 403, 404].includes(error.status);
}

function referenceIdentity(reference: ActivityDefinitionDependencyReference) {
  if (reference.versionId) return `${reference.kind}:${reference.definitionId}:${reference.versionId}`;
  if (reference.draftId) return `${reference.kind}:${reference.definitionId}:${reference.draftId}:${reference.revision ?? "?"}`;
  return `${reference.kind}:${reference.definitionId}`;
}

function referenceLabel(reference: ActivityDefinitionDependencyReference) {
  if (reference.version) return `${reference.kind} ${reference.version}`;
  if (reference.revision != null) return `${reference.kind} revision ${reference.revision}`;
  return reference.kind;
}

function exactOwnerHref(item: ActivityDefinitionDependencyItem) {
  const owner = item.owner;
  if (owner.kind === "ActivityVersion" && owner.versionId) {
    return `/workflows/activity-definitions?definition=${encodeURIComponent(owner.definitionId)}&section=versions&version=${encodeURIComponent(owner.versionId)}&occurrence=${encodeURIComponent(item.occurrence.occurrenceId)}`;
  }
  if (owner.kind === "ActivityDraft" && owner.draftId) {
    return `/workflows/activity-definitions?definition=${encodeURIComponent(owner.definitionId)}&section=drafts&draft=${encodeURIComponent(owner.draftId)}&occurrence=${encodeURIComponent(item.occurrence.occurrenceId)}`;
  }
  if (owner.kind === "WorkflowDraft" && owner.draftId) {
    return `/workflows/definitions?definition=${encodeURIComponent(owner.definitionId)}&draft=${encodeURIComponent(owner.draftId)}&occurrence=${encodeURIComponent(item.occurrence.occurrenceId)}`;
  }
  return null;
}

function formatTimestamp(value: string) {
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString();
}
