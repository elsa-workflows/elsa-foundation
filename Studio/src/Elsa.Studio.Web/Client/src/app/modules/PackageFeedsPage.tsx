import React, { useEffect, useMemo, useState } from "react";
import { Boxes, PackagePlus, RefreshCcw, Scissors, Trash2 } from "lucide-react";
import type { ElsaStudioModuleApi } from "../../sdk";
import { EmptyState, StudioTabs, StudioToolbar, StudioToolbarGroup } from "../ui";
import {
  addFeed,
  createModuleManagementHosts,
  defaultRetentionPolicy,
  deleteFeed,
  getErrorMessage,
  hostTabs,
  numberOrNull,
  postJson,
  saveRetentionPolicy,
  type HostId,
  type HostModel,
  type ModuleManagementFeed,
  type ModuleManagementRegistryResponse,
  type ModuleManagementRetentionPolicy,
  type RegistryState
} from "./moduleManagementApi";

interface HostRegistryState {
  state: RegistryState;
  registry: ModuleManagementRegistryResponse | null;
  error: string | null;
  status: string | null;
  operationBusy: boolean;
}

export function PackageFeedsPage({ api }: { api: ElsaStudioModuleApi }) {
  const hosts = useMemo<HostModel[]>(() => createModuleManagementHosts(api), [api]);
  const [activeHostId, setActiveHostId] = useState<HostId>("studio");
  const [stateByHost, setStateByHost] = useState<Record<HostId, HostRegistryState>>(() => ({
    studio: createInitialHostState(),
    server: createInitialHostState()
  }));
  const activeHost = hosts.find(host => host.id === activeHostId) ?? hosts[0];
  const activeState = stateByHost[activeHost.id];

  useEffect(() => {
    void refreshHost("studio");
    void refreshHost("server");
  }, []);

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
    <section className="modules-page package-feeds-page">
      <div className="section-header modules-header">
        <div>
          <h2>Package feeds</h2>
          <p>Manage Nuplane feeds, reconciliation, and retention separately for Studio and Server.</p>
        </div>
        <StudioToolbar>
          <StudioToolbarGroup>
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

      {activeState.registry ? (
        <PackageFeedsWorkbench
          host={activeHost}
          registry={activeState.registry}
          busy={activeState.operationBusy}
          onAddFeed={feed => runHostOperation(() => addFeed(activeHost.context, feed), `Added feed ${feed.name} to ${activeHost.label}. Restart is required to activate feed registration changes.`)}
          onDeleteFeed={feedName => runHostOperation(() => deleteFeed(activeHost.context, feedName), `Deleted feed ${feedName} from ${activeHost.label}. Restart is required to activate feed registration changes.`)}
          onSaveRetention={policy => runHostOperation(() => saveRetentionPolicy(activeHost.context, policy), `Updated ${activeHost.label} retention policy.`)}
          onReconcile={() => runHostOperation(() => postJson(activeHost.context, "/_elsa/module-management/reconcile", {}), `Reconciled ${activeHost.label}. Reload may be required.`)}
          onPrune={() => runHostOperation(() => postJson(activeHost.context, "/_elsa/module-management/prune", { dryRun: false }), `Pruned eligible package versions for ${activeHost.label}.`)}
        />
      ) : (
        <EmptyState icon={<Boxes size={22} />}>Loading {activeHost.label} package feeds.</EmptyState>
      )}
    </section>
  );
}

