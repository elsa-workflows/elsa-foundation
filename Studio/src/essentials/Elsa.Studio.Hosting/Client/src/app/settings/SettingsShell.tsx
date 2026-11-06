import { qualifyStudioBackendPath } from "@elsa-workflows/studio-sdk";
import React from "react";
import type { StudioNavigationContribution } from "../../sdk";
import { settingsPathFor, type SettingsSectionGroup } from "./settingsSections";
import "./settings.css";

export function SettingsShell({
  groups,
  activeId,
  onNavigate,
  children
}: {
  groups: SettingsSectionGroup[];
  activeId?: string;
  onNavigate: (path: string) => void;
  children: React.ReactNode;
}) {
  return (
    <div className="settings-shell">
      <nav className="settings-nav" aria-label="Settings sections">
        {groups.map(group => (
          <div key={group.id} className="settings-nav-group">
            <p className="settings-nav-heading">{group.label}</p>
            {group.sections.map(section => (
              <SettingsSectionLink
                key={section.id}
                section={section}
                current={section.id === activeId}
                onNavigate={onNavigate}
              />
            ))}
          </div>
        ))}
      </nav>
      <div className="settings-pane">{children}</div>
    </div>
  );
}

function SettingsSectionLink({
  section,
  current,
  onNavigate
}: {
  section: StudioNavigationContribution;
  current: boolean;
  onNavigate: (path: string) => void;
}) {
  const href = settingsPathFor(section);
  return (
    <a
      className="settings-nav-link"
      href={qualifyStudioBackendPath(href)}
      aria-current={current ? "page" : undefined}
      onClick={event => {
        event.preventDefault();
        onNavigate(href);
      }}
    >
      {section.label}
    </a>
  );
}
