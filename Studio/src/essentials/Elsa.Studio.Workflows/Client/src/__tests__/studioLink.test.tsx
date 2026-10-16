import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { studioNavigationRequestedEvent } from "@elsa-workflows/studio-sdk";
import { afterEach, describe, expect, it, vi } from "vitest";
import { StudioLink } from "../StudioLink";

let active: { root: Root; container: HTMLElement } | null = null;

afterEach(() => {
  if (active) {
    flushSync(() => active!.root.unmount());
    active.container.remove();
    active = null;
  }
  window.history.replaceState({}, "", "/");
});

function renderLink(navigateToStudioPath?: (path: string) => void, href = "/workflows/activity-definitions?definition=invoice") {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(
    <StudioLink href={href} navigateToStudioPath={navigateToStudioPath}>
      Open source
    </StudioLink>
  ));
  active = { root, container };
  return container.querySelector("a")!;
}

function dispatchClick(link: HTMLAnchorElement, init: MouseEventInit) {
  const event = new MouseEvent("click", { bubbles: true, cancelable: true, button: 0, ...init });
  flushSync(() => link.dispatchEvent(event));
  return event;
}

describe("StudioLink", () => {
  it("keeps the real href so copy-link and new-tab gestures still work", () => {
    const link = renderLink();
    expect(link.getAttribute("href")).toBe("/workflows/activity-definitions?definition=invoice");
  });

  it("routes an unmodified primary click through navigateToStudioPath", () => {
    const navigateToStudioPath = vi.fn();
    const link = renderLink(navigateToStudioPath);
    const event = dispatchClick(link, {});
    expect(event.defaultPrevented).toBe(true);
    expect(navigateToStudioPath).toHaveBeenCalledWith("/workflows/activity-definitions?definition=invoice");
  });

  it.each([
    { name: "ctrl-click", init: { ctrlKey: true } },
    { name: "meta-click", init: { metaKey: true } },
    { name: "shift-click", init: { shiftKey: true } },
    { name: "alt-click", init: { altKey: true } },
    { name: "middle-click", init: { button: 1 } }
  ])("leaves $name to the browser", ({ init }) => {
    const navigateToStudioPath = vi.fn();
    const link = renderLink(navigateToStudioPath, "#stay");
    const event = dispatchClick(link, init);
    expect(event.defaultPrevented).toBe(false);
    expect(navigateToStudioPath).not.toHaveBeenCalled();
  });

  it("honours the unsaved-changes guard and does not navigate", () => {
    const navigateToStudioPath = vi.fn();
    const link = renderLink(navigateToStudioPath);
    const block = (event: Event) => event.preventDefault();
    window.addEventListener(studioNavigationRequestedEvent, block);
    try {
      const event = dispatchClick(link, {});
      expect(event.defaultPrevented).toBe(true);
      expect(navigateToStudioPath).not.toHaveBeenCalled();
      expect(`${window.location.pathname}${window.location.search}`).toBe("/");
    } finally {
      window.removeEventListener(studioNavigationRequestedEvent, block);
    }
  });
});
