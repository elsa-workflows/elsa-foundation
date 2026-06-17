import React, { useEffect, useMemo, useState } from "react";
import type {
  ElsaStudioModuleApi,
  StudioSettingDescriptor,
  StudioSettingEditorContribution,
  StudioSettingEditorProps
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

const AllCategoriesId = "__all";
const UncategorizedId = "__uncategorized";

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

export function registerBuiltInSettingEditors(api: ElsaStudioModuleApi) {
  api.settingEditors.add({
    id: "select",
    order: 10,
    supports: setting => (setting.options?.length ?? 0) > 0 || includesHint(setting, "select"),
    component: SelectSettingEditor
  });
  api.settingEditors.add({
    id: "boolean",
    order: 20,
    supports: setting => normalizeType(setting.jsonType) === "boolean",
    component: BooleanSettingEditor
  });
  api.settingEditors.add({
    id: "number",
    order: 30,
    supports: setting => ["integer", "number"].includes(normalizeType(setting.jsonType)),
    component: NumberSettingEditor
  });
  api.settingEditors.add({
    id: "json",
    order: 40,
    supports: setting => ["object", "array"].includes(normalizeType(setting.jsonType)) || includesHint(setting, "json") || includesHint(setting, "textarea"),
    component: JsonSettingEditor
  });
  api.settingEditors.add({
    id: "secret",
    order: 50,
    supports: setting => setting.secret || setting.sensitive,
    component: SecretSettingEditor
  });
  api.settingEditors.add({
    id: "text",
    order: 100,
    supports: () => true,
    component: TextSettingEditor
  });
}

export function selectSettingEditor(api: ElsaStudioModuleApi, setting: StudioSettingDescriptor): StudioSettingEditorContribution {
  const editor = api.settingEditors
    .list()
    .filter(contribution => contribution.supports(setting))
    .sort((a, b) => (a.order ?? 500) - (b.order ?? 500))[0];

  if (!editor) {
    throw new Error(`No setting editor is registered for '${setting.name}'.`);
  }

  return editor;
}

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

export function FeatureManagementPage() {
  const [catalog, setCatalog] = useState<FeatureCatalogResponse | null>(null);
  const [draft, setDraft] = useState<DraftFeature[]>([]);
  const [selectedId, setSelectedId] = useState("");
  const [checkedFeatureIds, setCheckedFeatureIds] = useState<Set<string>>(() => new Set());
  const [selectedCategory, setSelectedCategory] = useState(AllCategoriesId);
  const [loading, setLoading] = useState(true);
  const [applying, setApplying] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    void refresh();
  }, []);

  const categories = useMemo(() => getCategoryFilters(draft), [draft]);
  const filteredDraft = useMemo(
    () => filterFeaturesByCategory(draft, selectedCategory),
    [draft, selectedCategory]);
  const selectedFeature = filteredDraft.find(feature => feature.id === selectedId)
    ?? filteredDraft.find(feature => feature.enabled)
    ?? filteredDraft[0]
    ?? null;
  const dirty = isDirty(catalog, draft);
  const enabledCount = draft.filter(feature => feature.enabled).length;
  const selectedCategoryLabel = categories.find(category => category.id === selectedCategory)?.label ?? "All categories";
  const visibleFeatureIds = useMemo(() => new Set(filteredDraft.map(feature => feature.id)), [filteredDraft]);
  const checkedVisibleCount = filteredDraft.filter(feature => checkedFeatureIds.has(feature.id)).length;
  const allVisibleChecked = filteredDraft.length > 0 && checkedVisibleCount === filteredDraft.length;
  const checkedFeatures = draft.filter(feature => checkedFeatureIds.has(feature.id));

  useEffect(() => {
    if (selectedFeature && selectedFeature.id !== selectedId) {
      setSelectedId(selectedFeature.id);
    }
  }, [selectedFeature, selectedId]);

  useEffect(() => {
    if (!categories.some(category => category.id === selectedCategory)) {
      setSelectedCategory(AllCategoriesId);
    }
  }, [categories, selectedCategory]);

  useEffect(() => {
    setCheckedFeatureIds(current => {
      const existingIds = new Set(draft.map(feature => feature.id));
      const next = new Set(Array.from(current).filter(id => existingIds.has(id)));
      return next.size === current.size ? current : next;
    });
  }, [draft]);

  async function refresh() {
    setLoading(true);
    setError(null);
    try {
      const response = await moduleApi.backend.http.getJson<FeatureCatalogResponse>("/modularity/features");
      setCatalog(response);
      setDraft(response.features.map(toDraftFeature));
      setCheckedFeatureIds(new Set());
      setStatus(null);
    } catch (e) {
      setError(getErrorMessage(e));
    } finally {
      setLoading(false);
    }
  }

  async function apply() {
    if (!catalog || !dirty) {
      return;
    }

    setApplying(true);
    setError(null);
    setStatus(null);
    try {
      const response = await moduleApi.backend.http.postJson<FeatureApplyResult>(
        "/modularity/features/apply",
        createApplyPayload(catalog.revision, draft));
      setCatalog(response.catalog);
      setDraft(response.catalog.features.map(toDraftFeature));
      setStatus(`Applied ${response.featureDescriptorCount} descriptor(s); reloaded ${response.reloadedShellCount} shell(s).`);
    } catch (e) {
      setError(getErrorMessage(e));
    } finally {
      setApplying(false);
    }
  }

  function updateFeature(id: string, update: (feature: DraftFeature) => DraftFeature) {
    setDraft(features => features.map(feature => feature.id === id ? update(feature) : feature));
  }

  function toggleFeature(feature: DraftFeature) {
    updateFeature(feature.id, current => ({ ...current, enabled: !current.enabled }));
  }

  function toggleCheckedFeature(id: string, checked: boolean) {
    setCheckedFeatureIds(current => {
      const next = new Set(current);
      if (checked) {
        next.add(id);
      } else {
        next.delete(id);
      }
      return next;
    });
  }

  function toggleVisibleFeatureSelection() {
    setCheckedFeatureIds(current => {
      const next = new Set(current);
      if (allVisibleChecked) {
        for (const id of visibleFeatureIds) {
          next.delete(id);
        }
      } else {
        for (const id of visibleFeatureIds) {
          next.add(id);
        }
      }
      return next;
    });
  }

  function setCheckedFeaturesEnabled(enabled: boolean) {
    if (checkedFeatureIds.size === 0) {
      return;
    }

    setDraft(features => features.map(feature => checkedFeatureIds.has(feature.id)
      ? { ...feature, enabled }
      : feature));
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

    setDraft(catalog.features.map(toDraftFeature));
    setStatus(null);
    setError(null);
  }

  return (
    <section className="feature-management-page">
      <div className="section-header feature-management-header">
        <div>
          <h2>Features</h2>
          <p>{enabledCount} enabled of {draft.length} available{dirty ? " - Unsaved changes" : ""}</p>
        </div>
        <div className="feature-management-actions">
          <button type="button" onClick={refresh} disabled={loading || applying}>Refresh</button>
          <button type="button" onClick={reset} disabled={!dirty || loading || applying}>Reset</button>
          <button type="button" className="primary" onClick={apply} disabled={!dirty || loading || applying}>
            {applying ? "Applying" : "Apply"}
          </button>
        </div>
      </div>

      {error ? <div className="feature-management-error">{error}</div> : null}
      {status ? <div className="feature-management-status">{status}</div> : null}

      <div className="feature-management-layout">
        <CategoryFilter
          categories={categories}
          selectedCategory={selectedCategory}
          onSelect={setSelectedCategory}
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
            disabled={loading || applying || filteredDraft.length === 0}
            selectedCount={checkedFeatureIds.size}
            visibleCount={filteredDraft.length}
            visibleSelectedCount={checkedVisibleCount}
            selectedEnabledCount={checkedFeatures.filter(feature => feature.enabled).length}
            allVisibleChecked={allVisibleChecked}
            onToggleVisible={toggleVisibleFeatureSelection}
            onClearSelection={() => setCheckedFeatureIds(new Set())}
            onEnableSelected={() => setCheckedFeaturesEnabled(true)}
            onDisableSelected={() => setCheckedFeaturesEnabled(false)}
          />

          <div className="feature-management-list" aria-busy={loading}>
            {loading && draft.length === 0 ? <p className="feature-management-muted">Loading features...</p> : null}
            {!loading && draft.length === 0 ? <p className="feature-management-muted">No features are available.</p> : null}
            {!loading && draft.length > 0 && filteredDraft.length === 0 ? <p className="feature-management-muted">No features match this category.</p> : null}
            {filteredDraft.map(feature => (
              <FeatureCard
                key={feature.id}
                feature={feature}
                selected={feature.id === selectedFeature?.id}
                checked={checkedFeatureIds.has(feature.id)}
                onSelect={() => setSelectedId(feature.id)}
                onToggle={() => toggleFeature(feature)}
                onCheckedChange={checked => toggleCheckedFeature(feature.id, checked)}
              />
            ))}
          </div>
        </div>

        <FeatureInspector
          feature={selectedFeature}
          disabled={applying || loading}
          dirty={dirty}
          onToggle={toggleFeature}
          onSettingChange={updateSetting}
          onReset={reset}
          onApply={apply}
        />
      </div>
    </section>
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
  onSelect
}: {
  categories: CategoryFilterItem[];
  selectedCategory: string;
  onSelect(categoryId: string): void;
}) {
  return (
    <aside className="feature-management-categories" aria-label="Feature categories">
      <span className="feature-management-panel-label">Categories</span>
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
  onSelect,
  onToggle,
  onCheckedChange
}: {
  feature: DraftFeature;
  selected: boolean;
  checked: boolean;
  onSelect(): void;
  onToggle(): void;
  onCheckedChange(checked: boolean): void;
}) {
  return (
    <article className={selected ? "feature-management-card selected" : "feature-management-card"}>
      <input
        type="checkbox"
        className="feature-management-row-checkbox"
        checked={checked}
        aria-label={`Select ${feature.displayName || feature.id}`}
        onChange={event => onCheckedChange(event.target.checked)}
      />
      <button
        type="button"
        className={feature.enabled ? "feature-management-switch enabled" : "feature-management-switch"}
        onClick={onToggle}
        role="switch"
        aria-checked={feature.enabled}
        aria-label={`${feature.enabled ? "Disable" : "Enable"} ${feature.displayName || feature.id}`}
      />
      <button type="button" className="feature-management-feature-button" onClick={onSelect}>
        <span className="feature-management-card-title">
          <strong>{feature.displayName || feature.id}</strong>
          {feature.experimental ? <em>Experimental</em> : null}
          {feature.advanced ? <em>Advanced</em> : null}
        </span>
        <code>{feature.id}</code>
        {feature.description ? <small>{feature.description}</small> : null}
        <span className="feature-management-card-meta">
          <span>{feature.sourceKind}</span>
          {feature.categories.slice(0, 2).map(category => <span key={category}>{category}</span>)}
          {feature.categories.length > 2 ? <span>+{feature.categories.length - 2}</span> : null}
        </span>
      </button>
      <span className="feature-management-card-count">{feature.settings.length}</span>
    </article>
  );
}

