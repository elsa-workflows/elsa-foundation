import type { ActivityCatalogItem, ActivityNode } from "../workflowTypes";
import { getChildSlots, type ChildSlot } from "../workflowAdapter";

/** Presentation hints supplied by the activity catalog, independent of category or CLR namespace. */
export interface ActivityPaletteHints {
  containerModes?: string[];
  slotIds?: string[];
  ancestorActivityTypes?: string[];
  resourceKinds?: string[];
  description?: string;
}

export interface ActivityPaletteContext {
  resourceKind: "workflow-definition" | "activity-definition-graph";
  containerMode?: string;
  slotId?: string;
  ancestorActivityTypes: string[];
}

export function getActivityPaletteContext(
  resourceKind: ActivityPaletteContext["resourceKind"],
  root: ActivityNode | null | undefined,
  ownerId: string | undefined,
  slot: ChildSlot | null | undefined,
  catalog: Map<string, ActivityCatalogItem>
): ActivityPaletteContext {
  const findAncestors = (node: ActivityNode, ancestors: string[]): string[] | null => {
    const activityType = catalog.get(node.activityVersionId)?.activityTypeKey;
    const path = activityType ? [...ancestors, activityType] : ancestors;
    if (node.nodeId === ownerId) return path;
    for (const child of getChildSlots(node, catalog).flatMap(childSlot => childSlot.activities)) {
      const found = findAncestors(child, path);
      if (found) return found;
    }
    return null;
  };
  return {
    resourceKind,
    containerMode: slot?.mode,
    slotId: slot?.id,
    ancestorActivityTypes: root && ownerId ? findAncestors(root, []) ?? [] : []
  };
}

export function isActivityOfferedInContext(activity: ActivityCatalogItem, context: ActivityPaletteContext): boolean {
  const hints = activity.paletteHints;
  if (!hints) return true;
  const matches = (allowed: string[] | undefined, actual: string | undefined) =>
    !allowed?.length || (!!actual && allowed.includes(actual));
  return matches(hints.containerModes, context.containerMode)
    && matches(hints.slotIds, context.slotId)
    && matches(hints.resourceKinds, context.resourceKind)
    && (!hints.ancestorActivityTypes?.length
      || hints.ancestorActivityTypes.some(type => context.ancestorActivityTypes.includes(type)));
}

/** A misplaced existing node stays inspectable; hints do not invalidate or rewrite the document. */
export function activityPaletteNotice(activity: ActivityCatalogItem | null | undefined, context: ActivityPaletteContext) {
  return activity && !isActivityOfferedInContext(activity, context)
    ? activity.paletteHints?.description ?? "This activity is intended for a different authoring context."
    : undefined;
}
