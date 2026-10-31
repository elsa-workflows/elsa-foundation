import { useCallback, useEffect, useRef, useState } from "react";
import { authSessionEndedEvent, authSessionStartedEvent, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { defaultDefinitionPageSize } from "./constants";
import {
  parseWorkflowDefinitionBrowseLocation,
  updateWorkflowDefinitionBrowseUrl,
  workflowFolderSelectionKey,
  workflowLibraryFilterKey,
  type WorkflowDefinitionBrowseLocation
} from "./workflowDefinitionBrowseLocation";

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
  const requestGenerationRef = useRef(0);
  const activeControllerRef = useRef<AbortController | null>(null);
  const effectiveFolderKeyRef = useRef<string | null>(null);
  const searchTimerRef = useRef<number | null>(null);
  const baseUrlRef = useRef(context.baseUrl);
  const requestsPausedRef = useRef(requestsPaused);
  requestsPausedRef.current = requestsPaused;

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
    const url = updateWorkflowDefinitionBrowseUrl(new URL(window.location.href), next);
    window.history[historyMode === "push" ? "pushState" : "replaceState"](
      {},
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
  }, [clearSearchTimer, invalidateRequests]);

  const changeSearch = useCallback((value: string) => {
    if (value === browseLocation.search) return;
    const next = { ...browseLocation, search: value };
    const url = updateWorkflowDefinitionBrowseUrl(new URL(window.location.href), next);
    window.history.replaceState({}, "", `${url.pathname}${url.search}${url.hash}`);
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
    if (current === key) return;
    effectiveFolderKeyRef.current = key;
    setEffectiveFolderKey(key);
    if (current === null) return;
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
      const changed = next.search !== browseLocation.search
        || workflowLibraryFilterKey(next) !== workflowLibraryFilterKey(browseLocation)
        || next.viewId !== browseLocation.viewId
        || JSON.stringify(next.columns) !== JSON.stringify(browseLocation.columns)
        || next.listState !== browseLocation.listState
        || workflowFolderSelectionKey(next.folderSelection) !== workflowFolderSelectionKey(browseLocation.folderSelection);
      if (!changed) return;

      clearSearchTimer();
      invalidateRequests();
      setBrowseLocation(next);
      setSettledSearch(next.search);
      setSearchPending(false);
      setPage(1);
      setContinuationTokens({});
      setScopeRevision(value => value + 1);
      setRequestsPaused(false);
      setBlockedReason(null);
    };
    window.addEventListener("popstate", syncFromLocation);
    return () => window.removeEventListener("popstate", syncFromLocation);
  }, [browseLocation, clearSearchTimer, invalidateRequests]);

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
