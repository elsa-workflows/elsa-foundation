import { describe, expect, it } from "vitest";
import { register } from "../module";

describe("dashboard sample module", () => {
  it("registers navigation, route, and widget contributions", () => {
    const api = stubApi();

    register(api);

    expect(api.navigation.items).toHaveLength(1);
    expect(api.routes.items).toHaveLength(1);
    expect(api.dashboardWidgets.items).toHaveLength(1);
  });
});

function stubApi() {
  return {
    navigation: registry(),
    routes: registry(),
    dashboardWidgets: registry()
  } as any;
}

function registry() {
  const items: unknown[] = [];
  return {
    items,
    add(item: unknown) {
      items.push(item);
    },
    list() {
      return items;
    }
  };
}

