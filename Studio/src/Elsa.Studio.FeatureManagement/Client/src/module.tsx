import React, { useEffect, useMemo, useState } from "react";
import { FlaskConical, Package, Settings2, ShieldAlert, RefreshCcw } from "lucide-react";
import { registerBuiltInSettingEditors, selectSettingEditor } from "@elsa-workflows/studio-ui";
import type {
  ElsaStudioModuleApi,
  StudioSettingDescriptor,
  StudioSettingDescriptor
} from "@elsa-workflows/studio-sdk";
import "./styles.css";

interface FeatureCatalogResponse {
  revision: string;
  features: FeatureCatalogItem[];
}

interface FeatureApplyResult {
  catalog: FeatureCatalogResponse;
  featureDescriptorCount: number;
  reloadedShellCount: number;
}

interface FeatureCatalogItem {
  id: string;
  displayName: string;
  description?: string | null;
  categories: string[];
  sourceKind: string;
  packageId?: string | null;
  packageVersion?: string | null;
  enabled: boolean;
  configuration: Record<string, unknown>;
  advanced: boolean;
  experimental: boolean;
  manifestPath?: string | null;
  manifestHash?: string | null;
  readError?: string | null;
  settings: StudioSettingDescriptor[];
  dependencies: FeatureDependency[];
}

// A feature dependency carries whether it is optional. Optional dependencies are surfaced for display but are
// excluded from the hard enable/disable cascade so they are never force-toggled like mandatory dependencies.
interface FeatureDependency {
  id: string;
  optional: boolean;
}

interface DraftFeature extends FeatureCatalogItem {
  configuration: Record<string, unknown>;
}

interface CategoryFilterItem {
  id: string;
  label: string;
  count: number;
}

interface SettingGroupItem {
  id: string;
  label: string;
  settings: StudioSettingDescriptor[];
}

type FeatureHostId = "studio" | "server";
type FeatureEndpointContext = Pick<ElsaStudioModuleApi["host"], "http">;

interface FeatureHostConfig {
  id: FeatureHostId;
  label: string;
  runtime: string;
  context: FeatureEndpointContext;
  writable: boolean;
}

interface FeatureHostState {
  catalog: FeatureCatalogResponse | null;
  draft: DraftFeature[];
  selectedId: string;
  checkedFeatureIds: Set<string>;
  selectedCategory: string;
  filterText: string;
  loading: boolean;
  applying: boolean;
  status: string | null;
  error: string | null;
}

const AllCategoriesId = "__all";
const UncategorizedId = "__uncategorized";
const ModulesChangedEventName = "elsa-studio:modules-changed";

export const featureKeys = {
  all: ["features"] as const,
  catalog: (hostId: FeatureHostId) => [...featureKeys.all, hostId, "catalog"] as const
};

let moduleApi: ElsaStudioModuleApi;

export function register(api: ElsaStudioModuleApi) {
  moduleApi = api;
  registerBuiltInSettingEditors(api);

  api.navigation.add({
    id: "feature-management",
    label: "Features",
    path: "/features",
    order: 130
  });

  api.routes.add({
    id: "feature-management",
    label: "Features",
    path: "/features",
    component: FeatureManagementPage
  });
}

export { registerBuiltInSettingEditors, selectSettingEditor };

export function createApplyPayload(revision: string, features: DraftFeature[]) {
  return {
    revision,
    features: features.map(feature => ({
      id: feature.id,
      enabled: feature.enabled,
      configuration: feature.enabled ? feature.configuration : {}
    }))
  };
}

export function isDirty(current: FeatureCatalogResponse | null, draft: DraftFeature[]) {
  return current ? canonicalize(current.features) !== canonicalize(draft) : false;
}

function featureIndexById(features: DraftFeature[]) {
  return new Map(features.map(feature => [normalizeType(feature.id), feature]));
}

// Direct dependants, indexed by the normalized feature id they require. Used for
// the inspector's "Required by" display and to resolve the disable cascade below.
export function buildDependentsIndex(features: DraftFeature[]): Map<string, string[]> {
  const index = new Map<string, string[]>();

  for (const feature of features) {
    for (const dependency of feature.dependencies) {
      // Optional dependencies form no hard dependant edge: disabling the dependency must not force-disable a feature
      // that only optionally uses it, and it should not appear under the "Required by" list.
      if (dependency.optional)
        continue;
      const key = normalizeType(dependency.id);
      const existing = index.get(key);
      if (existing) {
        existing.push(feature.id);
      } else {
        index.set(key, [feature.id]);
      }
    }
  }

  return index;
}

