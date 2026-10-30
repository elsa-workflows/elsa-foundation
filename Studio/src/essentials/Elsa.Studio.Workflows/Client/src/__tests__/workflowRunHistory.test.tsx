import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { afterEach, describe, expect, it, vi } from "vitest";
import { clearApiCapabilityCache } from "../api/capabilities";
import { getRunHistoryBackLocation, parseRunHistoryReturnTo, WorkflowInstances } from "../workflow-editor/WorkflowInstances";
import { setDialogs } from "../workflow-editor/dialogs";

const confirmRuns = vi.fn(async (_options: { title?: string; message: string }) => false);
setDialogs({ confirm: confirmRuns, prompt: async () => null, alert: async () => {} });

let mounted: { root: Root; container: HTMLDivElement } | null = null;
let previousClipboard: PropertyDescriptor | undefined;

afterEach(() => {
  if (previousClipboard) Object.defineProperty(navigator, "clipboard", previousClipboard);
  else Reflect.deleteProperty(navigator, "clipboard");
  previousClipboard = undefined;
  clearApiCapabilityCache();
  confirmRuns.mockReset().mockResolvedValue(false);
  vi.restoreAllMocks();
  if (mounted) {
    flushSync(() => mounted!.root.unmount());
    mounted.container.remove();
    mounted = null;
  }
  window.history.replaceState({}, "", "/");
});

