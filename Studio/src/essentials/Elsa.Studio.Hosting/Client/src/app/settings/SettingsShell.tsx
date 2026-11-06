import { qualifyStudioBackendPath } from "@elsa-workflows/studio-sdk";
import React from "react";
import { useCompactNavigation } from "../navigation/useCompactNavigation";
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
  const { compact, navigationRef, selectRef } = useCompactNavigation();
  return (
    <div className="settings-shell">
      {compact ? (
        <label className="settings-section-select">
          <span>Settings section</span>
          <select ref={selectRef} value={activeId ?? ""} onChange={event => {
            const section = groups.flatMap(group => group.sections).find(item => item.id === event.target.value);
            if (section) onNavigate(settingsPathFor(section));
          }}>
            <option value="" disabled>Choose a section</option>
            {groups.map(group => <optgroup key={group.id} label={group.label}>
              {group.sections.map(section => <option key={section.id} value={section.id}>{section.label}</option>)}
            </optgroup>)}
          </select>
        </label>
      ) : <nav ref={navigationRef} className="settings-nav" aria-label="Settings sections">
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
      </nav>}
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
