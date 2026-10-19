import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AnonymousAuthProvider } from "@elsa-workflows/studio-sdk";
import { HomePage } from "../HomePage";
import { homeLayoutLocalStoragePrefix } from "../homeLayout";
import { flush, seedHomeLayout, stubApi, waitUntil, widget } from "./testSupport";

describe("HomePage", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it("moves, resizes, hides, and restores widgets in Edit home", async () => {
    const api = stubApi();
    api.dashboardWidgets.add(widget({ id: "first", title: "First", component: () => <span>First body</span> }));
    api.dashboardWidgets.add(widget({ id: "second", title: "Second", component: () => <span>Second body</span> }));
    seedHomeLayout(api, ["first", "second"]);
    const { container, root } = render(api);
    await waitUntil(() => container.querySelectorAll("article").length === 2);

    clickText(container, "Edit home");
    click(container, "Move Second earlier");
    expect([...container.querySelectorAll("h3")].map(value => value.textContent?.replace("⠿", ""))).toEqual(["Second", "First"]);
    expect(container.querySelector("[aria-live='polite']")?.textContent).toBe("Second moved to position 1 of 2");
    expect(container.querySelector('button[aria-label="Move Second earlier"]')).toBe(document.activeElement);

    const size = container.querySelector('select[aria-label="Size of Second"]') as HTMLSelectElement;
    flushSync(() => { size.value = "medium"; size.dispatchEvent(new Event("change", { bubbles: true })); });
    expect(container.querySelector("article")?.getAttribute("data-size")).toBe("medium");

    click(container, "Hide Second");
    expect(container.textContent).not.toContain("Second body");
    const addWidget = [...container.querySelectorAll("button")].find(value => value.textContent?.includes("Add widget"));
    if (!addWidget) throw new Error("Missing Add widget");
    flushSync(() => addWidget.focus());
    flushSync(() => addWidget.click());
    await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
    const dialog = container.querySelector('[role="dialog"][aria-label="Add widget"]');
    expect(dialog).not.toBeNull();
    expect(dialog?.contains(document.activeElement)).toBe(true);
    flushSync(() => window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })));
    expect(container.querySelector('[role="dialog"][aria-label="Add widget"]')).toBeNull();
    expect(document.activeElement).toBe(addWidget);

    clickText(container, "Add widget");
    clickText(container, "Second");
    expect(container.textContent).toContain("Second body");
    flushSync(() => root.unmount());
  });

  it("reorders visible widgets past hidden entries", async () => {
    const api = stubApi();
    api.dashboardWidgets.add(widget({ id: "first", title: "First" }));
    api.dashboardWidgets.add(widget({ id: "second", title: "Second" }));
    api.dashboardWidgets.add(widget({ id: "third", title: "Third" }));
    seedHomeLayout(api, ["first", "second", "third"]);
    const { container, root } = render(api);
    await waitUntil(() => container.querySelectorAll("article").length === 3);

    clickText(container, "Edit home");
    click(container, "Hide Second");
    click(container, "Move First later");

    expect([...container.querySelectorAll("article h3")].map(value => value.textContent?.replace("⠿", ""))).toEqual(["Third", "First"]);
    expect(container.querySelector("[aria-live='polite']")?.textContent).toBe("First moved to position 2 of 2");
    flushSync(() => root.unmount());
  });

  it("isolates loader failure and retries only the failed widget", async () => {
    const api = stubApi();
    const load = vi.fn()
      .mockRejectedValueOnce(new Error("offline"))
      .mockResolvedValueOnce({ total: 12 });
    api.dashboardWidgets.add(widget({ load, component: ({ snapshot }) => <span>Total {(snapshot as { total?: number } | undefined)?.total}</span> }));
    seedHomeLayout(api, ["workflows.runs"]);
    const { container, root } = render(api);
    await waitUntil(() => container.textContent?.includes("offline") === true);

    clickText(container, "Retry");

    await waitUntil(() => container.textContent?.includes("Total 12") === true);
    expect(load).toHaveBeenCalledTimes(2);
    flushSync(() => root.unmount());
  });

  it("isolates a widget render failure and recovers through the host retry", async () => {
    const consoleError = vi.spyOn(console, "error").mockImplementation(() => undefined);
    const api = stubApi();
    let shouldThrow = true;
    api.dashboardWidgets.add(widget({
      id: "failing",
      title: "Failing widget",
      component: () => {
        if (shouldThrow) throw new Error("render failed");
        return <span>Recovered body</span>;
      }
    }));
    api.dashboardWidgets.add(widget({ id: "healthy", title: "Healthy widget", component: () => <span>Healthy body</span> }));
    seedHomeLayout(api, ["failing", "healthy"]);
    const { container, root } = render(api);

    await waitUntil(() => container.textContent?.includes("Failing widget could not be rendered.") === true);
    expect(container.textContent).toContain("Healthy body");

    shouldThrow = false;
    clickText(container, "Retry");

    await waitUntil(() => container.textContent?.includes("Recovered body") === true);
    expect(container.textContent).toContain("Healthy body");
    expect(consoleError).toHaveBeenCalled();
    flushSync(() => root.unmount());
  });

  it("does not load while hidden and resumes when Home becomes visible", async () => {
    const hidden = Object.getOwnPropertyDescriptor(document, "hidden");
    Object.defineProperty(document, "hidden", { configurable: true, value: true });
    const api = stubApi();
    const load = vi.fn(async () => ({}));
    api.dashboardWidgets.add(widget({ load }));
    seedHomeLayout(api, ["workflows.runs"]);
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
    seedHomeLayout(first, ["workflows.runs"]);
    const view = render(first);
    await waitUntil(() => signal != null);

    const second = stubApi();
    flushSync(() => view.root.render(<AnonymousAuthProvider><HomePage api={second} /></AnonymousAuthProvider>));

    await waitUntil(() => signal?.aborted === true);
    expect(view.container.textContent).not.toContain("Workflow runs");
    flushSync(() => view.root.unmount());
  });

  it("renders its empty state when no module contributes a widget", async () => {
    const api = stubApi();
    const { container, root } = render(api);
    await waitUntil(() => container.querySelector(".dashboard-empty") !== null);

    expect(container.textContent).toContain("No Home widgets are visible.");
    expect(container.querySelectorAll("article.dashboard-widget")).toHaveLength(0);
    expect(container.querySelector("h2")?.textContent).toBe("Home");
    flushSync(() => root.unmount());
  });

  it("skips an unregistered widget and keeps it in the saved layout", async () => {
    const api = stubApi();
    api.dashboardWidgets.add(widget({ id: "first", title: "First", component: () => <span>First body</span> }));
    seedHomeLayout(api, ["first", "weather.forecast"]);
    const { container, root } = render(api);
    await waitUntil(() => container.querySelectorAll("article").length === 1);

    expect(container.textContent).toContain("First body");
    expect(container.querySelector('[data-widget-id="weather.forecast"]')).toBeNull();

    clickText(container, "Edit home");
    click(container, "Hide First");
    const stored = JSON.parse(localStorage.getItem(`${homeLayoutLocalStoragePrefix}${encodeURIComponent(JSON.stringify({
      studioHostId: "test",
      backendBaseUrl: "https://backend.example",
      subjectId: "anonymous",
      tenantId: "default"
    }))}`)!);
    expect(stored.value.widgets.map((entry: { id: string }) => entry.id)).toEqual(["first", "weather.forecast"]);
    flushSync(() => root.unmount());
  });

  it("keeps unavailable and missing contributions out of Home without changing saved layout state", async () => {
    const api = stubApi();
    api.runtime.enabledFeatures = [];
    api.dashboardWidgets.add(widget({ id: "second", title: "Second", component: () => <span>Second body</span> }));
    api.dashboardWidgets.add(widget({ id: "first", title: "First", component: () => <span>First body</span> }));
    api.dashboardWidgets.add(widget({
      id: "workflows.portfolio",
      title: "Workflow portfolio",
      requiresFeature: "workflows",
      component: () => <span>Portfolio placeholder</span>
    }));
    const scope = {
      studioHostId: "test",
      backendBaseUrl: "https://backend.example",
      subjectId: "anonymous",
      tenantId: "default"
    };
    const original = {
      source: "local",
      namespace: "home-layout",
      schemaVersion: 1,
      value: { widgets: [
        { id: "workflows.portfolio", size: "medium", visible: true, options: { sort: "name" } },
        { id: "second", size: "wide", visible: true, options: { range: "7d", keepVisible: true } },
        { id: "workflows.run-health", size: "full", visible: true, options: { range: "12h", includeTestRuns: true } },
        { id: "first", size: "wide", visible: true, options: { color: "blue" } },
        { id: "hidden", size: "small", visible: false, options: { pinned: true } },
        { id: "weather.forecast", size: "medium", visible: true, options: { unit: "c" } }
      ] }
    };
    const storageKey = `${homeLayoutLocalStoragePrefix}${encodeURIComponent(JSON.stringify(scope))}`;
    localStorage.setItem(storageKey, JSON.stringify(original));
    const { container, root } = render(api);
    await waitUntil(() => container.querySelectorAll("article.dashboard-widget").length === 2);

    expect([...container.querySelectorAll("article.dashboard-widget")].map(article => article.getAttribute("data-widget-id")))
      .toEqual(["second", "first"]);
    expect(container.querySelector('[data-widget-id="workflows.run-health"]')).toBeNull();
    expect(container.querySelector('[data-widget-id="workflows.portfolio"]')).toBeNull();
    expect(container.querySelector('[data-widget-id="weather.forecast"]')).toBeNull();
    expect(container.textContent).not.toContain("Portfolio placeholder");
    expect(JSON.parse(localStorage.getItem(storageKey)!)).toEqual(original);
    flushSync(() => root.unmount());
  });

  it("merges widget defaults with metadata when adding a workflow run-health widget", async () => {
    const api = stubApi();
    const load = vi.fn(async ({ settings }: { settings: unknown }) => ({ settings }));
    api.dashboardWidgets.add(widget({
      id: "workflows.run-health",
      title: "Workflow run health",
      defaultSize: "full",
      supportedSizes: ["full", "medium"],
      settings: {
        schemaVersion: 1,
        defaults: { range: "24h", includeTestRuns: false },
        descriptors: [],
        validate: value => value as { range: string; includeTestRuns: boolean }
      },
      load,
      component: ({ settings }) => <span>{JSON.stringify(settings)}</span>
    }));
    seedHomeLayout(api, []);
    const { container, root } = render(api);
    await waitUntil(() => container.querySelector("h2")?.textContent === "Home");

    clickText(container, "Edit home");
    clickText(container, "Add widget");
    clickText(container, "Workflow run health");
    await waitUntil(() => load.mock.calls.length === 1);

    const settings = (load.mock.calls[0][0] as { settings: unknown }).settings;
    expect(settings).toEqual({ range: "24h", includeTestRuns: false, keepVisible: true });
    await waitUntil(() => container.textContent?.includes(JSON.stringify(settings)) === true);
    expect(container.textContent).toContain(JSON.stringify(settings));
    flushSync(() => root.unmount());
  });

  it("skips a widget whose required feature is disabled", async () => {
    const api = stubApi();
    api.runtime.enabledFeatures = [];
    api.dashboardWidgets.add(widget({
      id: "home.recent-workflows",
      title: "Recent workflows",
      requiresFeature: "workflows",
      component: () => <span>Recent body</span>
    }));
    seedHomeLayout(api, ["home.recent-workflows"]);
    const { container, root } = render(api);
    await waitUntil(() => container.querySelector("h2")?.textContent === "Home");
    expect(container.textContent).not.toContain("Recent body");
    flushSync(() => root.unmount());
  });

  it("treats Elsa.Studio.Workflows as the workflows feature gate", async () => {
    const api = stubApi();
    api.runtime.enabledFeatures = ["Elsa.Studio.Workflows"];
    api.dashboardWidgets.add(widget({
      id: "home.recent-workflows",
      title: "Recent workflows",
      requiresFeature: "workflows",
      component: () => <span>Recent body</span>
    }));
    seedHomeLayout(api, ["home.recent-workflows"]);
    const { container, root } = render(api);
    await waitUntil(() => container.textContent?.includes("Recent body") === true);
    expect(container.textContent).toContain("Recent body");
    flushSync(() => root.unmount());
  });

  it("shows Tasks only when user-tasks is enabled", async () => {
    const api = stubApi();
    api.runtime.enabledFeatures = ["Elsa.Studio.Workflows"];
    api.dashboardWidgets.add(widget({
      id: "home.quick-actions",
      title: "Quick actions",
      requiresFeature: "workflows",
      component: () => <a href="/workflows/definitions">Create a workflow</a>
    }));
    api.dashboardWidgets.add(widget({
      id: "home.tasks",
      title: "Tasks",
      requiresFeature: "user-tasks",
      component: () => <span>Task inbox</span>
    }));
    seedHomeLayout(api, ["home.quick-actions", "home.tasks"]);
    const hidden = render(api);
    await waitUntil(() => hidden.container.textContent?.includes("Create a workflow") === true);
    expect(hidden.container.textContent).not.toContain("Task inbox");
    flushSync(() => hidden.root.unmount());

    api.runtime.enabledFeatures = ["Elsa.Studio.Workflows", "Elsa.Studio.UserTasks"];
    const shown = render(api);
    await waitUntil(() => shown.container.textContent?.includes("Task inbox") === true);
    expect(shown.container.textContent).toContain("Create a workflow");
    flushSync(() => shown.root.unmount());
  });
});

function render(api: ReturnType<typeof stubApi>) {
  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);
  flushSync(() => root.render(<AnonymousAuthProvider><HomePage api={api} /></AnonymousAuthProvider>));
  const originalUnmount = root.unmount.bind(root);
  root.unmount = () => {
    originalUnmount();
    container.remove();
  };
  return { container, root };
}

function click(container: HTMLElement, label: string) {
  const button = container.querySelector(`button[aria-label="${label}"]`) as HTMLButtonElement | null;
  if (!button) throw new Error(`Missing button: ${label}`);
  flushSync(() => button.click());
}

function clickText(container: HTMLElement, text: string) {
  const button = [...container.querySelectorAll("button")].find(value => value.textContent?.trim() === text || value.textContent?.includes(text));
  if (!button) throw new Error(`Missing button text: ${text}`);
  flushSync(() => button.click());
}
