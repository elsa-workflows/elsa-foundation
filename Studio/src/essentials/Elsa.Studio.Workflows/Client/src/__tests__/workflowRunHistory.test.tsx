import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { afterEach, describe, expect, it, vi } from "vitest";
import { clearApiCapabilityCache } from "../api/capabilities";
import { WorkflowInstances } from "../workflow-editor/WorkflowInstances";

let mounted: { root: Root; container: HTMLDivElement } | null = null;

afterEach(() => {
  clearApiCapabilityCache();
  if (mounted) {
    flushSync(() => mounted!.root.unmount());
    mounted.container.remove();
    mounted = null;
  }
  window.history.replaceState({}, "", "/");
});

describe("workflow run history", () => {
  it("navigates retained forward-only cursor history and resets it for filters and page size", async () => {
    window.history.replaceState({}, "", "/workflows/instances?pageSize=10");
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities;
      if (url.includes("cursor=next-cursor")) return forwardOnlyPage("execution-2", "after-page-2");
      if (url.includes("cursor=after-page-2")) return forwardOnlyPage("execution-3");
      return forwardOnlyPage("execution-1", "next-cursor", 42);
    });
    const navigate = vi.fn((path: string) => window.history.pushState({}, "", path));
    const container = render(context(getJson), navigate);

    await waitFor(() => expect(container.textContent).toContain("execution-1"));
    click(button(container, "Next workflow run page"));
    await waitFor(() => expect(container.textContent).toContain("execution-2"));
    expect(button(container, "Previous workflow run page").disabled).toBe(false);

    click(button(container, "Previous workflow run page"));
    await waitFor(() => expect(container.textContent).toContain("execution-1"));
    expect(getJson).toHaveBeenCalledWith("/runtime/workflows/instances/page?take=10");

    click(button(container, "Next workflow run page"));
    await waitFor(() => expect(container.textContent).toContain("execution-2"));
    expect(button(container, "Previous workflow run page").disabled).toBe(false);

    fill(input(container, "Workflow run correlation"), "correlation-1");
    click(buttonByText(container, "Apply filters"));
    await waitFor(() => expect(container.textContent).toContain("execution-1"));
    expect(button(container, "Previous workflow run page").disabled).toBe(true);
    expect(navigate).toHaveBeenLastCalledWith(expect.not.stringContaining("cursor="));
    expect(getJson).toHaveBeenCalledWith(expect.stringMatching(/instances\/page\?.*correlationId=correlation-1.*take=10/));

    click(button(container, "Next workflow run page"));
    await waitFor(() => expect(container.textContent).toContain("execution-2"));
    select(container.querySelector<HTMLSelectElement>("select[aria-label='Workflow run page size']")!, "25");
    await waitFor(() => expect(container.textContent).toContain("execution-1"));
    expect(button(container, "Previous workflow run page").disabled).toBe(true);
    expect(navigate).toHaveBeenLastCalledWith(expect.not.stringContaining("cursor="));
    expect(getJson).toHaveBeenCalledWith(expect.stringMatching(/instances\/page\?.*correlationId=correlation-1.*take=25/));
  });

  it("restores filters from the URL and navigates cursor pages without rendering retained history", async () => {
    window.history.replaceState({}, "", "/workflows/instances?definitionId=definition-1&artifactId=artifact-1&pageSize=10");
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities;
      if (url.includes("cursor=next-cursor")) return page("execution-2", { previousCursor: "previous-cursor", hasPrevious: true });
      return page("execution-1", { nextCursor: "next-cursor", hasNext: true, totalCount: 42 });
    });
    const navigate = vi.fn((path: string) => window.history.pushState({}, "", path));
    const container = render(context(getJson), navigate);

    await waitFor(() => expect(container.textContent).toContain("execution-1"));
    expect(input(container, "Workflow run definition").value).toBe("definition-1");
    expect(input(container, "Workflow run artifact").value).toBe("artifact-1");
    expect(container.textContent).toContain("Showing 1 of 42 matching runs");
    expect(container.textContent).toContain("Current health unavailable");
    expect(getJson).toHaveBeenCalledWith(
      "/runtime/workflows/instances/page?definitionId=definition-1&artifactId=artifact-1&take=10");

    click(button(container, "Next workflow run page"));
    await waitFor(() => expect(container.textContent).toContain("execution-2"));
    expect(navigate).toHaveBeenCalledWith(expect.stringContaining("cursor=next-cursor"));
    expect(button(container, "Previous workflow run page").disabled).toBe(false);
    expect(container.textContent).toContain("End of results");

    fill(input(container, "Workflow run execution id"), "pending-execution");
    select(container.querySelector<HTMLSelectElement>("select[aria-label='Workflow run status']")!, "Faulted");
    expect(input(container, "Workflow run execution id").value).toBe("pending-execution");
    click(buttonByText(container, "Apply filters"));
    await waitFor(() => expect(container.textContent).toContain("execution-1"));
    expect(navigate).toHaveBeenLastCalledWith(expect.not.stringContaining("cursor="));
    expect(navigate).toHaveBeenLastCalledWith(expect.stringContaining("status=Faulted"));
    expect(navigate).toHaveBeenLastCalledWith(expect.stringContaining("workflowExecutionId=pending-execution"));

    click(button(container, "Next workflow run page"));
    await waitFor(() => expect(container.textContent).toContain("execution-2"));
    fill(input(container, "Workflow run correlation"), "pending-correlation");
    select(container.querySelector<HTMLSelectElement>("select[aria-label='Workflow run page size']")!, "25");
    await waitFor(() => expect(container.textContent).toContain("execution-1"));
    expect(input(container, "Workflow run correlation").value).toBe("pending-correlation");
    expect(navigate).toHaveBeenLastCalledWith(expect.not.stringContaining("cursor="));
    expect(navigate).toHaveBeenLastCalledWith(expect.not.stringContaining("pageSize=10"));

    window.history.replaceState({}, "", "/workflows/instances?definitionId=definition-1&artifactId=artifact-1&pageSize=10");
    window.dispatchEvent(new PopStateEvent("popstate"));
    await waitFor(() => expect(container.textContent).toContain("execution-1"));
  });

  it("announces loading, filtered empty, error, and end states", async () => {
    let resolvePage: ((value: unknown) => void) | undefined;
    const getJson = vi.fn((url: string) => {
      if (url === "/capabilities") return Promise.resolve(capabilities);
      return new Promise(resolve => { resolvePage = resolve; });
    });
    const container = render(context(getJson), vi.fn());

    await waitFor(() => expect(container.querySelector("[role='status'][aria-label='Loading workflow runs']")).toBeTruthy());
    await waitFor(() => expect(resolvePage).toBeTypeOf("function"));
    resolvePage!({ items: [], count: 0, totalCount: 0, hasNext: false, hasPrevious: false });
    await waitFor(() => expect(container.textContent).toContain("No workflow runs yet"));
    expect(container.textContent).toContain("End of results");

    flushSync(() => mounted!.root.unmount());
    mounted!.container.remove();
    mounted = null;
    clearApiCapabilityCache();
    const errorContainer = render(context(async url => {
      if (url === "/capabilities") return capabilities;
      throw new Error("Runtime unavailable");
    }), vi.fn());
    await waitFor(() => expect(errorContainer.querySelector("[role='alert']")?.textContent).toContain("Runtime unavailable"));
  });

  it("uses the advertised health filter and distinguishes current health from historical incident totals", async () => {
    window.history.replaceState({}, "", "/workflows/instances?incidentHealth=blocking");
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return healthCapabilities;
      return page("blocking-run", {
        items: [{
          ...page("blocking-run").items[0],
          status: "Faulted",
          subStatus: "Retrying",
          incidentCount: 4,
          activeIncidentCount: 2,
          blockingIncidentCount: 1
        }]
      });
    });
    const navigate = vi.fn();
    const container = render(context(getJson), navigate);

    await waitFor(() => expect(container.textContent).toContain("blocking-run"));
    expect(container.textContent).toContain("Needs intervention");
    expect(container.textContent).toContain("4 incidents");
    expect(container.querySelector(".wf-grid-row")?.getAttribute("aria-label"))
      .toContain("Faulted · Retrying · 2 active · 1 blocking · Needs intervention");
    expect(getJson.mock.calls.map(([url]) => url)).toContain("/runtime/workflows/instances/health?incidentHealth=blocking&take=25");

    select(container.querySelector<HTMLSelectElement>("select[aria-label='Workflow run incident health']")!, "active");
    click(buttonByText(container, "Apply filters"));
    await waitFor(() => expect(navigate).toHaveBeenLastCalledWith(expect.stringContaining("incidentHealth=active")));
    await waitFor(() => expect(getJson.mock.calls.map(([url]) => url)).toContain("/runtime/workflows/instances/health?incidentHealth=active&take=25"));
  });

  it("lets a legacy health deep link clear only that filter before searching older servers", async () => {
    window.history.replaceState({}, "", "/workflows/instances?status=Faulted&definitionId=definition-1&incidentHealth=active");
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities;
      if (url.startsWith("/runtime/workflows/instances/page?")) return page("legacy-run");
      throw new Error(`Unexpected request: ${url}`);
    });
    const navigate = vi.fn((path: string) => window.history.pushState({}, "", path));
    const container = render(context(getJson), navigate);

    await waitFor(() => expect(container.textContent).toContain("This host cannot filter by current incident health."));
    expect(container.textContent).toContain("No workflow runs were loaded.");
    expect(container.textContent).toContain("Clear the incident-health filter");
    expect(container.textContent).not.toContain("these results are not filtered");
    expect(container.textContent).not.toContain("No workflow runs match these filters");
    const healthFilter = container.querySelector<HTMLSelectElement>("select[aria-label='Workflow run incident health']")!;
    expect(healthFilter.disabled).toBe(false);
    expect(healthFilter.querySelector<HTMLOptionElement>("option[value='active']")?.disabled).toBe(true);
    expect(container.querySelector<HTMLSelectElement>("select[aria-label='Workflow run status']")?.value).toBe("Faulted");
    expect(input(container, "Workflow run definition").value).toBe("definition-1");
    expect(getJson).toHaveBeenCalledTimes(1);
    expect(getJson).toHaveBeenCalledWith("/capabilities");

    select(healthFilter, "");
    expect(healthFilter.disabled).toBe(true);
    click(buttonByText(container, "Apply filters"));
    await waitFor(() => expect(container.textContent).toContain("legacy-run"));
    const ordinaryRequest = getJson.mock.calls.map(([url]) => url).find(url => url.startsWith("/runtime/workflows/instances/page?"));
    expect(ordinaryRequest).toContain("status=Faulted");
    expect(ordinaryRequest).toContain("definitionId=definition-1");
    expect(ordinaryRequest).not.toContain("incidentHealth=");
    const appliedUrl = navigate.mock.lastCall?.[0];
    expect(appliedUrl).toContain("status=Faulted");
    expect(appliedUrl).toContain("definitionId=definition-1");
    expect(appliedUrl).not.toContain("incidentHealth=");
  });
});

