import { useEffect, useId, useLayoutEffect, useMemo, useRef, useState } from "react";
import { RefreshCcw } from "lucide-react";
import {
  defaultStudioHostId,
  notifyStudioNavigationGuardReleased,
  studioBackendCapabilitiesChangedEvent,
  studioNavigationRequestedEvent,
  type ElsaStudioModuleApi,
  type StudioNavigationRequest,
  type StudioSettingDescriptor,
  useAuthSession,
  usePermissions
} from "@elsa-workflows/studio-sdk";
import {
  SkeletonRows,
  StudioAlert,
  StudioButton,
  StudioPageHeader,
  StudioShellBreadcrumbs,
  StudioTabPanel,
  StudioTabs,
  StudioToggleButton,
  StudioToolbar,
  StudioToolbarGroup
} from "@elsa-workflows/studio-ui";
import { buildCapabilityGroups, getCapabilityDefinition } from "./capabilities";
import {
  AllCategoriesId,
  createApplyPayload,
  createInitialHostState,
  featureIndexById,
  filterFeaturesByCategory,
  filterFeaturesByText,
  getCategoryFilters,
  getErrorMessage,
  getFeatureHosts,
  getFeatureImpactEntries,
  getFeatureRelationships,
  isDirty,
  isFeaturePending,
  normalizeType,
  stableStringify,
  stageFeatureToggle,
  toDraftFeature,
  type DraftFeature,
  type FeatureApplyResult,
  type FeatureBrowseLocation,
  type FeatureCatalogResponse,
  type FeatureHostId,
  type FeatureHostState,
  type FeatureReviewSnapshot
} from "./featureModel";
import { FeatureImpactReviewDialog } from "./FeatureImpactReview";
import { BulkFeatureActions, CategorySelect, FeatureCard } from "./FeatureList";
import { FeatureInspector } from "./FeatureInspector";

const ModulesChangedEventName = "elsa-studio:modules-changed";

let featureCompletion: { hostId: FeatureHostId; message: string; targetKey:string } | null = null;

let featureVisitVersion = 0;

let pendingFeatureApplies = 0;

let removeFeatureNavigationObserver: (() => void) | null = null;

function isFeatureManagementPath(pathname: string) {
  const path = pathname.replace(/\/+$/, "") || "/";
  return path === "/features" || path === "/settings/feature-management";
}

function removeFeatureNavigationObserverIfIdle() {
  if (featureCompletion || pendingFeatureApplies > 0) {
    return;
  }
  removeFeatureNavigationObserver?.();
  removeFeatureNavigationObserver = null;
}

function observeFeatureNavigation() {
  if (typeof window === "undefined" || removeFeatureNavigationObserver) {
    return;
  }

  const onNavigation = () => {
    if (!isFeatureManagementPath(window.location.pathname)) {
      featureVisitVersion++;
      featureCompletion = null;
      removeFeatureNavigationObserverIfIdle();
    }
  };
  window.addEventListener("popstate", onNavigation);
  removeFeatureNavigationObserver = () => window.removeEventListener("popstate", onNavigation);
}

function clearFeatureCompletion() {
  featureCompletion = null;
  removeFeatureNavigationObserverIfIdle();
}

function beginFeatureApplyAttempt() {
  observeFeatureNavigation();
  pendingFeatureApplies++;
  return featureVisitVersion;
}

function endFeatureApplyAttempt() {
  pendingFeatureApplies--;
  removeFeatureNavigationObserverIfIdle();
}

function retainFeatureCompletion(hostId: FeatureHostId, message: string, visitVersion: number, targetKey:string) {
  if (typeof window === "undefined"
    || !isFeatureManagementPath(window.location.pathname)
    || visitVersion !== featureVisitVersion) {
    return;
  }

  clearFeatureCompletion();
  featureCompletion = { hostId, message, targetKey };
  observeFeatureNavigation();
}

