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

const interactivePointerDownSelector = "button, input, select, textarea, a, [role=button]";

export type AttachedRunEditorNodePointerDownEvent = {
  button: number;
  target: EventTarget | null;
  currentTarget?: EventTarget | null;
};

/** Primary-button presses on the node itself; ignore right-clicks and inner controls. */
export function shouldSelectAttachedRunEditorNodeOnPointerDown(event: AttachedRunEditorNodePointerDownEvent) {
  if (event.button !== 0) return false;
  if (!(event.target instanceof Element)) return true;
  const nodeRoot = event.currentTarget instanceof Element ? event.currentTarget : null;
  let element: Element | null = event.target;
  while (element && element !== nodeRoot) {
    if (element.matches(interactivePointerDownSelector)) return false;
    element = element.parentElement;
  }
  return true;
}