function render(contextValue: StudioEndpointContext, navigate: (path: string) => void) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  mounted = { root, container };
  flushSync(() => root.render(<WorkflowInstances context={contextValue} navigate={navigate} />));
  return container;
}

async function waitFor(assertion: () => void) {
  let lastError: unknown;
  for (let attempt = 0; attempt < 40; attempt++) {
    try {
      assertion();
      return;
    } catch (error) {
      lastError = error;
      await new Promise(resolve => setTimeout(resolve, 0));
    }
  }
  throw lastError;
}

function context(getJson: (url: string) => Promise<unknown>) {
  return {
    baseUrl: `test://run-history-${Math.random()}`,
    http: { getJson }
  } as unknown as StudioEndpointContext;
}

function page(workflowExecutionId: string, overrides: Record<string, unknown> = {}) {
  return {
    items: [{
      workflowExecutionId,
      artifactId: "artifact-1",
      definitionId: "definition-1",
      definitionVersionId: "version-1",
      artifactVersion: "1.0.0",
      artifactHash: "sha256:test",
      status: "Completed",
      createdAt: "2026-07-01T00:00:00Z",
      activityCount: 1,
      incidentCount: 0
    }],
    previousCursor: null,
    nextCursor: null,
    hasPrevious: false,
    hasNext: false,
    count: 1,
    totalCount: 1,
    ...overrides
  };
}

