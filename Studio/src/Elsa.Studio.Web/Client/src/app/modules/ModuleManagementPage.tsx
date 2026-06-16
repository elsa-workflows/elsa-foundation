import React, { useEffect, useMemo, useState } from "react";
import { Boxes, RefreshCcw } from "lucide-react";
import type {
  ElsaStudioModuleApi,
  StudioModuleDiagnostic,
  StudioModuleRegistryItem,
  StudioModuleRegistryResponse
} from "../../sdk";
import { EmptyState, StatusChip, StudioDataGrid, StudioToolbar, StudioToolbarGroup } from "../ui";

type ModuleRegistryState = "loading" | "ready" | "failed";

export function ModuleManagementPage({ api }: { api: ElsaStudioModuleApi }) {
  const [state, setState] = useState<ModuleRegistryState>("loading");
  const [registry, setRegistry] = useState<StudioModuleRegistryResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selectedId, setSelectedId] = useState("");
  const runtimeDiagnostics = useMemo(() => api.diagnostics.list(), [api]);
  const modules = useMemo(
    () => mergeRuntimeDiagnostics(registry?.modules ?? [], runtimeDiagnostics),
    [registry, runtimeDiagnostics]);
  const selectedModule = modules.find(module => module.id === selectedId) ?? modules[0] ?? null;
  const summary = useMemo(() => getModuleSummary(modules), [modules]);

  useEffect(() => {
    void refresh();
  }, []);

  useEffect(() => {
    if (selectedModule && selectedModule.id !== selectedId) {
      setSelectedId(selectedModule.id);
    }
  }, [selectedId, selectedModule]);

  async function refresh() {
    setState("loading");
    setError(null);
    try {
      const response = await api.host.http.getJson<StudioModuleRegistryResponse>("/_elsa/studio/module-registry");
      setRegistry(response);
      setState("ready");
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      setState("failed");
    }
  }

  return (
    <section className="modules-page">
      <div className="section-header modules-header">
        <div>
          <h2>Modules</h2>
          <p>{summary.loaded} loaded, {summary.available} available, {summary.failed} failed, {summary.disabled} disabled</p>
        </div>
        <StudioToolbar>
          <StudioToolbarGroup>
            <button type="button" className="studio-button" onClick={refresh} disabled={state === "loading"}>
              <RefreshCcw size={15} />
              {state === "loading" ? "Refreshing" : "Refresh"}
            </button>
          </StudioToolbarGroup>
        </StudioToolbar>
      </div>

      {error ? <div className="studio-alert" data-tone="danger">{error}</div> : null}

      <div className="modules-summary-strip">
        <SummaryItem label="Total" value={modules.length} />
        <SummaryItem label="Loaded" value={summary.loaded} />
        <SummaryItem label="Full-stack" value={summary.fullStack} />
        <SummaryItem label="Backend-aware" value={summary.backendAware} />
        <SummaryItem label="Attention" value={summary.attention} />
      </div>

      {state === "ready" && modules.length === 0 ? (
        <EmptyState icon={<Boxes size={22} />}>No Studio modules are registered.</EmptyState>
      ) : (
        <div className="modules-workbench">
          <div className="modules-grid-panel">
            <StudioDataGrid
              columns={moduleColumns}
              items={modules}
              getKey={module => module.id}
              selectedKey={selectedModule?.id}
              onSelect={module => setSelectedId(module.id)}
              gridColumns="minmax(260px, 1.6fr) 96px 120px 120px 90px 100px"
            />
          </div>
          <ModuleInspector module={selectedModule} />
        </div>
      )}
    </section>
  );
}

function SummaryItem({ label, value }: { label: string; value: number }) {
  return (
    <div className="modules-summary-item">
      <strong>{value}</strong>
      <span>{label}</span>
    </div>
  );
}

