import { describe, expect, it } from "vitest";
import { findRouteForPath } from "../app/App";

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

function route(id: string, path: string) {
  return { id, path };
}
