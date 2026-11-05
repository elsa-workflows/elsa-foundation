import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { CopyValueButton } from "../workflow-editor/executableShared";

let root: Root;
let container: HTMLDivElement;
const onCopied = vi.fn(), onCopyFailed = vi.fn();
const clipboardDescriptor = Object.getOwnPropertyDescriptor(navigator, "clipboard");
const execCommandDescriptor = Object.getOwnPropertyDescriptor(document, "execCommand");
beforeEach(() => {
  onCopied.mockClear(); onCopyFailed.mockClear();
  container = document.createElement("div"); document.body.appendChild(container); root = createRoot(container);
});
afterEach(() => {
  flushSync(() => root.unmount()); container.remove();
  if (clipboardDescriptor) Object.defineProperty(navigator, "clipboard", clipboardDescriptor);
  else Reflect.deleteProperty(navigator, "clipboard");
  if (execCommandDescriptor) Object.defineProperty(document, "execCommand", execCommandDescriptor);
  else Reflect.deleteProperty(document, "execCommand");
});
function render(value = "first", scope: object = {}) {
  flushSync(() => root.render(<CopyValueButton value={value} scope={scope} ariaLabel="Copy identifier" copiedLabel="identifier" onCopied={onCopied} onCopyFailed={onCopyFailed} />));
}
function copy() { flushSync(() => container.querySelector<HTMLButtonElement>("button")!.click()); }

it.each(["disposal", "value replacement", "scope replacement"])("suppresses delayed clipboard callbacks after %s", async change => {
  let finish!: () => void;
  const work = new Promise<void>(resolve => { finish = resolve; });
  const writeText = vi.fn(() => work);
  Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText } });
  const scope = {};
  render("first", scope); copy();
  expect(writeText).toHaveBeenCalledWith("first");
  if (change === "disposal") flushSync(() => root.render(<p>Another page</p>));
  else render(change === "value replacement" ? "second" : "first", change === "scope replacement" ? {} : scope);
  finish(); await work; await Promise.resolve();
  expect(onCopied).not.toHaveBeenCalled();
  expect(onCopyFailed).not.toHaveBeenCalled();
});

it.each(["absent", "throws", "refuses"] as const)("cleans the %s fallback on current-copy failure and permits a successful retry", async fallback => {
  const legacy = fallback === "absent" ? undefined : vi.fn(() => { if (fallback === "throws") throw new Error("Clipboard denied"); return false; });
  Object.defineProperty(document, "execCommand", { configurable: true, value: legacy });
  const writeText = vi.fn().mockRejectedValueOnce(new Error("Clipboard unavailable")).mockResolvedValue(undefined);
  Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText } });
  render(); copy();
  await vi.waitFor(() => expect(onCopyFailed).toHaveBeenCalledWith("identifier"));
  expect(onCopied).not.toHaveBeenCalled();
  expect(document.querySelectorAll("textarea")).toHaveLength(0);
  copy();
  await vi.waitFor(() => expect(onCopied).toHaveBeenCalledWith("identifier"));
  expect(onCopied).toHaveBeenCalledTimes(1);
  expect(document.querySelectorAll("textarea")).toHaveLength(0);
});