// Walks the dependency graph from `root`, adding every reachable feature id to
// `affected`: enabling follows the dependencies a feature requires; disabling
// follows the dependants that require it (via the shared reverse index).
function collectCascade(
  root: DraftFeature,
  enabled: boolean,
  byId: Map<string, DraftFeature>,
  dependents: Map<string, string[]>,
  affected: Set<string>) {
  const stack = [root];
  affected.add(root.id);

  while (stack.length > 0) {
    const current = stack.pop()!;
    const relatedIds = enabled
      ? current.dependencies.filter(dependency => !dependency.optional).map(dependency => dependency.id)
      : dependents.get(normalizeType(current.id)) ?? [];

    for (const relatedId of relatedIds) {
      const feature = byId.get(normalizeType(relatedId));
      if (feature && !affected.has(feature.id)) {
        affected.add(feature.id);
        stack.push(feature);
      }
    }
  }
}

// Shared cascade resolver for one or many seed ids. Builds the feature index (and,
// when disabling, the reverse dependants index) once, then folds every seed id
// through the graph so bulk toggles never rebuild those indexes per feature.
function collectToggleTargets(features: DraftFeature[], ids: Iterable<string>, enabled: boolean): Set<string> {
  const byId = featureIndexById(features);
  const dependents = enabled ? new Map<string, string[]>() : buildDependentsIndex(features);
  const affected = new Set<string>();

  for (const id of ids) {
    const root = byId.get(normalizeType(id));
    if (root) {
      collectCascade(root, enabled, byId, dependents, affected);
    }
  }

  return affected;
}

// Resolves the set of feature ids affected when toggling `id` to `enabled`:
// enabling pulls in every transitive dependency, disabling pushes out every
// transitive dependant, so the desired state never leaves a broken dependency edge.
export function computeFeatureCascade(features: DraftFeature[], id: string, enabled: boolean): Set<string> {
  return collectToggleTargets(features, [id], enabled);
}

// Applies `enabled` to every seed id and its cascade in a single pass over the draft.
function applyFeatureToggles(features: DraftFeature[], ids: Iterable<string>, enabled: boolean): DraftFeature[] {
  const affected = collectToggleTargets(features, ids, enabled);
  if (affected.size === 0) {
    return features;
  }

  return features.map(feature => affected.has(feature.id) && feature.enabled !== enabled
    ? { ...feature, enabled }
    : feature);
}

// Applies a toggle to the draft, cascading to dependencies/dependants.
export function applyFeatureToggle(features: DraftFeature[], id: string, enabled: boolean): DraftFeature[] {
  return applyFeatureToggles(features, [id], enabled);
}

function getFeatureHosts(api: ElsaStudioModuleApi): FeatureHostConfig[] {
  return [
    {
      id: "studio",
      label: "Studio",
      runtime: "Elsa.Studio.Web",
      context: api.host,
      writable: true
    },
    {
      id: "server",
      label: "Server",
      runtime: "Elsa.Server",
      context: api.backend,
      writable: true
    }
  ];
}

function createInitialHostState(): FeatureHostState {
  return {
    catalog: null,
    draft: [],
    selectedId: "",
    checkedFeatureIds: new Set(),
    selectedCategory: AllCategoriesId,
    filterText: "",
    loading: true,
    applying: false,
    status: null,
    error: null
  };
}

