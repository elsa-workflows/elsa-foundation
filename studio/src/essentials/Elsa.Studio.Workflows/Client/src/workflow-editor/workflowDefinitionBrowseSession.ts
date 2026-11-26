import { useCallback, useEffect, useRef, useState } from "react";
import { authSessionEndedEvent, authSessionStartedEvent, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { defaultDefinitionPageSize, definitionPageSizes } from "./constants";
import {
  parseWorkflowDefinitionBrowseLocation,
  updateWorkflowDefinitionBrowseUrl,
  workflowFolderSelectionKey,
  workflowLibraryFilterKey,
  type WorkflowDefinitionBrowseLocation
} from "./workflowDefinitionBrowseLocation";

const historyKey = "elsa.workflow-definition-browse";
interface BrowseHistoryEntry {
  sessionId: string;
  baseUrl: string;
  authorityRevision: number;
  locationKey: string;
  page: number;
  pageSize: number;
  continuationTokens: Record<number, string>;
  effectiveFolderKey: string | null;
}
function locationKey(location: WorkflowDefinitionBrowseLocation) {
  return JSON.stringify([workflowFolderSelectionKey(location.folderSelection), location.listState, location.search,
    workflowLibraryFilterKey(location), location.viewId ?? "", location.columns ?? []]);
}
function historyState() {
  return window.history.state && typeof window.history.state === "object" && !Array.isArray(window.history.state)
    ? { ...window.history.state } as Record<string, unknown> : {};
}
function readHistoryEntry(next: WorkflowDefinitionBrowseLocation, current: BrowseHistoryEntry): BrowseHistoryEntry | null {
  const raw = historyState()[historyKey];
  if (!raw || typeof raw !== "object") return null;
  const entry = raw as Partial<BrowseHistoryEntry>;
  if (entry.sessionId !== current.sessionId || entry.baseUrl !== current.baseUrl
    || entry.authorityRevision !== current.authorityRevision || entry.locationKey !== locationKey(next)
    || !Number.isInteger(entry.page) || entry.page! < 1 || !definitionPageSizes.includes(entry.pageSize!)
    || !entry.continuationTokens || typeof entry.continuationTokens !== "object" || Array.isArray(entry.continuationTokens)
    || !Object.entries(entry.continuationTokens).every(([page, cursor]) => Number.isInteger(Number(page)) && Number(page) >= 2 && typeof cursor === "string")
    || (entry.page! > 1 && typeof entry.continuationTokens[entry.page!] !== "string")
    || (entry.effectiveFolderKey !== null && typeof entry.effectiveFolderKey !== "string")) return null;
  return entry as BrowseHistoryEntry;
}

export interface WorkflowDefinitionBrowseSession {
  browseLocation: WorkflowDefinitionBrowseLocation;
  page: number;
  pageSize: number;
  continuationTokens: Record<number, string>;
  settledSearch: string;
  searchPending: boolean;
  requestNonce: number;
  authorityBaseUrl: string;
  authorityRevision: number;
  scopeRevision: number;
  effectiveFolderKey: string | null;
  requestsPaused: boolean;
  blockedReason: string | null;
  changeSearch(value: string): void;
  navigateBrowseLocation(next: WorkflowDefinitionBrowseLocation, historyMode: "push" | "replace", clearSelection?: boolean): void;
  changePage(page: number): void;
  changePageSize(pageSize: number): void;
  rebase(clearSelection?: boolean): void;
  recordNextCursor(page: number, cursor: string | null): void;
  observeEffectiveFolder(key: string): void;
  beginRequest(controller: AbortController): number;
  finishRequest(controller: AbortController): void;
  isRequestCurrent(generation: number): boolean;
  invalidateAuthority(): void;
  reportAccessDenied(message: string): void;
  retry(): void;
  resumeAfterReturn(): void;
}

export function useWorkflowDefinitionBrowseSession(context: StudioEndpointContext): WorkflowDefinitionBrowseSession {
  const [browseLocation, setBrowseLocation] = useState(() => parseWorkflowDefinitionBrowseLocation(window.location.search));
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(defaultDefinitionPageSize);
  const [continuationTokens, setContinuationTokens] = useState<Record<number, string>>({});
  const [settledSearch, setSettledSearch] = useState(() => parseWorkflowDefinitionBrowseLocation(window.location.search).search);
  const [searchPending, setSearchPending] = useState(false);
  const [requestNonce, setRequestNonce] = useState(0);
  const [authorityRevision, setAuthorityRevision] = useState(0);
  const [authorityBaseUrl, setAuthorityBaseUrl] = useState(context.baseUrl);
  const [scopeRevision, setScopeRevision] = useState(0);
  const [effectiveFolderKey, setEffectiveFolderKey] = useState<string | null>(null);
  const [requestsPaused, setRequestsPaused] = useState(false);
  const [blockedReason, setBlockedReason] = useState<string | null>(null);
  const [historySessionId] = useState(() => crypto.randomUUID());
  const requestGenerationRef = useRef(0);
  const activeControllerRef = useRef<AbortController | null>(null);
  const effectiveFolderKeyRef = useRef<string | null>(null);
  const restoringFolderKeyRef = useRef<string | null>(null);
  const searchTimerRef = useRef<number | null>(null);
  const baseUrlRef = useRef(context.baseUrl);
  const requestsPausedRef = useRef(requestsPaused);
  requestsPausedRef.current = requestsPaused;
  const snapshot: BrowseHistoryEntry = { sessionId: historySessionId, baseUrl: authorityBaseUrl, authorityRevision,
    locationKey: locationKey(browseLocation), page, pageSize, continuationTokens, effectiveFolderKey };
  const snapshotRef = useRef(snapshot);
  snapshotRef.current = snapshot;

  const saveHistoryEntry = useCallback(() => {
    const current = snapshotRef.current;
    // A pending effect from the previous query must not overwrite the entry just reached by Back.
    if (locationKey(parseWorkflowDefinitionBrowseLocation(window.location.search)) !== current.locationKey) return;
    window.history.replaceState({ ...historyState(), [historyKey]: current }, "", window.location.href);
  }, []);

  useEffect(() => { saveHistoryEntry(); }, [browseLocation, page, pageSize, continuationTokens, authorityBaseUrl, authorityRevision, effectiveFolderKey, saveHistoryEntry]);

  const invalidateRequests = useCallback(() => {
    requestGenerationRef.current += 1;
    activeControllerRef.current?.abort();
    activeControllerRef.current = null;
    setRequestNonce(value => value + 1);
  }, []);

  const clearSearchTimer = useCallback(() => {
    if (searchTimerRef.current !== null) window.clearTimeout(searchTimerRef.current);
    searchTimerRef.current = null;
  }, []);

  const navigateBrowseLocation = useCallback((
    next: WorkflowDefinitionBrowseLocation,
    historyMode: "push" | "replace",
    clearSelection = false
  ) => {
    saveHistoryEntry();
    const url = updateWorkflowDefinitionBrowseUrl(new URL(window.location.href), next);
    window.history[historyMode === "push" ? "pushState" : "replaceState"](
      { ...(historyMode === "replace" ? historyState() : {}), [historyKey]: { ...snapshotRef.current, locationKey: locationKey(next), page: 1, continuationTokens: {} } },
      "",
      `${url.pathname}${url.search}${url.hash}`
    );
    clearSearchTimer();
    invalidateRequests();
    setBrowseLocation(next);
    setSettledSearch(next.search);
    setSearchPending(false);
    setPage(1);
    setContinuationTokens({});
    if (clearSelection) setScopeRevision(value => value + 1);
    setRequestsPaused(false);
    setBlockedReason(null);
  }, [clearSearchTimer, invalidateRequests, saveHistoryEntry]);

  const changeSearch = useCallback((value: string) => {
    if (value === browseLocation.search) return;
    const next = { ...browseLocation, search: value };
    const url = updateWorkflowDefinitionBrowseUrl(new URL(window.location.href), next);
    window.history.replaceState({ ...historyState(), [historyKey]: { ...snapshotRef.current, locationKey: locationKey(next), page: 1, continuationTokens: {} } }, "", `${url.pathname}${url.search}${url.hash}`);
    invalidateRequests();
    setBrowseLocation(next);
    setPage(1);
    setContinuationTokens({});
    setSearchPending(true);
    setRequestsPaused(false);
    setBlockedReason(null);
    clearSearchTimer();
    searchTimerRef.current = window.setTimeout(() => {
      searchTimerRef.current = null;
      setSettledSearch(value);
      setSearchPending(false);
    }, 250);
  }, [browseLocation, clearSearchTimer, invalidateRequests]);

  const changePage = useCallback((nextPage: number) => {
    const normalized = Math.max(1, nextPage);
    if (normalized === page) return;
    clearSearchTimer();
    invalidateRequests();
    setSettledSearch(browseLocation.search);
    setSearchPending(false);
    setPage(normalized);
    setRequestsPaused(false);
    setBlockedReason(null);
  }, [browseLocation.search, clearSearchTimer, invalidateRequests, page]);

  const changePageSize = useCallback((nextPageSize: number) => {
    if (nextPageSize === pageSize) return;
    clearSearchTimer();
    invalidateRequests();
    setSettledSearch(browseLocation.search);
    setSearchPending(false);
    setPageSize(nextPageSize);
    setPage(1);
    setContinuationTokens({});
    setRequestsPaused(false);
    setBlockedReason(null);
  }, [browseLocation.search, clearSearchTimer, invalidateRequests, pageSize]);

  const rebase = useCallback((clearSelection = false) => {
    clearSearchTimer();
    invalidateRequests();
    setSettledSearch(browseLocation.search);
    setSearchPending(false);
    setPage(1);
    setContinuationTokens({});
    setRequestsPaused(false);
    setBlockedReason(null);
    if (clearSelection) setScopeRevision(value => value + 1);
  }, [browseLocation.search, clearSearchTimer, invalidateRequests]);

  const recordNextCursor = useCallback((requestedPage: number, cursor: string | null) => {
    setContinuationTokens(current => {
      const next = Object.fromEntries(
        Object.entries(current).filter(([storedPage]) => Number(storedPage) <= requestedPage)
      ) as Record<number, string>;
      if (cursor) next[requestedPage + 1] = cursor;
      return next;
    });
  }, []);

  const observeEffectiveFolder = useCallback((key: string) => {
    const current = effectiveFolderKeyRef.current;
    const restoring = restoringFolderKeyRef.current === key;
    if (restoring) restoringFolderKeyRef.current = null;
    if (current === key) return;
    effectiveFolderKeyRef.current = key;
    setEffectiveFolderKey(key);
    if (current === null || restoring) return;
    invalidateRequests();
    setPage(1);
    setContinuationTokens({});
    setScopeRevision(value => value + 1);
    setRequestsPaused(false);
    setBlockedReason(null);
  }, [invalidateRequests]);

  const beginRequest = useCallback((controller: AbortController) => {
    activeControllerRef.current?.abort();
    const generation = ++requestGenerationRef.current;
    activeControllerRef.current = controller;
    return generation;
  }, []);

  const finishRequest = useCallback((controller: AbortController) => {
    if (activeControllerRef.current === controller) activeControllerRef.current = null;
  }, []);

  const isRequestCurrent = useCallback((generation: number) => requestGenerationRef.current === generation, []);

  const pauseForAuthorityChange = useCallback((message: string) => {
    restoringFolderKeyRef.current = null;
    invalidateRequests();
    setAuthorityRevision(value => value + 1);
    setPage(1);
    setContinuationTokens({});
    setScopeRevision(value => value + 1);
    setRequestsPaused(true);
    setBlockedReason(message);
  }, [invalidateRequests]);

  const invalidateAuthority = useCallback(() => {
    pauseForAuthorityChange("Your access changed. Refresh to load the current workflow definitions.");
  }, [pauseForAuthorityChange]);

  const retry = useCallback(() => {
    clearSearchTimer();
    setSettledSearch(browseLocation.search);
    setSearchPending(false);
    setRequestsPaused(false);
    setBlockedReason(null);
    invalidateRequests();
  }, [browseLocation.search, clearSearchTimer, invalidateRequests]);

  const resumeAfterReturn = useCallback(() => {
    if (!requestsPausedRef.current) return;
    setRequestsPaused(false);
    setBlockedReason(null);
    invalidateRequests();
  }, [invalidateRequests]);

  useEffect(() => {
    const syncFromLocation = () => {
      const next = parseWorkflowDefinitionBrowseLocation(window.location.search);
      const current = snapshotRef.current;
      const saved = readHistoryEntry(next, current);
      const changed = locationKey(next) !== current.locationKey;
      if (!changed && (!saved || (saved.page === current.page && saved.pageSize === current.pageSize
        && JSON.stringify(saved.continuationTokens) === JSON.stringify(current.continuationTokens)))) return;

      clearSearchTimer();
      invalidateRequests();
      setBrowseLocation(next);
      setSettledSearch(next.search);
      setSearchPending(false);
      restoringFolderKeyRef.current = saved && saved.effectiveFolderKey !== current.effectiveFolderKey ? saved.effectiveFolderKey : null;
      setPage(saved?.page ?? 1);
      setPageSize(saved?.pageSize ?? current.pageSize);
      setContinuationTokens(saved?.continuationTokens ?? {});
      setScopeRevision(value => value + 1);
      setRequestsPaused(false);
      setBlockedReason(null);
    };
    window.addEventListener("popstate", syncFromLocation);
    return () => window.removeEventListener("popstate", syncFromLocation);
  }, [clearSearchTimer, invalidateRequests]);

  useEffect(() => {
    if (baseUrlRef.current === context.baseUrl) return;
    baseUrlRef.current = context.baseUrl;
    setAuthorityBaseUrl(context.baseUrl);
    invalidateAuthority();
  }, [context.baseUrl, invalidateAuthority]);

  useEffect(() => {
    window.addEventListener(authSessionStartedEvent, invalidateAuthority);
    window.addEventListener(authSessionEndedEvent, invalidateAuthority);
    return () => {
      window.removeEventListener(authSessionStartedEvent, invalidateAuthority);
      window.removeEventListener(authSessionEndedEvent, invalidateAuthority);
    };
  }, [invalidateAuthority]);

  useEffect(() => () => clearSearchTimer(), [clearSearchTimer]);

  return {
    browseLocation,
    page,
    pageSize,
    continuationTokens,
    settledSearch,
    searchPending,
    requestNonce,
    authorityBaseUrl,
    authorityRevision,
    scopeRevision,
    effectiveFolderKey,
    requestsPaused,
    blockedReason,
    changeSearch,
    navigateBrowseLocation,
    changePage,
    changePageSize,
    rebase,
    recordNextCursor,
    observeEffectiveFolder,
    beginRequest,
    finishRequest,
    isRequestCurrent,
    invalidateAuthority,
    reportAccessDenied: pauseForAuthorityChange,
    retry,
    resumeAfterReturn
  };
}
