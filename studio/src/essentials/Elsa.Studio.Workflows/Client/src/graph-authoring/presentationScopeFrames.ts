import { indexActivityPresentation, resolveActivityLabel } from "../activityPresentation";
import { findActivityNode, getChildSlots, slotCrumbLabel, type ScopeFrame } from "../workflowAdapter";
import type { ActivityCatalogItem, ActivityNode, ActivityPresentationRecord } from "../workflowTypes";

/** Live authored breadcrumb names, retaining hidden selector hops and their navigation addresses. */
export function presentationScopeFrames(root: ActivityNode | null | undefined, frames: ScopeFrame[], catalog: Map<string, ActivityCatalogItem>, presentation: ActivityPresentationRecord[] | undefined): ScopeFrame[] {
  const labels = indexActivityPresentation(presentation);
  return frames.map((frame, index) => {
    if (!frame.label) return frame;
    const frameOwner = findActivityNode(root, frame.ownerNodeId, catalog);
    const primary = frameOwner && getChildSlots(frameOwner, catalog)[0]?.id === frame.slotId;
    const previous = frames[index - 1];
    const rootSlot = root ? getChildSlots(root, catalog)[0] : undefined;
    const labelFrame = primary && previous?.label === "" ? previous
      : primary && index === 0 && rootSlot?.cardinality === "single" && rootSlot.activities[0]?.nodeId === frame.ownerNodeId
        ? { ownerNodeId: root!.nodeId, slotId: rootSlot.id } : frame;
    const owner = findActivityNode(root, labelFrame.ownerNodeId, catalog);
    const slot = owner && getChildSlots(owner, catalog).find(item => item.id === labelFrame.slotId);
    const item = owner && catalog.get(owner.activityVersionId);
    return owner && slot ? { ...frame, label: slotCrumbLabel(resolveActivityLabel(labels.get(owner.nodeId), item, item?.activityTypeKey ?? owner.activityVersionId), slot) } : frame;
  });
}
