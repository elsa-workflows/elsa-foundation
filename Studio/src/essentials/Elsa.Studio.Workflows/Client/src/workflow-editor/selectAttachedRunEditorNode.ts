/**
 * Bind canvas selection to the Inspector while a Test Run overlay or captured snapshot is shown.
 * React Flow may emit a select change without onNodeClick (already-selected node, remount, or a
 * click classified as a tiny drag), so the host select path — not only the pointer click handler —
 * must reveal Configuration / Run data.
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