function ModuleInspector({ module }: { module: StudioModuleRegistryItem | null }) {
  if (!module) {
    return (
      <aside className="modules-inspector">
        <p className="modules-muted">Select a module.</p>
      </aside>
    );
  }

  return (
    <aside className="modules-inspector">
      <div className="modules-inspector-heading">
        <div>
          <span>{module.sourceKind} / {module.scope}</span>
          <h3>{module.displayName || module.id}</h3>
          <code>{module.id}</code>
        </div>
        <StatusChip tone={statusTone(module.status)}>{module.status}</StatusChip>
      </div>

      <dl className="modules-metadata">
        <div><dt>Version</dt><dd>{module.version}</dd></div>
        <div><dt>Host</dt><dd>{module.requiredHostVersion}</dd></div>
        <div><dt>SDK</dt><dd>{module.requiredSdkVersion}</dd></div>
        <div><dt>Compatibility</dt><dd><StatusChip tone={compatibilityTone(module.compatibility)}>{module.compatibility}</StatusChip></dd></div>
      </dl>

      <div className="modules-inspector-section">
        <h4>Contributions</h4>
        <div className="modules-contribution-list">
          {module.contributions.length === 0 ? <p className="modules-muted">No contributions reported.</p> : null}
          {module.contributions.map(contribution => (
            <div key={contribution.id} className="modules-contribution-row">
              <span>{contribution.label}</span>
              <code>{contribution.type}</code>
            </div>
          ))}
        </div>
      </div>

      <div className="modules-inspector-section">
        <h4>Manifest</h4>
        <p className="modules-manifest-path">{module.manifest.entry}</p>
        <div className="modules-chip-list">
          {module.manifest.capabilities.map(capability => <StatusChip key={capability}>{capability}</StatusChip>)}
        </div>
      </div>

      <div className="modules-inspector-section">
        <h4>Diagnostics</h4>
        <div className="modules-diagnostics-list">
          {module.diagnostics.map((diagnostic, index) => (
            <div key={`${diagnostic.moduleId}-${diagnostic.status}-${index}`} className="modules-diagnostic-row">
              <StatusChip tone={statusTone(diagnostic.status)}>{diagnostic.status}</StatusChip>
              <span>{diagnostic.reason}</span>
            </div>
          ))}
        </div>
      </div>
    </aside>
  );
}

const moduleColumns = [
  {
    id: "module",
    header: "Module",
    render: (module: StudioModuleRegistryItem) => (
      <span className="modules-name-cell">
        <strong>{module.displayName || module.id}</strong>
        <code>{module.id}</code>
      </span>
    )
  },
  {
    id: "scope",
    header: "Scope",
    render: (module: StudioModuleRegistryItem) => module.scope
  },
  {
    id: "source",
    header: "Source",
    render: (module: StudioModuleRegistryItem) => module.sourceKind
  },
  {
    id: "status",
    header: "Status",
    render: (module: StudioModuleRegistryItem) => <StatusChip tone={statusTone(module.status)}>{module.status}</StatusChip>
  },
  {
    id: "version",
    header: "Version",
    render: (module: StudioModuleRegistryItem) => module.version
  },
  {
    id: "contributions",
    header: "Contrib.",
    render: (module: StudioModuleRegistryItem) => module.contributions.length
  }
];

function mergeRuntimeDiagnostics(
  modules: StudioModuleRegistryItem[],
  runtimeDiagnostics: StudioModuleDiagnostic[]
): StudioModuleRegistryItem[] {
  return modules.map(module => {
    const diagnostics = [
      ...module.diagnostics,
      ...runtimeDiagnostics.filter(diagnostic => diagnostic.moduleId === module.id)
    ];
    const runtimeStatus = getDominantRuntimeStatus(diagnostics) ?? module.status;

    return {
      ...module,
      status: runtimeStatus,
      diagnostics
    };
  });
}

function getDominantRuntimeStatus(diagnostics: StudioModuleDiagnostic[]) {
  if (diagnostics.some(x => x.status === "failed")) return "failed";
  if (diagnostics.some(x => x.status === "incompatible")) return "incompatible";
  if (diagnostics.some(x => x.status === "loaded")) return "loaded";
  if (diagnostics.some(x => x.status === "disabled")) return "disabled";
  if (diagnostics.some(x => x.status === "available")) return "available";
  return null;
}

function getModuleSummary(modules: StudioModuleRegistryItem[]) {
  return {
    loaded: modules.filter(module => module.status === "loaded").length,
    available: modules.filter(module => module.status === "available").length,
    failed: modules.filter(module => module.status === "failed").length,
    disabled: modules.filter(module => module.status === "disabled").length,
    fullStack: modules.filter(module => module.scope === "full-stack").length,
    backendAware: modules.filter(module => module.scope === "backend" || module.scope === "full-stack").length,
    attention: modules.filter(module => ["failed", "incompatible", "disabled"].includes(module.status)).length
  };
}

function statusTone(status: string) {
  if (status === "loaded" || status === "available") return "success";
  if (status === "disabled" || status === "pending") return "warning";
  if (status === "failed" || status === "incompatible") return "danger";
  return "neutral";
}

function compatibilityTone(compatibility: string) {
  if (compatibility === "compatible") return "success";
  if (compatibility === "warning" || compatibility === "unknown") return "warning";
  if (compatibility === "incompatible") return "danger";
  return "neutral";
}
