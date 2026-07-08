import React, { useEffect, useMemo, useRef, useState } from "react";
import { Check, Copy, Download, FileUp, ImagePlus, Paintbrush, RefreshCcw, Save, Trash2 } from "lucide-react";
import type { ElsaStudioModuleApi } from "../../sdk";
import { EmptyState, StudioAlert, StudioTabs, StudioToolbar, StudioToolbarGroup } from "../ui";
import {
  cloneThemeDefinition,
  themeTokenNames,
  toTheme,
  type StudioThemeDefinition,
  type ThemeMode
} from "../themes/presets";
import {
  createCustomThemeFrom,
  deleteTheme,
  deleteThemeAsset,
  duplicateTheme,
  fetchThemeStore,
  importThemePack,
  saveTheme,
  setDefaultTheme,
  uploadThemeAsset,
  validateThemeDefinition,
  type ThemeStoreResponse,
  type ThemeValidationIssue
} from "../themes/themeStoreApi";
import { useTheme } from "../components/ThemeProvider";

type BuilderTab = "overview" | "tokens" | "assets" | "preview";

const modeTabs = [
  { id: "light", label: "Light" },
  { id: "dark", label: "Dark" }
];

const builderTabs = [
  { id: "overview", label: "Overview" },
  { id: "tokens", label: "Tokens" },
  { id: "assets", label: "Assets" },
  { id: "preview", label: "Preview" }
];