export function FeatureManagementPage() {
  const hosts = useMemo(() => getFeatureHosts(moduleApi), []);
  const [activeHostId, setActiveHostId] = useState<FeatureHostId>("server");
  const [hostStates, setHostStates] = useState<Record<FeatureHostId, FeatureHostState>>(() => ({
    studio: createInitialHostState(),
    server: createInitialHostState()
  }));

  const activeHost = hosts.find(host => host.id === activeHostId) ?? hosts[0];
  const hostState = hostStates[activeHost.id];
  const { catalog, draft, selectedId, checkedFeatureIds, selectedCategory, filterText, loading, applying, status, error } = hostState;

  useEffect(() => {
    for (const host of hosts) {
      void refreshHost(host.id);
    }
  }, []);

  const textFilteredDraft = useMemo(() => filterFeaturesByText(draft, filterText), [draft, filterText]);
  const categories = useMemo(() => getCategoryFilters(textFilteredDraft), [textFilteredDraft]);
  const filteredDraft = useMemo(
    () => filterFeaturesByCategory(textFilteredDraft, selectedCategory),
    [textFilteredDraft, selectedCategory]);
  const selectedFeature = filteredDraft.find(feature => feature.id === selectedId)
    ?? filteredDraft.find(feature => feature.enabled)
    ?? filteredDraft[0]
    ?? null;
  const dependentsIndex = useMemo(() => buildDependentsIndex(draft), [draft]);
  const selectedDependents = selectedFeature ? dependentsIndex.get(normalizeType(selectedFeature.id)) ?? [] : [];
  const dirty = isDirty(catalog, draft);
  const enabledCount = draft.filter(feature => feature.enabled).length;
  const selectedCategoryLabel = categories.find(category => category.id === selectedCategory)?.label ?? "All categories";
  const visibleFeatureIds = useMemo(() => new Set(filteredDraft.map(feature => feature.id)), [filteredDraft]);
  const checkedVisibleCount = filteredDraft.filter(feature => checkedFeatureIds.has(feature.id)).length;
  const allVisibleChecked = filteredDraft.length > 0 && checkedVisibleCount === filteredDraft.length;
  const checkedFeatures = draft.filter(feature => checkedFeatureIds.has(feature.id));
  const readOnly = !activeHost.writable;
  const editingDisabled = loading || applying || readOnly;

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

  async function refreshHost(hostId = activeHost.id) {
    const host = hosts.find(candidate => candidate.id === hostId) ?? activeHost;
    updateHostState(host.id, state => ({ ...state, loading: true, error: null }));
    try {
      const response = await host.context.http.getJson<FeatureCatalogResponse>("/modularity/features");
      updateHostState(host.id, state => ({
        ...state,
        catalog: response,
        draft: response.features.map(toDraftFeature),
        checkedFeatureIds: new Set(),
        status: null,
        loading: false
      }));
    } catch (e) {
      updateHostState(host.id, state => ({ ...state, error: getErrorMessage(e), loading: false }));
    }
  }

  async function apply() {
    if (!catalog || !dirty || readOnly) {
      return;
    }

    updateHostState(activeHost.id, state => ({ ...state, applying: true, error: null, status: null }));
    try {
      const response = await activeHost.context.http.postJson<FeatureApplyResult>(
        "/modularity/features/apply",
        createApplyPayload(catalog.revision, draft));
      updateHostState(activeHost.id, state => ({
        ...state,
        catalog: response.catalog,
        draft: response.catalog.features.map(toDraftFeature),
        status: `Applied ${response.featureDescriptorCount} descriptor(s); reloaded ${response.reloadedShellCount} shell(s).`,
        applying: false
      }));

      if (activeHost.id === "studio") {
        window.dispatchEvent(new Event(ModulesChangedEventName));
      }
    } catch (e) {
      updateHostState(activeHost.id, state => ({ ...state, error: getErrorMessage(e), applying: false }));
    }
  }

  function updateFeature(id: string, update: (feature: DraftFeature) => DraftFeature) {
    if (readOnly) {
      return;
    }

    updateHostState(activeHost.id, state => ({
      ...state,
      draft: state.draft.map(feature => feature.id === id ? update(feature) : feature)
    }));
  }

  function toggleFeature(feature: DraftFeature) {
    if (readOnly) {
      return;
    }

    const enabled = !feature.enabled;
    updateHostState(activeHost.id, state => ({
      ...state,
      draft: applyFeatureToggle(state.draft, feature.id, enabled)
    }));
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
    if (readOnly || checkedFeatureIds.size === 0) {
      return;
    }

    updateHostState(activeHost.id, state => ({
      ...state,
      draft: applyFeatureToggles(state.draft, state.checkedFeatureIds, enabled)
    }));
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
    if (!catalog) {
      return;
    }

    updateHostState(activeHost.id, state => ({
      ...state,
      draft: catalog.features.map(toDraftFeature),
      status: null,
      error: null
    }));
  }

  return (
    <section className="feature-management-page">
      <div className="section-header feature-management-header">
        <div>
          <h2>Features</h2>
          <p>{activeHost.label}: {enabledCount} enabled of {draft.length} available{dirty ? " - Unsaved changes" : ""}</p>
        </div>
        <div className="feature-management-actions">
          <button type="button" className="feature-management-icon-button" aria-label="Refresh features" title="Refresh" onClick={() => refreshHost()} disabled={loading || applying}>
            <RefreshCcw size={15} />
          </button>
          <button type="button" onClick={reset} disabled={!dirty || loading || applying || readOnly}>Reset</button>
          <button type="button" className="primary" onClick={apply} disabled={!dirty || loading || applying || readOnly}>
            {applying ? "Applying" : "Apply"}
          </button>
        </div>
      </div>

      <HostTabs hosts={hosts} activeHostId={activeHost.id} onSelect={setActiveHostId} />

      {error ? <div className="feature-management-error">{error}</div> : null}
      {status ? <div className="feature-management-status">{status}</div> : null}
      <div className="feature-management-layout">
        <CategoryFilter
          categories={categories}
          selectedCategory={selectedCategory}
          filterText={filterText}
          onSelect={category => updateHostState(activeHost.id, state => ({ ...state, selectedCategory: category }))}
          onFilterTextChange={value => updateHostState(activeHost.id, state => ({ ...state, filterText: value }))}
        />

        <div className="feature-management-list-column">
          <div className="feature-management-list-heading">
            <div>
              <strong>{selectedCategoryLabel}</strong>
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
            {loading && draft.length === 0 ? <p className="feature-management-muted">Loading features...</p> : null}
            {!loading && draft.length === 0 ? <p className="feature-management-muted">No features are available.</p> : null}
            {!loading && draft.length > 0 && filteredDraft.length === 0 ? <p className="feature-management-muted">{filterText.trim() ? "No features match this filter." : "No features match this category."}</p> : null}
            {filteredDraft.map(feature => (
              <FeatureCard
                key={feature.id}
                feature={feature}
                selected={feature.id === selectedFeature?.id}
                checked={checkedFeatureIds.has(feature.id)}
                disabled={editingDisabled}
                onSelect={() => updateHostState(activeHost.id, state => ({ ...state, selectedId: feature.id }))}
                onToggle={() => toggleFeature(feature)}
                onCheckedChange={checked => toggleCheckedFeature(feature.id, checked)}
              />
            ))}
          </div>
        </div>

        <FeatureInspector
          feature={selectedFeature}
          dependents={selectedDependents}
          disabled={editingDisabled}
          onToggle={toggleFeature}
          onSettingChange={updateSetting}
        />
      </div>
    </section>
  );
}

