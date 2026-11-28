import React from "react";
import type { StudioNavigationContribution } from "../../sdk";
import { useShellNavigationAccess } from "../navigation/useShellNavigationAccess";
import { AppearancePage } from "./AppearancePage";
import { SettingsShell } from "./SettingsShell";
import {
  getSettingsSectionGroups,
  resolveSettingsPane,
  settingsAccessDeniedMessage,
  settingsEmptyMessage,
  settingsPathFor
} from "./settingsSections";

export function SettingsRoute({
  path,
  navigation,
  enabledFeatures,
  onNavigate,
  renderSection
}: {
  path: string;
  navigation: StudioNavigationContribution[];
  enabledFeatures?: ReadonlySet<string> | readonly string[];
  onNavigate: (path: string) => void;
  renderSection: (section: StudioNavigationContribution) => React.ReactNode;
}) {
  const access = useShellNavigationAccess(enabledFeatures);
  const groups = getSettingsSectionGroups(navigation, access);
  const pane = resolveSettingsPane(path, groups, navigation);
  const themeBuilder = groups
    .flatMap(group => group.sections)
    .find(item => item.id === "theme-builder" || item.path === "/theme-builder");

  let content: React.ReactNode;
  if (pane.status === "empty") {
    content = <div className="empty-state">{settingsEmptyMessage}</div>;
  } else if (pane.status === "forbidden") {
    content = <div className="empty-state" role="status">{settingsAccessDeniedMessage}</div>;
  } else if (pane.section.id === "appearance") {
    content = (
      <AppearancePage
        themeBuilderHref={themeBuilder ? settingsPathFor(themeBuilder) : undefined}
        onNavigate={onNavigate}
      />
    );
  } else {
    content = renderSection(pane.section) ?? (
      <div className="empty-state">No Studio route is registered for {pane.section.path}.</div>
    );
  }

  return (
    <SettingsShell groups={groups} activeId={pane.status === "ok" ? pane.section.id : undefined} onNavigate={onNavigate}>
      {content}
    </SettingsShell>
  );
}