function forwardOnlyPage(workflowExecutionId: string, nextCursor: string | null = null, totalCount = 1) {
  const items = page(workflowExecutionId).items;
  return { items, nextCursor, hasNext: Boolean(nextCursor), count: items.length, totalCount };
}

const capabilities = {
  capabilities: [{
    id: "elsa.api.runtime",
    contractVersion: "1",
    links: [
      { rel: "workflow-instances", href: "runtime/workflows/instances" },
      { rel: "workflow-instances-page", href: "runtime/workflows/instances/page" }
    ]
  }]
};

const healthCapabilities = {
  capabilities: [{
    id: "elsa.api.runtime",
    contractVersion: "1",
    links: [
      { rel: "workflow-instances", href: "runtime/workflows/instances" },
      { rel: "workflow-instances-page", href: "runtime/workflows/instances/page" },
      { rel: "workflow-instances-health-filter", href: "runtime/workflows/instances/health" }
    ]
  }]
};

function input(container: HTMLElement, label: string) {
  return container.querySelector<HTMLInputElement>(`input[aria-label='${label}']`)!;
}

function button(container: HTMLElement, label: string) {
  return container.querySelector<HTMLButtonElement>(`button[aria-label='${label}']`)!;
}

function buttonByText(container: HTMLElement, text: string) {
  return [...container.querySelectorAll<HTMLButtonElement>("button")].find(candidate => candidate.textContent === text)!;
}

function click(element: HTMLElement) {
  flushSync(() => element.click());
}

function select(element: HTMLSelectElement, value: string) {
  flushSync(() => {
    const setter = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value")!.set!;
    setter.call(element, value);
    element.dispatchEvent(new Event("change", { bubbles: true }));
  });
}

function fill(element: HTMLInputElement, value: string) {
  flushSync(() => {
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!;
    setter.call(element, value);
    element.dispatchEvent(new Event("input", { bubbles: true }));
  });
}
