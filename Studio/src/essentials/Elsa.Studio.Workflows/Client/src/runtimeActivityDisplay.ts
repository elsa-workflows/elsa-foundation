import type { ActivityCatalogItem, ActivityPresentationRecord } from "./workflowTypes";
import { isTechnicalActivityLabel, resolveActivityLabel } from "./activityPresentation";

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
  const authored = presentation?.displayName?.trim();
  const usablePresentation = authored && !isTechnicalActivityLabel(authored)
    ? presentation
    : undefined;
  const label = resolveActivityLabel(usablePresentation, catalogItem, activityType);
  if (!isTechnicalActivityLabel(label) && (catalogItem || usablePresentation)) return label;
  return getIntrinsicActivityDisplay(activityType) ?? label;
}
