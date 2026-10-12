import { describe, expect, it } from "vitest";
import { Hammer } from "lucide-react";
import type { ElsaStudioModuleApi, StudioNavigationContribution, StudioRouteContribution } from "@elsa-workflows/studio-sdk";
import { register } from "../module";

describe("extension builder module", () => {
  it("contributes the Extension Builder navigation item and route", () => {
    const navigation: StudioNavigationContribution[] = [];
    const routes: StudioRouteContribution[] = [];
    const api = {
      navigation: { add: (item: StudioNavigationContribution) => navigation.push(item) },
      routes: { add: (route: StudioRouteContribution) => routes.push(route) }
    } as unknown as ElsaStudioModuleApi;

    register(api);

    // The nav item places itself under Settings with its own icon — the host no longer special-cases the id (#535).
    expect(navigation).toEqual([{
      id: "extension-builder",
      label: "Extension Builder",
      path: "/extension-builder",
      order: 40,
      iconColor: "#ec4899",
      section: "settings",
      icon: Hammer
    }]);
    expect(routes).toHaveLength(1);
    expect(routes[0]).toMatchObject({ id: "extension-builder", label: "Extension Builder", path: "/extension-builder" });
    expect(typeof routes[0].component).toBe("function");
  });
});
