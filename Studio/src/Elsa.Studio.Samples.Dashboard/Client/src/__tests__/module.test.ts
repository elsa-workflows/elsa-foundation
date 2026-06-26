import { describe, expect, it } from "vitest";
import { register } from "../module";

describe("dashboard sample module", () => {
  it("registers dashboard widget contributions without owning the dashboard route", () => {
    const api = stubApi();

    register(api);

    expect(api.navigation.items).toHaveLength(0);
    expect(api.routes.items).toHaveLength(0);
    expect(api.dashboardWidgets.items).toHaveLength(3);
    expect(api.dashboardWidgets.items.map(widget => widget.id)).toEqual([
      "dashboard-sample-health",
      "dashboard-sample-route",
      "dashboard-sample-backend"
    ]);
  });
});

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
