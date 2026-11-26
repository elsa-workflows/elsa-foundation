import type { ActivityCatalogItem } from "./workflowTypes";

export function getActivityDisplay(activity: ActivityCatalogItem) {
  const shortName = activity.activityTypeKey.split(".").at(-1) || activity.activityTypeKey;
  const displayName = activity.displayName?.trim();
  if (!displayName || displayName === activity.activityTypeKey || displayName.includes(".")) {
    return humanizeActivityTypeName(shortName);
  }

  return displayName;
}

function humanizeActivityTypeName(name: string) {
  return name
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2")
    .trim();
}
