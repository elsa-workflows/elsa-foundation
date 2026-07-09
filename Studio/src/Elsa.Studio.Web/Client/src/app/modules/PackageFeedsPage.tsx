import React, { useEffect, useState } from "react";
import { Boxes, PackagePlus, Pencil, RefreshCcw, Scissors, Trash2, X } from "lucide-react";
import type { ElsaStudioModuleApi } from "../../sdk";
import { EmptyState, StudioAlert, StudioTabs, StudioToolbar, StudioToolbarGroup } from "../ui";
import {
  addFeed,
  defaultRetentionPolicy,
  deleteFeed,
  hostTabs,
  numberOrNull,
  postJson,
  saveRetentionPolicy,
  updateFeed,
  type HostId,
  type HostModel,
  type ModuleManagementFeed,
  type ModuleManagementRegistryResponse,
  type ModuleManagementRetentionPolicy
} from "./moduleManagementApi";
import { BackendRegistryUnavailable } from "./BackendRegistryUnavailable";
import { readActiveRegistry, useHostOperations, useModuleManagementRegistries } from "./useModuleManagement";

interface FeedDraft {
  name: string;
  serviceIndex: string;
  directoryPath: string;
  includePatterns: string;
  includeAllPackages: boolean;
}

export function PackageFeedsPage({ api }: { api: ElsaStudioModuleApi }) {
  const { hosts, byHost } = useModuleManagementRegistries(api);
  const [activeHostId, setActiveHostId] = useState<HostId>("studio");
  const activeHost = hosts.find(host => host.id === activeHostId) ?? hosts[0];
  const activeQuery = byHost(activeHost.id);
  // Present only when the host's registry read resolved to a ready result; an unavailable Server backend surfaces its
  // explicit bridge state (#246) instead.
  const { registry, unavailable } = readActiveRegistry(activeQuery.data);
  const { isPending, activeError, activeStatus, runHostOperation, confirmAndRun } = useHostOperations(activeHost, activeQuery.error);
  const isLoading = activeQuery.isPending || activeQuery.isFetching;

  // Deleting a feed is destructive and irreversible from the UI, so confirm before firing.
  function confirmAndDeleteFeed(feedName: string) {
    return confirmAndRun(
      () => api.dialogs.confirm({
        title: "Delete feed",
        message: `Delete feed "${feedName}" from ${activeHost.label}? This removes its package-source registration. A restart is required to apply the change.`,
        confirmLabel: "Delete feed",
        tone: "danger"
      }),
      () => deleteFeed(activeHost.context, feedName),
      `Deleted feed ${feedName} from ${activeHost.label}. Restart is required to activate feed registration changes.`
    );
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
            <button type="button" className="studio-icon-button" aria-label="Refresh package feeds" title="Refresh" onClick={() => void activeQuery.refetch()} disabled={isLoading}>
              <RefreshCcw size={15} />
            </button>
          </StudioToolbarGroup>
        </StudioToolbar>
      </div>

      <StudioTabs tabs={hostTabs} activeTab={activeHostId} onSelect={tabId => setActiveHostId(tabId as HostId)} ariaLabel="Hosts" />

      {activeError ? <StudioAlert tone="danger">{activeError}</StudioAlert> : null}
      {activeStatus ? <StudioAlert tone="success">{activeStatus}</StudioAlert> : null}

      {registry ? (
        <PackageFeedsWorkbench
          host={activeHost}
          registry={registry}
          busy={isPending}
          onAddFeed={feed => runHostOperation(() => addFeed(activeHost.context, feed), `Added feed ${feed.name} to ${activeHost.label}. Restart is required to activate feed registration changes.`)}
          onUpdateFeed={(feedName, feed) => runHostOperation(() => updateFeed(activeHost.context, feedName, feed), `Updated feed ${feedName} on ${activeHost.label}. Restart is required to activate feed registration changes.`)}
          onDeleteFeed={feedName => confirmAndDeleteFeed(feedName)}
          onSaveRetention={policy => runHostOperation(() => saveRetentionPolicy(activeHost.context, policy), `Updated ${activeHost.label} retention policy.`)}
          onReconcile={() => runHostOperation(() => postJson(activeHost.context, "/_elsa/module-management/reconcile", {}), `Reconciled ${activeHost.label}. Reload may be required.`)}
          onPrune={() => runHostOperation(() => postJson(activeHost.context, "/_elsa/module-management/prune", { dryRun: false }), `Pruned eligible package versions for ${activeHost.label}.`)}
        />
      ) : unavailable ? (
        <BackendRegistryUnavailable host={activeHost} status={unavailable.status} detail={unavailable.detail} />
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
  onUpdateFeed,
  onDeleteFeed,
  onSaveRetention,
  onReconcile,
  onPrune
}: {
  host: HostModel;
  registry: ModuleManagementRegistryResponse;
  busy: boolean;
  onAddFeed(feed: ModuleManagementFeed): void;
  onUpdateFeed(feedName: string, feed: ModuleManagementFeed): void;
  onDeleteFeed(feedName: string): void;
  onSaveRetention(policy: ModuleManagementRetentionPolicy): void;
  onReconcile(): void;
  onPrune(): void;
}) {
  const [addFeedDialogOpen, setAddFeedDialogOpen] = useState(false);
  const [editingFeed, setEditingFeed] = useState<ModuleManagementFeed | null>(null);
  const [retentionDraft, setRetentionDraft] = useState<ModuleManagementRetentionPolicy>(() => registry.retentionPolicy ?? defaultRetentionPolicy());
  const effectiveRetentionPolicy = registry.retentionPolicy ?? defaultRetentionPolicy();
  const retentionDirty = !retentionPolicyEquals(retentionDraft, effectiveRetentionPolicy);

  useEffect(() => {
    setRetentionDraft(effectiveRetentionPolicy);
  }, [registry.retentionPolicy]);

  function addDraftFeed(feedDraft: FeedDraft) {
    onAddFeed(createFeedFromDraft(feedDraft));
    setAddFeedDialogOpen(false);
  }

  function updateDraftFeed(feedName: string, feedDraft: FeedDraft) {
    onUpdateFeed(feedName, createFeedFromDraft(feedDraft));
    setEditingFeed(null);
  }

  return (
    <div className="package-feeds-layout">
      <div className="package-feeds-workbench">
        <section className="package-feeds-panel" aria-label={`${host.label} feeds`}>
          <div className="package-feeds-panel-heading">
            <div>
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
                <div className="package-feed-row-actions">
                  <button type="button" className="studio-icon-button" disabled={busy || !registry.capabilities.canManageFeeds} title={`Edit ${feed.name} on ${host.label}`} aria-label={`Edit ${feed.name}`} onClick={() => setEditingFeed(feed)}>
                    <Pencil size={14} />
                  </button>
                  <button type="button" className="studio-icon-button" disabled={busy || !registry.capabilities.canManageFeeds} title={`Delete ${feed.name} from ${host.label}`} aria-label={`Delete ${feed.name}`} onClick={() => onDeleteFeed(feed.name)}>
                    <Trash2 size={14} />
                  </button>
                </div>
              </div>
            ))}
          </div>

          <button type="button" className="studio-button package-feeds-add-button" disabled={busy || !registry.capabilities.canManageFeeds} onClick={() => setAddFeedDialogOpen(true)}>
            <PackagePlus size={15} />
            Add feed
          </button>

          {addFeedDialogOpen ? (
            <FeedDialog
              mode="add"
              host={host}
              busy={busy}
              feedChangesRequireRestart={registry.capabilities.feedChangesRequireRestart}
              onClose={() => setAddFeedDialogOpen(false)}
              onSubmit={addDraftFeed}
            />
          ) : null}

          {editingFeed ? (
            <FeedDialog
              mode="edit"
              host={host}
              busy={busy}
              feed={editingFeed}
              feedChangesRequireRestart={registry.capabilities.feedChangesRequireRestart}
              onClose={() => setEditingFeed(null)}
              onSubmit={feedDraft => updateDraftFeed(editingFeed.name, feedDraft)}
            />
          ) : null}
        </section>

        <aside className="package-feeds-side-column" aria-label={`${host.label} package feed controls`}>
          <section className="package-feeds-panel" aria-label={`${host.label} feed operations`}>
            <div className="package-feeds-panel-heading">
              <div>
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
          </section>

          <section className="package-feeds-panel" aria-label={`${host.label} retention settings`}>
            <div className="package-feeds-panel-heading">
              <div>
                <h3>Retention</h3>
              </div>
            </div>
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
              <button type="button" className="studio-button" disabled={busy || !retentionDirty} onClick={() => onSaveRetention(retentionDraft)}>Save retention</button>
            </div>
          </section>
        </aside>
      </div>

      <ChangeLedger
        host={host}
        retentionDirty={retentionDirty}
        feedChangesRequireRestart={registry.capabilities.feedChangesRequireRestart}
        onDiscardRetention={() => setRetentionDraft(effectiveRetentionPolicy)}
      />
    </div>
  );
}

