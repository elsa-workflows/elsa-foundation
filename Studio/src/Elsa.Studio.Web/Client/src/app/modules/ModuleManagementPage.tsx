import React, { useMemo, useRef, useState } from "react";
import { Boxes, CloudUpload, RefreshCcw, Trash2 } from "lucide-react";
import type { ElsaStudioModuleApi } from "../../sdk";
import { EmptyState, StatusChip, StudioAlert, StudioDataGrid, StudioTabs, StudioToolbar, StudioToolbarGroup } from "../ui";
import {
  deleteDropFolderPackage,
  formatFileSize,
  hostTabs,
  uploadPackage,
  type HostId,
  type HostModel,
  type ModuleManagementPackageManifest,
  type ModuleManagementModule,
  type ModuleManagementPackage,
  type ModuleManagementRegistryResponse,
  type ModuleManagementStudioManifest
} from "./moduleManagementApi";
import { BackendRegistryUnavailable } from "./BackendRegistryUnavailable";
import { readActiveRegistry, useHostOperations, useModuleManagementRegistries } from "./useModuleManagement";

type InspectorTab = "overview" | "contributions" | "package" | "manifest" | "diagnostics";
type ModuleOrigin = "built-in" | "nuplane";
type NuplaneState = "installed";
type SourceView = ModuleOrigin;

interface ModuleRow {
  key: string;
  kind: "module" | "package";
  id: string;
  displayName: string;
  surface: string;
  runtime: string;
  sourceKind: string;
  scope: string;
  version: string;
  status: string;
  origin: ModuleOrigin;
  packageSource: string;
  nuplaneState?: NuplaneState;
  packageId?: string | null;
  packageVersion?: string | null;
  module?: ModuleManagementModule;
  package?: ModuleManagementPackage;
}

const inspectorTabs = [
  { id: "overview", label: "Overview" },
  { id: "contributions", label: "Contributions" },
  { id: "package", label: "Package" },
  { id: "manifest", label: "Manifest" },
  { id: "diagnostics", label: "Diagnostics" }
];

// Extra registry refetches after a write, polling async Nuplane reconciliation that completes after
// the mutation resolves (was ModuleManagementPage.scheduleFollowUpRefreshes).
const followUpRefreshDelaysMs = [500, 1500, 3000, 6000];

