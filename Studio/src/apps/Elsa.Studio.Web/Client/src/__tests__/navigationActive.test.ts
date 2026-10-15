import { describe, expect, it } from "vitest";
import type { StudioNavigationContribution } from "../sdk";
import {
  isExactSegmentPrefix,
  isNavigationItemActive,
  resolveActiveNavigationItem
} from "../app/navigation/active";

const workflows: StudioNavigationContribution = {
  id: "workflows",
  label: "Workflows",
  path: "/workflows/definitions",
  activePathPrefix: "/workflows",
  placement: "main"
};
const runs: StudioNavigationContribution = {
  id: "workflows-runs",
  label: "Runs",
  path: "/workflows/instances",
  activePathPrefix: "/workflows/instances",
  placement: "main"
};
const modules: StudioNavigationContribution = {
  id: "modules",
  label: "Modules",
  path: "/modules",
  activePathPrefix: "/modules",
  placement: "admin"
};
const navigation = [workflows, runs, modules];

describe("navigation active matching", () => {
  it("matches prefixes on exact segments only, so /modulesX does not activate /modules", () => {
    expect(isExactSegmentPrefix("/modules", "/modules")).toBe(true);
    expect(isExactSegmentPrefix("/modules/logs", "/modules")).toBe(true);
    expect(isExactSegmentPrefix("/modulesX", "/modules")).toBe(false);
    expect(isNavigationItemActive(modules, "/modulesX", navigation)).toBe(false);
    expect(isNavigationItemActive(modules, "/modules", navigation)).toBe(true);
  });

  it("activates only the most specific item when two prefixes match", () => {
    expect(resolveActiveNavigationItem("/workflows/instances", navigation)?.id).toBe("workflows-runs");
    expect(isNavigationItemActive(workflows, "/workflows/instances", navigation)).toBe(false);
    expect(isNavigationItemActive(runs, "/workflows/instances", navigation)).toBe(true);
    expect(isNavigationItemActive(workflows, "/workflows/definitions", navigation)).toBe(true);
    expect(isNavigationItemActive(runs, "/workflows/definitions", navigation)).toBe(false);
    expect(isNavigationItemActive(runs, "/workflows/instances/exec-1", navigation)).toBe(true);
    expect(isNavigationItemActive(workflows, "/workflows/instances/exec-1", navigation)).toBe(false);
  });
});
