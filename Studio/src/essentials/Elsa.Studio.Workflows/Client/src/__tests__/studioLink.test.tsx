import { type AnchorHTMLAttributes } from "react";
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

function renderLink(
  navigateToStudioPath?: (path: string) => void,
  href = "/workflows/activity-definitions?definition=invoice",
  extra?: Omit<AnchorHTMLAttributes<HTMLAnchorElement>, "href" | "children" | "onClick">
) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(
    <StudioLink href={href} navigateToStudioPath={navigateToStudioPath} {...extra}>
      Open source
    </StudioLink>
  ));
  active = { root, container };
  return container.querySelector("a")!;
}

function dispatchClick(link: HTMLAnchorElement, init: MouseEventInit) {
  const event = new MouseEvent("click", { bubbles: true, cancelable: true, button: 0, ...init });
  let defaultPrevented = false;
  // React 19 delegates to the render root. Read the link's decision there, then
  // cancel jsdom's default navigation on window so fall-through hrefs stay quiet.
  const note = () => { defaultPrevented = event.defaultPrevented; };
  const quiet = (e: Event) => { e.preventDefault(); };
  active!.container.addEventListener("click", note);
  window.addEventListener("click", quiet);
  try {
    flushSync(() => link.dispatchEvent(event));
  } finally {
    active!.container.removeEventListener("click", note);
    window.removeEventListener("click", quiet);
  }
  return { defaultPrevented };
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

  it("still intercepts an in-app path when target is _self", () => {
    const navigateToStudioPath = vi.fn();
    const link = renderLink(navigateToStudioPath, "/workflows/activity-definitions?definition=invoice", { target: "_self" });
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

  it.each([
    { name: "target=_blank", href: "/workflows/activity-definitions", extra: { target: "_blank" as const } },
    { name: "download", href: "/workflows/activity-definitions", extra: { download: "export.json" } },
    { name: "an external https URL", href: "https://example.com/docs", extra: {} },
    { name: "mailto", href: "mailto:ops@example.com", extra: {} }
  ])("leaves $name to the browser", ({ href, extra }) => {
    const navigateToStudioPath = vi.fn();
    const link = renderLink(navigateToStudioPath, href, extra);
    const event = dispatchClick(link, {});
    expect(event.defaultPrevented).toBe(false);
    expect(navigateToStudioPath).not.toHaveBeenCalled();
  });

  it("leaves an absolute same-origin URL outside the Studio base path to the browser", () => {
    const navigateToStudioPath = vi.fn();
    const link = renderLink(navigateToStudioPath, `${window.location.origin}/admin`);
    const event = dispatchClick(link, {});
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
