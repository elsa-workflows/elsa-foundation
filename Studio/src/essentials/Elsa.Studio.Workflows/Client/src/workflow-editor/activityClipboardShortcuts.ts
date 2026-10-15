export const workflowEditorOverlaySelector = "[role='dialog'], [aria-modal='true'], [role='menu'], .wf-connect-menu";

const designerOwnedOverlaySelector = ".wf-connect-menu, .wf-dialog, .wf-dialog-backdrop, .wf-editor-actions-popover";
const monacoEditorSelector = ".monaco-editor, .monaco-mouse-cursor-text, textarea.inputarea";

export function isWorkflowEditorKeyboardTarget(targetOrEvent: EventTarget | Event | null) {
  return collectShortcutNodes(targetOrEvent).some(isEditableOrCodeEditorNode);
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
  targetOrEvent: EventTarget | Event | null,
  designer: HTMLElement | null = null
) {
  if (isWorkflowEditorKeyboardTarget(targetOrEvent)) return true;
  if (isWorkflowEditorOverlayOpen(designer)) return true;
  const target = resolvePrimaryTarget(targetOrEvent);
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

function collectShortcutNodes(targetOrEvent: EventTarget | Event | null): EventTarget[] {
  if (targetOrEvent instanceof Event) {
    const path = typeof targetOrEvent.composedPath === "function" ? targetOrEvent.composedPath() : [];
    if (path.length > 0) return path;
    return targetOrEvent.target != null ? [targetOrEvent.target] : [];
  }
  return targetOrEvent != null ? [targetOrEvent] : [];
}

function resolvePrimaryTarget(targetOrEvent: EventTarget | Event | null): EventTarget | null {
  if (targetOrEvent instanceof Event) return targetOrEvent.target;
  return targetOrEvent;
}

function isEditableOrCodeEditorNode(node: EventTarget | null) {
  const element = asElement(node);
  if (!element) return false;
  if (element instanceof HTMLElement && element.isContentEditable) return true;
  if (/^(INPUT|TEXTAREA|SELECT)$/.test(element.tagName)) return true;
  if (element.closest("[contenteditable='true']")) return true;
  if (element.closest("[data-studio-code-editor]")) return true;
  if (element.closest(monacoEditorSelector)) return true;
  return false;
}

function asElement(node: EventTarget | null): Element | null {
  if (node instanceof Element) return node;
  if (typeof Text !== "undefined" && node instanceof Text) return node.parentElement;
  return null;
}