function FeatureInspector({
  feature,
  disabled,
  dirty,
  onToggle,
  onSettingChange,
  onReset,
  onApply
}: {
  feature: DraftFeature | null;
  disabled: boolean;
  dirty: boolean;
  onToggle(feature: DraftFeature): void;
  onSettingChange(feature: DraftFeature, setting: StudioSettingDescriptor, value: unknown): void;
  onReset(): void;
  onApply(): void;
}) {
  if (!feature) {
    return <aside className="feature-management-inspector"><p className="feature-management-muted">Select a feature.</p></aside>;
  }

  const settingGroups = getSettingGroups(feature.settings);
  const packageLabel = feature.packageId
    ? [feature.packageId, feature.packageVersion].filter(Boolean).join(" ")
    : null;

  return (
    <aside className="feature-management-inspector">
      <div className="feature-management-inspector-heading">
        <div>
          <span>{feature.sourceKind}</span>
          <h3>{feature.displayName || feature.id}</h3>
          <code>{feature.id}</code>
        </div>
        <button type="button" onClick={() => onToggle(feature)} disabled={disabled}>
          {feature.enabled ? "Disable" : "Enable"}
        </button>
      </div>

      {feature.description ? <p>{feature.description}</p> : null}
      {feature.readError ? <div className="feature-management-warning">{feature.readError}</div> : null}

      <div className="feature-management-inspector-tags">
        <span>{feature.sourceKind}</span>
        {packageLabel ? <span>{packageLabel}</span> : null}
        {feature.categories.map(category => <span key={category}>{category}</span>)}
      </div>

      {feature.manifestHash || feature.manifestPath ? (
        <dl className="feature-management-detail-list">
          {feature.manifestHash ? <div><dt>Manifest</dt><dd>{feature.manifestHash}</dd></div> : null}
          {feature.manifestPath ? <div><dt>Path</dt><dd>{feature.manifestPath}</dd></div> : null}
        </dl>
      ) : null}

      {feature.settings.length === 0 ? (
        <p className="feature-management-muted">No configurable settings.</p>
      ) : (
        <div className="feature-management-settings">
          {settingGroups.map(group => (
            <section key={group.id} className="feature-management-setting-group">
              <h4>{group.label}</h4>
              <div>
                {group.settings.map(setting => {
                  const Editor = selectSettingEditor(moduleApi, setting).component;

                  return (
                    <SettingField key={setting.name} setting={setting}>
                      <Editor
                        setting={setting}
                        value={getSettingValue(feature, setting)}
                        disabled={disabled || !feature.enabled}
                        onChange={value => onSettingChange(feature, setting, value)}
                      />
                    </SettingField>
                  );
                })}
              </div>
            </section>
          ))}
        </div>
      )}

      <div className="feature-management-sticky-actions">
        <span>{dirty ? "Unsaved changes" : "No pending changes"}</span>
        <div>
          <button type="button" onClick={onReset} disabled={!dirty || disabled}>Reset</button>
          <button type="button" className="primary" onClick={onApply} disabled={!dirty || disabled}>Apply</button>
        </div>
      </div>
    </aside>
  );
}

