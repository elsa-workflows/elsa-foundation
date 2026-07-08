import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it } from "vitest";
import { ModuleHealthWidget, SharedKitWidget, register } from "../module";

let cleanup: (() => void) | null = null;

afterEach(() => {
  cleanup?.();
  cleanup = null;
});

describe("dashboard sample module", () => {
  it("registers dashboard widget contributions without owning the dashboard route", () => {
    const api = stubApi();

    register(api);

    expect(api.navigation.items).toHaveLength(0);
    expect(api.routes.items).toHaveLength(0);
    expect(api.dashboardWidgets.items).toHaveLength(4);
    expect(api.dashboardWidgets.items.map(widget => widget.id)).toEqual([
      "dashboard-sample-health",
      "dashboard-sample-route",
      "dashboard-sample-backend",
      "dashboard-sample-kit"
    ]);
  });

  it("renders the shared StatusChip primitive in the health widget", () => {
    const host = mount(<ModuleHealthWidget />);

    const chip = host.querySelector(".studio-status-chip");
    expect(chip).not.toBeNull();
    expect(chip?.getAttribute("data-tone")).toBe("success");
    expect(chip?.textContent).toBe("Loaded");
  });

  it("composes the shared stat + list kit primitives in the kit demo widget", () => {
    const host = mount(<SharedKitWidget />);

    expect(host.querySelectorAll(".studio-stat-tile")).toHaveLength(2);
    expect(host.querySelector(".studio-sparkline")).not.toBeNull();

    const rows = host.querySelectorAll(".studio-list-row");
    expect(rows).toHaveLength(3);
    // First row is selected by default and every row carries a trailing pill.
    expect(rows[0].getAttribute("aria-selected")).toBe("true");
    expect(host.querySelectorAll(".studio-list-row-trailing .studio-status-pill")).toHaveLength(3);
  });
});

function mount(node: React.ReactElement): HTMLElement {
  const host = document.createElement("div");
  document.body.appendChild(host);
  const root = createRoot(host);
  flushSync(() => root.render(node));
  cleanup = () => {
    flushSync(() => root.unmount());
    host.remove();
  };
  return host;
}

function stubApi() {
  return {
    navigation: registry(),
    routes: registry(),
    dashboardWidgets: registry()
  } as any;
}

function registry<T = any>() {
  const items: T[] = [];
  return {
    items,
    add(item: T) {
      items.push(item);
    },
    list() {
      return items;
    }
  };
}