function HostTabs({
  hosts,
  activeHostId,
  onSelect
}: {
  hosts: FeatureHostConfig[];
  activeHostId: FeatureHostId;
  onSelect(hostId: FeatureHostId): void;
}) {
  return (
    <div className="feature-management-host-tabs" role="tablist" aria-label="Feature host">
      {hosts.map(host => (
        <button
          key={host.id}
          type="button"
          role="tab"
          aria-selected={host.id === activeHostId}
          className={host.id === activeHostId ? "active" : ""}
          onClick={() => onSelect(host.id)}
        >
          <span>{host.label}</span>
          <small>{host.runtime}</small>
        </button>
      ))}
    </div>
  );
}

function BulkFeatureActions({
  disabled,
  selectedCount,
  visibleCount,
  visibleSelectedCount,
  selectedEnabledCount,
  allVisibleChecked,
  onToggleVisible,
  onClearSelection,
  onEnableSelected,
  onDisableSelected
}: {
  disabled: boolean;
  selectedCount: number;
  visibleCount: number;
  visibleSelectedCount: number;
  selectedEnabledCount: number;
  allVisibleChecked: boolean;
  onToggleVisible(): void;
  onClearSelection(): void;
  onEnableSelected(): void;
  onDisableSelected(): void;
}) {
  return (
    <div className="feature-management-bulk-actions" aria-label="Bulk feature actions">
      <label>
        <input
          type="checkbox"
          checked={allVisibleChecked}
          disabled={disabled}
          onChange={onToggleVisible}
          aria-label={allVisibleChecked ? "Clear visible feature selection" : "Select visible features"}
        />
        <span>{visibleSelectedCount}/{visibleCount} visible</span>
      </label>
      <div>
        <span title={`${selectedEnabledCount} enabled`}>{selectedCount} selected</span>
        <button
          type="button"
          aria-label="Enable selected features"
          disabled={disabled || selectedCount === 0 || selectedEnabledCount === selectedCount}
          onClick={onEnableSelected}
        >
          Enable
        </button>
        <button
          type="button"
          aria-label="Disable selected features"
          disabled={disabled || selectedCount === 0 || selectedEnabledCount === 0}
          onClick={onDisableSelected}
        >
          Disable
        </button>
        <button type="button" aria-label="Clear selected features" disabled={disabled || selectedCount === 0} onClick={onClearSelection}>Clear</button>
      </div>
    </div>
  );
}

function CategoryFilter({
  categories,
  selectedCategory,
  filterText,
  onFilterTextChange,
  onSelect
}: {
  categories: CategoryFilterItem[];
  selectedCategory: string;
  filterText: string;
  onFilterTextChange(value: string): void;
  onSelect(categoryId: string): void;
}) {
  return (
    <aside className="feature-management-categories" aria-label="Feature categories">
      <span className="feature-management-panel-label">Categories</span>
      <label className="feature-management-filter">
        <span>Filter features</span>
        <input
          type="search"
          aria-label="Filter features"
          placeholder="Filter by name, package, setting..."
          value={filterText}
          onChange={event => onFilterTextChange(event.target.value)}
        />
      </label>
      <div className="feature-management-category-list">
        {categories.map(category => (
          <button
            key={category.id}
            type="button"
            className={category.id === selectedCategory ? "active" : ""}
            onClick={() => onSelect(category.id)}
          >
            <span>{category.label}</span>
            <em>{category.count}</em>
          </button>
        ))}
      </div>
    </aside>
  );
}