function SettingField({ setting, children }: { setting: StudioSettingDescriptor; children: React.ReactNode }) {
  return (
    <label className="feature-management-setting">
      <span>
        <strong>{setting.displayName || setting.name}</strong>
        {setting.required ? <em>Required</em> : null}
        {setting.restartRequired ? <em>Reload</em> : null}
        {setting.advanced ? <em>Advanced</em> : null}
      </span>
      {setting.description ? <small>{setting.description}</small> : null}
      {children}
      <code>{setting.name}</code>
    </label>
  );
}

function BooleanSettingEditor({ value, disabled, onChange }: StudioSettingEditorProps) {
  return <input type="checkbox" checked={Boolean(value)} disabled={disabled} onChange={event => onChange(event.target.checked)} />;
}

function TextSettingEditor({ value, disabled, onChange }: StudioSettingEditorProps) {
  return <input type="text" value={toInputValue(value)} disabled={disabled} onChange={event => onChange(event.target.value)} />;
}

function SecretSettingEditor({ value, disabled, onChange }: StudioSettingEditorProps) {
  return <input type="password" value={toInputValue(value)} disabled={disabled} onChange={event => onChange(event.target.value)} />;
}

function NumberSettingEditor({ setting, value, disabled, onChange }: StudioSettingEditorProps) {
  return (
    <input
      type="number"
      value={toInputValue(value)}
      disabled={disabled}
      onChange={event => onChange(normalizeType(setting.jsonType) === "integer"
        ? Number.parseInt(event.target.value || "0", 10)
        : Number.parseFloat(event.target.value || "0"))}
    />
  );
}

