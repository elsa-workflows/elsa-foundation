import React, { useLayoutEffect } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { expect, it, vi } from "vitest";
import { useNotificationGuard } from "../workflow-editor/notifications";

it("rejects outcomes and callbacks from a disposed page or obsolete context", () => {
  const container = document.createElement("div");
  const root = createRoot(container);
  let capture!: ReturnType<typeof useNotificationGuard>;
  function Probe({ context }: { context: object }) {
    capture = useNotificationGuard(context);
    return null;
  }
  const first = {};
  flushSync(() => root.render(<React.StrictMode><Probe context={first} /></React.StrictMode>));
  const firstCapture = capture;
  const firstOutcome = capture();
  expect(firstOutcome()).toBe(true);
  flushSync(() => root.render(<React.StrictMode><Probe context={{}} /></React.StrictMode>));
  expect(firstOutcome()).toBe(false);
  expect(firstCapture()()).toBe(false);
  flushSync(() => root.render(<React.StrictMode><Probe context={first} /></React.StrictMode>));
  expect(firstCapture()()).toBe(false);
  const currentCapture = capture;
  expect(currentCapture()()).toBe(true);
  flushSync(() => root.unmount());
  expect(currentCapture()()).toBe(false);
});

it("accepts a current outcome at commit and in the first clipboard completion microtask", async () => {
  const container = document.createElement("div");
  const root = createRoot(container);
  const observed: boolean[] = [];
  const context = {};
  function Probe() {
    const capture = useNotificationGuard(context);
    useLayoutEffect(() => {
      const current = capture();
      observed.push(current());
      void Promise.resolve().then(() => observed.push(current()));
    }, [capture]);
    return null;
  }
  try {
    root.render(<Probe />);
    await vi.waitFor(() => expect(observed).toHaveLength(2));
    expect(observed).toEqual([true, true]);
  } finally {
    flushSync(() => root.unmount());
  }
});

it("keeps domain ownership across a retained visit but retires its confirmation", () => {
  const container = document.createElement("div"); const root = createRoot(container);
  let capture!: ReturnType<typeof useNotificationGuard>;
  const first = {}; const second = {};
  function Probe({ context }: { context: object }) { capture = useNotificationGuard(context); return null; }
  const render = (context: object, mode: "visible" | "hidden" = "visible") => flushSync(() => root.render(<React.Activity mode={mode}><Probe context={context} /></React.Activity>));
  try {
    render(first); const result = capture(); expect(result()).toBe(true);
    render(first, "hidden"); expect(result()).toBe(false); expect(result.isOwned()).toBe(true);
    render(first); expect(result()).toBe(false); expect(result.isOwned()).toBe(true);
    render(second); expect(result.isOwned()).toBe(false);
    render(first); expect(result.isOwned()).toBe(false);
  } finally { flushSync(() => root.unmount()); }
});