function PackageFeedsWorkbench({
  host,
  registry,
  busy,
  onAddFeed,
  onDeleteFeed,
  onSaveRetention,
  onReconcile,
  onPrune
}: {
  host: HostModel;
  registry: ModuleManagementRegistryResponse;
  busy: boolean;
  onAddFeed(feed: ModuleManagementFeed): void;
  onDeleteFeed(feedName: string): void;
  onSaveRetention(policy: ModuleManagementRetentionPolicy): void;
  onReconcile(): void;
  onPrune(): void;
}) {
  const [feedDraft, setFeedDraft] = useState({ name: "", serviceIndex: "", includePatterns: "*" });
  const [retentionDraft, setRetentionDraft] = useState<ModuleManagementRetentionPolicy>(() => registry.retentionPolicy ?? defaultRetentionPolicy());

  useEffect(() => {
    setRetentionDraft(registry.retentionPolicy ?? defaultRetentionPolicy());
  }, [registry.retentionPolicy]);

  function addDraftFeed() {
    onAddFeed({
      name: feedDraft.name.trim(),
      serviceIndex: feedDraft.serviceIndex.trim() || null,
      directoryPath: null,
      credentials: null,
      includeAll: feedDraft.includePatterns.trim() === "*",
      includePatterns: feedDraft.includePatterns.split(/\r?\n|,/).map(x => x.trim()).filter(Boolean),
      directory: { watch: true, debounceWindow: "00:00:01" }
    });
    setFeedDraft({ name: "", serviceIndex: "", includePatterns: "*" });
  }

  return (
    <div className="package-feeds-workbench">
      <section className="package-feeds-panel" aria-label={`${host.label} feeds`}>
        <div className="package-feeds-panel-heading">
          <div>
            <span>{host.label} host</span>
            <h3>Feeds</h3>
          </div>
          <strong>{registry.feeds.length}</strong>
        </div>

        <div className="modules-list package-feeds-list">
          {registry.feeds.length === 0 ? <p className="modules-muted">No feeds are configured for this host.</p> : null}
          {registry.feeds.map(feed => (
            <div key={feed.name} className="modules-list-row package-feed-row">
              <span>
                <strong>{feed.name}</strong>
                <code>{feed.serviceIndex ?? feed.directoryPath ?? "no source"}</code>
                <small>{feed.includeAll ? "All packages" : feed.includePatterns.join(", ")}</small>
              </span>
              <button type="button" className="studio-icon-button" disabled={busy || !registry.capabilities.canManageFeeds} title={`Delete ${feed.name} from ${host.label}`} onClick={() => onDeleteFeed(feed.name)}>
                <Trash2 size={14} />
              </button>
            </div>
          ))}
        </div>

        <div className="package-feeds-form-block">
          <h4>Add feed</h4>
          <div className="modules-form">
            <input aria-label="Feed name" placeholder="Feed name" value={feedDraft.name} onChange={event => setFeedDraft({ ...feedDraft, name: event.target.value })} />
            <input aria-label="Service index" placeholder="NuGet V3 service index" value={feedDraft.serviceIndex} onChange={event => setFeedDraft({ ...feedDraft, serviceIndex: event.target.value })} />
            <textarea aria-label="Include patterns" placeholder="Include patterns" value={feedDraft.includePatterns} onChange={event => setFeedDraft({ ...feedDraft, includePatterns: event.target.value })} />
            <button type="button" className="studio-button" disabled={busy || !registry.capabilities.canManageFeeds || !feedDraft.name.trim()} onClick={addDraftFeed}>
              <PackagePlus size={15} />
              Add feed
            </button>
          </div>
          {registry.capabilities.feedChangesRequireRestart ? <p className="modules-muted">Feed registration changes require a host restart before Nuplane can use them.</p> : null}
        </div>
      </section>

      <aside className="package-feeds-panel package-feeds-side-panel" aria-label={`${host.label} feed operations`}>
        <div className="package-feeds-panel-heading">
          <div>
            <span>{host.label} host</span>
            <h3>Operations</h3>
          </div>
        </div>

        <div className="modules-operation-grid">
          <button type="button" className="studio-button" disabled={busy || !registry.capabilities.canReconcile} onClick={onReconcile}>
            <RefreshCcw size={15} />
            Reconcile
          </button>
          <button type="button" className="studio-button" disabled={busy || !registry.capabilities.canPrunePackages} onClick={onPrune}>
            <Scissors size={15} />
            Prune old versions
          </button>
        </div>

        <div className="package-feeds-form-block">
          <h4>Retention</h4>
          <div className="modules-form two-column">
            <label>
              <span>Last versions</span>
              <input
                type="number"
                min="0"
                value={retentionDraft.retainLastNVersions ?? ""}
                onChange={event => setRetentionDraft({ ...retentionDraft, retainLastNVersions: numberOrNull(event.target.value) })}
              />
            </label>
            <label>
              <span>Younger than days</span>
              <input
                type="number"
                min="0"
                value={retentionDraft.retainYoungerThanDays ?? ""}
                onChange={event => setRetentionDraft({ ...retentionDraft, retainYoungerThanDays: numberOrNull(event.target.value) })}
              />
            </label>
            <label>
              <span>Mode</span>
              <select value={retentionDraft.mode} onChange={event => setRetentionDraft({ ...retentionDraft, mode: event.target.value })}>
                <option value="Automatic">Automatic</option>
                <option value="ManualOnly">Manual only</option>
              </select>
            </label>
            <label className="modules-checkbox">
              <input
                type="checkbox"
                checked={retentionDraft.protectLastKnownGood}
                onChange={event => setRetentionDraft({ ...retentionDraft, protectLastKnownGood: event.target.checked })}
              />
              Protect last-known-good
            </label>
            <button type="button" className="studio-button" disabled={busy} onClick={() => onSaveRetention(retentionDraft)}>Save retention</button>
          </div>
        </div>
      </aside>
    </div>
  );
}

function createInitialHostState(): HostRegistryState {
  return {
    state: "loading",
    registry: null,
    error: null,
    status: null,
    operationBusy: false
  };
}
