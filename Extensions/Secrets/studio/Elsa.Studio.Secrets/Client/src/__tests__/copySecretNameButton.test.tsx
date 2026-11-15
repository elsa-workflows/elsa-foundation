import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { CopySecretNameButton } from "../CopySecretNameButton";

let root: Root;
let container: HTMLDivElement;
const notifications = { notify: vi.fn(() => ({ id: "copy", update() {}, dismiss() {} })), track: <T,>(work: Promise<T>) => work };
const clipboardDescriptor = Object.getOwnPropertyDescriptor(navigator, "clipboard");
beforeEach(() => {
  notifications.notify.mockClear();
  container = document.createElement("div"); document.body.appendChild(container); root = createRoot(container);
});
afterEach(() => {
  flushSync(() => root.unmount()); container.remove();
  if (clipboardDescriptor) Object.defineProperty(navigator, "clipboard", clipboardDescriptor);
  else Reflect.deleteProperty(navigator, "clipboard");
});
function render(name = "first", scope: object = {}) {
  flushSync(() => root.render(<CopySecretNameButton name={name} scope={scope} notifications={notifications} />));
}
function copy() { flushSync(() => container.querySelector<HTMLButtonElement>("button")!.click()); }

it.each(["disposal", "name replacement", "backend replacement"])("does not toast a delayed secret-name copy after %s", async change => {
  let finish!: () => void;
  const work = new Promise<void>(resolve => { finish = resolve; });
  Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText: vi.fn(() => work) } });
  const scope = {};
  render("first", scope); copy();
  if (change === "disposal") flushSync(() => root.render(<p>Another page</p>));
  else render(change === "name replacement" ? "second" : "first", change === "backend replacement" ? {} : scope);
  finish(); await work; await Promise.resolve();
  expect(notifications.notify).not.toHaveBeenCalled();
  expect(container.textContent).not.toContain("Copy failed");
});

it("keeps the current failure inline and toasts a successful retry once", async () => {
  const writeText = vi.fn().mockRejectedValueOnce(new Error("Clipboard unavailable")).mockResolvedValue(undefined);
  Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText } });
  render(); copy();
  await vi.waitFor(() => expect(container.textContent).toContain("Copy failed"));
  expect(notifications.notify).not.toHaveBeenCalled();
  copy();
  await vi.waitFor(() => expect(notifications.notify).toHaveBeenCalledTimes(1));
  expect(container.textContent).not.toContain("Copy failed");
});
