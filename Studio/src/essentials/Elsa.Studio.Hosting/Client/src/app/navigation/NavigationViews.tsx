import React from "react";
import { isStudioWorkspaceTabClick, openStudioWorkspaceTab, qualifyStudioBackendPath } from "@elsa-workflows/studio-sdk";
import type { StudioNavigationContribution } from "../../sdk";
import { compareNavigationItems, getChildNavigationItems } from "./placement";
import { isNavigationItemActive } from "./active";

export function findNavigationViews(path: string, navigation: StudioNavigationContribution[]) {
  return navigation.flatMap(parent => {
    if (parent.childDisplay !== "views") return [];
    const children = getChildNavigationItems(navigation, parent).sort(compareNavigationItems);
    const views = [...(parent.viewLabel ? [{ ...parent, label: parent.viewLabel }] : []), ...children];
    return views.some(view => view.path === path) ? [{ parent, views }] : [];
  })[0];
}

/** These are routed views, so links keep bookmarks and native new-tab intent. */
export function NavigationViews({ parent, views, path, onNavigate }: {
  parent: StudioNavigationContribution;
  views: StudioNavigationContribution[];
  path: string;
  onNavigate(path: string): void;
}) {
  return <nav className="studio-view-navigation" aria-label={`${parent.label} views`}>
    {views.map(view => <NavigationContextLink key={view.id} path={view.path} current={isNavigationItemActive(view, path, views)} onNavigate={onNavigate}>{view.label}</NavigationContextLink>)}
  </nav>;
}

export function NavigationContextLink({ path, current, className, onNavigate, children }: {
  path: string;
  current?: boolean;
  className?: string;
  onNavigate(path: string): void;
  children: React.ReactNode;
}) {
  return <a className={className} href={qualifyStudioBackendPath(path)} aria-current={current ? "page" : undefined} onClick={event => {
    if (isStudioWorkspaceTabClick(event) && openStudioWorkspaceTab(path)) { event.preventDefault(); return; }
    if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    event.preventDefault(); onNavigate(path);
  }}>{children}</a>;
}