function ChangeLedger({
  host,
  retentionDirty,
  feedChangesRequireRestart,
  onDiscardRetention
}: {
  host: HostModel;
  retentionDirty: boolean;
  feedChangesRequireRestart: boolean;
  onDiscardRetention(): void;
}) {
  return (
    <section className="package-change-ledger" aria-label={`${host.label} change ledger`}>
      <div className="package-change-ledger-header">
        <div>
          <h3>Change ledger</h3>
          <span>Affects: {host.label}</span>
        </div>
        {retentionDirty ? (
          <button type="button" className="studio-button" onClick={onDiscardRetention}>Discard local changes</button>
        ) : null}
      </div>
      <div className="package-change-ledger-row">
        <span className={retentionDirty ? "ledger-dot unsaved" : "ledger-dot"} aria-hidden="true" />
        <strong>{retentionDirty ? "Updated retention policy" : "No unsaved retention changes"}</strong>
        <span>{retentionDirty ? "Unsaved" : "Current"}</span>
      </div>
      {feedChangesRequireRestart ? (
        <div className="package-change-ledger-row">
          <span className="ledger-dot restart" aria-hidden="true" />
          <strong>Feed registration changes require restart</strong>
          <span>Host restart required after save</span>
        </div>
      ) : null}
    </section>
  );
}

