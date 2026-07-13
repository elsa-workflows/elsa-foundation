import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AnonymousAuthProvider } from "@elsa-workflows/studio-sdk";
import { DashboardPage } from "../DashboardPage";
import { flush, stubApi, waitUntil, widget } from "./testSupport";

describe("DashboardPage", () => {
  afterEach(() => vi.restoreAllMocks());

  it("moves, resizes, hides, and restores widgets with keyboard-operable controls", async () => {
    const api = stubApi();
    api.dashboardWidgets.add(widget({ id: "first", title: "First", component: () => <span>First body</span> }));
    api.dashboardWidgets.add(widget({ id: "second", title: "Second", component: () => <span>Second body</span> }));
    const { container, root } = render(api);
    await waitUntil(() => container.querySelectorAll("article").length === 2);

    click(container, "Move Second earlier");
    expect([...container.querySelectorAll("h3")].map(value => value.textContent)).toEqual(["Second", "First"]);

    const size = container.querySelector('select[aria-label="Size of Second"]') as HTMLSelectElement;
    flushSync(() => { size.value = "medium"; size.dispatchEvent(new Event("change", { bubbles: true })); });
    expect(container.querySelector("article")?.getAttribute("data-size")).toBe("medium");

    click(container, "Hide Second");
    expect(container.textContent).not.toContain("Second body");
    clickText(container, "Restore Second");
    expect(container.textContent).toContain("Second body");
    flushSync(() => root.unmount());
  });

  it("isolates loader failure and retries only the failed widget", async () => {
    const api = stubApi();
    const load = vi.fn()
      .mockRejectedValueOnce(new Error("offline"))
      .mockResolvedValueOnce({ total: 12 });
    api.dashboardWidgets.add(widget({ load, component: ({ snapshot }) => <span>Total {(snapshot as { total?: number } | undefined)?.total}</span> }));
    const { container, root } = render(api);
    await waitUntil(() => container.textContent?.includes("offline") === true);

    clickText(container, "Retry");

    await waitUntil(() => container.textContent?.includes("Total 12") === true);
    expect(load).toHaveBeenCalledTimes(2);
    flushSync(() => root.unmount());
  });

  it("does not load while hidden and resumes when the Dashboard becomes visible", async () => {
    const hidden = Object.getOwnPropertyDescriptor(document, "hidden");
    Object.defineProperty(document, "hidden", { configurable: true, value: true });
    const api = stubApi();
    const load = vi.fn(async () => ({}));
    api.dashboardWidgets.add(widget({ load }));
    const { root } = render(api);
    await flush();
    expect(load).not.toHaveBeenCalled();

    Object.defineProperty(document, "hidden", { configurable: true, value: false });
    document.dispatchEvent(new Event("visibilitychange"));
    await waitUntil(() => load.mock.calls.length === 1);
    flushSync(() => root.unmount());
    if (hidden) Object.defineProperty(document, "hidden", hidden);
  });

  it("aborts and removes a widget when a rebuilt registry no longer contributes it", async () => {
    const first = stubApi();
    let signal: AbortSignal | undefined;
    first.dashboardWidgets.add(widget({ load: ({ signal: next }) => { signal = next; return new Promise(() => undefined); } }));
    const view = render(first);
    await waitUntil(() => signal != null);

    const second = stubApi();
    flushSync(() => view.root.render(<AnonymousAuthProvider><DashboardPage api={second} /></AnonymousAuthProvider>));

    await waitUntil(() => signal?.aborted === true);
    expect(view.container.textContent).not.toContain("Workflow runs");
    flushSync(() => view.root.unmount());
  });
});

function render(api: ReturnType<typeof stubApi>) {
  const container = document.createElement("div");
  const root = createRoot(container);
  flushSync(() => root.render(<AnonymousAuthProvider><DashboardPage api={api} /></AnonymousAuthProvider>));
  return { container, root };
}

function click(container: HTMLElement, label: string) {
  const button = container.querySelector(`button[aria-label="${label}"]`) as HTMLButtonElement | null;
  if (!button) throw new Error(`Missing button: ${label}`);
  flushSync(() => button.click());
}

function clickText(container: HTMLElement, text: string) {
  const button = [...container.querySelectorAll("button")].find(value => value.textContent?.includes(text));
  if (!button) throw new Error(`Missing button text: ${text}`);
  flushSync(() => button.click());
}