function FeatureCard({
  feature,
  selected,
  checked,
  disabled,
  onSelect,
  onToggle,
  onCheckedChange
}: {
  feature: DraftFeature;
  selected: boolean;
  checked: boolean;
  disabled: boolean;
  onSelect(): void;
  onToggle(): void;
  onCheckedChange(checked: boolean): void;
}) {
  const displayName = feature.displayName || feature.id;
  const showTechnicalName = !sameText(displayName, feature.id);

  return (
    <article className={selected ? "feature-management-card selected" : "feature-management-card"}>
      <input
        type="checkbox"
        className="feature-management-row-checkbox"
        checked={checked}
        disabled={disabled}
        aria-label={`Select ${feature.displayName || feature.id}`}
        onChange={event => onCheckedChange(event.target.checked)}
      />
      <button
        type="button"
        className={feature.enabled ? "feature-management-switch enabled" : "feature-management-switch"}
        onClick={onToggle}
        disabled={disabled}
        role="switch"
        aria-checked={feature.enabled}
        aria-label={`${feature.enabled ? "Disable" : "Enable"} ${feature.displayName || feature.id}`}
      />
      <button type="button" className="feature-management-feature-button" onClick={onSelect}>
        <span className="feature-management-card-title">
          <strong>{displayName}</strong>
          {feature.experimental ? <FeatureBadge tone="experimental"><FlaskConical size={11} />Experimental</FeatureBadge> : null}
          {feature.advanced ? <FeatureBadge tone="advanced"><ShieldAlert size={11} />Advanced</FeatureBadge> : null}
        </span>
        {showTechnicalName ? <code>{feature.id}</code> : null}
        {feature.description ? <small>{feature.description}</small> : null}
        <span className="feature-management-card-meta">
          <SourceBadge sourceKind={feature.sourceKind} />
          <CategoryChips categories={feature.categories} max={2} />
        </span>
      </button>
      <span className="feature-management-card-count" title={`${feature.settings.length} setting${feature.settings.length === 1 ? "" : "s"}`}>
        <Settings2 size={13} />
        {feature.settings.length}
      </span>
    </article>
  );
}

function FeatureInspector({
  feature,
  dependents,
  disabled,
  onToggle,
  onSettingChange
}: {
  feature: DraftFeature | null;
  dependents: string[];
  disabled: boolean;
  onToggle(feature: DraftFeature): void;
  onSettingChange(feature: DraftFeature, setting: StudioSettingDescriptor, value: unknown): void;
}) {
  const [settingsDialogFeatureId, setSettingsDialogFeatureId] = useState<string | null>(null);

  if (!feature) {
    return <aside className="feature-management-inspector"><p className="feature-management-muted">Select a feature.</p></aside>;
  }

  const displayName = feature.displayName || feature.id;
  const editSettingsDisabled = disabled || !feature.enabled;
  const settingsDialogOpen = settingsDialogFeatureId === feature.id;

  return (
    <aside className="feature-management-inspector">
      <div className="feature-management-inspector-heading">
        <div>
          <span className="feature-management-inspector-badges">
            <SourceBadge sourceKind={feature.sourceKind} />
            <FeatureBadge tone={feature.enabled ? "enabled" : "neutral"}>{feature.enabled ? "Enabled" : "Disabled"}</FeatureBadge>
            {feature.experimental ? <FeatureBadge tone="experimental"><FlaskConical size={11} />Experimental</FeatureBadge> : null}
            {feature.advanced ? <FeatureBadge tone="advanced"><ShieldAlert size={11} />Advanced</FeatureBadge> : null}
          </span>
          <h3>{displayName}</h3>
        </div>
        <button type="button" onClick={() => onToggle(feature)} disabled={disabled}>
          {feature.enabled ? "Disable" : "Enable"}
        </button>
      </div>

      {feature.description ? <p>{feature.description}</p> : null}
      {feature.readError ? <div className="feature-management-warning">{feature.readError}</div> : null}

      <section className="feature-management-inspector-section" aria-label="Feature metadata">
        <FeatureMetadata feature={feature} displayName={displayName} dependents={dependents} />
      </section>

      <section className="feature-management-inspector-section feature-management-settings-panel" aria-label="Feature settings">
        <div className="feature-management-section-heading">
          <h4>Settings</h4>
          {feature.settings.length > 0 ? (
            <button
              type="button"
              onClick={() => setSettingsDialogFeatureId(feature.id)}
              disabled={editSettingsDisabled}
              title={!feature.enabled ? "Enable the feature before editing settings." : undefined}
            >
              Edit settings
            </button>
          ) : null}
        </div>
        {feature.settings.length === 0 ? (
          <p className="feature-management-muted">No configurable settings.</p>
        ) : (
          <FeatureSettingsSummary feature={feature} />
        )}
      </section>

      {settingsDialogOpen ? (
        <FeatureSettingsDialog
          feature={feature}
          disabled={editSettingsDisabled}
          onSettingChange={onSettingChange}
          onClose={() => setSettingsDialogFeatureId(null)}
        />
      ) : null}
    </aside>
  );
}

