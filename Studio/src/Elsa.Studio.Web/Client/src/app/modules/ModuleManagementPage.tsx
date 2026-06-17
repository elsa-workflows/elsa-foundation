import React, { useEffect, useMemo, useRef, useState } from "react";
import { Boxes, CloudUpload, RefreshCcw, Trash2, X } from "lucide-react";
import type { ElsaStudioModuleApi } from "../../sdk";
import { EmptyState, StatusChip, StudioDataGrid, StudioTabs, StudioToolbar, StudioToolbarGroup } from "../ui";
import {
  createModuleManagementHosts,
  deleteDropFolderPackage,
  formatFileSize,
  getErrorMessage,
  hostTabs,
  uploadPackage,
  type HostId,
  type HostModel,
  type ModuleManagementDropFolderPackage,
  type ModuleManagementPackageManifest,
  type ModuleManagementModule,
  type ModuleManagementPackage,
  type ModuleManagementRegistryResponse,
  type ModuleManagementStudioManifest,
  type RegistryState
} from "./moduleManagementApi";

type InspectorTab = "overview" | "contributions" | "package" | "manifest" | "diagnostics";

interface ModuleRow {
  key: string;
  kind: "module" | "package" | "drop-folder";
  id: string;
  displayName: string;
  surface: string;
  runtime: string;
  sourceKind: string;
  scope: string;
  version: string;
  status: string;
  packageId?: string | null;
  packageVersion?: string | null;
  module?: ModuleManagementModule;
  package?: ModuleManagementPackage;
  dropFolderPackage?: ModuleManagementDropFolderPackage;
}

const inspectorTabs = [
  { id: "overview", label: "Overview" },
  { id: "contributions", label: "Contributions" },
  { id: "package", label: "Package" },
  { id: "manifest", label: "Manifest" },
  { id: "diagnostics", label: "Diagnostics" }
];