function FeedDialog({
  mode,
  host,
  busy,
  feed,
  feedChangesRequireRestart,
  onClose,
  onSubmit
}: {
  mode: "add" | "edit";
  host: HostModel;
  busy: boolean;
  feed?: ModuleManagementFeed;
  feedChangesRequireRestart: boolean;
  onClose(): void;
  onSubmit(feedDraft: FeedDraft): void;
}) {
  const [feedDraft, setFeedDraft] = useState<FeedDraft>(() => createFeedDraft(feed));
  const title = mode === "edit" ? "Edit Feed" : "Add Feed";
  const submitLabel = mode === "edit" ? "Save feed" : "Add feed";

  return (
    <div className="modules-dialog-backdrop" role="presentation">
      <section className="modules-upload-dialog" role="dialog" aria-modal="true" aria-labelledby="package-feed-dialog-title">
        <div className="modules-upload-dialog-heading">
          <div>
            <h3 id="package-feed-dialog-title">{title}</h3>
            {feedChangesRequireRestart ? <p>Feed registration changes require a host restart before Nuplane can use them.</p> : null}
          </div>
          <button type="button" className="studio-icon-button" aria-label={`Close ${mode} feed`} onClick={onClose}>
            <X size={14} />
          </button>
        </div>

        <div className="modules-form">
          <label>
            <span>Name</span>
            <input aria-label="Feed name" placeholder="internal-packages" value={feedDraft.name} readOnly={mode === "edit"} onChange={event => setFeedDraft({ ...feedDraft, name: event.target.value })} />
          </label>
          <label>
            <span>Service index</span>
            <input aria-label="Service index" placeholder="NuGet V3 service index" value={feedDraft.serviceIndex} onChange={event => setFeedDraft({ ...feedDraft, serviceIndex: event.target.value })} />
          </label>
          <label>
            <span>Directory path</span>
            <input aria-label="Directory path" placeholder="/feeds/internal" value={feedDraft.directoryPath} onChange={event => setFeedDraft({ ...feedDraft, directoryPath: event.target.value })} />
          </label>
          <label>
            <span>Include patterns</span>
            <textarea aria-label="Include patterns" placeholder="**\\*.nupkg" value={feedDraft.includePatterns} disabled={feedDraft.includeAllPackages} onChange={event => setFeedDraft({ ...feedDraft, includePatterns: event.target.value })} />
          </label>
          <label className="modules-checkbox">
            <input
              type="checkbox"
              aria-label="Include all packages"
              checked={feedDraft.includeAllPackages}
              onChange={event => setFeedDraft({ ...feedDraft, includeAllPackages: event.target.checked })}
            />
            Include all packages
          </label>
        </div>

        <div className="modules-dialog-actions">
          <button type="button" className="studio-button" onClick={onClose}>Cancel</button>
          <button type="button" className="studio-button" disabled={busy || !feedDraft.name.trim()} onClick={() => onSubmit(feedDraft)}>
            <PackagePlus size={15} />
            {submitLabel}
          </button>
        </div>
      </section>
    </div>
  );
}

function createFeedDraft(feed?: ModuleManagementFeed): FeedDraft {
  return {
    name: feed?.name ?? "",
    serviceIndex: feed?.serviceIndex ?? "",
    directoryPath: feed?.directoryPath ?? "",
    includePatterns: feed ? feed.includePatterns.join("\n") || "*" : "*",
    includeAllPackages: feed?.includeAll ?? false
  };
}

function createFeedFromDraft(feedDraft: FeedDraft): ModuleManagementFeed {
  const includePatterns = feedDraft.includeAllPackages
    ? ["*"]
    : feedDraft.includePatterns.split(/\r?\n|,/).map(x => x.trim()).filter(Boolean);

  return {
    name: feedDraft.name.trim(),
    serviceIndex: feedDraft.serviceIndex.trim() || null,
    directoryPath: feedDraft.directoryPath.trim() || null,
    credentials: null,
    includeAll: feedDraft.includeAllPackages || feedDraft.includePatterns.trim() === "*",
    includePatterns,
    directory: { watch: true, debounceWindow: "00:00:01" }
  };
}

function retentionPolicyEquals(left: ModuleManagementRetentionPolicy, right: ModuleManagementRetentionPolicy) {
  return left.retainLastNVersions === right.retainLastNVersions &&
    left.retainYoungerThanDays === right.retainYoungerThanDays &&
    left.mode === right.mode &&
    left.protectLastKnownGood === right.protectLastKnownGood;
}
