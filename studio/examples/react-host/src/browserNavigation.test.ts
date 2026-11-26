import { afterEach, describe, expect, it, vi } from "vitest";
import { createBrowserNavigationAdapter, routeMatchesPath, subscribeToBrowserNavigation } from "./browserNavigation";

afterEach(() => {
  window.history.replaceState({}, "", "/");
  vi.restoreAllMocks();
});

describe("React example host browser navigation", () => {
  it("pushes changed routes once and emits the host router signal", () => {
    window.history.replaceState({}, "", "/workflows/instances");
    const pushState = vi.spyOn(window.history, "pushState");
    const onPopState = vi.fn();
    window.addEventListener("popstate", onPopState);
    const navigate = createBrowserNavigationAdapter(window);

    navigate("/workflows/instances/run-1?tab=details#activity");
    navigate("/workflows/instances/run-1?tab=details#activity");

    expect(window.location.pathname).toBe("/workflows/instances/run-1");
    expect(window.location.search).toBe("?tab=details");
    expect(window.location.hash).toBe("#activity");
    expect(pushState).toHaveBeenCalledTimes(1);
    expect(onPopState).toHaveBeenCalledTimes(1);
    expect(onPopState.mock.calls[0]?.[0]).toBeInstanceOf(PopStateEvent);
    window.removeEventListener("popstate", onPopState);
  });

  it("keeps the host route synchronized with browser back and forward signals", () => {
    window.history.replaceState({}, "", "/workflows/instances");
    const onRoute = vi.fn();
    const unsubscribe = subscribeToBrowserNavigation(onRoute, window);
    const navigate = createBrowserNavigationAdapter(window);

    navigate("/workflows/instances/run-1");
    window.history.replaceState({}, "", "/workflows/instances");
    window.dispatchEvent(new PopStateEvent("popstate"));
    window.history.replaceState({}, "", "/workflows/instances/run-1");
    window.dispatchEvent(new PopStateEvent("popstate"));

    expect(onRoute.mock.calls.map(([path]) => path)).toEqual([
      "/workflows/instances/run-1",
      "/workflows/instances",
      "/workflows/instances/run-1"
    ]);
    unsubscribe();
  });

  it("distinguishes static routes from one-segment dynamic detail routes", () => {
    expect(routeMatchesPath("/workflows/instances", "/workflows/instances/run-1")).toBe(false);
    expect(routeMatchesPath("/workflows/instances/:workflowExecutionId", "/workflows/instances/run-1")).toBe(true);
    expect(routeMatchesPath("/workflows/instances/:workflowExecutionId", "/workflows/instances/run-1/details")).toBe(false);
  });
});