export function ModuleManagementPage({ api }: { api: ElsaStudioModuleApi }) {
  const hosts = useMemo<HostModel[]>(() => createModuleManagementHosts(api), [api]);
  const [activeHostId, setActiveHostId] = useState<HostId>("studio");
  const [stateByHost, setStateByHost] = useState<Record<HostId, HostRegistryState>>(() => ({
    studio: createInitialHostState(),
    server: createInitialHostState()
  }));
  const uploadInputRef = useRef<HTMLInputElement>(null);
  const [uploadDialogOpen, setUploadDialogOpen] = useState(false);
  const activeHost = hosts.find(host => host.id === activeHostId) ?? hosts[0];
  const activeState = stateByHost[activeHost.id];
  const rows = useMemo(() => buildRows(activeState.registry), [activeState.registry]);
  const selectedRow = rows.find(row => row.key === activeState.selectedKey) ?? rows[0] ?? null;
  const summary = useMemo(() => getSummary(activeState.registry, rows), [activeState.registry, rows]);

  useEffect(() => {
    void refreshHost("studio");
    void refreshHost("server");
  }, []);

  useEffect(() => {
    if (selectedRow && selectedRow.key !== activeState.selectedKey) {
      patchHostState(activeHost.id, { selectedKey: selectedRow.key });
    }
  }, [activeHost.id, activeState.selectedKey, selectedRow?.key]);

  async function refreshHost(hostId: HostId) {
    const host = hosts.find(item => item.id === hostId);
    if (!host) return;

    patchHostState(hostId, { state: "loading", error: null });
    try {
      const registry = await host.context.http.getJson<ModuleManagementRegistryResponse>("/_elsa/module-management/registry");
      patchHostState(hostId, { state: "ready", registry, status: null, error: null });
    } catch (e) {
      patchHostState(hostId, { state: "failed", error: getErrorMessage(e) });
    }
  }

  function patchHostState(hostId: HostId, patch: Partial<HostRegistryState>) {
    setStateByHost(current => ({
      ...current,
      [hostId]: { ...current[hostId], ...patch }
    }));
  }

  async function runHostOperation<T>(operation: () => Promise<T>, success: string) {
    patchHostState(activeHost.id, { operationBusy: true, error: null, status: null });
    try {
      await operation();
      patchHostState(activeHost.id, { status: success });
      await refreshHost(activeHost.id);
    } catch (e) {
      patchHostState(activeHost.id, { error: getErrorMessage(e) });
    } finally {
      patchHostState(activeHost.id, { operationBusy: false });
    }
  }

  return (
    <section className="modules-page">
      <div className="section-header modules-header">
        <div>
          <h2>Modules</h2>
          <p>{activeHost.label}: {summary.loaded} loaded, {summary.available} available, {summary.failed} failed, {summary.packages} package(s)</p>
        </div>
        <StudioToolbar>
          <StudioToolbarGroup>
            <button type="button" className="studio-button" onClick={() => setUploadDialogOpen(true)} disabled={!activeState.registry}>
              <CloudUpload size={15} />
              Upload Packages
            </button>
            <button type="button" className="studio-button" onClick={() => refreshHost(activeHost.id)} disabled={activeState.state === "loading"}>
              <RefreshCcw size={15} />
              {activeState.state === "loading" ? "Refreshing" : "Refresh"}
            </button>
          </StudioToolbarGroup>
        </StudioToolbar>
      </div>

      <StudioTabs tabs={hostTabs} activeTab={activeHostId} onSelect={tabId => setActiveHostId(tabId as HostId)} />

      {activeState.error ? <div className="studio-alert" data-tone="danger">{activeState.error}</div> : null}
      {activeState.status ? <div className="studio-alert" data-tone="success">{activeState.status}</div> : null}

      <div className="modules-summary-strip">
        <SummaryItem label="Rows" value={rows.length} />
        <SummaryItem label="Loaded" value={summary.loaded} />
        <SummaryItem label="Packages" value={summary.packages} />
        <SummaryItem label="Feeds" value={summary.feeds} />
        <SummaryItem label="Attention" value={summary.attention} />
      </div>

      {uploadDialogOpen && activeState.registry ? (
        <UploadPackagesDialog
          host={activeHost}
          registry={activeState.registry}
          busy={activeState.operationBusy}
          uploadInputRef={uploadInputRef}
          onClose={() => setUploadDialogOpen(false)}
          onUpload={file => runHostOperation(() => uploadPackage(activeHost.context, file), `Uploaded ${file.name} to ${activeHost.label}. Reconcile completed; reload may be required.`)}
          onDeleteDropFolderPackage={fileName => runHostOperation(() => deleteDropFolderPackage(activeHost.context, fileName), `Deleted ${fileName} from ${activeHost.label}. Reconcile completed; reload may be required.`)}
        />
      ) : null}

      {activeState.state === "ready" && rows.length === 0 ? (
        <EmptyState icon={<Boxes size={22} />}>No modules or packages are reported for {activeHost.label}.</EmptyState>
      ) : (
        <div className="modules-workbench">
          <div className="modules-grid-panel">
            <StudioDataGrid
              columns={moduleColumns}
              items={rows}
              getKey={row => row.key}
              selectedKey={selectedRow?.key}
              onSelect={row => patchHostState(activeHost.id, { selectedKey: row.key })}
              gridColumns="minmax(260px, 1.5fr) 90px 110px 120px 90px 120px"
            />
          </div>
          <ModuleInspector
            host={activeHost}
            registry={activeState.registry}
            selectedRow={selectedRow}
            activeTab={activeState.inspectorTab}
            onSelectTab={tab => patchHostState(activeHost.id, { inspectorTab: tab })}
          />
        </div>
      )}
    </section>
  );
}

interface HostRegistryState {
  state: RegistryState;
  registry: ModuleManagementRegistryResponse | null;
  error: string | null;
  status: string | null;
  selectedKey: string;
  inspectorTab: InspectorTab;
  operationBusy: boolean;
}

function createInitialHostState(): HostRegistryState {
  return {
    state: "loading",
    registry: null,
    error: null,
    status: null,
    selectedKey: "",
    inspectorTab: "overview",
    operationBusy: false
  };
}

function SummaryItem({ label, value }: { label: string; value: number }) {
  return (
    <div className="modules-summary-item">
      <strong>{value}</strong>
      <span>{label}</span>
    </div>
  );
}

