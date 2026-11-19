import React, { Activity, useMemo } from "react";
// The provider must come from the shared SDK runtime that modules import, not the host's own copy of
// `../../sdk`, or module hooks would read a different React context.
import { StudioWorkspaceTabProvider, type StudioWorkspaceTabHandle, type StudioWorkspaceTabTitle } from "@elsa-workflows/studio-sdk";
import type { WorkspaceTab } from "./model";
import { workspacePanelDomId, workspaceTabDomId } from "./WorkspaceTabStrip";

export interface WorkspaceTabPanelsProps {
  baseId: string;
  tabs: WorkspaceTab[];
  activeId: string;
  renderScreen(location: string): React.ReactNode;
  titleOf(tab: WorkspaceTab): string;
  setTitle(id: string, title: StudioWorkspaceTabTitle | null): void;
  isActive(id: string): boolean;
  markPendingWork(id: string): void;
  onCloseTab(id: string): void;
}

/**
 * Renders every retained tab. Background tabs stay mounted inside a hidden `<Activity>`: React keeps
 * their state (drafts, undo history, selection, graph viewport) but tears down their effects, so they
 * stop polling, drop subscriptions and stop listening to history and navigation guards until they
 * return. Tabs that were restored or released stay unmounted until visited.
 */
export function WorkspaceTabPanels({ baseId, tabs, activeId, renderScreen, titleOf, setTitle, isActive, markPendingWork, onCloseTab }: WorkspaceTabPanelsProps) {
  return (
    <>
      {tabs.filter(tab => tab.retained || tab.id === activeId).map(tab => (
        <WorkspaceTabPanel
          key={tab.id}
          baseId={baseId}
          tab={tab}
          active={tab.id === activeId}
          title={titleOf(tab)}
          renderScreen={renderScreen}
          setTitle={setTitle}
          isActive={isActive}
          markPendingWork={markPendingWork}
          onCloseTab={onCloseTab}
        />
      ))}
    </>
  );
}

function WorkspaceTabPanel({ baseId, tab, active, title, renderScreen, setTitle, isActive, markPendingWork, onCloseTab }: { baseId: string; tab: WorkspaceTab; active: boolean; title: string; renderScreen(location: string): React.ReactNode; setTitle(id: string, title: StudioWorkspaceTabTitle | null): void; isActive(id: string): boolean; markPendingWork(id: string): void; onCloseTab(id: string): void }) {
  const handle = useMemo<StudioWorkspaceTabHandle>(() => ({ id: tab.id, active, location: tab.location, isActive: () => isActive(tab.id), markPendingWork: () => markPendingWork(tab.id), setTitle: value => setTitle(tab.id, value) }), [active, isActive, markPendingWork, setTitle, tab.id, tab.location]);
  // Background tabs re-render only for their own state; a new shell render does not reach them.
  const screen = useMemo(() => renderScreen(tab.location), [renderScreen, tab.location]);
  return (
    <div
      className="studio-workspace-panel"
      role="tabpanel"
      id={workspacePanelDomId(baseId, tab.id)}
      aria-labelledby={workspaceTabDomId(baseId, tab.id)}
      hidden={!active}
    >
      <Activity mode={active ? "visible" : "hidden"}>
        <StudioWorkspaceTabProvider value={handle}>
          <WorkspaceTabBoundary resetKey={tab.location} title={title} onClose={() => onCloseTab(tab.id)}>
            {screen}
          </WorkspaceTabBoundary>
        </StudioWorkspaceTabProvider>
      </Activity>
    </div>
  );
}

type BoundaryProps = { resetKey: string; title: string; onClose(): void; children: React.ReactNode };
type BoundaryState = { error: string | null; resetKey: string };

/** A screen that throws takes down only its own tab; the strip and other tabs keep working. */
export class WorkspaceTabBoundary extends React.Component<BoundaryProps, BoundaryState> {
  state: BoundaryState = { error: null, resetKey: this.props.resetKey };

  static getDerivedStateFromError(error: unknown): Partial<BoundaryState> {
    return { error: error instanceof Error ? error.message : String(error) };
  }

  static getDerivedStateFromProps(props: BoundaryProps, state: BoundaryState): Partial<BoundaryState> | null {
    return props.resetKey !== state.resetKey ? { error: null, resetKey: props.resetKey } : null;
  }

  render() {
    if (!this.state.error) return this.props.children;
    return (
      <section className="error-state studio-workspace-tab-error" role="alert">
        <strong>{this.props.title} stopped working in this tab.</strong>
        <span>{this.state.error}</span>
        <span className="studio-workspace-tab-error-actions">
          <button type="button" onClick={() => this.setState({ error: null })}>Reload this tab</button>
          <button type="button" onClick={this.props.onClose}>Close tab</button>
        </span>
      </section>
    );
  }
}
