import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { createNotificationController } from "../../../sdk";
import { ToastHost } from "../feedback/ToastHost";
import { assertToastPlacementHold, installToastPlacementHold, releaseToastPlacementHold } from "../../../../../../../../tests/acceptance/toastPlacementControl";

let root: Root, container: HTMLDivElement;
let controller: ReturnType<typeof createNotificationController>;
const held: Element[] = [];
const entered = vi.fn();
beforeEach(() => {
  vi.useFakeTimers(); entered.mockReset();
  controller = createNotificationController();
  container = document.createElement("div"); document.body.appendChild(container); root = createRoot(container);
  flushSync(() => root.render(<><ToastHost controller={controller} navigate={() => {}} /><button onMouseEnter={entered}>Other action</button></>));
});
afterEach(() => {
  held.splice(0).forEach(releaseToastPlacementHold);
  flushSync(() => root.unmount()); container.remove(); vi.useRealTimers();
});
function publish(title: string) {
  let id = "";
  flushSync(() => { id = controller.api.notify({ level: "success", title }).id; });
  const element = [...container.querySelectorAll(".studio-toast")].find(node => node.textContent?.includes(title))!;
  expect(element).toBeDefined();
  return { id, element };
}
function tick(ms: number) { flushSync(() => vi.advanceTimersByTime(ms)); }

it("lets an uncontrolled real success expire at the unchanged eight seconds", () => {
  const notice = publish("Unheld confirmation");
  tick(7999); expect(notice.element.isConnected).toBe(true);
  tick(1); expect(notice.element.isConnected).toBe(false);
});

it("holds only the captured notice, preserves destination hover and other timers, then resumes its remaining lifetime", () => {
  const notice = publish("Held confirmation");
  const other = publish("Other confirmation");
  tick(1000);
  flushSync(() => installToastPlacementHold(notice.element)); held.push(notice.element);
  const independent = vi.fn(); window.setTimeout(independent, 250);
  const destination = [...container.querySelectorAll("button")].find(button => button.textContent === "Other action")!;
  flushSync(() => notice.element.dispatchEvent(new MouseEvent("mouseout", { bubbles: true, relatedTarget: destination })));
  expect(entered).toHaveBeenCalledTimes(1);
  tick(9000);
  expect(independent).toHaveBeenCalledTimes(1);
  expect(other.element.isConnected).toBe(false);
  expect(notice.element.isConnected).toBe(true);
  assertToastPlacementHold(notice.element);
  flushSync(() => releaseToastPlacementHold(notice.element));
  tick(6999); expect(notice.element.isConnected).toBe(true);
  tick(1); expect(notice.element.isConnected).toBe(false);
});

it("fails on removal rather than silently holding a replacement with the same title", () => {
  const notice = publish("Captured confirmation");
  flushSync(() => installToastPlacementHold(notice.element)); held.push(notice.element);
  flushSync(() => controller.hideToast(notice.id));
  const replacement = publish("Captured confirmation");
  expect(replacement.element).not.toBe(notice.element);
  expect(() => assertToastPlacementHold(notice.element)).toThrow("disappeared or changed");
});