export function ModuleManagementPage({ api }: { api: ElsaStudioModuleApi }) {
  const { hosts, byHost } = useModuleManagementRegistries(api);
  const [activeHostId, setActiveHostId] = useState<HostId>("studio");
  // Per-host view state (selection, source view, inspector tab). Registry data itself now lives in the
  // Query cache, not here.
  const [viewByHost, setViewByHost] = useState<Record<HostId, HostViewState>>(() => ({
    studio: createInitialViewState(),
    server: createInitialViewState()
  }));
  const uploadInputRef = useRef<HTMLInputElement>(null);
  const [uploadDragActive, setUploadDragActive] = useState(false);
  const activeHost = hosts.find(host => host.id === activeHostId) ?? hosts[0];
  const activeView = viewByHost[activeHost.id];
  const activeQuery = byHost(activeHost.id);
  // The registry is present only when the host's read resolved to a ready result; a Server tab whose backend management
  // is unconfigured/unreachable/unauthorized/degraded resolves to an explicit `unavailable` state instead (#246).
  const { registry, unavailable } = readActiveRegistry(activeQuery.data);
  const { isPending, activeError, activeStatus, runHostOperation, confirmAndRun, reportError } = useHostOperations(activeHost, activeQuery.error, followUpRefreshDelaysMs);
  const isLoading = activeQuery.isPending || activeQuery.isFetching;

  const rows = useMemo(() => buildRows(registry), [registry]);
  const builtInRows = useMemo(() => rows.filter(row => row.origin === "built-in"), [rows]);
  const nuplaneRows = useMemo(() => rows.filter(row => row.origin === "nuplane"), [rows]);
  const packageSourceOptions = useMemo(() => getPackageSourceOptions(nuplaneRows), [nuplaneRows]);
  // A package-source filter that no longer matches any row falls back to "all" so we never show an
  // empty grid for a stale filter (was a dedicated effect over patchHostState).
  const effectivePackageSourceFilter = activeView.packageSourceFilter !== AllPackageSources && !packageSourceOptions.includes(activeView.packageSourceFilter)
    ? AllPackageSources
    : activeView.packageSourceFilter;
  const filteredNuplaneRows = useMemo(
    () => effectivePackageSourceFilter === AllPackageSources
      ? nuplaneRows
      : nuplaneRows.filter(row => row.packageSource === effectivePackageSourceFilter),
    [effectivePackageSourceFilter, nuplaneRows]);
  const visibleRows = activeView.sourceView === "nuplane" ? filteredNuplaneRows : builtInRows;
  const selectedRow = visibleRows.find(row => row.key === activeView.selectedKey) ?? visibleRows[0] ?? null;
  const summary = useMemo(() => getSummary(registry, rows), [registry, rows]);
  const activeColumns = activeView.sourceView === "nuplane" ? nuplaneModuleColumns : builtInModuleColumns;
  const gridColumns = activeView.sourceView === "nuplane"
    ? "minmax(250px, 1.45fr) 88px 104px minmax(140px, .9fr) 86px 82px 116px"
    : "minmax(250px, 1.45fr) 88px 104px minmax(120px, .8fr) 82px 116px";

  function patchViewState(hostId: HostId, patch: Partial<HostViewState>) {
    setViewByHost(current => ({
      ...current,
      [hostId]: { ...current[hostId], ...patch }
    }));
  }

  function selectSourceView(sourceView: SourceView, packageSourceFilter = activeView.packageSourceFilter) {
    patchViewState(activeHost.id, { sourceView, packageSourceFilter, selectedKey: "" });
  }

  // Deleting a staged drop-folder package permanently removes the uploaded .nupkg, so confirm first
  // (#192 guard), then run the delete through the #189 Query mutation on confirm; cancel is a no-op.
  function confirmAndDeleteDropFolderPackage(fileName: string) {
    return confirmAndRun(
      () => api.dialogs.confirm({
        title: "Delete staged package",
        message: `Delete "${fileName}" from the ${activeHost.label} drop folder? This permanently removes the uploaded package file.`,
        confirmLabel: "Delete package",
        tone: "danger"
      }),
      () => deleteDropFolderPackage(activeHost.context, fileName),
      `Deleted ${fileName} from ${activeHost.label}. Nuplane reconciliation is running.`
    );
  }

  function uploadSelectedFiles(files: FileList | File[]) {
    const packageFiles = Array.from(files).filter(file => file.name.toLowerCase().endsWith(".nupkg"));
    if (packageFiles.length === 0) {
      reportError("Select one or more .nupkg package files.");
      return;
    }

    // The uploads are independent drop-folder writes, so fan them out rather than awaiting one-by-one.
    void runHostOperation(
      () => Promise.all(packageFiles.map(file => uploadPackage(activeHost.context, file))),
      `Uploaded ${packageFiles.length} package${packageFiles.length === 1 ? "" : "s"} to ${activeHost.label}. Reconcile completed; reload may be required.`
    );
  }

  const isReady = activeQuery.isSuccess;

  return (
    <section className="modules-page">
      <div className="section-header modules-header">
        <div>
          <h2>Modules</h2>
          <p>{activeHost.label}: {summary.loaded} loaded, {summary.available} available, {summary.failed} failed, {summary.packages} package(s)</p>
        </div>
        <StudioToolbar>
          <StudioToolbarGroup>
            <button type="button" className="studio-button" onClick={() => void activeQuery.refetch()} disabled={isLoading}>
              <RefreshCcw size={15} />
              {isLoading ? "Refreshing" : "Refresh"}
            </button>
          </StudioToolbarGroup>
        </StudioToolbar>
      </div>

      <StudioTabs tabs={hostTabs} activeTab={activeHostId} onSelect={tabId => setActiveHostId(tabId as HostId)} ariaLabel="Hosts" />

      {activeError ? <StudioAlert tone="danger">{activeError}</StudioAlert> : null}
      {activeStatus ? <StudioAlert tone="success">{activeStatus}</StudioAlert> : null}

      <div className="modules-summary-strip">
        <SummaryItem label="Rows" value={rows.length} />
        <SummaryItem label="Loaded" value={summary.loaded} />
        <SummaryItem label="Packages" value={summary.packages} />
        <SummaryItem label="Feeds" value={summary.feeds} />
        <SummaryItem label="Attention" value={summary.attention} />
      </div>

      {unavailable ? (
        <BackendRegistryUnavailable host={activeHost} status={unavailable.status} detail={unavailable.detail} />
      ) : isReady && rows.length === 0 && (registry?.dropFolderPackages.length ?? 0) === 0 ? (
        <EmptyState icon={<Boxes size={22} />}>No modules or packages are reported for {activeHost.label}.</EmptyState>
      ) : (
        <div className="modules-workbench">
          <SourceNavigator
            builtInCount={builtInRows.length}
            nuplaneCount={nuplaneRows.length}
            packageSourceOptions={packageSourceOptions}
            packageSourceCounts={getPackageSourceCounts(nuplaneRows)}
            activeSourceView={activeView.sourceView}
            activePackageSource={effectivePackageSourceFilter}
            onSelectBuiltIn={() => selectSourceView("built-in")}
            onSelectNuplane={() => selectSourceView("nuplane", AllPackageSources)}
            onSelectPackageSource={source => selectSourceView("nuplane", source)}
          />
          <div className="modules-grid-panel">
            <div className="modules-grid-heading">
              <div>
                <h3>{activeView.sourceView === "nuplane" ? "Nuplane packages" : "Built-in modules"}</h3>
                <p>{activeView.sourceView === "nuplane"
                  ? "Installed through Nuplane at runtime."
                  : "Referenced by the host deployment. Changes require rebuild or redeploy."}</p>
              </div>
            </div>
            {activeView.sourceView === "nuplane" && registry ? (
              <InlinePackageUploads
                host={activeHost}
                registry={registry}
                busy={isPending}
                dragActive={uploadDragActive}
                uploadInputRef={uploadInputRef}
                onUploadFiles={uploadSelectedFiles}
                onDragActiveChange={setUploadDragActive}
                onDeleteDropFolderPackage={fileName => confirmAndDeleteDropFolderPackage(fileName)}
              />
            ) : null}
            {visibleRows.length === 0 ? (
              <p className="modules-muted">{getEmptyRowsMessage(activeHost, activeView.sourceView, nuplaneRows.length, effectivePackageSourceFilter)}</p>
            ) : (
              <StudioDataGrid
                columns={activeColumns}
                items={visibleRows}
                getKey={row => row.key}
                selectedKey={selectedRow?.key}
                onSelect={row => patchViewState(activeHost.id, { selectedKey: row.key })}
                gridColumns={gridColumns}
              />
            )}
          </div>
          <ModuleInspector
            host={activeHost}
            registry={registry}
            selectedRow={selectedRow}
            activeTab={activeView.inspectorTab}
            onSelectTab={tab => patchViewState(activeHost.id, { inspectorTab: tab })}
          />
        </div>
      )}
    </section>
  );
}