export function ThemeBuilderPage({ api }: { api: ElsaStudioModuleApi }) {
  const { previewTheme, refreshThemes } = useTheme();
  const [store, setStore] = useState<ThemeStoreResponse | null>(null);
  const [selectedThemeId, setSelectedThemeId] = useState("");
  const [draft, setDraft] = useState<StudioThemeDefinition | null>(null);
  const [tab, setTab] = useState<BuilderTab>("overview");
  const [mode, setMode] = useState<ThemeMode>("light");
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const assetInputRef = useRef<HTMLInputElement>(null);
  const importInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    void loadStore();
  }, []);

  useEffect(() => {
    if (!store) return;
    const selected = store.themes.find(theme => theme.id === selectedThemeId) ?? store.themes[0] ?? null;
    setSelectedThemeId(selected?.id ?? "");
    setDraft(selected ? cloneThemeDefinition(selected) : null);
  }, [store, selectedThemeId]);

  const selectedTheme = store?.themes.find(theme => theme.id === selectedThemeId) ?? null;
  const validation = useMemo(() => draft ? validateThemeDefinition(draft) : { valid: false, issues: [] }, [draft]);
  const isReadOnly = draft?.source === "built-in";
  const hasChanges = JSON.stringify(selectedTheme) !== JSON.stringify(draft);

  async function loadStore() {
    await run(async () => {
      const nextStore = await fetchThemeStore(api.host);
      setStore(nextStore);
      setSelectedThemeId(current => current || nextStore.defaultThemeId || nextStore.themes[0]?.id || "");
    }, "Theme Store refreshed.");
  }

  async function persistDraft(nextDraft = draft) {
    if (!nextDraft) return;
    if (nextDraft.source === "built-in") {
      setError("Built-in themes are read-only. Duplicate the theme before editing.");
      return;
    }

    const result = validateThemeDefinition(nextDraft);
    if (!result.valid) {
      setError(result.issues.find(issue => issue.severity === "error")?.message ?? "Theme validation failed.");
      return;
    }

    await run(async () => {
      const nextStore = await saveTheme(api.host, nextDraft);
      setStore(nextStore);
      setSelectedThemeId(nextDraft.id);
      await refreshThemes();
      window.dispatchEvent(new Event("elsa-studio:theme-store-changed"));
    }, "Theme saved.");
  }

  async function duplicateSelected() {
    if (!draft) return;
    await run(async () => {
      const duplicate = draft.source === "built-in"
        ? await saveBuiltInDuplicate(draft)
        : await duplicateTheme(api.host, draft.id, `${draft.name} Copy`);
      const nextStore = await fetchThemeStore(api.host);
      setStore(nextStore);
      setSelectedThemeId(duplicate.id);
      await refreshThemes();
    }, "Theme duplicated as an editable custom theme.");
  }

  async function saveBuiltInDuplicate(theme: StudioThemeDefinition) {
    const nextStore = await fetchThemeStore(api.host);
    const duplicate = createCustomThemeFrom(theme, uniqueThemeId(slugify(`${theme.name} Copy`), nextStore.themes), `${theme.name} Copy`);
    await saveTheme(api.host, duplicate);
    return duplicate;
  }

  async function removeSelected() {
    if (!draft || draft.source === "built-in") return;
    if (!(await api.dialogs.confirm({
      title: "Delete theme",
      message: `Delete "${draft.name}" from the Theme Store?`,
      confirmLabel: "Delete theme",
      tone: "danger"
    }))) {
      return;
    }

    await run(async () => {
      const nextStore = await deleteTheme(api.host, draft.id);
      setStore(nextStore);
      setSelectedThemeId(nextStore.defaultThemeId);
      await refreshThemes();
      window.dispatchEvent(new Event("elsa-studio:theme-store-changed"));
    }, "Theme deleted.");
  }

  async function makeDefault() {
    if (!draft) return;
    await run(async () => {
      const nextStore = await setDefaultTheme(api.host, draft.id);
      setStore(nextStore);
      await refreshThemes();
    }, "Default theme updated.");
  }

  async function applyPreview() {
    if (!draft || !validation.valid) return;
    previewTheme(draft);
    setStatus("Preview applied.");
  }

  async function uploadAsset(file: File) {
    await run(async () => {
      await uploadThemeAsset(api.host, file);
      setStore(await fetchThemeStore(api.host));
    }, "Material texture asset uploaded.");
  }

  async function removeAsset(assetId: string) {
    await run(async () => {
      setStore(await deleteThemeAsset(api.host, assetId));
    }, "Material texture asset deleted.");
  }

  async function exportSelected() {
    if (!draft) return;
    await run(async () => {
      const pack = { version: 1, themes: [draft] };
      const blob = new Blob([JSON.stringify(pack, null, 2)], { type: "application/json" });
      const url = URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = url;
      link.download = `${draft.id}-theme-pack.json`;
      link.click();
      URL.revokeObjectURL(url);
    }, "Theme pack exported.");
  }

  async function importSelectedFile(file: File) {
    await run(async () => {
      const pack = JSON.parse(await file.text());
      const nextStore = await importThemePack(api.host, pack);
      setStore(nextStore);
      await refreshThemes();
      window.dispatchEvent(new Event("elsa-studio:theme-store-changed"));
    }, "Theme pack imported.");
  }

  async function run(operation: () => Promise<void>, success: string) {
    setError(null);
    setStatus(null);
    try {
      await operation();
      setStatus(success);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  }

  function patchDraft(patch: Partial<StudioThemeDefinition>) {
    setDraft(current => current ? { ...current, ...patch } : current);
  }

  function patchMode(token: string, value: string) {
    setDraft(current => current ? {
      ...current,
      modes: {
        ...current.modes,
        [mode]: {
          ...current.modes[mode],
          [token]: value
        }
      }
    } : current);
  }

  if (!store || !draft) {
    return (
      <section className="theme-builder-page">
        <div className="section-header">
          <div>
            <h2>Theme Builder</h2>
            <p>Loading Theme Store definitions.</p>
          </div>
        </div>
        <EmptyState icon={<Paintbrush size={22} />}>Loading themes...</EmptyState>
      </section>
    );
  }

  return (
    <section className="theme-builder-page">
      <div className="section-header">
        <div>
          <h2>Theme Builder</h2>
          <p>{store.themes.length} definitions, {store.assets.length} material texture assets, default {store.defaultThemeId}</p>
        </div>
        <StudioToolbar>
          <StudioToolbarGroup>
            <button type="button" className="studio-button" onClick={() => void loadStore()}>
              <RefreshCcw size={15} /> Refresh
            </button>
            <button type="button" className="studio-button" onClick={() => importInputRef.current?.click()}>
              <FileUp size={15} /> Import
            </button>
          </StudioToolbarGroup>
        </StudioToolbar>
      </div>

      {error ? <StudioAlert tone="danger">{error}</StudioAlert> : null}
      {status ? <StudioAlert tone="success">{status}</StudioAlert> : null}

      <div className="theme-builder-workbench">
        <aside className="theme-builder-list" aria-label="Themes">
          {store.themes.map(theme => (
            <button
              key={theme.id}
              type="button"
              className={theme.id === draft.id ? "active" : ""}
              onClick={() => setSelectedThemeId(theme.id)}
            >
              <span className="theme-builder-swatch" aria-hidden="true">
                <span style={{ backgroundColor: theme.modes.light.primary }} />
                <span style={{ backgroundColor: theme.modes.dark.primary }} />
              </span>
              <span>
                <strong>{theme.name}</strong>
                <small>{theme.source} · {theme.published ? "published" : "draft"} · {theme.enabled ? "enabled" : "disabled"}</small>
              </span>
            </button>
          ))}
        </aside>

        <div className="theme-builder-detail">
          <div className="theme-builder-actions">
            <div>
              <h3>{draft.name}</h3>
              <p>{isReadOnly ? "Built-in themes are read-only seeds. Duplicate to customize." : "Custom theme stored by the Studio host."}</p>
            </div>
            <div>
              <button type="button" className="studio-button" onClick={() => void duplicateSelected()}><Copy size={15} /> Duplicate</button>
              <button type="button" className="studio-button" onClick={() => void exportSelected()}><Download size={15} /> Export</button>
              <button type="button" className="studio-button" onClick={() => void makeDefault()} disabled={!draft.enabled || !draft.published}><Check size={15} /> Default</button>
              <button type="button" className="studio-button" onClick={() => void persistDraft()} disabled={isReadOnly || !hasChanges}><Save size={15} /> Save</button>
              <button type="button" className="studio-button danger" onClick={() => void removeSelected()} disabled={isReadOnly}><Trash2 size={15} /> Delete</button>
            </div>
          </div>

          <StudioTabs tabs={builderTabs} activeTab={tab} onSelect={id => setTab(id as BuilderTab)} ariaLabel="Theme Builder views" />

          {tab === "overview" ? (
            <OverviewEditor draft={draft} readOnly={isReadOnly} validationIssues={validation.issues} onPatch={patchDraft} />
          ) : null}

          {tab === "tokens" ? (
            <div className="theme-token-editor">
              <StudioTabs tabs={modeTabs} activeTab={mode} onSelect={id => setMode(id as ThemeMode)} ariaLabel="Theme mode" />
              <div className="theme-token-grid">
                {themeTokenNames.map(token => (
                  <label key={token} className="theme-token-field">
                    <span>{token}</span>
                    <input value={String(draft.modes[mode][token])} disabled={isReadOnly} onChange={event => patchMode(token, event.target.value)} />
                  </label>
                ))}
              </div>
            </div>
          ) : null}

          {tab === "assets" ? (
            <div className="theme-assets-panel">
              <input
                ref={assetInputRef}
                className="modules-hidden-input"
                type="file"
                accept="image/png,image/webp,image/jpeg"
                onChange={event => {
                  const file = event.currentTarget.files?.[0];
                  if (file) void uploadAsset(file);
                  event.currentTarget.value = "";
                }}
              />
              <button type="button" className="theme-upload-dropzone" onClick={() => assetInputRef.current?.click()}>
                <ImagePlus size={20} /> Upload tiled material texture
              </button>
              <div className="theme-assets-grid">
                {store.assets.map(asset => (
                  <div className="theme-asset-row" key={asset.id}>
                    <span>{asset.fileName}</span>
                    <small>{asset.contentType} · {formatBytes(asset.size)}</small>
                    <button type="button" className="studio-button danger" onClick={() => void removeAsset(asset.id)}><Trash2 size={14} /> Delete</button>
                  </div>
                ))}
              </div>
            </div>
          ) : null}

          {tab === "preview" ? (
            <ThemePreview theme={draft} mode={mode} onApply={() => void applyPreview()} validationIssues={validation.issues} />
          ) : null}
        </div>
      </div>

      <input
        ref={importInputRef}
        className="modules-hidden-input"
        type="file"
        accept="application/json"
        onChange={event => {
          const file = event.currentTarget.files?.[0];
          if (file) void importSelectedFile(file);
          event.currentTarget.value = "";
        }}
      />
    </section>
  );
}

