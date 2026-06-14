import { describe, expect, it } from "vitest";
import { register } from "../module";

describe("weather forecast sample module", () => {
  it("registers navigation and route contributions", () => {
    const api = stubApi();

    register(api);

    expect(api.navigation.items).toHaveLength(1);
    expect(api.routes.items).toHaveLength(1);
  });
});

function stubApi() {
  return {
    host: { http: { getJson: async () => [] } },
    navigation: registry(),
    routes: registry()
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

