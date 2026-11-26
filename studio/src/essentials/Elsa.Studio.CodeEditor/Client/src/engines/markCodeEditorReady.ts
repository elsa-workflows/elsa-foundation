/** Marks the nearest `[data-studio-code-editor]` root when an engine is mounted, so shortcut guards can tell a live editor from a fallback-to-rich swap. */
export function markCodeEditorReady(element: HTMLElement | null, ready: boolean) {
  const root = element?.closest("[data-studio-code-editor]");
  if (!(root instanceof HTMLElement)) return;
  if (ready) root.dataset.studioCodeEditorReady = "true";
  else delete root.dataset.studioCodeEditorReady;
}
