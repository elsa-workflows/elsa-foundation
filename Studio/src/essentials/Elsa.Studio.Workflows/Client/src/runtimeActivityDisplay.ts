import type { ActivityCatalogItem, ActivityPresentationRecord } from "./workflowTypes";
import { resolveActivityLabel } from "./activityPresentation";

// Engine-owned operations retain friendly names when a historical catalog is unavailable.
export function getIntrinsicActivityDisplay(activityType: string): string | undefined {
  const kind = activityType.match(/^(?:executable-missing:)?elsa\.intrinsic\.([a-z-]+)(?:@[^@]+)?$/)?.[1]?.replaceAll("-", "");
  const name = ["Set", "Merge", "Reduce", "Return", "Control", "SetOutput", "SetCorrelationId", "SetInstanceName", "Finish"]
    .find(value => value.toLowerCase() === kind);
  return name ? (name === "Set" ? "Set Variable" : name.replace(/([a-z0-9])([A-Z])/g, "$1 $2")) : undefined;
}

export function resolveRuntimeActivityLabel(
  presentation: ActivityPresentationRecord | null | undefined,
  catalogItem: ActivityCatalogItem | null | undefined,
  activityType: string
) {
  const label = resolveActivityLabel(presentation, catalogItem, activityType);
  return !catalogItem && !presentation?.displayName?.trim()
    ? getIntrinsicActivityDisplay(activityType) ?? label
    : label;
}
