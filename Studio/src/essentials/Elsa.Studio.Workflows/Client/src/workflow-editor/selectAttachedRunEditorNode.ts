/**
 * Bind canvas selection to the Inspector while a Test Run overlay is attached.
 * React Flow may emit a select change without onNodeClick (already-selected node, remount, or a
 * click classified as a tiny drag), so the host select path — not only the pointer click handler —
 * must reveal Configuration / Run data. A pinned snapshot without an attached run keeps normal
 * selection and does not switch the inspector.
 */
export function selectAttachedRunEditorNode(
  nodeId: string | null,
  select: (nodeId: string | null) => void,
  revealRunInspector: boolean,
  onRevealRunInspector: () => void
) {
  select(nodeId);
  if (nodeId && revealRunInspector) onRevealRunInspector();
}

export const attachedRunEditorNodeSelector = ".react-flow__node";
const interactivePointerDownSelector = "button, input, select, textarea, a, [role=button]";

export type AttachedRunEditorNodePointerDownEvent = {
  button: number;
  target: EventTarget | null;
};

export function attachedRunEditorNodeIdFromEventTarget(target: EventTarget | null) {
  if (!(target instanceof Element)) return null;
  const node = target.closest(attachedRunEditorNodeSelector);
  return node instanceof HTMLElement ? node.getAttribute("data-id") : null;
}

/**
 * Primary-button presses on a React Flow node. The node wrapper is role=button, so the inner-control
 * walk stops at `.react-flow__node` and does not treat that wrapper as a control.
 */
export function shouldSelectAttachedRunEditorNodeOnPointerDown(event: AttachedRunEditorNodePointerDownEvent) {
  if (event.button !== 0) return false;
  if (!(event.target instanceof Element)) return false;
  const nodeRoot = event.target.closest(attachedRunEditorNodeSelector);
  if (!nodeRoot) return false;
  let element: Element | null = event.target;
  while (element && element !== nodeRoot) {
    if (element.matches(interactivePointerDownSelector)) return false;
    element = element.parentElement;
  }
  return true;
}

export function selectAttachedRunEditorNodeOnCanvasPointerDown(
  event: AttachedRunEditorNodePointerDownEvent,
  attached: boolean,
  onSelect: (nodeId: string) => void
) {
  if (!attached) return false;
  if (!shouldSelectAttachedRunEditorNodeOnPointerDown(event)) return false;
  const nodeId = attachedRunEditorNodeIdFromEventTarget(event.target);
  if (!nodeId) return false;
  onSelect(nodeId);
  return true;
}
