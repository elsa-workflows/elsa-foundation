import React from "react";
import { createRoot, type Root } from "react-dom/client";
import { flushSync } from "react-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { NeedsAttentionWidget, QuickActionsWidget, RecentRunsWidget, RecentWorkflowsWidget } from "../widgets/HomeListWidget";
import { navigateStudioPath } from "../widgets/homeData";

vi.mock("../widgets/homeData", async importOriginal => ({
  ...await importOriginal<typeof import("../widgets/homeData")>(),
  navigateStudioPath: vi.fn()
}));

const mounted: Array<{ root: Root; container: HTMLDivElement }> = [];
afterEach(() => {
  for (const { root, container } of mounted.splice(0)) {
    flushSync(() => root.unmount());
    container.remove();
  }
  vi.clearAllMocks();
});

function render(content: React.ReactNode) {
  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);
  mounted.push({ root, container });
  flushSync(() => root.render(content));
  return container;
}

const props = { settings: undefined, size: "medium" as const };

describe("Home widget content", () => {
  it("opens a recent definition through the supported query route", () => {
    const host = render(<RecentWorkflowsWidget {...props} snapshot={[{ id: "definition / 1", name: "Order intake", description: "Receive and acknowledge an order", lastModifiedAt: "2026-10-17T12:00:00Z" }]} />);
    const link = host.querySelector<HTMLAnchorElement>(".home-record")!;
    expect(link.getAttribute("href")).toBe("/workflows/definitions?definition=definition%20%2F%201");
    expect(link.textContent).toContain("Order intake");
    expect(link.textContent).toContain("Receive and acknowledge an order");
    expect(link.querySelector("time")?.dateTime).toBe("2026-10-17T12:00:00Z");
    link.click();
    expect(navigateStudioPath).toHaveBeenCalledWith(link.getAttribute("href"));
  });

  it("keeps unknown metadata out of a workflow row and bounds the preview list", () => {
    const host = render(<RecentWorkflowsWidget {...props} snapshot={Array.from({ length: 12 }, (_, index) => ({ id: String(index), name: `Workflow ${index}`, createdAt: "invalid date" }))} />);
    expect(host.querySelectorAll(".home-record")).toHaveLength(4);
    expect(host.querySelector("time")).toBeNull();
    expect(host.querySelector(".home-browse-link")?.getAttribute("href")).toBe("/workflows/definitions");
    expect(host.textContent).not.toContain("Invalid Date");
  });

  it("separates technical run identity from its name and retains the run link and status", () => {
    const host = render(<RecentRunsWidget {...props} snapshot={[{ id: "run-1", name: "Order intake", status: "Completed", startedAt: "2026-10-17T12:00:00Z" }, { id: "run-2", name: "run-2", status: "Running" }]} />);
    expect(host.querySelectorAll(".home-record-title")[0]?.textContent).toBe("Order intake");
    expect(host.querySelectorAll(".home-record-title")[1]?.textContent).toBe("Workflow run");
    expect(host.querySelector(".home-record-id")?.textContent).toBe("run-1");
    expect(host.querySelector(".home-record")?.getAttribute("href")).toBe("/workflows/instances/run-1");
    expect(host.querySelector('[data-tone="success"]')).not.toBeNull();
    expect(host.textContent).toContain("Completed");
  });

  it("shows active incidents even when the affected run is still Running", () => {
    const host = render(<NeedsAttentionWidget {...props} snapshot={[{ id: "run-1", name: "Order intake", status: "Running", activeIncidentCount: 2 }]} />);
    expect(host.textContent).toContain("2 active incidents");
    expect(host.textContent).toContain("Running");
    expect(host.textContent).not.toContain("All clear");
  });

  it("offers a clear empty state and actionable shortcuts", () => {
    const host = render(<><NeedsAttentionWidget {...props} snapshot={[]} /><QuickActionsWidget /></>);
    expect(host.querySelector('[role="status"]')?.textContent).toContain("All clear");
    expect(host.querySelectorAll(".home-action")).toHaveLength(2);
    expect(host.querySelector(".home-action-shortcut")?.getAttribute("href")).toBe("/workflows/instances");
  });
});