export function FeatureManagementScreen({ moduleApi }: { moduleApi: ElsaStudioModuleApi }) {
  const hostTabsBaseId = useId();
  const session = useAuthSession();
  const permissions = usePermissions();
  const hosts = useMemo(() => getFeatureHosts(moduleApi), [moduleApi]);
  const [activeHostId, setActiveHostId] = useState<FeatureHostId>(() => {const target=new URLSearchParams(window.location.search).get("target");return target === "studio" || target === "server" ? target : featureCompletion?.hostId ?? defaultStudioHostId;});
  const completionRef = useRef<HTMLDivElement | null>(null);
  const [reviewSnapshot, setReviewSnapshot] = useState<FeatureReviewSnapshot | null>(null);
  const reviewSnapshotRef = useRef<FeatureReviewSnapshot | null>(null);
  const applyInProgressRef = useRef(false);
  const [hostStates, setHostStates] = useState<Record<FeatureHostId, FeatureHostState>>(() => ({
    studio: createInitialHostState(),
    server: createInitialHostState()
  }));

  const activeHost = hosts.find(host => host.id === activeHostId) ?? hosts[0];
  const hostState = hostStates[activeHost.id];
  const { catalog, draft, selectedId, checkedFeatureIds, selectedCategory, filterText, loading, applying, status, error, enabledRoots, stateCauses } = hostState;
  const completionMessage = featureCompletion?.hostId === activeHost.id && featureCompletion.targetKey === activeHost.key ? featureCompletion.message : status;
  const refreshHostRef = useRef(refreshHost);
  refreshHostRef.current = refreshHost;

  // Load once. A background workspace tab mounts its effects again when it returns, and a refresh
  // would replace an unapplied feature draft with the server catalog.
  const initialRefreshRef = useRef(false);
  useEffect(() => {
    if (initialRefreshRef.current) return;
    initialRefreshRef.current = true;
    for (const host of hosts) {
      void refreshHostRef.current(host.id, false);
    }
  }, [hosts]);

  useLayoutEffect(() => {
    if (completionMessage) {
      completionRef.current?.focus();
    }
  }, [activeHost.id, completionMessage]);

  const baselineById = useMemo(
    () => featureIndexById(catalog?.features.map(toDraftFeature) ?? []),
    [catalog]
  );
  const capabilityFeatures = useMemo(() => draft.map(feature => {
    const baseline = baselineById.get(normalizeType(feature.id));
    return {
      ...feature,
      pending: isFeaturePending(baseline, feature),
      problem: !!feature.readError || (feature.enabled && (feature.runtimeAvailable === false || feature.availability?.status === "dormant"))
    };
  }), [draft, baselineById]);
  const capabilityGroups = useMemo(() => buildCapabilityGroups(capabilityFeatures), [capabilityFeatures]);
  const selectedCapability = capabilityGroups.find(group => group.id === hostState.selectedCapability);
  const scopedDraft = useMemo(
    () => hostState.browseView === "group" && !filterText.trim() ? selectedCapability?.features ?? [] : draft,
    [hostState.browseView, filterText, selectedCapability, draft]
  );
  const textFilteredDraft = useMemo(() => filterFeaturesByText(scopedDraft, filterText), [scopedDraft, filterText]);
  const stateFilteredDraft = useMemo(() => textFilteredDraft.filter(feature => {
    const pending = isFeaturePending(baselineById.get(normalizeType(feature.id)), feature);
    if (hostState.featureStateFilter === "enabled" && !feature.enabled) return false;
    if (hostState.featureStateFilter === "disabled" && feature.enabled) return false;
    if (hostState.featureStateFilter === "pending" && !pending) return false;
    return hostState.includeAdvanced || !feature.advanced;
  }), [textFilteredDraft, baselineById, hostState.featureStateFilter, hostState.includeAdvanced]);
  const categories = useMemo(() => getCategoryFilters(stateFilteredDraft), [stateFilteredDraft]);
  const filteredDraft = useMemo(
    () => filterFeaturesByCategory(stateFilteredDraft, selectedCategory),
    [stateFilteredDraft, selectedCategory]);
  const selectedFeature = filteredDraft.find(feature => feature.id === selectedId)
    ?? filteredDraft.find(feature => feature.enabled)
    ?? filteredDraft[0]
    ?? null;
  const priorFeatureId = hostState.navigationHistory.at(-1)?.selectedId;
  const priorFeature = draft.find(feature => feature.id === priorFeatureId);
  const selectedRelationships = selectedFeature ? getFeatureRelationships(selectedFeature, draft) : null;
  const selectedFeaturePending = selectedFeature
    ? isFeaturePending(baselineById.get(normalizeType(selectedFeature.id)), selectedFeature)
    : false;
  const dirty = isDirty(catalog, draft);
  const impactEntries = useMemo(
    () => getFeatureImpactEntries(catalog, draft, enabledRoots, stateCauses),
    [catalog, draft, enabledRoots, stateCauses]);
  const enabledCount = draft.filter(feature => feature.enabled).length;
  const selectedCategoryLabel = categories.find(category => category.id === selectedCategory)?.label ?? "All categories";
  const isOverview = hostState.browseView === "overview" && !filterText.trim();
  const listHeading = hostState.browseView === "group" && selectedCapability
    ? selectedCapability.label
    : filterText.trim() ? "Search results" : "All features";
  const visibleFeatureIds = useMemo(() => new Set(filteredDraft.map(feature => feature.id)), [filteredDraft]);
  const checkedVisibleCount = filteredDraft.filter(feature => checkedFeatureIds.has(feature.id)).length;
  const allVisibleChecked = filteredDraft.length > 0 && checkedVisibleCount === filteredDraft.length;
  const checkedFeatures = draft.filter(feature => checkedFeatureIds.has(feature.id));
  const targetSession = activeHost.session ?? session;
  const readOnly = !activeHost.writable
    || (targetSession.status === "authenticated" && !(activeHost.session ? (targetSession.permissions.includes("*") || targetSession.permissions.includes("module-management:manage")) : permissions.has("module-management:manage")));
  const editingDisabled = loading || applying || readOnly;
  const currentTargetKey = (id:FeatureHostId) => getFeatureHosts(moduleApi).find(host => host.id === id)?.key;
  useEffect(() => {
    const guard = (event:Event) => {
      if (Object.values(hostStates).some(state => state.applying || isDirty(state.catalog,state.draft))) {
        event.preventDefault();
        // A probe only asks whether unsaved work exists; it must not change what the page shows.
        if ((event as CustomEvent<StudioNavigationRequest>).detail?.intent === "probe") return;
        updateHostState(activeHost.id,state => ({...state,error:"Review and apply, or reset, the current feature draft before leaving this backend."}));
      }
    };
    window.addEventListener(studioNavigationRequestedEvent,guard);
    return () => window.removeEventListener(studioNavigationRequestedEvent,guard);
  },[hostStates,activeHost.id]);
  useEffect(() => {
    if (!Object.values(hostStates).some(state => state.applying || isDirty(state.catalog,state.draft)))
      notifyStudioNavigationGuardReleased();
  },[hostStates]);

  useEffect(() => {
    if (selectedFeature && selectedFeature.id !== selectedId) {
      updateHostState(activeHost.id, state => ({ ...state, selectedId: selectedFeature.id }));
    }
  }, [activeHost.id, selectedFeature, selectedId]);

  useEffect(() => {
    if (!categories.some(category => category.id === selectedCategory)) {
      updateHostState(activeHost.id, state => ({ ...state, selectedCategory: AllCategoriesId }));
    }
  }, [activeHost.id, categories, selectedCategory]);

  useEffect(() => {
    updateHostState(activeHost.id, state => {
      const existingIds = new Set(state.draft.map(feature => feature.id));
      const next = new Set(Array.from(state.checkedFeatureIds).filter(id => existingIds.has(id)));
      return next.size === state.checkedFeatureIds.size ? state : { ...state, checkedFeatureIds: next };
    });
  }, [activeHost.id, draft]);

  function updateHostState(hostId: FeatureHostId, update: (state: FeatureHostState) => FeatureHostState) {
    setHostStates(current => ({
      ...current,
      [hostId]: update(current[hostId])
    }));
  }

  function browseLocation(state: FeatureHostState, featureId = state.selectedId): FeatureBrowseLocation {
    return {
      browseView: state.browseView,
      selectedCapability: state.selectedCapability,
      selectedCategory: state.selectedCategory,
      filterText: state.filterText,
      featureStateFilter: state.featureStateFilter,
      includeAdvanced: state.includeAdvanced,
      selectedId: featureId
    };
  }

  function openAllFeatures() {
    updateHostState(activeHost.id, state => ({
      ...state,
      browseView: "all",
      selectedCategory: AllCategoriesId,
      selectedId: selectedId || state.selectedId
    }));
  }

  function openOverview() {
    updateHostState(activeHost.id, state => ({
      ...state,
      browseView: "overview",
      selectedCategory: AllCategoriesId,
      filterText: ""
    }));
  }

  function openCapability(id: string) {
    updateHostState(activeHost.id, state => ({
      ...state,
      browseView: "group",
      selectedCapability: id,
      selectedCategory: AllCategoriesId,
      featureStateFilter: "all",
      includeAdvanced: true
    }));
  }

  function openRelatedFeature(id: string) {
    const target = draft.find(feature => normalizeType(feature.id) === normalizeType(id));
    if (!target) return;
    const capability = getCapabilityDefinition(target.categories);
    updateHostState(activeHost.id, state => ({
      ...state,
      navigationHistory: [...state.navigationHistory, browseLocation(state, selectedFeature?.id ?? state.selectedId)],
      browseView: "group",
      selectedCapability: capability.id,
      selectedCategory: AllCategoriesId,
      filterText: "",
      featureStateFilter: "all",
      includeAdvanced: true,
      selectedId: target.id
    }));
  }

  function backToPreviousFeature() {
    updateHostState(activeHost.id, state => {
      const previous = state.navigationHistory[state.navigationHistory.length - 1];
      if (!previous) return state;
      return {
        ...state,
        ...previous,
        navigationHistory: state.navigationHistory.slice(0, -1)
      };
    });
  }

  function clearReview() {
    reviewSnapshotRef.current = null;
    setReviewSnapshot(null);
  }

  function closeReview() {
    if (!applyInProgressRef.current) {
      clearReview();
    }
  }

  async function refreshHost(hostId = activeHost.id, manual = true) {
    if (applyInProgressRef.current) {
      return;
    }
    if (manual) {
      clearFeatureCompletion();
    }
    const host = hosts.find(candidate => candidate.id === hostId) ?? activeHost;
    if (reviewSnapshotRef.current?.hostId === host.id) {
      clearReview();
    }
    updateHostState(host.id, state => ({ ...state, loading: true, error: null, status: null }));
    try {
      const response = await host.context.http.getJson<FeatureCatalogResponse>("/modularity/features");
      if (host.key !== currentTargetKey(host.id)) return;
      if (reviewSnapshotRef.current?.hostId === host.id) {
        clearReview();
      }
      updateHostState(host.id, state => ({
        ...state,
        catalog: response,
        draft: response.features.map(toDraftFeature),
        checkedFeatureIds: new Set(),
        enabledRoots: [],
        stateCauses: new Map(),
        status: null,
        loading: false
      }));
    } catch (e) {
      updateHostState(host.id, state => ({ ...state, error: getErrorMessage(e), loading: false }));
    }
  }

  async function applyReviewedSnapshot() {
    const snapshot = reviewSnapshotRef.current;
    if (!snapshot || applyInProgressRef.current || readOnly) {
      return;
    }

    const currentState = hostStates[snapshot.hostId];
    const snapshotIsCurrent = snapshot.hostId === activeHost.id
      && snapshot.targetKey === activeHost.key
      && snapshot.targetKey === currentTargetKey(snapshot.hostId)
      && snapshot.catalog === currentState.catalog
      && snapshot.catalogSignature === stableStringify(currentState.catalog)
      && snapshot.draftSignature === stableStringify(currentState.draft)
      && !currentState.loading
      && isDirty(currentState.catalog, currentState.draft);
    if (!snapshotIsCurrent) {
      clearReview();
      return;
    }

    const host = hosts.find(candidate => candidate.id === snapshot.hostId);
    if (!host) {
      clearReview();
      return;
    }

    const applyVisitVersion = beginFeatureApplyAttempt();
    applyInProgressRef.current = true;
    clearFeatureCompletion();
    updateHostState(snapshot.hostId, state => ({ ...state, applying: true, error: null, status: null }));
    try {
      const response = await snapshot.context.http.postJson<FeatureApplyResult>(
        "/modularity/features/apply",
        JSON.parse(JSON.stringify(snapshot.payload)));
      if (host.key !== currentTargetKey(host.id)) return;
      const message = `Applied ${response.featureDescriptorCount} descriptor(s); reloaded ${response.reloadedShellCount} shell(s).`;
      updateHostState(snapshot.hostId, state => ({
        ...state,
        catalog: response.catalog,
        draft: response.catalog.features.map(toDraftFeature),
        enabledRoots: [],
        stateCauses: new Map(),
        status: message,
        applying: false
      }));
      retainFeatureCompletion(snapshot.hostId, message, applyVisitVersion, snapshot.targetKey);
      clearReview();

      if (snapshot.hostId === "studio") {
        window.dispatchEvent(new Event(ModulesChangedEventName));
      } else if (moduleApi.backendSession) {
        window.dispatchEvent(new CustomEvent(studioBackendCapabilitiesChangedEvent,{detail:{key:moduleApi.backendSession.key}}));
      }
    } catch (e) {
      updateHostState(snapshot.hostId, state => ({ ...state, error: getErrorMessage(e), applying: false, status: null }));
    } finally {
      applyInProgressRef.current = false;
      endFeatureApplyAttempt();
    }
  }

  function openReview() {
    if (!catalog || !dirty || loading || applying || readOnly || applyInProgressRef.current) {
      return;
    }

    const snapshot: FeatureReviewSnapshot = {
      targetKey: activeHost.key,
      context: activeHost.context,
      hostId: activeHost.id,
      catalog,
      catalogSignature: stableStringify(catalog),
      draftSignature: stableStringify(draft),
      payload: JSON.parse(JSON.stringify(createApplyPayload(catalog.revision, draft)))
    };
    updateHostState(activeHost.id, state => ({ ...state, error: null, status: null }));
    reviewSnapshotRef.current = snapshot;
    setReviewSnapshot(snapshot);
  }

  function updateFeature(id: string, update: (feature: DraftFeature) => DraftFeature) {
    if (readOnly || applyInProgressRef.current) {
      return;
    }

    clearReview();
    clearFeatureCompletion();
    updateHostState(activeHost.id, state => ({
      ...state,
      status: null,
      error: null,
      draft: state.draft.map(feature => feature.id === id ? update(feature) : feature)
    }));
  }

  function toggleFeature(feature: DraftFeature) {
    if (readOnly || applyInProgressRef.current) {
      return;
    }

    clearReview();
    clearFeatureCompletion();
    const enabled = !feature.enabled;
    updateHostState(activeHost.id, state => ({ ...stageFeatureToggle(state, [feature.id], enabled), status: null, error: null }));
  }

  function toggleCheckedFeature(id: string, checked: boolean) {
    if (readOnly) {
      return;
    }

    updateHostState(activeHost.id, state => {
      const next = new Set(state.checkedFeatureIds);
      if (checked) {
        next.add(id);
      } else {
        next.delete(id);
      }
      return { ...state, checkedFeatureIds: next };
    });
  }

  function toggleVisibleFeatureSelection() {
    if (readOnly) {
      return;
    }

    updateHostState(activeHost.id, state => {
      const next = new Set(state.checkedFeatureIds);
      if (allVisibleChecked) {
        for (const id of visibleFeatureIds) {
          next.delete(id);
        }
      } else {
        for (const id of visibleFeatureIds) {
          next.add(id);
        }
      }
      return { ...state, checkedFeatureIds: next };
    });
  }

  function setCheckedFeaturesEnabled(enabled: boolean) {
    if (readOnly || applyInProgressRef.current || checkedFeatureIds.size === 0) {
      return;
    }

    clearReview();
    clearFeatureCompletion();
    updateHostState(activeHost.id, state => ({ ...stageFeatureToggle(state, state.checkedFeatureIds, enabled), status: null, error: null }));
  }

  function updateSetting(feature: DraftFeature, setting: StudioSettingDescriptor, value: unknown) {
    updateFeature(feature.id, current => ({
      ...current,
      configuration: {
        ...current.configuration,
        [setting.name]: value
      }
    }));
  }

  function reset() {
    if (!catalog || readOnly || applyInProgressRef.current) {
      return;
    }

    clearReview();
    clearFeatureCompletion();
    updateHostState(activeHost.id, state => ({
      ...state,
      draft: catalog.features.map(toDraftFeature),
      enabledRoots: [],
      stateCauses: new Map(),
      status: null,
      error: null
    }));
  }

  return (
    <section className="studio-page feature-management-page">
      <StudioPageHeader headingLevel={2} title={<>Features</>} description={<>{activeHost.label}: {enabledCount} enabled of {draft.length} available{dirty ? " - Unsaved changes" : ""}</>} actions={<><StudioToolbar>
          <StudioToolbarGroup>
            <StudioButton className="studio-icon-button" aria-label="Refresh features" title="Refresh" onClick={() => refreshHost()} disabled={loading || applying}>
              <RefreshCcw size={15} />
            </StudioButton>
            <StudioButton onClick={reset} disabled={!dirty || loading || applying || readOnly}>Reset</StudioButton>
            <StudioButton variant="primary" onClick={openReview} disabled={!dirty || loading || applying || readOnly}>
              Review changes
            </StudioButton>
          </StudioToolbarGroup>
        </StudioToolbar></>} />

      <StudioTabs
        tabs={hosts.map(host => ({ id: host.id, label: host.label }))}
        activeTab={activeHost.id}
        onSelect={hostId => {
          if (applyInProgressRef.current || hostId === activeHost.id) {
            return;
          }
          clearFeatureCompletion();
          clearReview();
          setHostStates(current => ({
            studio: { ...current.studio, status: null },
            server: { ...current.server, status: null }
          }));
          setActiveHostId(hostId as FeatureHostId);
        }}
        baseId={hostTabsBaseId}
        ariaLabel="Hosts"
      />
      {hosts.map((item, index) => item.id !== activeHost.id ? (
        <StudioTabPanel key={item.id} baseId={hostTabsBaseId} index={index} hidden>{null}</StudioTabPanel>
      ) : null)}
      <StudioTabPanel baseId={hostTabsBaseId} index={hosts.findIndex(item => item.id === activeHost.id)} className="feature-management-host-panel">

      {!reviewSnapshot && error ? <StudioAlert tone="danger">{error}</StudioAlert> : null}
      {!reviewSnapshot && completionMessage ? (
        <div ref={completionRef} tabIndex={-1} data-feature-apply-completion>
          <StudioAlert tone="success">{completionMessage}</StudioAlert>
        </div>
      ) : null}
      <div className={`feature-management-layout${isOverview ? " overview" : ""}`}>
        <div className="feature-management-list-column">
          {hostState.navigationHistory.length > 0 ? <StudioShellBreadcrumbs items={[{ label: priorFeature?.displayName || priorFeatureId || "Features", onClick: backToPreviousFeature }]} current={selectedFeature?.displayName || selectedId || "Features"} /> : null}
          <nav className="feature-management-browse-nav" aria-label="Feature views">
            <StudioToggleButton size="sm" pressed={isOverview} onClick={openOverview}>Capabilities</StudioToggleButton>
            <StudioToggleButton size="sm" pressed={!isOverview && hostState.browseView !== "group"} onClick={openAllFeatures}>All features</StudioToggleButton>
            {hostState.browseView === "group" && selectedCapability ? (
              <StudioToggleButton size="sm" pressed={true} onClick={() => openCapability(selectedCapability.id)}>{selectedCapability.label}</StudioToggleButton>
            ) : null}
          </nav>
          <div className="feature-management-list-toolbar">
            <input
              type="search"
              aria-label="Filter features"
              placeholder="Filter by name, package, setting..."
              value={filterText}
              onChange={event => updateHostState(activeHost.id, state => ({
                ...state,
                filterText: event.target.value,
                browseView: event.target.value.trim() ? "all" : state.browseView
              }))}
            />
            {!isOverview ? (
              <CategorySelect
                categories={categories}
                selectedCategory={selectedCategory}
                onSelect={category => updateHostState(activeHost.id, state => ({ ...state, selectedCategory: category }))}
              />
            ) : null}
          </div>

          {isOverview ? (
            <div className="feature-management-overview" aria-label="Feature capabilities">
              {loading && draft.length === 0 ? <SkeletonRows rows={6} label="Loading capabilities" /> : null}
              {!loading && capabilityGroups.length === 0 ? <p className="feature-management-muted">No features are available.</p> : null}
              {capabilityGroups.map(group => (
                <StudioButton
                  key={group.id}
                  type="button"
                  className="feature-management-capability-card"
                  aria-label={`View ${group.label} features`}
                  onClick={() => openCapability(group.id)}
                >
                  <span className="feature-management-capability-card-heading">
                    <strong>{group.label}</strong>
                    <span>{group.features.length} feature{group.features.length === 1 ? "" : "s"}</span>
                  </span>
                  <span className="feature-management-capability-description">{group.description}</span>
                  <span className="feature-management-capability-summary">{group.enabledCount} of {group.features.length} enabled</span>
                  {group.pendingCount > 0 || group.problemCount > 0 ? (
                    <span className="feature-management-capability-alerts">
                      {group.pendingCount > 0 ? `${group.pendingCount} pending change${group.pendingCount === 1 ? "" : "s"}` : null}
                      {group.pendingCount > 0 && group.problemCount > 0 ? " · " : null}
                      {group.problemCount > 0 ? `${group.problemCount} configuration issue${group.problemCount === 1 ? "" : "s"}` : null}
                    </span>
                  ) : null}
                  <span className="feature-management-capability-action">View features</span>
                </StudioButton>
              ))}
            </div>
          ) : (
            <>
              <div className="feature-management-list-filters">
                <label>
                  <span>Feature state</span>
                  <select
                    className="feature-management-state-filter"
                    aria-label="Filter by feature state"
                    value={hostState.featureStateFilter}
                    onChange={event => updateHostState(activeHost.id, state => ({ ...state, featureStateFilter: event.target.value as FeatureHostState["featureStateFilter"] }))}
                  >
                    <option value="all">All states</option>
                    <option value="enabled">Enabled</option>
                    <option value="disabled">Disabled</option>
                    <option value="pending">Pending changes</option>
                  </select>
                </label>
                <label className="feature-management-advanced-filter">
                  <input
                    type="checkbox"
                    checked={hostState.includeAdvanced}
                    onChange={event => updateHostState(activeHost.id, state => ({ ...state, includeAdvanced: event.target.checked }))}
                  />
                  <span>Include advanced</span>
                </label>
              </div>

              <div className="feature-management-list-heading">
                <div>
                  <strong>{listHeading}{selectedCategory !== AllCategoriesId ? ` · ${selectedCategoryLabel}` : ""}</strong>
                  <span>{filteredDraft.length} feature{filteredDraft.length === 1 ? "" : "s"}</span>
                </div>
                <span>{filteredDraft.filter(feature => feature.enabled).length} enabled</span>
              </div>

              <BulkFeatureActions
                disabled={editingDisabled || filteredDraft.length === 0}
                selectedCount={checkedFeatureIds.size}
                visibleCount={filteredDraft.length}
                visibleSelectedCount={checkedVisibleCount}
                selectedEnabledCount={checkedFeatures.filter(feature => feature.enabled).length}
                allVisibleChecked={allVisibleChecked}
                onToggleVisible={toggleVisibleFeatureSelection}
                onClearSelection={() => updateHostState(activeHost.id, state => ({ ...state, checkedFeatureIds: new Set() }))}
                onEnableSelected={() => setCheckedFeaturesEnabled(true)}
                onDisableSelected={() => setCheckedFeaturesEnabled(false)}
              />

              <div className="feature-management-list" aria-busy={loading}>
                {loading && draft.length === 0 ? <SkeletonRows rows={6} label="Loading features" /> : null}
                {!loading && draft.length === 0 ? <p className="feature-management-muted">No features are available.</p> : null}
                {!loading && draft.length > 0 && filteredDraft.length === 0 ? <p className="feature-management-muted">{filterText.trim() ? "No features match this filter." : "No features match the selected filters."}</p> : null}
                {filteredDraft.map(feature => (
                  <FeatureCard
                    key={feature.id}
                    feature={feature}
                    pending={isFeaturePending(baselineById.get(normalizeType(feature.id)), feature)}
                    selected={feature.id === selectedFeature?.id}
                    checked={checkedFeatureIds.has(feature.id)}
                    disabled={editingDisabled}
                    onSelect={() => updateHostState(activeHost.id, state => ({ ...state, selectedId: feature.id }))}
                    onToggle={() => toggleFeature(feature)}
                    onCheckedChange={checked => toggleCheckedFeature(feature.id, checked)}
                  />
                ))}
              </div>
            </>
          )}
        </div>

        {!isOverview ? (
          <FeatureInspector
            moduleApi={moduleApi}
            feature={selectedFeature}
            relationships={selectedRelationships}
            pending={selectedFeaturePending}
            disabled={editingDisabled}
            onNavigate={openRelatedFeature}
            onToggle={toggleFeature}
            onSettingChange={updateSetting}
          />
        ) : null}
      </div>

      </StudioTabPanel>
      {reviewSnapshot && reviewSnapshot.hostId === activeHost.id ? (
        <FeatureImpactReviewDialog
          hostLabel={activeHost.label}
          entries={impactEntries}
          dirty={dirty}
          applying={applying}
          readOnly={readOnly}
          error={error}
          onClose={closeReview}
          onApply={applyReviewedSnapshot}
        />
      ) : null}
    </section>
  );
}