function UploadPackagesDialog({
  host,
  registry,
  busy,
  uploadInputRef,
  onClose,
  onUpload,
  onDeleteDropFolderPackage
}: {
  host: HostModel;
  registry: ModuleManagementRegistryResponse;
  busy: boolean;
  uploadInputRef: React.RefObject<HTMLInputElement>;
  onClose(): void;
  onUpload(file: File): void;
  onDeleteDropFolderPackage(fileName: string): void;
}) {
  return (
    <div className="modules-dialog-backdrop" role="presentation">
      <section className="modules-upload-dialog" role="dialog" aria-modal="true" aria-labelledby="modules-upload-dialog-title">
        <div className="modules-upload-dialog-heading">
          <div>
            <span>{host.label} host</span>
            <h3 id="modules-upload-dialog-title">Upload Packages</h3>
            <p>Upload .nupkg files to this host's Nuplane drop folder.</p>
          </div>
          <button type="button" className="studio-icon-button" aria-label="Close upload packages" onClick={onClose}>
            <X size={14} />
          </button>
        </div>

        <div className="modules-upload-dialog-actions">
          <button type="button" className="studio-button" disabled={busy || !registry.capabilities.canUploadPackages} onClick={() => uploadInputRef.current?.click()}>
            <CloudUpload size={15} />
            Upload .nupkg
          </button>
          <input
            ref={uploadInputRef}
            type="file"
            accept=".nupkg"
            className="modules-hidden-input"
            onChange={event => {
              const file = event.target.files?.[0];
              if (file) onUpload(file);
              event.currentTarget.value = "";
            }}
          />
        </div>

        <div className="modules-drop-folder-list" aria-label={`${host.label} drop-folder packages`}>
          {registry.dropFolderPackages.length === 0 ? <p className="modules-muted">No drop-folder packages for this host.</p> : null}
          {registry.dropFolderPackages.map(file => (
            <div key={file.fileName} className="modules-drop-folder-row">
              <span>
                <strong>{file.fileName}</strong>
                <code>{formatFileSize(file.size)} · {new Date(file.lastWriteTimeUtc).toLocaleString()}</code>
              </span>
              <button type="button" className="studio-icon-button" disabled={busy} title={`Delete ${file.fileName} from ${host.label} drop folder`} onClick={() => onDeleteDropFolderPackage(file.fileName)}>
                <Trash2 size={14} />
              </button>
            </div>
          ))}
        </div>
      </section>
    </div>
  );
}

function ModuleInspector({
  host,
  registry,
  selectedRow,
  activeTab,
  onSelectTab
}: {
  host: HostModel;
  registry: ModuleManagementRegistryResponse | null;
  selectedRow: ModuleRow | null;
  activeTab: InspectorTab;
  onSelectTab(tab: InspectorTab): void;
}) {
  if (!registry) {
    return (
      <aside className="modules-inspector">
        <p className="modules-muted">Loading {host.label} modules.</p>
      </aside>
    );
  }

  return (
    <aside className="modules-inspector">
      <div className="modules-inspector-heading">
        <div>
          <span>{host.label} / {host.runtime}</span>
          <h3>{selectedRow?.displayName ?? registry.host.displayName}</h3>
          <code>{selectedRow?.id ?? registry.host.id}</code>
        </div>
        <StatusChip tone={statusTone(selectedRow?.status ?? "available")}>{selectedRow?.status ?? "available"}</StatusChip>
      </div>

      <StudioTabs tabs={inspectorTabs} activeTab={activeTab} onSelect={tab => onSelectTab(tab as InspectorTab)} />

      {activeTab === "overview" ? <OverviewTab host={host} registry={registry} selectedRow={selectedRow} /> : null}
      {activeTab === "contributions" ? <ContributionsTab selectedRow={selectedRow} /> : null}
      {activeTab === "package" ? <PackageTab registry={registry} selectedRow={selectedRow} /> : null}
      {activeTab === "manifest" ? <ManifestTab registry={registry} selectedRow={selectedRow} /> : null}
      {activeTab === "diagnostics" ? <DiagnosticsTab registry={registry} selectedRow={selectedRow} /> : null}
    </aside>
  );
}