function OverviewEditor({
  draft,
  readOnly,
  validationIssues,
  onPatch
}: {
  draft: StudioThemeDefinition;
  readOnly: boolean;
  validationIssues: ThemeValidationIssue[];
  onPatch: (patch: Partial<StudioThemeDefinition>) => void;
}) {
  return (
    <div className="theme-overview-editor">
      <label>
        <span>Name</span>
        <input value={draft.name} disabled={readOnly} onChange={event => onPatch({ name: event.target.value })} />
      </label>
      <label>
        <span>Description</span>
        <textarea value={draft.description ?? ""} disabled={readOnly} onChange={event => onPatch({ description: event.target.value })} />
      </label>
      <div className="theme-toggle-row">
        <label><input type="checkbox" checked={draft.published} disabled={readOnly} onChange={event => onPatch({ published: event.target.checked })} /> Published</label>
        <label><input type="checkbox" checked={draft.enabled} disabled={readOnly} onChange={event => onPatch({ enabled: event.target.checked })} /> Enabled</label>
      </div>
      <ValidationList issues={validationIssues} />
    </div>
  );
}

function ThemePreview({
  theme,
  mode,
  validationIssues,
  onApply
}: {
  theme: StudioThemeDefinition;
  mode: ThemeMode;
  validationIssues: ThemeValidationIssue[];
  onApply: () => void;
}) {
  const colors = toTheme(theme)[mode];
  const previewStyle = {
    "--preview-primary": colors.primary,
    "--preview-background": colors.background,
    "--preview-foreground": colors.foreground,
    "--preview-card": colors.card,
    "--preview-border": colors.border,
    "--preview-muted": colors.muted
  } as React.CSSProperties;

  return (
    <div className="theme-preview-panel">
      <div className="theme-preview-surface" style={previewStyle}>
        <nav>
          <span>Dashboard</span>
          <span>Workflows</span>
          <span>Weaver</span>
        </nav>
        <section>
          <div className="theme-preview-card">
            <strong>Workflow health</strong>
            <p>12 active workflows · 2 incidents · 98% success</p>
          </div>
          <div className="theme-preview-node">HTTP Endpoint</div>
          <div className="theme-preview-node secondary">Send Email</div>
          <div className="theme-preview-weaver">Weaver can inspect this draft and suggest safe changes.</div>
        </section>
      </div>
      <ValidationList issues={validationIssues} />
      <button type="button" className="studio-button" onClick={onApply} disabled={validationIssues.some(issue => issue.severity === "error")}>
        <Paintbrush size={15} /> Apply preview
      </button>
    </div>
  );
}

function ValidationList({ issues }: { issues: ThemeValidationIssue[] }) {
  if (issues.length === 0) {
    return <StudioAlert tone="success">Validation passed.</StudioAlert>;
  }

  return (
    <div className="theme-validation-list">
      {issues.map((issue, index) => (
        <StudioAlert key={`${issue.path}-${index}`} tone={issue.severity === "error" ? "danger" : "warning"}>
          {issue.path}: {issue.message}
        </StudioAlert>
      ))}
    </div>
  );
}

function formatBytes(bytes: number) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

function slugify(value: string) {
  return value.trim().toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "") || "custom-theme";
}

function uniqueThemeId(baseId: string, themes: StudioThemeDefinition[]) {
  let candidate = baseId;
  let index = 2;
  while (themes.some(theme => theme.id === candidate)) {
    candidate = `${baseId}-${index++}`;
  }
  return candidate;
}
