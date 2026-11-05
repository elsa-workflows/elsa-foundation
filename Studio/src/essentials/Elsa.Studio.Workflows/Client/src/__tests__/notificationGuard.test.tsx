import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { expect, it } from "vitest";
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