function OverviewTab({ host, registry, selectedRow }: { host: HostModel; registry: ModuleManagementRegistryResponse; selectedRow: ModuleRow | null }) {
  return (
    <div className="modules-inspector-section">
      <h4>Overview</h4>
      <dl className="modules-metadata">
        <div><dt>Surface</dt><dd>{selectedRow?.surface ?? host.label}</dd></div>
        <div><dt>Runtime</dt><dd>{selectedRow?.runtime ?? registry.host.runtime}</dd></div>
        <div><dt>Scope</dt><dd>{selectedRow?.scope ?? "host"}</dd></div>
        <div><dt>Source</dt><dd>{selectedRow?.sourceKind ?? "cshells-host"}</dd></div>
        <div><dt>Feed</dt><dd>{selectedRow?.package?.feedName ?? "n/a"}</dd></div>
        <div><dt>Package</dt><dd>{selectedRow?.packageId ?? "n/a"}</dd></div>
        <div><dt>Version</dt><dd>{selectedRow?.version || "n/a"}</dd></div>
        <div><dt>Compatibility</dt><dd>{selectedRow?.module ? <StatusChip tone={compatibilityTone(selectedRow.module.compatibility)}>{selectedRow.module.compatibility}</StatusChip> : "n/a"}</dd></div>
      </dl>
      <p className="modules-muted">Content root: <code>{registry.host.contentRootPath}</code></p>
    </div>
  );
}

function ContributionsTab({ selectedRow }: { selectedRow: ModuleRow | null }) {
  const contributions = selectedRow?.module?.contributions ?? [];
  return (
    <div className="modules-inspector-section">
      <h4>Contributions</h4>
      <div className="modules-contribution-list">
        {contributions.length === 0 ? <p className="modules-muted">No contributions reported for this row.</p> : null}
        {contributions.map(contribution => (
          <div key={contribution.id} className="modules-contribution-row">
            <span>{contribution.label}</span>
            <code>{contribution.type}</code>
          </div>
        ))}
      </div>
    </div>
  );
}

function PackageTab({
  registry,
  selectedRow
}: {
  registry: ModuleManagementRegistryResponse;
  selectedRow: ModuleRow | null;
}) {
  const selectedPackage = selectedRow?.package ?? findPackageForModule(registry, selectedRow?.module ?? null);
  if (selectedRow?.dropFolderPackage) {
    return (
      <div className="modules-inspector-section">
        <h4>Package</h4>
        <dl className="modules-metadata">
          <div><dt>File</dt><dd>{selectedRow.dropFolderPackage.fileName}</dd></div>
          <div><dt>Status</dt><dd>Uploaded to drop folder</dd></div>
          <div><dt>Size</dt><dd>{formatFileSize(selectedRow.dropFolderPackage.size)}</dd></div>
          <div><dt>Modified</dt><dd>{new Date(selectedRow.dropFolderPackage.lastWriteTimeUtc).toLocaleString()}</dd></div>
        </dl>
        <p className="modules-muted">This package file has been uploaded to the selected host. It will appear as loaded after Nuplane reconciles it successfully and the host can load its assemblies.</p>
      </div>
    );
  }

  return (
    <div className="modules-inspector-section">
      <h4>Package</h4>
      {selectedPackage ? (
        <>
          <dl className="modules-metadata">
            <div><dt>Package</dt><dd>{selectedPackage.id}</dd></div>
            <div><dt>Version</dt><dd>{selectedPackage.version}</dd></div>
            <div><dt>Feed</dt><dd>{selectedPackage.feedName || "n/a"}</dd></div>
            <div><dt>Source</dt><dd>{selectedPackage.sourceName || "n/a"}</dd></div>
          </dl>
          <PackageDependencies package={selectedPackage} />
        </>
      ) : <p className="modules-muted">This module is built into the selected host or loaded as a static module. It was not installed from a NuGet package.</p>}
    </div>
  );
}

function PackageDependencies({ package: pkg }: { package: ModuleManagementPackage }) {
  return (
    <div className="modules-package-dependencies">
      <h5>Dependencies</h5>
      {pkg.dependencies.length === 0 ? <p className="modules-muted">No package dependencies are reported.</p> : null}
      {pkg.dependencies.map(dependency => (
        <div key={`${dependency.source}:${dependency.group ?? ""}:${dependency.id}:${dependency.versionRange}`} className="modules-dependency-row">
          <span>
            <strong>{dependency.id}</strong>
            {dependency.versionRange ? <code>{dependency.versionRange}</code> : null}
          </span>
          <span>
            {dependency.group ? <code>{dependency.group}</code> : null}
            <StatusChip tone="neutral">{dependency.source}</StatusChip>
          </span>
        </div>
      ))}
    </div>
  );
}

