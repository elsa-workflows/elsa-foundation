export const workflowEditorOverlaySelector = "[role='dialog'], [aria-modal='true'], [role='menu'], .wf-connect-menu";

const designerOwnedOverlaySelector = ".wf-connect-menu, .wf-dialog, .wf-dialog-backdrop, .wf-editor-actions-popover";

export function isWorkflowEditorKeyboardTarget(target: EventTarget | null) {
  if (!(target instanceof Element)) return false;
  return (target instanceof HTMLElement && target.isContentEditable)
    || /^(INPUT|TEXTAREA|SELECT)$/.test(target.tagName)
    || Boolean(target.closest("[contenteditable='true']"))
    || Boolean(target.closest("[data-studio-code-editor]"));
}

export function isWorkflowEditorOverlayTarget(target: EventTarget | null) {
  return target instanceof Element && Boolean(target.closest(workflowEditorOverlaySelector));
}

export function isWorkflowEditorOverlayOpen(designer: ParentNode | null = null) {
  return Array.from(document.querySelectorAll(workflowEditorOverlaySelector)).some(element =>
    isVisibleOverlay(element) && isDesignerOwnedOverlay(element, designer)
  );
}

function isDesignerOwnedOverlay(element: Element, designer: ParentNode | null) {
  if (designer instanceof Node && designer.contains(element)) return true;
  return Boolean(element.closest(designerOwnedOverlaySelector));
}

function isVisibleOverlay(element: Element) {
  if (!(element instanceof HTMLElement) || element.hidden || element.getAttribute("aria-hidden") === "true") {
    return false;
  }
  const style = window.getComputedStyle(element);
  if (style.display === "none" || style.visibility === "hidden") return false;
  const rect = element.getBoundingClientRect();
  return rect.width > 0 && rect.height > 0;
}

export function isActivityClipboardShortcutBlocked(
  target: EventTarget | null,
  designer: HTMLElement | null = null
) {
  if (isWorkflowEditorKeyboardTarget(target)) return true;
  if (isWorkflowEditorOverlayOpen(designer)) return true;
  if (!(target instanceof Element)) return false;
  if (isWorkflowEditorOverlayTarget(target) && isDesignerOwnedOverlay(target, designer)) return true;
  if (
    designer
    && target !== document.body
    && target !== document.documentElement
    && !designer.contains(target)
  ) {
    return true;
  }
  return false;
}

export function hasNonCollapsedTextSelection() {
  const selection = window.getSelection();
  return !!selection && !selection.isCollapsed && selection.toString().length > 0;
}