function SelectSettingEditor({ setting, value, disabled, onChange }: StudioSettingEditorProps) {
  const selected = JSON.stringify(value ?? "");
  return (
    <select value={selected} disabled={disabled} onChange={event => {
      const option = setting.options.find(candidate => JSON.stringify(candidate.value) === event.target.value);
      onChange(option?.value ?? "");
    }}>
      {!setting.required ? <option value={JSON.stringify("")}>Empty</option> : null}
      {setting.options.map(option => (
        <option key={`${setting.name}-${JSON.stringify(option.value)}`} value={JSON.stringify(option.value)}>
          {option.label}
        </option>
      ))}
    </select>
  );
}

function JsonSettingEditor({ setting, value, disabled, onChange }: StudioSettingEditorProps) {
  const [text, setText] = useState(toJsonText(value, setting));
  const [invalid, setInvalid] = useState(false);

  useEffect(() => {
    setText(toJsonText(value, setting));
    setInvalid(false);
  }, [setting.name, value]);

  return (
    <>
      <textarea value={text} disabled={disabled} rows={5} onChange={event => {
        const next = event.target.value;
        setText(next);
        try {
          onChange(JSON.parse(next || defaultJsonText(setting)));
          setInvalid(false);
        } catch {
          setInvalid(true);
        }
      }} />
      {invalid ? <small className="feature-management-setting-error">Invalid JSON</small> : null}
    </>
  );
}

function toDraftFeature(feature: FeatureCatalogItem): DraftFeature {
  return {
    ...feature,
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

function defaultSettingValue(setting: StudioSettingDescriptor) {
  const jsonType = normalizeType(setting.jsonType);
  if (jsonType === "boolean") return false;
  if (jsonType === "integer" || jsonType === "number") return 0;
  if (jsonType === "array") return [];
  if (jsonType === "object") return {};
  return "";
}

function toInputValue(value: unknown) {
  return value == null ? "" : typeof value === "object" ? JSON.stringify(value) : String(value);
}

function toJsonText(value: unknown, setting: StudioSettingDescriptor) {
  if (value == null || value === "") {
    return defaultJsonText(setting);
  }

  return JSON.stringify(value, null, 2);
}

function defaultJsonText(setting: StudioSettingDescriptor) {
  return normalizeType(setting.jsonType) === "array" ? "[]" : "{}";
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

function includesHint(setting: StudioSettingDescriptor, value: string) {
  return (setting.uiHint ?? "").toLowerCase().includes(value);
}

function normalizeType(value?: string | null) {
  return (value ?? "").trim().toLowerCase();
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