function FeatureMetadata({ feature, displayName, dependents }: { feature: DraftFeature; displayName: string; dependents: string[] }) {
  const source = getSourceMeta(feature.sourceKind);
  const dependencies = feature.dependencies.map(dependency => dependency.id);
  const packageName = feature.packageId
    ? [feature.packageId, feature.packageVersion].filter(Boolean).join(" ")
    : "Not package-backed";

  return (
    <section className="feature-management-metadata" aria-label="Feature metadata">
      <h4>Metadata</h4>
      <dl className="feature-management-detail-list">
        <div><dt>Display name</dt><dd>{displayName}</dd></div>
        <div><dt>Technical name</dt><dd><code>{feature.id}</code></dd></div>
        <div><dt>Status</dt><dd><FeatureBadge tone={feature.enabled ? "enabled" : "neutral"}>{feature.enabled ? "Enabled" : "Disabled"}</FeatureBadge></dd></div>
        <div><dt>Source</dt><dd><FeatureBadge tone={source.tone} title={source.title}>{source.label}</FeatureBadge></dd></div>
        <div className="feature-management-detail-wide"><dt>Categories</dt><dd><CategoryChips categories={feature.categories} /></dd></div>
        <div className="feature-management-detail-wide"><dt>Package</dt><dd>{feature.packageId ? <FeatureBadge tone="violet"><Package size={11} />{packageName}</FeatureBadge> : <span className="feature-management-muted">{packageName}</span>}</dd></div>
        <div><dt>Settings</dt><dd>{feature.settings.length}</dd></div>
        {dependencies.length > 0 ? <div className="feature-management-detail-wide"><dt>Depends on</dt><dd><FeatureLinkChips ids={dependencies} /></dd></div> : null}
        {dependents.length > 0 ? <div className="feature-management-detail-wide"><dt>Required by</dt><dd><FeatureLinkChips ids={dependents} /></dd></div> : null}
        <div><dt>Advanced</dt><dd>{feature.advanced ? <FeatureBadge tone="advanced">Advanced</FeatureBadge> : <span className="feature-management-muted">No</span>}</dd></div>
        <div><dt>Experimental</dt><dd>{feature.experimental ? <FeatureBadge tone="experimental">Experimental</FeatureBadge> : <span className="feature-management-muted">No</span>}</dd></div>
        {feature.description ? <div className="feature-management-detail-wide"><dt>Description</dt><dd>{feature.description}</dd></div> : null}
        {feature.manifestHash ? <div className="feature-management-detail-wide"><dt>Manifest hash</dt><dd><code>{feature.manifestHash}</code></dd></div> : null}
        {feature.manifestPath ? <div className="feature-management-detail-wide"><dt>Manifest path</dt><dd><code>{feature.manifestPath}</code></dd></div> : null}
      </dl>
    </section>
  );
}

function FeatureBadge({ tone = "neutral", title, children }: { tone?: string; title?: string; children: React.ReactNode }) {
  return <span className={`feature-management-badge tone-${tone}`} title={title}>{children}</span>;
}

function SourceBadge({ sourceKind }: { sourceKind: string }) {
  const source = getSourceMeta(sourceKind);
  return <FeatureBadge tone={source.tone} title={source.title}>{source.label}</FeatureBadge>;
}

function CategoryChips({ categories, max }: { categories: string[]; max?: number }) {
  if (categories.length === 0) {
    return <span className="feature-management-muted">Uncategorized</span>;
  }

  const shown = max ? categories.slice(0, max) : categories;
  const overflow = max ? categories.length - shown.length : 0;

  return (
    <span className="feature-management-chips">
      {shown.map(category => <span key={category} className="feature-management-chip">{category}</span>)}
      {overflow > 0 ? <span className="feature-management-chip feature-management-chip-overflow" title={categories.slice(shown.length).join(", ")}>+{overflow}</span> : null}
    </span>
  );
}

function FeatureLinkChips({ ids }: { ids: string[] }) {
  return (
    <span className="feature-management-chips">
      {ids.map(id => <span key={id} className="feature-management-chip"><code>{id}</code></span>)}
    </span>
  );
}

function getSourceMeta(sourceKind: string): { label: string; tone: string; title: string } {
  switch (normalizeType(sourceKind)) {
    case "shell":
      return { label: "Built-in", tone: "neutral", title: "Compiled into the host shell." };
    case "runtime":
      return { label: "Runtime", tone: "info", title: "Discovered at runtime from loaded assemblies." };
    case "manifest":
      return { label: "Package", tone: "violet", title: "Provided by an installed package manifest." };
    case "manifest-error":
      return { label: "Package error", tone: "danger", title: "The package manifest could not be read." };
    default:
      return { label: sourceKind || "Unknown", tone: "neutral", title: sourceKind };
  }
}

function FeatureSettingsSummary({ feature }: { feature: DraftFeature }) {
  const settingGroups = getSettingGroups(feature.settings);

  return (
    <div className="feature-management-settings-summary">
      {settingGroups.map(group => (
        <section key={group.id} className="feature-management-setting-summary-group">
          <h5>{group.label}</h5>
          <div className="feature-management-setting-summary-list">
            {group.settings.map(setting => (
              <div key={setting.name} className="feature-management-setting-summary-row">
                <span className="feature-management-setting-summary-meta">
                  <span>
                    <strong>{setting.displayName || setting.name}</strong>
                    {setting.required ? <em>Required</em> : null}
                    {setting.restartRequired ? <em>Reload</em> : null}
                    {setting.advanced ? <em>Advanced</em> : null}
                  </span>
                  {setting.description ? <small>{setting.description}</small> : null}
                  <code>{setting.name}</code>
                </span>
                <span className="feature-management-setting-summary-value">
                  {formatSettingPreview(setting, getSettingValue(feature, setting))}
                </span>
              </div>
            ))}
          </div>
        </section>
      ))}
    </div>
  );
}

