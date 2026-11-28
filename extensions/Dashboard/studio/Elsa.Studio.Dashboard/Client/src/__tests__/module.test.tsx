import { ChartColumn } from "lucide-react";
import { describe, expect, it } from "vitest";
import { register } from "../module";
import { stubApi } from "./testSupport";

describe("Dashboard module registration", () => {
  it("adds saved dashboards and questions and leaves Home to the shell", () => {
    const api = stubApi();
    register(api);
    expect(api.navigation.list().map(item => item.path)).toEqual(["/dashboards", "/questions"]);
    expect(api.navigation.list().find(item => item.id === "questions")?.icon).toBe(ChartColumn);
    expect(api.routes.list().map(item => item.path)).toEqual(["/dashboards", "/dashboards/new", "/dashboards/:resourceId/edit", "/dashboards/:resourceId", "/questions", "/questions/new", "/questions/:resourceId"]);
    expect(api.routes.list().every(item =>
      item.requiresBackend?.some(requirement => requirement.id === "elsa.api.studio-dashboard" && requirement.contractVersion === "1"))).toBe(true);
    expect(api.homeWidgets.list()).toEqual([]);
  });
});
