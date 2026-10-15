/** Marks the nearest `[data-studio-code-editor]` root as ready for deterministic tests. */
export function markCodeEditorReady(element: HTMLElement | null, ready: boolean) {
  const root = element?.closest("[data-studio-code-editor]");
  if (!(root instanceof HTMLElement)) return;
  if (ready) root.dataset.studioCodeEditorReady = "true";
  else delete root.dataset.studioCodeEditorReady;
}