interface HostViewState {
  selectedKey: string;
  inspectorTab: InspectorTab;
  sourceView: SourceView;
  packageSourceFilter: string;
}

function createInitialViewState(): HostViewState {
  return {
    selectedKey: "",
    inspectorTab: "overview",
    sourceView: "built-in",
    packageSourceFilter: AllPackageSources
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

function SourceNavigator({
  builtInCount,
  nuplaneCount,
  packageSourceOptions,
  packageSourceCounts,
  activeSourceView,
  activePackageSource,
  onSelectBuiltIn,
  onSelectNuplane,
  onSelectPackageSource
}: {
  builtInCount: number;
  nuplaneCount: number;
  packageSourceOptions: string[];
  packageSourceCounts: Map<string, number>;
  activeSourceView: SourceView;
  activePackageSource: string;
  onSelectBuiltIn(): void;
  onSelectNuplane(): void;
  onSelectPackageSource(source: string): void;
}) {
  const allNuplaneActive = activeSourceView === "nuplane" && activePackageSource === AllPackageSources;

  return (
    <aside className="modules-source-nav" aria-label="Module sources">
      <div className="modules-source-nav-heading">
        <span>Sources</span>
        <strong>{builtInCount + nuplaneCount}</strong>
      </div>

      <button
        type="button"
        className={activeSourceView === "built-in" ? "modules-source-button active" : "modules-source-button"}
        aria-pressed={activeSourceView === "built-in"}
        onClick={onSelectBuiltIn}
      >
        <span>
          <strong>Built-in</strong>
          <small>Host deployment</small>
        </span>
        <em>{builtInCount}</em>
      </button>

      <button
        type="button"
        className={allNuplaneActive ? "modules-source-button active" : "modules-source-button"}
        aria-pressed={allNuplaneActive}
        onClick={onSelectNuplane}
      >
        <span>
          <strong>Nuplane</strong>
          <small>Runtime packages</small>
        </span>
        <em>{nuplaneCount}</em>
      </button>

      {nuplaneCount > 0 ? (
        <div className="modules-source-group" aria-label="Nuplane package sources">
          <span>Nuplane sources</span>
          {packageSourceOptions.map(source => (
            <button
              key={source}
              type="button"
              className={activeSourceView === "nuplane" && activePackageSource === source ? "active" : ""}
              aria-pressed={activeSourceView === "nuplane" && activePackageSource === source}
              onClick={() => onSelectPackageSource(source)}
            >
              <span>{source}</span>
              <em>{packageSourceCounts.get(source) ?? 0}</em>
            </button>
          ))}
        </div>
      ) : null}
    </aside>
  );
}

function getEmptyRowsMessage(host: HostModel, sourceView: SourceView, nuplaneCount: number, packageSourceFilter: string) {
  if (sourceView === "built-in") {
    return `No built-in modules are reported for ${host.label}.`;
  }

  if (nuplaneCount === 0) {
    return `No active Nuplane packages are installed for ${host.label}.`;
  }

  return packageSourceFilter === AllPackageSources
    ? "No Nuplane packages are visible."
    : "No Nuplane packages match this source.";
}

function InlinePackageUploads({
  host,
  registry,
  busy,
  dragActive,
  uploadInputRef,
  onUploadFiles,
  onDragActiveChange,
  onDeleteDropFolderPackage
}: {
  host: HostModel;
  registry: ModuleManagementRegistryResponse;
  busy: boolean;
  dragActive: boolean;
  uploadInputRef: React.RefObject<HTMLInputElement>;
  onUploadFiles(files: FileList | File[]): void;
  onDragActiveChange(active: boolean): void;
  onDeleteDropFolderPackage(fileName: string): void;
}) {
  const uploadDisabled = busy || !registry.capabilities.canUploadPackages;

  return (
    <section className="modules-inline-upload" aria-label={`${host.label} Nuplane package uploads`}>
      <div
        className={dragActive ? "modules-upload-dropzone dragging" : "modules-upload-dropzone"}
        onDragEnter={event => {
          event.preventDefault();
          if (!uploadDisabled) onDragActiveChange(true);
        }}
        onDragOver={event => {
          event.preventDefault();
          if (!uploadDisabled) onDragActiveChange(true);
        }}
        onDragLeave={event => {
          event.preventDefault();
          onDragActiveChange(false);
        }}
        onDrop={event => {
          event.preventDefault();
          onDragActiveChange(false);
          if (!uploadDisabled && event.dataTransfer.files.length > 0) {
            onUploadFiles(event.dataTransfer.files);
          }
        }}
      >
        <span>
          <strong>Drop .nupkg files here</strong>
          <small>Upload packages to this host's Nuplane drop folder.</small>
        </span>
        <button type="button" className="studio-button" disabled={uploadDisabled} onClick={() => uploadInputRef.current?.click()}>
          <CloudUpload size={15} />
          Upload Packages
        </button>
        <input
          ref={uploadInputRef}
          type="file"
          accept=".nupkg"
          multiple
          className="modules-hidden-input"
          onChange={event => {
            if (event.target.files?.length) onUploadFiles(event.target.files);
            event.currentTarget.value = "";
          }}
        />
      </div>

      <div className="modules-staged-uploads">
        <div className="modules-staged-uploads-heading">
          <h4>Staged uploads</h4>
          <span>{registry.dropFolderPackages.length}</span>
        </div>
        <div className="modules-drop-folder-list" aria-label={`${host.label} staged uploads`}>
          {registry.dropFolderPackages.length === 0 ? <p className="modules-muted">No staged uploads for this host.</p> : null}
          {registry.dropFolderPackages.map(file => {
            const packageIdentity = parsePackageFileName(file.fileName);
            return (
              <div key={file.fileName} className="modules-drop-folder-row">
                <span>
                  <strong>{file.fileName}</strong>
                  <code>{packageIdentity ? `${packageIdentity.id} ${packageIdentity.version} · ` : ""}{formatFileSize(file.size)} · {new Date(file.lastWriteTimeUtc).toLocaleString()}</code>
                </span>
                <button type="button" className="studio-icon-button" disabled={busy} title={`Delete ${file.fileName} from ${host.label} drop folder`} onClick={() => onDeleteDropFolderPackage(file.fileName)}>
                  <Trash2 size={14} />
                </button>
              </div>
            );
          })}
        </div>
      </div>
    </section>
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

      <StudioTabs tabs={inspectorTabs} activeTab={activeTab} onSelect={tab => onSelectTab(tab as InspectorTab)} ariaLabel="Inspector" />

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
        <div><dt>Origin</dt><dd>{selectedRow ? getOriginLabel(selectedRow.origin) : "Built-in"}</dd></div>
        <div><dt>Source kind</dt><dd>{selectedRow?.sourceKind ?? "host"}</dd></div>
        <div><dt>Package source</dt><dd>{selectedRow?.origin === "nuplane" ? selectedRow.packageSource : "n/a"}</dd></div>
        <div><dt>Nuplane state</dt><dd>{selectedRow?.nuplaneState ?? "n/a"}</dd></div>
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
            <div><dt>Install path</dt><dd><code>{selectedPackage.installPath}</code></dd></div>
          </dl>
          <PackageDependencies package={selectedPackage} />
        </>
      ) : <p className="modules-muted">This module is part of the selected host deployment and is not managed by Nuplane. Change it by updating package references and rebuilding or redeploying the host.</p>}
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

const builtInModuleColumns = [
  {
    id: "module",
    header: "Module",
    render: (row: ModuleRow) => (
      <span className="modules-name-cell">
        <strong>{row.displayName}</strong>
        <code>{row.id}</code>
      </span>
    )
  },
  { id: "surface", header: "Surface", render: (row: ModuleRow) => row.surface },
  { id: "scope", header: "Scope", render: (row: ModuleRow) => row.scope },
  { id: "sourceKind", header: "Source kind", render: (row: ModuleRow) => row.sourceKind },
  { id: "version", header: "Version", render: (row: ModuleRow) => row.version || "n/a" },
  {
    id: "status",
    header: "Status",
    render: (row: ModuleRow) => <StatusChip tone={statusTone(row.status)}>{row.status}</StatusChip>
  }
];

const nuplaneModuleColumns = [
  {
    id: "module",
    header: "Package / Module",
    render: (row: ModuleRow) => (
      <span className="modules-name-cell">
        <strong>{row.displayName}</strong>
        <code>{row.id}</code>
      </span>
    )
  },
  { id: "surface", header: "Surface", render: (row: ModuleRow) => row.surface },
  { id: "scope", header: "Scope", render: (row: ModuleRow) => row.scope },
  { id: "packageSource", header: "Package source", render: (row: ModuleRow) => row.packageSource },
  { id: "state", header: "State", render: (row: ModuleRow) => row.nuplaneState ?? "installed" },
  { id: "version", header: "Version", render: (row: ModuleRow) => row.version || "n/a" },
  {
    id: "status",
    header: "Status",
    render: (row: ModuleRow) => <StatusChip tone={statusTone(row.status)}>{row.nuplaneState ?? row.status}</StatusChip>
  }
];

function buildRows(registry: ModuleManagementRegistryResponse | null): ModuleRow[] {
  if (!registry) return [];

  const packagesByKey = new Map(registry.packages.map(pkg => [packageKey(pkg.id, pkg.version), pkg]));
  const moduleRows = registry.modules.map(module => {
    const modulePackage = module.packageId
      ? packagesByKey.get(packageKey(module.packageId, module.packageVersion ?? ""))
      : null;
    const origin: ModuleOrigin = modulePackage || module.packageId ? "nuplane" : "built-in";

    return {
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
      origin,
      packageSource: origin === "nuplane" ? getPackageSource(modulePackage) : "",
      nuplaneState: origin === "nuplane" ? "installed" as const : undefined,
      packageId: module.packageId,
      packageVersion: module.packageVersion,
      module,
      package: modulePackage ?? undefined
    };
  });

  const modulePackageKeys = new Set(moduleRows
    .filter(row => row.packageId)
    .map(row => packageKey(row.packageId ?? "", row.packageVersion ?? "")));
  const packageRows = registry.packages
    .filter(pkg => !modulePackageKeys.has(packageKey(pkg.id, pkg.version)))
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
      origin: "nuplane" as const,
      packageSource: getPackageSource(pkg),
      nuplaneState: "installed" as const,
      packageId: pkg.id,
      packageVersion: pkg.version,
      package: pkg
    }));

  return [...moduleRows, ...packageRows].sort((a, b) => a.displayName.localeCompare(b.displayName));
}

