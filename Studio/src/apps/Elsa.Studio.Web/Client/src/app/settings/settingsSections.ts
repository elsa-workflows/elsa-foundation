import {
  filterAccessibleNavigation,
  getTopLevelNavigationItemsByPlacement,
  type NavigationAccess
} from "../navigation/placement";
import type { StudioNavigationContribution } from "../../sdk";

export const settingsPathPrefix = "/settings";

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
  navigation: StudioNavigationContribution[],
  access: NavigationAccess = {}
): SettingsSectionGroup[] {
  const visible = filterAccessibleNavigation(navigation, access);
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

export function isSettingsSurfacePath(path: string, groups: SettingsSectionGroup[]) {
  if (isSettingsDeepLinkPath(path)) {
    return true;
  }
  return listSettingsSections(groups).some(section => isSettingsSectionActive(section, path));
}

export function hasSettingsSections(groups: SettingsSectionGroup[]) {
  return listSettingsSections(groups).length > 0;
}
