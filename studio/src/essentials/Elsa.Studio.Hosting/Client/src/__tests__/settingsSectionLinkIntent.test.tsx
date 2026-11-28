import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { SettingsRoute } from "../app/settings/SettingsRoute";
import { studioWorkspaceTabRequestedEvent, type StudioWorkspaceTabRequest, type StudioNavigationContribution } from "../sdk";

const destinations: StudioNavigationContribution[] = [
  { id: "workflows-activity-availability", label: "Activity availability", path: "/workflows/activity-availability", placement: "authoring" },
  { id: "workflows-runtime-diagnostics", label: "Runtime evidence settings", path: "/workflows/runtime-diagnostics", placement: "runtime" }
];
let container: HTMLDivElement;
let root: Root;
let visited: string[];
let requests: StudioWorkspaceTabRequest[];
let handled: boolean;
const onRequest = (event: Event) => {
  requests.push((event as CustomEvent<StudioWorkspaceTabRequest>).detail);
  if (handled) event.preventDefault();
};
beforeEach(() => {
  container = document.createElement("div"); document.body.append(container);
  root = createRoot(container); visited = []; requests = []; handled = false;
  window.addEventListener(studioWorkspaceTabRequestedEvent, onRequest);
  flushSync(() => root.render(<SettingsRoute path="/settings" navigation={destinations}
    onNavigate={path => visited.push(path)} renderSection={section => <p>{section.label} content</p>} />));
});
afterEach(() => {
  flushSync(() => root.unmount()); container.remove();
  window.removeEventListener(studioWorkspaceTabRequestedEvent, onRequest);
});
// Observe the component's cancellation at document bubble, after React's root handler.
// Cancel there only to prevent jsdom from attempting a real browser navigation.
function click(link: HTMLAnchorElement, modifiers: MouseEventInit = {}) {
  let componentPrevented: boolean | undefined;
  const nativeCleanup = (event: MouseEvent) => { componentPrevented = event.defaultPrevented; event.preventDefault(); };
  document.addEventListener("click", nativeCleanup, { once: true });
  try { flushSync(() => link.dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true, button: 0, ...modifiers }))); }
  finally { document.removeEventListener("click", nativeCleanup); }
  return componentPrevented;
}
for (const destination of destinations) {
  describe(destination.label, () => {
    const targetPath = `/settings/${destination.id}`;
    const link = () => container.querySelector<HTMLAnchorElement>(`nav[aria-label="Settings sections"] a[href="${targetPath}"]`)!;
    it("keeps ordinary section navigation, qualified href and Settings styling", () => {
      expect(link()).not.toBeNull();
      expect(link().className).toBe("settings-nav-link");
      expect(link().getAttribute("aria-current")).toBe(destination.placement === "authoring" ? "page" : null);
      expect(click(link())).toBe(true);
      expect(visited).toEqual([targetPath]); expect(requests).toEqual([]);
    });
    it("requests a separate workspace tab for Alt/Option without replacing the current screen", () => {
      handled = true;
      expect(click(link(), { altKey: true })).toBe(true);
      expect(requests).toEqual([{ path: targetPath, activate: true }]);
      expect(visited).toEqual([]);
    });
    it("preserves native fallback when no workspace host handles Alt/Option", () => {
      expect(click(link(), { altKey: true })).toBe(false);
      expect(requests).toEqual([{ path: targetPath, activate: true }]);
      expect(visited).toEqual([]);
    });
    it.each([
      ["Ctrl", { ctrlKey: true }], ["Cmd", { metaKey: true }], ["Shift", { shiftKey: true }],
      ["middle", { button: 1 }], ["Alt+Ctrl", { altKey: true, ctrlKey: true }]
    ] as const)("leaves %s click to native browser handling", (_name, modifiers) => {
      handled = true;
      expect(click(link(), modifiers)).toBe(false);
      expect(requests).toEqual([]); expect(visited).toEqual([]);
    });
  });
}