function ManifestTab({
  registry,
  selectedRow
}: {
  registry: ModuleManagementRegistryResponse;
  selectedRow: ModuleRow | null;
}) {
  const selectedPackage = selectedRow?.package ?? findPackageForModule(registry, selectedRow?.module ?? null);
  const packageManifest = selectedPackage?.manifest ?? null;
  const moduleManifest = selectedRow?.module?.manifest ?? null;

  return (
    <div className="modules-inspector-section">
      <h4>Manifest</h4>
      {packageManifest ? <PackageManifestView manifest={packageManifest} /> : null}
      {!packageManifest && moduleManifest ? <StudioManifestView manifest={moduleManifest} /> : null}
      {!packageManifest && !moduleManifest ? <p className="modules-muted">No package or module manifest is reported for this row.</p> : null}
    </div>
  );
}

function PackageManifestView({ manifest }: { manifest: ModuleManagementPackageManifest }) {
  return (
    <div className="modules-manifest-view">
      <span className="modules-manifest-kind">{manifest.kind}</span>
      <code className="modules-manifest-path">{manifest.path}</code>
      {manifest.content ? (
        <pre className="modules-manifest-code">{formatManifestJson(manifest.content)}</pre>
      ) : manifest.text ? (
        <pre className="modules-manifest-code">{manifest.text}</pre>
      ) : (
        <p className="modules-muted">The package manifest is empty.</p>
      )}
    </div>
  );
}

function StudioManifestView({ manifest }: { manifest: ModuleManagementStudioManifest }) {
  return (
    <div className="modules-manifest-view">
      <dl className="modules-metadata">
        <div><dt>Entry</dt><dd>{manifest.entry}</dd></div>
        <div><dt>Styles</dt><dd>{manifest.styles.length}</dd></div>
        <div><dt>Capabilities</dt><dd>{manifest.capabilities.length}</dd></div>
      </dl>
      {manifest.styles.length > 0 ? (
        <>
          <h5>Styles</h5>
          <ul className="modules-manifest-list">
            {manifest.styles.map(style => <li key={style}><code>{style}</code></li>)}
          </ul>
        </>
      ) : null}
      {manifest.capabilities.length > 0 ? (
        <>
          <h5>Capabilities</h5>
          <ul className="modules-manifest-list">
            {manifest.capabilities.map(capability => <li key={capability}>{capability}</li>)}
          </ul>
        </>
      ) : null}
    </div>
  );
}

