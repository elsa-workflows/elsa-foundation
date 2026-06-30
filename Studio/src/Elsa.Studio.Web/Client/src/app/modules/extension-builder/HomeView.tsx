import type { ReactNode } from "react";
import { Boxes, FolderGit2, FolderPlus, Package, RefreshCcw } from "lucide-react";
import { StatusChip } from "../../ui";
import type {
  ExtensionBuilderCapabilities,
  ExtensionRepositorySummary,
  ExtensionWorkspace
} from "../extensionBuilderApi";
import { buildTone, formatRemoteState } from "./helpers";

export interface HomeStat {
  label: string;
  value: ReactNode;
}

export function HomeView({
  capabilities,
  advanced,
  busy,
  loading,
  stats,
  repositories,
  workspaces,
  selectedWorkspaceId,
  extensionName,
  workspaceName,
  serverLocalPath,
  serverLocalName,
  cloneRepositoryUrl,
  cloneRepositoryName,
  onToggleAdvanced,
  onRefresh,
  onOpenSolution,
  onExtensionNameChange,
  onCreateExtension,
  onWorkspaceNameChange,
  onCreateWorkspace,
  onServerLocalPathChange,
  onServerLocalNameChange,
  onAttachServerLocalRepository,
  onCloneRepositoryUrlChange,
  onCloneRepositoryNameChange,
  onCloneRepository
}: {
  capabilities: ExtensionBuilderCapabilities;
  advanced: boolean;
  busy: boolean;
  loading: boolean;
  stats: HomeStat[];
  repositories: ExtensionRepositorySummary[];
  workspaces: ExtensionWorkspace[];
  selectedWorkspaceId: string;
  extensionName: string;
  workspaceName: string;
  serverLocalPath: string;
  serverLocalName: string;
  cloneRepositoryUrl: string;
  cloneRepositoryName: string;
  onToggleAdvanced(value: boolean): void;
  onRefresh(): void;
  onOpenSolution(workspaceId: string): void;
  onExtensionNameChange(value: string): void;
  onCreateExtension(): void;
  onWorkspaceNameChange(value: string): void;
  onCreateWorkspace(): void;
  onServerLocalPathChange(value: string): void;
  onServerLocalNameChange(value: string): void;
  onAttachServerLocalRepository(): void;
  onCloneRepositoryUrlChange(value: string): void;
  onCloneRepositoryNameChange(value: string): void;
  onCloneRepository(): void;
}) {
  const canCreate = capabilities.canCreateWorkspace;
  const rows: ExtensionRepositorySummary[] = repositories.length > 0
    ? repositories
    : workspaces.map(workspace => ({
      id: workspace.id,
      name: workspace.name,
      owner: workspace.owner,
      activeBranch: null,
      isDirty: false,
      remoteState: "unknown",
      latestBuildStatus: workspace.projects[0]?.latestBuildStatus ?? null,
      attentionCount: 0,
      projectCount: workspace.projects.length,
      updatedAt: workspace.updatedAt
    } satisfies ExtensionRepositorySummary));

  return (
    <section className="extension-builder-home">
      <header className="extension-builder-hero">
        <div className="extension-builder-hero-text">
          <h2>Extension Builder</h2>
          <p>Create custom NuGet packages online, compile them in the cloud, and load them straight into Elsa &mdash; like Visual Studio, simplified.</p>
        </div>
        <div className="extension-builder-hero-actions">
          <label className="extension-builder-advanced-toggle">
            <input type="checkbox" aria-label="Advanced mode" checked={advanced} disabled={busy} onChange={event => onToggleAdvanced(event.target.checked)} />
            <span>Advanced</span>
          </label>
          <button type="button" className="studio-button" disabled={loading || busy} onClick={onRefresh}>
            <RefreshCcw size={15} />
            Refresh
          </button>
        </div>
      </header>

      <div className="extension-builder-stats">
        {stats.map(stat => (
          <div key={stat.label} className="extension-builder-stat">
            <strong>{stat.value}</strong>
            <span>{stat.label}</span>
          </div>
        ))}
      </div>

      <div className="extension-builder-create-bar">
        {advanced ? (
          <>
            <div className="extension-builder-create-block">
              <h4><FolderPlus size={15} /> New managed solution</h4>
              <div className="extension-builder-create-fields">
                <input aria-label="Repository name" placeholder="Solution name" value={workspaceName} disabled={busy || !canCreate} onChange={event => onWorkspaceNameChange(event.target.value)} />
                <button type="button" className="studio-button studio-button-primary" disabled={busy || !canCreate || !workspaceName.trim()} title={canCreate ? "Create managed repository" : "Requires canCreateWorkspace"} onClick={onCreateWorkspace}>Create managed repo</button>
              </div>
            </div>
            <div className="extension-builder-create-block">
              <h4><FolderGit2 size={15} /> Clone from Git</h4>
              <div className="extension-builder-create-fields">
                <input aria-label="Clone repository URL" placeholder="https://github.com/acme/extension.git" value={cloneRepositoryUrl} disabled={busy || !canCreate} onChange={event => onCloneRepositoryUrlChange(event.target.value)} />
                <input aria-label="Clone repository name" placeholder="Display name (optional)" value={cloneRepositoryName} disabled={busy || !canCreate} onChange={event => onCloneRepositoryNameChange(event.target.value)} />
                <button type="button" className="studio-button" disabled={busy || !canCreate || !cloneRepositoryUrl.trim()} title={canCreate ? "Clone from Git" : "Requires canCreateWorkspace"} onClick={onCloneRepository}>Clone from Git</button>
              </div>
            </div>
            <div className="extension-builder-create-block">
              <h4><FolderPlus size={15} /> Attach server-local</h4>
              <div className="extension-builder-create-fields">
                <input aria-label="Server-local repository path" placeholder="/srv/elsa/extensions/repo" value={serverLocalPath} disabled={busy || !canCreate} onChange={event => onServerLocalPathChange(event.target.value)} />
                <input aria-label="Server-local repository name" placeholder="Display name (optional)" value={serverLocalName} disabled={busy || !canCreate} onChange={event => onServerLocalNameChange(event.target.value)} />
                <button type="button" className="studio-button" disabled={busy || !canCreate || !serverLocalPath.trim()} title={canCreate ? "Attach server-local repository" : "Requires canCreateWorkspace"} onClick={onAttachServerLocalRepository}>Attach server-local</button>
              </div>
            </div>
          </>
        ) : (
          <div className="extension-builder-create-block extension-builder-create-primary">
            <h4><Package size={15} /> New extension</h4>
            <div className="extension-builder-create-fields">
              <input aria-label="Extension name" placeholder="Acme Orders" value={extensionName} disabled={busy || !canCreate} onChange={event => onExtensionNameChange(event.target.value)} onKeyDown={event => { if (event.key === "Enter" && extensionName.trim()) onCreateExtension(); }} />
              <button type="button" className="studio-button studio-button-primary" disabled={busy || !canCreate || !extensionName.trim()} title={canCreate ? "Create a new extension" : "Requires canCreateWorkspace"} onClick={onCreateExtension}>
                <FolderPlus size={15} />
                New extension
              </button>
            </div>
          </div>
        )}
      </div>

      <h3 className="extension-builder-section-title">{advanced ? "Repositories" : "Your extensions"}</h3>
      {rows.length === 0 ? (
        <div className="extension-builder-empty">
          <Boxes size={26} />
          <p>No extensions yet. Create one above to start building a NuGet package.</p>
        </div>
      ) : (
        <div className="extension-builder-solution-grid">
          {rows.map(repository => (
            <button
              key={repository.id}
              type="button"
              className={repository.id === selectedWorkspaceId ? "extension-builder-solution-card active" : "extension-builder-solution-card"}
              onClick={() => onOpenSolution(repository.id)}
            >
              <div className="extension-builder-solution-card-top">
                <Package size={20} />
                <StatusChip tone={buildTone(repository.latestBuildStatus)}>{repository.latestBuildStatus ?? "new"}</StatusChip>
              </div>
              <h4>{repository.name}</h4>
              <p className="modules-muted">{repository.owner ?? "current owner"} · {repository.projectCount} project(s)</p>
              <div className="extension-builder-solution-card-meta">
                {advanced ? <span>{repository.activeBranch ?? "no branch"} · {formatRemoteState(repository.remoteState)}{repository.isDirty ? " · dirty" : ""}</span> : <span>Open to edit, build &amp; publish</span>}
                {advanced && repository.attentionCount > 0 ? <StatusChip tone="warning">{repository.attentionCount}</StatusChip> : null}
              </div>
            </button>
          ))}
        </div>
      )}
    </section>
  );
}
