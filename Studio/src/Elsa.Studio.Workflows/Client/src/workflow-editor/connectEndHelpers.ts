import type { WorkflowConnectSource } from "./editorTypes";

export function clientPointFromEvent(event: MouseEvent | TouchEvent) {
  if ("changedTouches" in event && event.changedTouches.length > 0)
    return { x: event.changedTouches[0].clientX, y: event.changedTouches[0].clientY };

  return { x: (event as MouseEvent).clientX, y: (event as MouseEvent).clientY };
}

export function isConnectEndOverExistingWorkflowNode(event: MouseEvent | TouchEvent) {
  const point = clientPointFromEvent(event);
  const releaseTarget = document.elementFromPoint?.(point.x, point.y) as HTMLElement | null | undefined;
  const target = releaseTarget ?? (event.target as HTMLElement | null);
  return !!target?.closest(".react-flow__handle, .react-flow__node");
}

export function resolveConnectEndSource(
  currentSource: WorkflowConnectSource | null,
  connectionState: { fromNode?: { id?: string | null } | null; fromHandle?: { id?: string | null } | null }
): WorkflowConnectSource | null {
  if (currentSource) return currentSource;
  const nodeId = connectionState.fromNode?.id;
  return nodeId ? { nodeId, handleId: connectionState.fromHandle?.id ?? null } : null;
}