function FeatureSettingsDialog({
  feature,
  disabled,
  onSettingChange,
  onClose
}: {
  feature: DraftFeature;
  disabled: boolean;
  onSettingChange(feature: DraftFeature, setting: StudioSettingDescriptor, value: unknown): void;
  onClose(): void;
}) {
  const [values, setValues] = useState<Record<string, unknown>>(() => getFeatureSettingValues(feature));
  const settingGroups = getSettingGroups(feature.settings);

  useEffect(() => {
    setValues(getFeatureSettingValues(feature));
  }, [feature.id]);

  function updateSettingValue(setting: StudioSettingDescriptor, value: unknown) {
    setValues(current => ({
      ...current,
      [setting.name]: value
    }));
  }

  function resetDialogChanges() {
    setValues(getFeatureSettingValues(feature));
  }

  function saveDraft() {
    for (const setting of feature.settings) {
      onSettingChange(feature, setting, values[setting.name]);
    }

    onClose();
  }

  return (
    <div className="feature-management-dialog-backdrop" role="presentation">
      <section
        className="feature-management-settings-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="feature-management-settings-dialog-title"
      >
        <div className="feature-management-dialog-heading">
          <div>
            <span>Settings</span>
            <h3 id="feature-management-settings-dialog-title">{feature.displayName || feature.id}</h3>
          </div>
          <button type="button" onClick={onClose}>Close</button>
        </div>

        <div className="feature-management-dialog-body">
          <div className="feature-management-settings">
            {settingGroups.map(group => (
              <section key={group.id} className="feature-management-setting-group">
                <h5>{group.label}</h5>
                <div>
                  {group.settings.map(setting => {
                    const Editor = selectSettingEditor(moduleApi, setting).component;

                    return (
                      <SettingField key={setting.name} setting={setting}>
                        <Editor
                          setting={setting}
                          value={values[setting.name]}
                          disabled={disabled}
                          onChange={value => updateSettingValue(setting, value)}
                        />
                      </SettingField>
                    );
                  })}
                </div>
              </section>
            ))}
          </div>
        </div>

        <div className="feature-management-dialog-actions">
          <span>Save draft keeps changes local until the page Apply action runs.</span>
          <div>
            <button type="button" onClick={onClose}>Cancel</button>
            <button type="button" onClick={resetDialogChanges} disabled={disabled}>Reset changes</button>
            <button type="button" className="primary" onClick={saveDraft} disabled={disabled}>Save draft</button>
          </div>
        </div>
      </section>
    </div>
  );
}

function SettingField({ setting, children }: { setting: StudioSettingDescriptor; children: React.ReactNode }) {
  const booleanSetting = isBooleanSetting(setting);

  return (
    <label className={booleanSetting ? "feature-management-setting boolean" : "feature-management-setting"}>
      <span className="feature-management-setting-main">
        <span className="feature-management-setting-title">
          <strong>{setting.displayName || setting.name}</strong>
          {setting.required ? <em>Required</em> : null}
          {setting.restartRequired ? <em>Reload</em> : null}
          {setting.advanced ? <em>Advanced</em> : null}
        </span>
        {booleanSetting ? (
          <span className="feature-management-setting-control">
            {children}
          </span>
        ) : null}
      </span>
      {!booleanSetting ? (
        <span className="feature-management-setting-control">
          {children}
        </span>
      ) : null}
      {setting.description ? <small>{setting.description}</small> : null}
      <code>{setting.name}</code>
    </label>
  );
}

// Coerce the wire dependency list into FeatureDependency objects. Tolerates a legacy `string[]` payload (treated as
// mandatory) so an older backend still cascades correctly.
function normalizeDependencies(raw: unknown): FeatureDependency[] {
  if (!Array.isArray(raw))
    return [];

  const dependencies: FeatureDependency[] = [];
  for (const entry of raw) {
    if (typeof entry === "string") {
      dependencies.push({ id: entry, optional: false });
    } else if (entry && typeof entry === "object" && typeof (entry as { id?: unknown }).id === "string") {
      dependencies.push({ id: (entry as { id: string }).id, optional: (entry as { optional?: unknown }).optional === true });
    }
  }

  return dependencies;
}

function toDraftFeature(feature: FeatureCatalogItem): DraftFeature {
  return {
    ...feature,
    dependencies: normalizeDependencies(feature.dependencies),
    configuration: feature.configuration && typeof feature.configuration === "object" && !Array.isArray(feature.configuration)
      ? { ...feature.configuration }
      : {}
  };
}

