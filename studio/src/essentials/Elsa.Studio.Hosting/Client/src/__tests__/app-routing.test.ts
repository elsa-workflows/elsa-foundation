import { describe, expect, it } from "vitest";
import { describeScreen, findRouteForPath } from "../app/App";

describe("app route matching", () => {
  it("prefers exact route matches over parameterized routes", () => {
    const routes = [
      route("instance-detail", "/workflows/instances/:workflowExecutionId"),
      route("instances", "/workflows/instances")
    ];

    expect(findRouteForPath(routes, "/workflows/instances")?.id).toBe("instances");
  });

  it("matches simple parameterized route segments", () => {
    const routes = [
      route("instances", "/workflows/instances"),
      route("instance-detail", "/workflows/instances/:workflowExecutionId")
    ];

    expect(findRouteForPath(routes, "/workflows/instances/wfexec-1")?.id).toBe("instance-detail");
  });

  it("does not match routes with different segment counts", () => {
    const routes = [
      route("instance-detail", "/workflows/instances/:workflowExecutionId")
    ];

    expect(findRouteForPath(routes, "/workflows/instances/wfexec-1/activity")).toBeUndefined();
  });
});

describe("workspace screen description", () => {
  it("names a registered child screen inside an active navigation section", () => {
    const parent = "/workflows/activity-definitions";
    const path = parent + "/upgrades";
    const routes = [{ path, label: "Activity definition upgrades" }];
    const navigation = [{ id: "activities", path: parent, label: "Activity definitions" }];
    expect(describeScreen(path, routes, navigation, [], []).title).toBe("Activity definition upgrades");
  });

  it("survives a malformed percent-encoded segment in a restored address", () => {
    const routes = [{ path: "/workflows/instances/:workflowExecutionId", label: "Workflow run" }];
    expect(() => describeScreen("/workflows/instances/%E0%A4%A", routes, [], [], [])).not.toThrow();
    expect(describeScreen("/workflows/instances/%E0%A4%A", routes, [], [], [])).toEqual({ title: "Workflow run", detail: "%E0%A4%A" });
    expect(describeScreen("/workflows/instances/run%201", routes, [], [], []).detail).toBe("run 1");
  });
});

function route(id: string, path: string) {
  return { id, path };
}
