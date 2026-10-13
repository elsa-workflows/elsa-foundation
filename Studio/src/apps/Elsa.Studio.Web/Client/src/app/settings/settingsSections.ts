import {
  filterAccessibleNavigation,
  getTopLevelNavigationItemsByPlacement,
  type NavigationAccess
} from "../navigation/placement";
import type { StudioNavigationContribution } from "../../sdk";

export const settingsPathPrefix = "/settings";
export const settingsAccessDeniedMessage = "You don't have access to this section.";
export const settingsEmptyMessage = "No settings sections are available.";

export interface SettingsSectionGroup {
  id: "studio" | "administration";
  label: string;
  sections: StudioNavigationContribution[];
}

export function settingsPathFor(item: Pick<StudioNavigationContribution, "id">) {
  return `${settingsPathPrefix}/${item.id}`;
}

export function isSettingsRootPath(path: string) {
  return path === settingsPathPrefix;
}

export function isSettingsDeepLinkPath(path: string) {
  return path === settingsPathPrefix || path.startsWith(`${settingsPathPrefix}/`);
}

export function isSettingsSectionActive(item: StudioNavigationContribution, path: string) {
  if (path === settingsPathFor(item) || path.startsWith(`${settingsPathFor(item)}/`)) {
    return true;
  }
  if (path === item.path) {
    return true;
  }
  if (!item.activePathPrefix) {
    return false;
  }
  return path === item.activePathPrefix || path.startsWith(`${item.activePathPrefix}/`);
}

export function getSettingsSectionGroups(
  navigation: readonly StudioNavigationContribution[],
  access: NavigationAccess = {}
): SettingsSectionGroup[] {
  const visible = filterAccessibleNavigation([...navigation], access);
  const groups: SettingsSectionGroup[] = [];
  const studio = getTopLevelNavigationItemsByPlacement(visible, "settings");
  const administration = getTopLevelNavigationItemsByPlacement(visible, "admin");
  if (studio.length > 0) {
    groups.push({ id: "studio", label: "Studio", sections: studio });
  }
  if (administration.length > 0) {
    groups.push({ id: "administration", label: "Administration", sections: administration });
  }
  return groups;
}

export function listSettingsSections(groups: SettingsSectionGroup[]) {
  return groups.flatMap(group => group.sections);
}

/** Every declared settings/admin section, ignoring the host-control gate. Used to own legacy URLs. */
export function listDeclaredSettingsSections(navigation: readonly StudioNavigationContribution[]) {
  return listSettingsSections(getSettingsSectionGroups(navigation));
}

export function resolveSettingsSection(path: string, groups: SettingsSectionGroup[]) {
  const sections = listSettingsSections(groups);
  if (sections.length === 0) {
    return undefined;
  }
  if (isSettingsRootPath(path)) {
    return sections[0];
  }
  return sections.find(section => isSettingsSectionActive(section, path));
}

export function resolveSettingsPane(path: string, visibleGroups: SettingsSectionGroup[], navigation: readonly StudioNavigationContribution[]) {
  const allowed = resolveSettingsSection(path, visibleGroups);
  if (allowed) {
    return { status: "ok" as const, section: allowed };
  }
  const declared = resolveSettingsSection(path, getSettingsSectionGroups(navigation));
  if (declared || hasSettingsSections(visibleGroups)) {
    return { status: "forbidden" as const, section: declared };
  }
  return { status: "empty" as const, section: undefined };
}

export function isSettingsSurfacePath(path: string, navigation: readonly StudioNavigationContribution[]) {
  if (isSettingsDeepLinkPath(path)) {
    return true;
  }
  return listDeclaredSettingsSections(navigation).some(section => isSettingsSectionActive(section, path));
}

export function hasSettingsSections(groups: SettingsSectionGroup[]) {
  return listSettingsSections(groups).length > 0;
}
