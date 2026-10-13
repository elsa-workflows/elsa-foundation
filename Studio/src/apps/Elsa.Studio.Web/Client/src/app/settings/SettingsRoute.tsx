import React from "react";
import type { StudioNavigationContribution } from "../../sdk";
import { useShellNavigationAccess } from "../navigation/useShellNavigationAccess";
import { AppearancePage } from "./AppearancePage";
import { SettingsShell } from "./SettingsShell";
import {
  getSettingsSectionGroups,
  resolveSettingsSection,
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
  const section = resolveSettingsSection(path, groups);
  const themeBuilder = groups
    .flatMap(group => group.sections)
    .find(item => item.id === "theme-builder" || item.path === "/theme-builder");

  let content: React.ReactNode;
  if (!section) {
    content = <div className="empty-state">No settings sections are available.</div>;
  } else if (section.id === "appearance") {
    content = (
      <AppearancePage
        themeBuilderHref={themeBuilder ? settingsPathFor(themeBuilder) : undefined}
        onNavigate={onNavigate}
      />
    );
  } else {
    content = renderSection(section) ?? (
      <div className="empty-state">No Studio route is registered for {section.path}.</div>
    );
  }

  return (
    <SettingsShell groups={groups} activeId={section?.id} onNavigate={onNavigate}>
      {content}
    </SettingsShell>
  );
}
