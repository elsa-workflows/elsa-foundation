import { expect } from "vitest";
import { getChildSlots, resolveScope, type ChildSlot, type ScopeFrame, type WorkflowNodeData } from "../workflowAdapter";
import type { ActivityCatalogItem, ActivityNode } from "../workflowTypes";

interface NavigableCanvas {
  nodes: { id: string; data: WorkflowNodeData }[];
}

export function expectExecutableSlotsNavigable(
  root: ActivityNode,
  owner: ActivityNode,
  catalog: ActivityCatalogItem[],
  buildCanvases: ((onNavigate: (frames: ScopeFrame[]) => void) => NavigableCanvas)[],
  visibleSlots: ChildSlot[] = getChildSlots(owner, catalog)
) {
  for (const buildCanvas of buildCanvases) {
    let destination: ScopeFrame[] | null = null;
    const canvas = buildCanvas(frames => { destination = frames; });
    const node = canvas.nodes.find(candidate => candidate.id === owner.nodeId);
    expect(node, `owner ${owner.nodeId} is rendered on the inspection canvas`).toBeDefined();
    expect(node!.data.childSlots.map(slot => slot.id)).toEqual(visibleSlots.map(slot => slot.id));

    for (const slot of visibleSlots) {
      destination = null;
      expect(node!.data.onEnterSlot, `owner ${owner.nodeId} exposes slot navigation`).toBeDefined();
      node!.data.onEnterSlot!(slot);
      expect(destination, `slot ${slot.label} navigates to a scope`).not.toBeNull();
      const scope = resolveScope(root, destination!, catalog);
      expect(scope, `slot ${slot.label} resolves in the adapted graph`).toBeDefined();
      const reachableNodeIds = new Set([
        scope!.owner.nodeId,
        ...scope!.slot.activities.map(activity => activity.nodeId)
      ]);
      for (const activity of slot.activities) {
        expect(reachableNodeIds, `child ${activity.nodeId} remains reachable through ${slot.label}`).toContain(activity.nodeId);
      }
    }
  }
}