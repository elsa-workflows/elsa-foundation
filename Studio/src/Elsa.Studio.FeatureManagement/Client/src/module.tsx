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
  const [loading, setLoading] = useState(true);
  const [applying, setApplying] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    void refresh();
  }, []);

  const selectedFeature = draft.find(feature => feature.id === selectedId)
    ?? draft.find(feature => feature.enabled)
    ?? draft[0]
    ?? null;
  const dirty = isDirty(catalog, draft);
  const enabledCount = draft.filter(feature => feature.enabled).length;

  useEffect(() => {
    if (selectedFeature && selectedFeature.id !== selectedId) {
      setSelectedId(selectedFeature.id);
    }
  }, [selectedFeature, selectedId]);

  async function refresh() {
    setLoading(true);
    setError(null);
    try {
      const response = await moduleApi.backend.http.getJson<FeatureCatalogResponse>("/modularity/features");
      setCatalog(response);
      setDraft(response.features.map(toDraftFeature));
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
    try {
      const response = await moduleApi.backend.http.getJson<FeatureApplyResult>("/modularity/features/apply", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(createApplyPayload(catalog.revision, draft))
      });
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

  function updateSetting(feature: DraftFeature, setting: StudioSettingDescriptor, value: unknown) {
    updateFeature(feature.id, current => ({
      ...current,
      configuration: {
        ...current.configuration,
        [setting.name]: value
      }
    }));
  }

  return (
    <section className="feature-management-page">
      <div className="section-header feature-management-header">
        <div>
          <h2>Features</h2>
          <p>{enabledCount} enabled of {draft.length} available</p>
        </div>
        <div className="feature-management-actions">
          <button type="button" onClick={refresh} disabled={loading || applying}>Refresh</button>
          <button type="button" className="primary" onClick={apply} disabled={!dirty || loading || applying}>
            {applying ? "Applying" : "Apply"}
          </button>
        </div>
      </div>

      {error ? <div className="feature-management-error">{error}</div> : null}
      {status ? <div className="feature-management-status">{status}</div> : null}

      <div className="feature-management-layout">
        <div className="feature-management-list" aria-busy={loading}>
          {loading && draft.length === 0 ? <p className="feature-management-muted">Loading features...</p> : null}
          {!loading && draft.length === 0 ? <p className="feature-management-muted">No features are available.</p> : null}
          {draft.map(feature => (
            <button
              key={feature.id}
              type="button"
              className={feature.id === selectedFeature?.id ? "feature-management-row selected" : "feature-management-row"}
              onClick={() => setSelectedId(feature.id)}
            >
              <span className={feature.enabled ? "feature-management-switch enabled" : "feature-management-switch"} onClick={event => {
                event.stopPropagation();
                toggleFeature(feature);
              }} role="switch" aria-checked={feature.enabled} tabIndex={0} />
              <span>
                <strong>{feature.displayName || feature.id}</strong>
                <code>{feature.id}</code>
              </span>
              <em>{feature.settings.length}</em>
            </button>
          ))}
        </div>

        <FeatureInspector
          feature={selectedFeature}
          disabled={applying || loading}
          onToggle={toggleFeature}
          onSettingChange={updateSetting}
        />
      </div>
    </section>
  );
}

function FeatureInspector({
  feature,
  disabled,
  onToggle,
  onSettingChange
}: {
  feature: DraftFeature | null;
  disabled: boolean;
  onToggle(feature: DraftFeature): void;
  onSettingChange(feature: DraftFeature, setting: StudioSettingDescriptor, value: unknown): void;
}) {
  const editors = useMemo(() => moduleApi.settingEditors.list(), []);

  if (!feature) {
    return <aside className="feature-management-inspector"><p className="feature-management-muted">Select a feature.</p></aside>;
  }

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
      {feature.packageId ? <p className="feature-management-muted">{feature.packageId} {feature.packageVersion}</p> : null}

      {feature.settings.length === 0 ? (
        <p className="feature-management-muted">No configurable settings.</p>
      ) : (
        <div className="feature-management-settings">
          {feature.settings.map(setting => {
            const editor = editors
              .filter(candidate => candidate.supports(setting))
              .sort((a, b) => (a.order ?? 500) - (b.order ?? 500))[0];
            const Editor = editor?.component ?? TextSettingEditor;

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
      )}
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
  return error instanceof Error ? error.message : String(error);
}