function parsePackageFileName(fileName: string) {
  const name = fileName.replace(/\.nupkg$/i, "");
  const match = name.match(/^(?<id>.+)\.(?<version>\d+\.\d+\.\d+(?:\.\d+)?(?:[-+][0-9A-Za-z.-]+)?)$/);
  if (!match?.groups)
    return null;

  return { id: match.groups.id, version: match.groups.version };
}

const AllPackageSources = "__all";

function packageKey(id: string, version: string) {
  return `${id}@${version}`.toLowerCase();
}

function getPackageSource(pkg?: ModuleManagementPackage | null) {
  return pkg?.sourceName || pkg?.feedName || "Unknown source";
}

function getPackageSourceOptions(rows: ModuleRow[]) {
  return Array.from(new Set(rows
    .map(row => row.packageSource)
    .filter(source => source.trim().length > 0)))
    .sort((a, b) => a.localeCompare(b));
}

function getPackageSourceCounts(rows: ModuleRow[]) {
  return rows.reduce((counts, row) => {
    counts.set(row.packageSource, (counts.get(row.packageSource) ?? 0) + 1);
    return counts;
  }, new Map<string, number>());
}

function getOriginLabel(origin: ModuleOrigin) {
  return origin === "nuplane" ? "Nuplane" : "Built-in";
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
