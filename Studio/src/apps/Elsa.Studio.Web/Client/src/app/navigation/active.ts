import type { StudioNavigationContribution } from "../../sdk";
import { isSettingsSectionActive, settingsPathFor } from "../settings/settingsSections";

export function isDashboardPath(path: string) {
  return path === "/" || path === "/dashboard" || path === "/overview";
}

/** True when `path` is `prefix` or a later segment under it. `/modules` does not match `/modulesX`. */
export function isExactSegmentPrefix(path: string, prefix: string | null | undefined) {
  if (!prefix) return false;
  const normalized = prefix.length > 1 && prefix.endsWith("/") ? prefix.slice(0, -1) : prefix;
  return path === normalized || path.startsWith(`${normalized}/`);
}

export function navigationMatchScore(item: StudioNavigationContribution, path: string) {
  if ((item.id === "dashboard" || item.id === "home") && isDashboardPath(path)) {
    return 1;
  }

  let score = 0;
  if (path === item.path) {
    score = Math.max(score, item.path.length * 2);
  }
  if (isExactSegmentPrefix(path, item.activePathPrefix)) {
    score = Math.max(score, item.activePathPrefix!.length);
  }
  if (item.path !== "/" && isExactSegmentPrefix(path, item.path)) {
    score = Math.max(score, item.path.length);
  }
  if (isExactSegmentPrefix(path, settingsPathFor(item))) {
    score = Math.max(score, settingsPathFor(item).length);
  }
  return score;
}

export function resolveActiveNavigationItem(
  path: string,
  items: readonly StudioNavigationContribution[]
) {
  let best: StudioNavigationContribution | undefined;
  let bestScore = 0;
  for (const item of items) {
    const score = navigationMatchScore(item, path);
    if (score > bestScore) {
      best = item;
      bestScore = score;
    }
  }
  return bestScore > 0 ? best : undefined;
}

export function isNavigationItemActive(
  item: StudioNavigationContribution,
  path: string,
  items: readonly StudioNavigationContribution[] = [item]
) {
  const winner = resolveActiveNavigationItem(path, items);
  return winner?.id === item.id;
}
