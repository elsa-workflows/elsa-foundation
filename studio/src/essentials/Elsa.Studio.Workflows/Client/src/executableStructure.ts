import type { ActivityNode, WorkflowExecutableChildSlot, WorkflowExecutableNode } from "./workflowTypes";
import type { ChildSlot } from "./workflowAdapter";

const executableChildSlots = new WeakMap<ActivityNode, ChildSlot[]>();

export function createExecutableChildSlots(
  slots: WorkflowExecutableChildSlot[],
  adapt: (node: WorkflowExecutableNode) => ActivityNode,
  mode: ChildSlot["mode"] = "generic"
): ChildSlot[] {
  return slots.map((slot, index) => ({
    id: `executable:${index}:${encodeURIComponent(slot.name)}`,
    label: slot.name,
    property: slot.name,
    cardinality: "many",
    mode,
    activities: slot.activities.map(adapt)
  }));
}

export function registerExecutableChildSlots(activity: ActivityNode, slots: ChildSlot[]) {
  executableChildSlots.set(activity, slots);
}

export function readExecutableChildSlots(activity: ActivityNode): ChildSlot[] | undefined {
  return executableChildSlots.get(activity);
}