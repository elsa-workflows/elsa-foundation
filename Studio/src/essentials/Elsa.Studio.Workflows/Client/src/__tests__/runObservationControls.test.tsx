import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { RunObservationControls, RunObservationNotice } from "../workflow-editor/RunObservationControls";

let container: HTMLDivElement;
let root: Root;
beforeEach(() => { container = document.createElement("div"); document.body.append(container); root = createRoot(container); });
afterEach(() => { flushSync(() => root.unmount()); container.remove(); });

it("pauses observation, keeps its focused button, and permits an explicit refresh while paused", () => {
  const change = vi.fn(); const refresh = vi.fn();
  function Probe() {
    const [following, setFollowing] = React.useState(true);
    return <RunObservationControls following={following} onFollowingChange={value => { change(value); setFollowing(value); }} onRefresh={refresh} />;
  }
  flushSync(() => root.render(<Probe />));
  const toggle = container.querySelector<HTMLButtonElement>('[aria-label="Pause live updates"]')!;
  toggle.focus(); flushSync(() => toggle.click());
  expect(change).toHaveBeenLastCalledWith(false);
  expect(document.activeElement).toBe(toggle);
  expect(toggle.getAttribute("aria-label")).toBe("Resume live updates");
  expect(toggle.getAttribute("aria-pressed")).toBe("false");
  expect(toggle.title).toContain("does not stop the workflow");
  container.querySelector<HTMLButtonElement>('[aria-label="Refresh execution"]')!.click();
  expect(refresh).toHaveBeenCalledOnce();
  expect(change).toHaveBeenCalledTimes(1);
  flushSync(() => toggle.click()); expect(change).toHaveBeenLastCalledWith(true);
});

it("shows completed observation without offering a meaningless pause, and keeps refresh available", () => {
  const change = vi.fn(); const refresh = vi.fn();
  flushSync(() => root.render(<RunObservationControls following onFollowingChange={change} onRefresh={refresh} complete />));
  const toggle = container.querySelector<HTMLButtonElement>('[aria-label="Live updates complete"]')!;
  expect(toggle.disabled).toBe(true); toggle.click(); expect(change).not.toHaveBeenCalled();
  container.querySelector<HTMLButtonElement>('[aria-label="Refresh execution"]')!.click();
  expect(refresh).toHaveBeenCalledOnce();
});

it.each([
  ["paused", "last confirmed evidence"], ["disconnected", "disconnected"], ["unavailable", "unavailable"],
  ["expired", "expired"], ["loading", "Connecting"]
] as const)("keeps %s observation and its failure reason visible", (status, label) => {
  flushSync(() => root.render(<RunObservationNotice status={status} error="Evidence could not be read" />));
  expect(container.querySelector('[role="status"]')?.textContent).toContain(label);
  expect(container.textContent).toContain("Evidence could not be read");
});

it("describes pausing observation without asserting the workflow is still running", () => {
  flushSync(() => root.render(<><RunObservationControls following={false} complete onFollowingChange={() => {}} onRefresh={() => {}} /><RunObservationNotice status="paused" /></>));
  expect(container.querySelector<HTMLButtonElement>('[aria-label="Resume live updates"]')!.disabled).toBe(false);
  expect(container.textContent).not.toContain("The workflow keeps running");
  expect(container.textContent).toContain("does not stop the workflow");
});
