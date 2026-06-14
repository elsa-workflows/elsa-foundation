import { describe, expect, it } from "vitest";
import { createStudioRegistry } from "../app/registry";

describe("studio registry", () => {
  it("tracks typed bottom panel contributions", () => {
    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      http: { getJson: async () => ({}) }
    });

    api.panels.add({ id: "second", title: "Second", order: 20, component: () => null });
    api.panels.add({ id: "first", title: "First", order: 10, component: () => null });

    expect(api.panels.list().map(panel => panel.id)).toEqual(["second", "first"]);
  });
});