describe("workflow run history", () => {
  it("keeps the execution ID primary and discloses copyable secondary IDs without opening the row", async () => {
    const exactRun = {
      ...page("workflow-execution-0123456789abcdef0123456789abcdef").items[0],
      artifactId: "workflow-artifact-exact",
      definitionId: "workflow-definition-exact",
      definitionVersionId: "workflow-definition-version-exact"
    };
    const getJson = vi.fn(async (url: string) => url === "/capabilities" ? capabilities : page(exactRun.workflowExecutionId, { items: [exactRun] }));
    const writeText = vi.fn().mockResolvedValue(undefined);
    previousClipboard = Object.getOwnPropertyDescriptor(navigator, "clipboard");
    Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText } });
    const navigate = vi.fn();
    const container = render(context(getJson), navigate);

    await waitFor(() => expect(container.querySelector(".wf-run-primary-identifier .studio-copyable-identifier__value")?.getAttribute("aria-label")).toBe(`Execution ID: ${exactRun.workflowExecutionId}`));
    const row = container.querySelector<HTMLElement>(".wf-instance-grid .wf-grid-row")!;
    const disclosure = row.querySelector<HTMLButtonElement>(".wf-run-secondary-identifiers button[aria-haspopup=dialog]")!;
    const copyExecutionId = row.querySelector<HTMLButtonElement>('button[aria-label="Copy Execution ID"]')!;
    expect(document.querySelector('[role="dialog"]')).toBeNull();
    for (const key of ["Enter", " "]) {
      flushSync(() => disclosure.dispatchEvent(new KeyboardEvent("keydown", { key, bubbles: true })));
      flushSync(() => copyExecutionId.dispatchEvent(new KeyboardEvent("keydown", { key, bubbles: true })));
    }
    expect(navigate).not.toHaveBeenCalled();

    click(disclosure);
    expect(disclosure.getAttribute("aria-expanded")).toBe("true");
    expect(document.body.textContent).toContain("Artifact ID");
    expect(document.body.textContent).toContain(exactRun.artifactId);
    expect(document.body.textContent).toContain(exactRun.definitionId);
    expect(document.body.textContent).toContain(exactRun.definitionVersionId);
    const copyIconPath = copyExecutionId.querySelector("svg path")!;
    flushSync(() => copyIconPath.dispatchEvent(new MouseEvent("click", { bubbles: true })));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith(exactRun.workflowExecutionId));
    expect(navigate).not.toHaveBeenCalled();
  });

  it("canonicalizes only an exact same-origin Runs return location", () => {
    const origin = window.location.origin;
    expect(parseRunHistoryReturnTo("/workflows/instances?status=Running&pageSize=50&cursor=opaque&ignored=yes", origin))
      .toBe("/workflows/instances?status=Running&pageSize=50&cursor=opaque");
    expect(parseRunHistoryReturnTo(`${origin}/workflows/instances?artifactId=artifact-1`, origin))
      .toBe("/workflows/instances?artifactId=artifact-1");
    expect(getRunHistoryBackLocation(null)).toBe("/workflows/instances");
    expect(getRunHistoryBackLocation("https://external.example/workflows/instances")).toBe("/workflows/instances");
    for (const invalid of [
      "https://external.example/workflows/instances?status=Running",
      "//external.example/workflows/instances?status=Running",
      "//localhost/workflows/instances?status=Running",
      "/workflows/instances/child?status=Running",
      "/workflows/instances-extra?status=Running",
      "/workflows/instances/../instances?status=Running",
      "javascript:alert(1)"
    ]) {
      expect(parseRunHistoryReturnTo(invalid, origin), invalid).toBeNull();
    }
  });

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

    openMoreFilters(container);
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
    expect(input(container, "Workflow").value).toBe("definition-1");
    expect(button(container, "Copy Workflow definition ID")).toBeTruthy();
    expect(input(container, "Workflow run artifact").value).toBe("artifact-1");
    expect(container.textContent).toContain("Showing 1 of 42 matching runs");
    expect(container.querySelector(".wf-run-health-indicator .wf-visually-hidden")?.textContent).toBe("Current health unavailable");
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

  it("applies primary triage choices immediately and preserves unrelated advanced drafts", async () => {
    window.history.replaceState({}, "", "/workflows/instances?pageSize=10");
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return healthCapabilities;
      return page("active-running");
    });
    const navigate = vi.fn((path: string) => window.history.pushState({}, "", path));
    const container = render(context(getJson), navigate);

    await waitFor(() => expect(container.textContent).toContain("active-running"));
    const attention = buttonByTextOptional(container, "Needs attention");
    expect(attention).toBeTruthy();
    click(attention!);
    await waitFor(() => expect(navigate).toHaveBeenLastCalledWith(expect.stringContaining("incidentHealth=active")));
    await waitFor(() => expect(getJson).toHaveBeenCalledWith("/runtime/workflows/instances/health?incidentHealth=active&take=10"));

    openMoreFilters(container);
    fill(input(container, "Workflow run artifact"), "draft-artifact");
    const running = buttonByTextOptional(container, "Running");
    expect(running).toBeTruthy();
    click(running!);
    await waitFor(() => expect(navigate).toHaveBeenLastCalledWith(expect.stringMatching(/status=Running.*incidentHealth=active|incidentHealth=active.*status=Running/)));
    await waitFor(() => expect(getJson).toHaveBeenCalledWith(expect.stringMatching(/instances\/health\?.*status=Running.*incidentHealth=active|instances\/health\?.*incidentHealth=active.*status=Running/)));
    expect(input(container, "Workflow run artifact").value).toBe("draft-artifact");

    click(buttonByText(container, "Apply filters"));
    await waitFor(() => expect(navigate).toHaveBeenLastCalledWith(expect.stringContaining("artifactId=draft-artifact")));
    expect(navigate).toHaveBeenLastCalledWith(expect.stringContaining("status=Running"));
    expect(navigate).toHaveBeenLastCalledWith(expect.stringContaining("incidentHealth=active"));
  });

  it("removes only the selected applied chip and preserves page size", async () => {
    window.history.replaceState({}, "", "/workflows/instances?incidentHealth=active&artifactId=artifact-1&pageSize=10&cursor=next-cursor");
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return healthCapabilities;
      return page(url.includes("cursor=next-cursor") ? "execution-2" : "execution-1");
    });
    const navigate = vi.fn((path: string) => window.history.pushState({}, "", path));
    const container = render(context(getJson), navigate);

    await waitFor(() => expect(container.textContent).toContain("execution-2"));
    expect(container.querySelector(".wf-run-filter-chip")).toBeTruthy();
    const removeArtifact = button(container, "Remove Artifact filter");
    click(removeArtifact);

    await waitFor(() => expect(navigate).toHaveBeenLastCalledWith("/workflows/instances?incidentHealth=active&pageSize=10"));
    await waitFor(() => expect(getJson).toHaveBeenCalledWith("/runtime/workflows/instances/health?incidentHealth=active&take=10"));
    expect(input(container, "Workflow run artifact").value).toBe("");
  });

  it("adds the validated current Runs location when opening a real row", async () => {
    window.history.replaceState({}, "", "/workflows/instances?status=Running&incidentHealth=active&pageSize=10&cursor=second-page");
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return healthCapabilities;
      return page("execution-2");
    });
    const navigate = vi.fn();
    const container = render(context(getJson), navigate);

    await waitFor(() => expect(container.textContent).toContain("execution-2"));
    click(button(container, "Inspect workflow run execution-2 · definition-1"));

    const detail = new URL(navigate.mock.lastCall?.[0] ?? "", "https://studio.example");
    expect(detail.pathname).toBe("/workflows/instances/execution-2");
    expect(detail.searchParams.get("returnTo")).toBe("/workflows/instances?status=Running&incidentHealth=active&pageSize=10&cursor=second-page");
  });

  it("leads with the workflow name and copies exact identifiers without inspecting the run", async () => {
    previousClipboard = Object.getOwnPropertyDescriptor(navigator, "clipboard");
    const writeText = vi.fn(async (_value: string) => {});
    Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText } });
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return definitionCapabilities;
      if (url.startsWith("/design/workflows/definitions")) {
        return { items: [{ id: "definition-1", name: "Order fulfillment" }] };
      }
      return page("execution-1");
    });
    const navigate = vi.fn();
    const container = render(context(getJson), navigate);

    await waitFor(() => expect(container.querySelector(".wf-run-inspect")?.textContent).toContain("Order fulfillment"));
    const disclosure = container.querySelector<HTMLButtonElement>(".wf-run-secondary-identifiers button[aria-haspopup=dialog]")!;
    expect(document.querySelector('[role="dialog"]')).toBeNull();
    click(disclosure);
    expect(disclosure.getAttribute("aria-expanded")).toBe("true");
    const expectedValues = [
      ["Copy Execution ID", "execution-1"],
      ["Copy Artifact ID", "artifact-1"],
      ["Copy Definition ID", "definition-1"],
      ["Copy Definition version ID", "version-1"]
    ] as const;
    for (const [label, exactValue] of expectedValues) {
      const copyButton = button(label === "Copy Execution ID" ? container : document.body, label);
      if (label === "Copy Execution ID") {
        flushSync(() => copyButton.querySelector("svg path")!.dispatchEvent(new MouseEvent("click", { bubbles: true })));
      } else click(copyButton);
      await waitFor(() => expect(writeText).toHaveBeenLastCalledWith(exactValue));
    }

    expect(navigate).not.toHaveBeenCalled();
    expect(container.querySelector(".wf-run-inspect strong")?.textContent).toBe("Order fulfillment");
    expect(container.querySelector(".wf-run-inspect")?.getAttribute("aria-label")).toBe("Inspect workflow run execution-1 · Order fulfillment");
  });

  it("lets browser history restore the same applied Runs query after inspecting a row", async () => {
    const returnPath = "/workflows/instances?status=Running&pageSize=10";
    window.history.replaceState({}, "", returnPath);
    const getJson = vi.fn(async (url: string) => url === "/capabilities" ? capabilities : page("execution-1"));
    const navigate = vi.fn((path: string) => window.history.pushState({}, "", path));
    const container = render(context(getJson), navigate);

    await waitFor(() => expect(container.textContent).toContain("execution-1"));
    click(button(container, "Inspect workflow run execution-1 · definition-1"));
    expect(navigate).toHaveBeenLastCalledWith("/workflows/instances/execution-1?returnTo=%2Fworkflows%2Finstances%3Fstatus%3DRunning%26pageSize%3D10");

    window.history.replaceState({}, "", returnPath);
    window.dispatchEvent(new PopStateEvent("popstate"));
    await waitFor(() => expect(buttonByTextOptional(container, "Running")?.getAttribute("aria-pressed")).toBe("true"));
    expect(getJson).toHaveBeenCalledWith("/runtime/workflows/instances/page?status=Running&take=10");
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
    let fail = true;
    const errorContainer = render(context(async url => {
      if (url === "/capabilities") return capabilities;
      if (fail) throw new Error("Runtime unavailable");
      return page("retry-run");
    }), vi.fn());
    await waitFor(() => expect(errorContainer.querySelector("[role='alert']")?.textContent).toContain("Runtime unavailable"));
    const retry = buttonByTextOptional(errorContainer, "Retry");
    expect(retry).toBeTruthy();
    fail = false;
    click(retry!);
    await waitFor(() => expect(errorContainer.textContent).toContain("retry-run"));
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
    expect(container.querySelector('[title="1 activities · 4 incidents"]')).not.toBeNull();
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
    expect(input(container, "Workflow").value).toBe("definition-1");
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

  it("hides deletion controls when Runtime does not advertise the deletion relation", async () => {
    const deleteJson = vi.fn(async () => ({ workflowExecutionId: "execution-1", status: "Deleted" }));
    const getJson = vi.fn(async (url: string) => url === "/capabilities" ? capabilities : page("execution-1"));
    const container = render(context(getJson, deleteJson), vi.fn());

    await waitFor(() => expect(container.textContent).toContain("execution-1"));
    expect(container.querySelector("input[aria-label='Select all workflow runs on this page']")).toBeNull();
    expect(container.querySelector("button[aria-label^='Actions for workflow run']")).toBeNull();
    expect(buttonByTextOptional(container, "Delete selected")).toBeNull();
    expect(deleteJson).not.toHaveBeenCalled();
  });

  it("keeps an unavailable or resource-denied run selected without claiming success", async () => {
    const getJson = vi.fn(async (url: string) => url === "/capabilities" ? deletionCapabilities : pageWithRuns("execution-1"));
    const deleteJson = vi.fn(async () => { throw { status: 404 }; });
    const container = render(context(getJson, deleteJson), vi.fn());
    confirmRuns.mockResolvedValue(true);
    await waitFor(() => expect(container.textContent).toContain("execution-1"));
    click(input(container, "Select workflow run execution-1"));
    click(buttonByText(container, "Delete selected"));
    await waitFor(() => expect(container.querySelector("[role='alert']")?.textContent).toContain("do not have permission"));
    expect(container.querySelector("[role='alert']")?.textContent).toContain("No workflow runs were deleted.");
    expect(input(container, "Select workflow run execution-1").checked).toBe(true);
  });

  it("drops selections that disappear when refreshing the visible page", async () => {
    let ids = ["execution-1", "execution-2"];
    const getJson = vi.fn(async (url: string) => url === "/capabilities" ? deletionCapabilities : pageWithRuns(...ids));
    const container = render(context(getJson, vi.fn()), vi.fn());
    await waitFor(() => expect(container.textContent).toContain("execution-1"));
    click(input(container, "Select all workflow runs on this page"));
    expect(container.querySelector(".wf-run-deletion-toolbar")?.textContent).toContain("2 workflow runs selected");
    ids = ["execution-2"];
    click(buttonByText(container, "Refresh"));
    await waitFor(() => expect(container.querySelector(".wf-run-deletion-toolbar")?.textContent).toContain("1 workflow run selected"));
    expect(input(container, "Select workflow run execution-2").checked).toBe(true);
    expect(container.querySelector("input[aria-label='Select workflow run execution-1']")).toBeNull();
  });

  it("selects visible runs, confirms exact IDs, cancels without a request, and preserves row keyboard inspection", async () => {
    const deleteJson = vi.fn(async (url: string) => ({
      workflowExecutionId: decodeURIComponent(url.split("/").pop()!),
      status: "Deleted"
    }));
    const getJson = vi.fn(async (url: string) => url === "/capabilities" ? deletionCapabilities : pageWithRuns("execution-1", "execution-2"));
    const navigate = vi.fn();
    const container = render(context(getJson, deleteJson), navigate);
    const confirm = confirmRuns.mockResolvedValue(false);

    await waitFor(() => expect(container.textContent).toContain("execution-1"));
    expect(container.querySelector(".wf-run-deletion-toolbar")).toBeNull();
    const selectAll = input(container, "Select all workflow runs on this page");
    expect(selectAll.checked).toBe(false);
    click(input(container, "Select workflow run execution-1"));
    await waitFor(() => expect(selectAll.indeterminate).toBe(true));
    click(input(container, "Select workflow run execution-2"));
    expect(selectAll.checked).toBe(true);
    expect(selectAll.indeterminate).toBe(false);
    expect(container.querySelector(".wf-run-deletion-toolbar")?.textContent).toContain("2 workflow runs selected");

    const deleteSelected = buttonByText(container, "Delete selected");
    deleteSelected.focus();
    click(deleteSelected);
    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ title: "Delete these 2 workflow runs?", tone: "danger" }));
    expect(confirm).toHaveBeenLastCalledWith(expect.objectContaining({ message: expect.stringContaining("execution-1\nexecution-2") }));
    expect(confirm).toHaveBeenLastCalledWith(expect.objectContaining({ message: expect.stringContaining("Active runs will be stopped before deletion.") }));
    expect(confirm).toHaveBeenLastCalledWith(expect.objectContaining({ message: expect.stringContaining("This action cannot be undone.") }));
    expect(deleteJson).not.toHaveBeenCalled();
    expect(selectAll.checked).toBe(true);

    const row = container.querySelector<HTMLElement>(".wf-grid-row")!;
    row.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true }));
    expect(navigate).toHaveBeenCalledWith("/workflows/instances/execution-1?returnTo=%2Fworkflows%2Finstances");
  });

  it("keeps only failed runs selected after a partial deletion so they can be retried", async () => {
    let failExecutionTwo = true;
    const deleteJson = vi.fn(async (url: string) => {
      if (url.endsWith("execution-2") && failExecutionTwo) throw new Error("Runtime unavailable");
      return { workflowExecutionId: url.split("/").pop(), status: "Deleted" };
    });
    const getJson = vi.fn(async (url: string) => url === "/capabilities" ? deletionCapabilities : pageWithRuns("execution-1", "execution-2"));
    const container = render(context(getJson, deleteJson), vi.fn());
    confirmRuns.mockResolvedValue(true);

    await waitFor(() => expect(container.textContent).toContain("execution-1"));
    click(input(container, "Select all workflow runs on this page"));
    click(buttonByText(container, "Delete selected"));

    await waitFor(() => expect(container.querySelector("[role='alert']")?.textContent).toContain("Runtime unavailable"));
    expect(container.querySelector("[role='alert']")?.textContent).toContain("1 workflow run deleted.");
    await waitFor(() => expect(input(container, "Select workflow run execution-1").checked).toBe(false));
    expect(input(container, "Select workflow run execution-2").checked).toBe(true);
    expect(container.querySelector(".wf-run-deletion-toolbar")?.textContent).toContain("1 workflow run selected");

    failExecutionTwo = false;
    click(buttonByText(container, "Delete selected"));
    await waitFor(() => expect(container.querySelector(".wf-run-deletion-feedback")?.textContent).toContain("1 workflow run deleted."));
    await waitFor(() => expect(deleteJson).toHaveBeenCalledTimes(3));
    await waitFor(() => expect(input(container, "Select workflow run execution-2").checked).toBe(false));
  });

  it("clears selection when the visible page or applied filters change", async () => {
    window.history.replaceState({}, "", "/workflows/instances?pageSize=10");
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return deletionCapabilities;
      if (url.includes("cursor=next-cursor")) return page("execution-2");
      if (new URL(url, window.location.origin).searchParams.get("workflowExecutionId") === "filtered-run") return page("filtered-run");
      return page("execution-1", { nextCursor: "next-cursor", hasNext: true });
    });
    const navigate = vi.fn((path: string) => window.history.pushState({}, "", path));
    const container = render(context(getJson), navigate);

    await waitFor(() => expect(container.textContent).toContain("execution-1"));
    click(input(container, "Select all workflow runs on this page"));
    expect(container.querySelector(".wf-run-deletion-toolbar")?.textContent).toContain("1 workflow run selected");

    click(button(container, "Next workflow run page"));
    await waitFor(() => expect(container.textContent).toContain("execution-2"));
    expect(input(container, "Select workflow run execution-2").checked).toBe(false);
    expect(container.querySelector(".wf-run-deletion-toolbar")).toBeNull();

    click(input(container, "Select workflow run execution-2"));
    openMoreFilters(container);
    fill(input(container, "Workflow run execution id"), "filtered-run");
    click(buttonByText(container, "Apply filters"));
    await waitFor(() => expect(container.querySelector("[aria-label='Select workflow run filtered-run']")).not.toBeNull());
    expect(input(container, "Select workflow run filtered-run").checked).toBe(false);
    expect(container.querySelector(".wf-run-deletion-toolbar")).toBeNull();
  });

  it("limits bulk deletion to three concurrent requests", async () => {
    let activeRequests = 0;
    let maximumConcurrentRequests = 0;
    const completions: Array<() => void> = [];
    const runIds = ["execution-1", "execution-2", "execution-3", "execution-4"];
    const deleteJson = vi.fn((url: string) => {
      activeRequests++;
      maximumConcurrentRequests = Math.max(maximumConcurrentRequests, activeRequests);
      return new Promise(resolve => completions.push(() => {
        activeRequests--;
        resolve({ workflowExecutionId: decodeURIComponent(url.split("/").pop()!), status: "Deleted" });
      }));
    });
    const getJson = vi.fn(async (url: string) => url === "/capabilities" ? deletionCapabilities : pageWithRuns(...runIds));
    const container = render(context(getJson, deleteJson), vi.fn());
    confirmRuns.mockResolvedValue(true);

    await waitFor(() => expect(container.textContent).toContain("execution-1"));
    click(input(container, "Select all workflow runs on this page"));
    click(buttonByText(container, "Delete selected"));
    await waitFor(() => expect(deleteJson).toHaveBeenCalledTimes(3));
    expect(maximumConcurrentRequests).toBe(3);

    completions.splice(0, 3).forEach(complete => complete());
    await waitFor(() => expect(deleteJson).toHaveBeenCalledTimes(4));
    expect(maximumConcurrentRequests).toBe(3);
    completions.shift()!();
    await waitFor(() => expect(container.querySelector(".wf-run-deletion-feedback")?.textContent).toContain("4 workflow runs deleted."));
  });

  it("reloads the current cursor after deletion and returns to the first page when that cursor is empty", async () => {
    window.history.replaceState({}, "", "/workflows/instances?pageSize=10");
    let nextPageLoads = 0;
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return deletionCapabilities;
      if (url.includes("cursor=next-cursor")) {
        nextPageLoads++;
        return nextPageLoads === 1
          ? page("execution-2")
          : page("execution-2", { items: [], count: 0, totalCount: 1 });
      }
      return page("execution-1", { nextCursor: "next-cursor", hasNext: true, totalCount: 2 });
    });
    const deleteJson = vi.fn(async () => ({ workflowExecutionId: "execution-2", status: "Deleted" }));
    const navigate = vi.fn((path: string) => window.history.pushState({}, "", path));
    const container = render(context(getJson, deleteJson), navigate);
    confirmRuns.mockResolvedValue(true);

    await waitFor(() => expect(container.textContent).toContain("execution-1"));
    click(button(container, "Next workflow run page"));
    await waitFor(() => expect(container.textContent).toContain("execution-2"));
    click(button(container, "Actions for workflow run execution-2"));
    click(buttonByText(document.body, "Delete"));

    await waitFor(() => expect(container.textContent).toContain("execution-1"));
    expect(getJson.mock.calls.map(([url]) => url).filter(url => url.includes("cursor=next-cursor"))).toHaveLength(2);
    expect(navigate).toHaveBeenLastCalledWith("/workflows/instances?pageSize=10");
  });

  it("ignores a deletion result after the endpoint context changes", async () => {
    let resolveDeletion: ((value: unknown) => void) | undefined;
    const oldDeleteJson = vi.fn(() => new Promise(resolve => { resolveDeletion = resolve; }));
    const oldGetJson = vi.fn(async (url: string) => url === "/capabilities" ? deletionCapabilities : page("old-run"));
    const navigate = vi.fn();
    const container = render(context(oldGetJson, oldDeleteJson), navigate);
    confirmRuns.mockResolvedValue(true);

    await waitFor(() => expect(container.textContent).toContain("old-run"));
    const deleteAction = button(container, "Actions for workflow run old-run");
    click(deleteAction);
    click(buttonByText(document.body, "Delete"));
    await waitFor(() => expect(oldDeleteJson).toHaveBeenCalledTimes(1));
    click(deleteAction);
    expect(buttonByText(document.body, "Delete").disabled).toBe(true);

    const nextGetJson = vi.fn(async (url: string) => url === "/capabilities" ? deletionCapabilities : page("new-run"));
    rerender(context(nextGetJson), navigate);
    await waitFor(() => expect(container.textContent).toContain("new-run"));
    resolveDeletion!({ workflowExecutionId: "old-run", status: "Deleted" });
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(container.querySelector(".wf-run-deletion-feedback")).toBeNull();
    expect(oldGetJson).toHaveBeenCalledTimes(2);
    expect(nextGetJson).toHaveBeenCalledTimes(2);
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

function rerender(contextValue: StudioEndpointContext, navigate: (path: string) => void) {
  flushSync(() => mounted!.root.render(<WorkflowInstances context={contextValue} navigate={navigate} />));
  return mounted!.container;
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

function context(
  getJson: (url: string) => Promise<unknown>,
  deleteJson: (url: string) => Promise<unknown> = async () => ({})
) {
  return {
    baseUrl: `test://run-history-${Math.random()}`,
    http: { getJson, deleteJson }
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

function pageWithRuns(...workflowExecutionIds: string[]) {
  const template = page(workflowExecutionIds[0] ?? "execution-1").items[0];
  const items = workflowExecutionIds.map(workflowExecutionId => ({ ...template, workflowExecutionId }));
  return page(workflowExecutionIds[0] ?? "execution-1", { items, count: items.length, totalCount: items.length });
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

const definitionCapabilities = {
  capabilities: [
    ...capabilities.capabilities,
    {
      id: "elsa.api.workflow-design",
      contractVersion: "1",
      links: [{ rel: "workflow-definitions", href: "design/workflows/definitions" }]
    }
  ]
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

const deletionCapabilities = {
  capabilities: [{
    id: "elsa.api.runtime",
    contractVersion: "1",
    links: [
      { rel: "workflow-instances", href: "runtime/workflows/instances" },
      { rel: "workflow-instances-page", href: "runtime/workflows/instances/page" },
      { rel: "workflow-instance-delete", href: "runtime/workflows/instances/{workflowExecutionId}", templated: true }
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

function buttonByTextOptional(container: HTMLElement, text: string) {
  return [...container.querySelectorAll<HTMLButtonElement>("button")].find(candidate => candidate.textContent === text) ?? null;
}

function openMoreFilters(container: HTMLElement) {
  const trigger = [...container.querySelectorAll<HTMLButtonElement>(".studio-disclosure-fold-trigger")]
    .find(candidate => candidate.textContent?.includes("More filters"));
  if (trigger?.getAttribute("aria-expanded") !== "true") click(trigger!);
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
