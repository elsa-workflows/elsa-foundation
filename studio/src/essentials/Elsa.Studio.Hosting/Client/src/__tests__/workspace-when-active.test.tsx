import React, { useEffect } from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { StudioWorkspaceTabProvider, useStudioWorkspaceWhenActive, type StudioWorkspaceTabHandle } from "../sdk";

let container: HTMLDivElement;
let root: Root;

async function update(action: () => void) {
  flushSync(action);
  for (let index = 0; index < 3; index++) await new Promise(resolve => setTimeout(resolve, 0));
}

beforeEach(() => {
  container = document.createElement("div");
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await update(() => root.unmount());
  container.remove();
});

describe("useStudioWorkspaceWhenActive", () => {
  it("holds navigation from a background tab until it returns, after the screen's listeners are back", async () => {
    let active = true;
    const handle: StudioWorkspaceTabHandle = { id: "a", active: true, location: "/a", isActive: () => active, markPendingWork: vi.fn(), setTitle: () => {} };
    const heard = vi.fn();
    let whenActive: (action: () => void) => void = () => {};
    function Screen() {
      whenActive = useStudioWorkspaceWhenActive();
      // Declared after the hook, like a screen's own popstate listener.
      useEffect(() => {
        window.addEventListener("test-navigation", heard);
        return () => window.removeEventListener("test-navigation", heard);
      }, []);
      return null;
    }
    const tab = (mode: "visible" | "hidden") => <React.Activity mode={mode}><StudioWorkspaceTabProvider value={handle}><Screen /></StudioWorkspaceTabProvider></React.Activity>;
    await update(() => root.render(tab("visible")));

    const ran = vi.fn(() => window.dispatchEvent(new Event("test-navigation")));
    whenActive(ran);
    expect(ran).toHaveBeenCalledTimes(1);

    // A request completes after another tab became active.
    active = false;
    await update(() => root.render(tab("hidden")));
    const first = vi.fn();
    const latest = vi.fn(() => window.dispatchEvent(new Event("test-navigation")));
    whenActive(first);
    whenActive(latest);
    expect(latest).not.toHaveBeenCalled();
    expect(handle.markPendingWork).toHaveBeenCalled();

    active = true;
    await update(() => root.render(tab("visible")));
    expect(first).not.toHaveBeenCalled();
    expect(latest).toHaveBeenCalledTimes(1);
    expect(heard).toHaveBeenCalledTimes(2);
  });

  it("runs at once outside a workspace tab", async () => {
    let whenActive: (action: () => void) => void = () => {};
    function Screen() {
      whenActive = useStudioWorkspaceWhenActive();
      return null;
    }
    await update(() => root.render(<Screen />));
    const ran = vi.fn();
    whenActive(ran);
    expect(ran).toHaveBeenCalledTimes(1);
  });
});
