import { Hammer } from "lucide-react";
import type { ElsaStudioModuleApi } from "../../../sdk";
import { EmptyState, StudioAlert } from "../../ui";
import { isTrusted } from "../extensionBuilderApi";
import { ExtensionBuilderProvider, useExtensionBuilder } from "./ExtensionBuilderContext";
import { ExtensionBuilderWorkspace } from "./ExtensionBuilderWorkspace";
import { HomeView, type HomeStat } from "./HomeView";

// Top-level module entry point. It hosts the ExtensionBuilder context provider (which owns all
// shared state, effects and actions) and renders the gate/home/workspace surfaces from context.
// The previous single component owned 46 useState hooks and threaded ~50 props through the tree;
// state now lives in the provider and each panel reads it via `useExtensionBuilder()`.
export function ExtensionBuilderPage({ api }: { api: ElsaStudioModuleApi }) {
  return (
    <ExtensionBuilderProvider api={api}>
      <ExtensionBuilderView />
    </ExtensionBuilderProvider>
  );
}

function ExtensionBuilderView() {
  const builder = useExtensionBuilder();
  const { state, capabilities, error, status, advanced, inWorkspace } = builder;

  if (state === "loading") {
    return <EmptyState icon={<Hammer size={22} />}>Loading Extension Builder capabilities.</EmptyState>;
  }

  if (state === "failed") {
    return <StudioAlert tone="danger">{error ?? "Extension Builder is unavailable."}</StudioAlert>;
  }

  if (!isTrusted(capabilities)) {
    return (
      <section className="extension-builder-page">
        <div className="section-header modules-header">
          <div>
            <h2>Extension Builder</h2>
            <p>Trusted-team project workspaces, builds, promotion, and runtime recovery.</p>
          </div>
        </div>
        <StudioAlert tone="warning">
          You do not have Extension Builder capabilities for this backend. Access is gated by `GetCapabilities`; server enforcement remains authoritative.
        </StudioAlert>
      </section>
    );
  }

  const stats = buildHomeStats(builder);

  return (
    <section className={inWorkspace ? "extension-builder-page extension-builder-page-workspace" : "extension-builder-page"}>
      {error ? <StudioAlert tone="danger">{error}</StudioAlert> : null}
      {status ? <StudioAlert tone="success">{status}</StudioAlert> : null}

      {!inWorkspace ? (
        <HomeView
          capabilities={capabilities!}
          advanced={advanced}
          busy={builder.isBusy("home")}
          loading={state === "loading"}
          stats={stats}
          repositories={builder.repositories}
          workspaces={builder.workspaces}
          selectedWorkspaceId={builder.selectedRepository?.id ?? builder.selectedWorkspace?.id ?? ""}
          extensionName={builder.extensionName}
          workspaceName={builder.workspaceName}
          serverLocalPath={builder.serverLocalPath}
          serverLocalName={builder.serverLocalName}
          cloneRepositoryUrl={builder.cloneRepositoryUrl}
          cloneRepositoryName={builder.cloneRepositoryName}
          onToggleAdvanced={builder.setAdvanced}
          onRefresh={builder.bootstrap}
          onOpenSolution={builder.openSolution}
          onExtensionNameChange={builder.setExtensionName}
          onCreateExtension={builder.handleCreateExtension}
          onWorkspaceNameChange={builder.setWorkspaceName}
          onCreateWorkspace={builder.handleCreateWorkspace}
          onServerLocalPathChange={builder.setServerLocalPath}
          onServerLocalNameChange={builder.setServerLocalName}
          onAttachServerLocalRepository={builder.handleAttachServerLocalRepository}
          onCloneRepositoryUrlChange={builder.setCloneRepositoryUrl}
          onCloneRepositoryNameChange={builder.setCloneRepositoryName}
          onCloneRepository={builder.handleCloneRepository}
        />
      ) : (
        <ExtensionBuilderWorkspace />
      )}
    </section>
  );
}

function buildHomeStats(builder: ReturnType<typeof useExtensionBuilder>): HomeStat[] {
  const { repositories, workspaces, advanced } = builder;
  const totalProjects = repositories.reduce((count, repository) => count + repository.projectCount, 0) || workspaces.reduce((count, workspace) => count + workspace.projects.length, 0);
  const loadedCount = workspaces.reduce((count, workspace) => count + workspace.projects.filter(project => project.runtimeStatus === "Loaded").length, 0);
  const attentionTotal = repositories.reduce((count, repository) => count + repository.attentionCount, 0);
  return advanced
    ? [
      { label: "Repositories", value: repositories.length || workspaces.length },
      { label: "Projects", value: totalProjects },
      { label: "Loaded at runtime", value: loadedCount },
      { label: "Needs attention", value: attentionTotal }
    ]
    : [
      { label: "Extensions", value: repositories.length || workspaces.length },
      { label: "Projects", value: totalProjects },
      { label: "Loaded at runtime", value: loadedCount }
    ];
}