function DiagnosticsTab({ registry, selectedRow }: { registry: ModuleManagementRegistryResponse; selectedRow: ModuleRow | null }) {
  const diagnostics = [
    ...(selectedRow?.module?.diagnostics ?? []),
    ...registry.diagnostics
  ];
  return (
    <div className="modules-inspector-section">
      <h4>Diagnostics</h4>
      <div className="modules-diagnostics-list">
        {diagnostics.length === 0 ? <p className="modules-muted">No diagnostics reported.</p> : null}
        {diagnostics.map((diagnostic, index) => (
          <div key={`${diagnostic.source}-${diagnostic.status}-${index}`} className="modules-diagnostic-row">
            <StatusChip tone={statusTone(diagnostic.status)}>{diagnostic.status}</StatusChip>
            <span>{diagnostic.reason}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

const moduleColumns = [
  {
    id: "module",
    header: "Module / Package",
    render: (row: ModuleRow) => (
      <span className="modules-name-cell">
        <strong>{row.displayName}</strong>
        <code>{row.id}</code>
      </span>
    )
  },
  { id: "surface", header: "Surface", render: (row: ModuleRow) => row.surface },
  { id: "scope", header: "Scope", render: (row: ModuleRow) => row.scope },
  { id: "source", header: "Source", render: (row: ModuleRow) => row.sourceKind },
  { id: "version", header: "Version", render: (row: ModuleRow) => row.version || "n/a" },
  {
    id: "status",
    header: "Status",
    render: (row: ModuleRow) => <StatusChip tone={statusTone(row.status)}>{row.status}</StatusChip>
  }
];

function buildRows(registry: ModuleManagementRegistryResponse | null): ModuleRow[] {
  if (!registry) return [];

  const moduleRows = registry.modules.map(module => ({
    key: `module:${module.id}`,
    kind: "module" as const,
    id: module.id,
    displayName: module.displayName || module.id,
    surface: module.surface,
    runtime: module.runtime,
    sourceKind: module.sourceKind,
    scope: module.scope,
    version: module.version,
    status: module.status,
    packageId: module.packageId,
    packageVersion: module.packageVersion,
    module
  }));

  const modulePackageKeys = new Set(moduleRows
    .filter(row => row.packageId)
    .map(row => `${row.packageId}@${row.packageVersion ?? ""}`.toLowerCase()));
  const activePackageKeys = new Set([
    ...modulePackageKeys,
    ...registry.packages.map(pkg => `${pkg.id}@${pkg.version}`.toLowerCase())
  ]);
  const packageRows = registry.packages
    .filter(pkg => !modulePackageKeys.has(`${pkg.id}@${pkg.version}`.toLowerCase()))
    .map(pkg => ({
      key: `package:${pkg.id}:${pkg.version}`,
      kind: "package" as const,
      id: pkg.id,
      displayName: pkg.id,
      surface: registry.host.id === "studio" ? "Studio" : "Server",
      runtime: registry.host.runtime,
      sourceKind: "nuplane-package",
      scope: registry.host.id === "studio" ? "frontend" : "backend",
      version: pkg.version,
      status: "loaded",
      packageId: pkg.id,
      packageVersion: pkg.version,
      package: pkg
    }));
  const dropFolderRows = registry.dropFolderPackages
    .map(file => ({ file, identity: parsePackageFileName(file.fileName) }))
    .filter(({ identity }) => !identity || !activePackageKeys.has(`${identity.id}@${identity.version}`.toLowerCase()))
    .map(({ file, identity }) => ({
      key: `drop-folder:${file.fileName}`,
      kind: "drop-folder" as const,
      id: identity?.id ?? file.fileName,
      displayName: identity?.id ?? file.fileName,
      surface: registry.host.id === "studio" ? "Studio" : "Server",
      runtime: registry.host.runtime,
      sourceKind: "drop-folder",
      scope: registry.host.id === "studio" ? "frontend" : "backend",
      version: identity?.version ?? "",
      status: "uploaded",
      packageId: identity?.id,
      packageVersion: identity?.version,
      dropFolderPackage: file
    }));

  return [...moduleRows, ...packageRows, ...dropFolderRows].sort((a, b) => a.displayName.localeCompare(b.displayName));
}

function parsePackageFileName(fileName: string) {
  const name = fileName.replace(/\.nupkg$/i, "");
  const match = name.match(/^(?<id>.+)\.(?<version>\d+\.\d+\.\d+(?:\.\d+)?(?:[-+][0-9A-Za-z.-]+)?)$/);
  if (!match?.groups)
    return null;

  return { id: match.groups.id, version: match.groups.version };
}

function getSummary(registry: ModuleManagementRegistryResponse | null, rows: ModuleRow[]) {
  return {
    loaded: rows.filter(row => row.status === "loaded").length,
    available: rows.filter(row => row.status === "available").length,
    failed: rows.filter(row => row.status === "failed").length,
    packages: registry?.packages.length ?? 0,
    feeds: registry?.feeds.length ?? 0,
    attention: rows.filter(row => ["failed", "incompatible", "disabled"].includes(row.status)).length
  };
}

function findPackageForModule(registry: ModuleManagementRegistryResponse, module: ModuleManagementModule | null) {
  if (!module?.packageId) return null;
  return registry.packages.find(pkg =>
    pkg.id.toLowerCase() === module.packageId?.toLowerCase() &&
    (!module.packageVersion || pkg.version.toLowerCase() === module.packageVersion.toLowerCase())) ?? null;
}

function formatManifestJson(content: unknown) {
  return JSON.stringify(content, null, 2);
}

function statusTone(status: string) {
  if (status === "loaded" || status === "available") return "success";
  if (status === "disabled" || status === "pending" || status === "uploaded" || status === "would-delete") return "warning";
  if (status === "failed" || status === "incompatible" || status === "deleted") return "danger";
  return "neutral";
}

function compatibilityTone(compatibility: string) {
  if (compatibility === "compatible") return "success";
  if (compatibility === "warning" || compatibility === "unknown") return "warning";
  if (compatibility === "incompatible") return "danger";
  return "neutral";
}
