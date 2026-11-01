/**
 * Width rules for resizable activity nodes. A node without an explicit width renders at the
 * `.wf-node` CSS default; a resized node stores its width in the Layout Sidecar record.
 */
export const defaultNodeWidth = 246;
export const minNodeWidth = defaultNodeWidth;
export const maxNodeWidth = 640;

/**
 * A saved layout width the canvas can apply, or undefined for the default width. Clamped to the range
 * the resize handle allows, since other writers (imports, JSON edits) can store any width.
 */
export function readSavedNodeWidth(width: number | null | undefined): number | undefined {
  if (typeof width !== "number" || !Number.isFinite(width) || width <= 0) return undefined;
  return Math.min(maxNodeWidth, Math.max(minNodeWidth, width));
}

/** The width a layout record should keep: the node's explicit width, else the record's saved one. */
export function persistedNodeWidth(node: { width?: number }, saved: number | null | undefined): number | undefined {
  const width = readSavedNodeWidth(node.width);
  return width !== undefined ? Math.round(width) : readSavedNodeWidth(saved);
}