function getSettingValue(feature: DraftFeature, setting: StudioSettingDescriptor) {
  return Object.prototype.hasOwnProperty.call(feature.configuration, setting.name)
    ? feature.configuration[setting.name]
    : setting.defaultValue ?? defaultSettingValue(setting);
}

function getFeatureSettingValues(feature: DraftFeature) {
  return Object.fromEntries(feature.settings.map(setting => [setting.name, getSettingValue(feature, setting)]));
}

function isBooleanSetting(setting: StudioSettingDescriptor) {
  return normalizeType(setting.jsonType) === "boolean";
}

function formatSettingPreview(setting: StudioSettingDescriptor, value: unknown) {
  if (setting.secret || setting.sensitive) {
    return value == null || value === "" ? "Not set" : "********";
  }

  if (normalizeType(setting.jsonType) === "boolean") {
    return value ? "Enabled" : "Disabled";
  }

  if (value == null || value === "") {
    return "Not set";
  }

  const text = typeof value === "object" ? JSON.stringify(value) : String(value);
  return text.length > 96 ? `${text.slice(0, 93)}...` : text;
}

function defaultSettingValue(setting: StudioSettingDescriptor) {
  const jsonType = normalizeType(setting.jsonType);
  if (jsonType === "boolean") return false;
  if (jsonType === "integer" || jsonType === "number") return 0;
  if (jsonType === "array") return [];
  if (jsonType === "object") return {};
  return "";
}

function getCategoryFilters(features: DraftFeature[]): CategoryFilterItem[] {
  const counts = new Map<string, number>();

  for (const feature of features) {
    const categories = getFeatureCategoryIds(feature);
    for (const category of categories) {
      counts.set(category, (counts.get(category) ?? 0) + 1);
    }
  }

  return [
    { id: AllCategoriesId, label: "All categories", count: features.length },
    ...Array.from(counts.entries())
      .sort(([a], [b]) => getCategoryLabel(a).localeCompare(getCategoryLabel(b)))
      .map(([id, count]) => ({ id, label: getCategoryLabel(id), count }))
  ];
}

function filterFeaturesByCategory(features: DraftFeature[], category: string) {
  if (category === AllCategoriesId) {
    return features;
  }

  return features.filter(feature => getFeatureCategoryIds(feature).includes(category));
}

export function filterFeaturesByText(features: DraftFeature[], filterText: string) {
  const terms = filterText
    .trim()
    .toLowerCase()
    .split(/\s+/)
    .filter(Boolean);

  if (terms.length === 0) {
    return features;
  }

  return features.filter(feature => {
    const searchable = [
      feature.id,
      feature.displayName,
      feature.description,
      feature.sourceKind,
      feature.packageId,
      feature.packageVersion,
      ...feature.categories,
      ...feature.settings.flatMap(setting => [
        setting.name,
        setting.displayName,
        setting.description,
        setting.category,
        setting.group,
        setting.uiHint
      ])
    ]
      .filter((value): value is string => typeof value === "string")
      .join(" ")
      .toLowerCase();

    return terms.every(term => searchable.includes(term));
  });
}

function getFeatureCategoryIds(feature: DraftFeature) {
  return feature.categories.length > 0 ? feature.categories : [UncategorizedId];
}

function getCategoryLabel(category: string) {
  return category === UncategorizedId ? "Uncategorized" : category;
}

function getSettingGroups(settings: StudioSettingDescriptor[]): SettingGroupItem[] {
  const groups = new Map<string, SettingGroupItem>();

  for (const setting of settings) {
    const id = setting.group || setting.category || "General";
    const existing = groups.get(id);
    if (existing) {
      existing.settings.push(setting);
    } else {
      groups.set(id, { id, label: id, settings: [setting] });
    }
  }

  return Array.from(groups.values());
}

function normalizeType(value?: string | null) {
  return (value ?? "").trim().toLowerCase();
}

function sameText(left?: string | null, right?: string | null) {
  return (left ?? "").trim().toLowerCase() === (right ?? "").trim().toLowerCase();
}

function canonicalize(features: FeatureCatalogItem[] | DraftFeature[]) {
  return JSON.stringify(features
    .map(feature => ({
      id: feature.id,
      enabled: feature.enabled,
      configuration: feature.enabled ? feature.configuration : {}
    }))
    .sort((a, b) => a.id.localeCompare(b.id)));
}

export function getErrorMessage(error: unknown) {
  if (isHttpError(error)) {
    if (error.status === 409) {
      return `The feature catalog changed before your changes were applied. Refresh and review the latest feature state. ${error.message}`;
    }

    if (error.status === 400) {
      return `The feature configuration was rejected. ${error.message}`;
    }
  }

  return error instanceof Error ? error.message : String(error);
}

function isHttpError(error: unknown): error is { status: number; message: string } {
  return error instanceof Error && typeof (error as { status?: unknown }).status === "number";
}
