import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import type { StudioDialogApi, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { EtlRunsPage } from "../EtlRunsPage";
import {
  EtlApiError,
  cancelEtlRun,
  getEtlRun,
  invalidateEtlApiCapabilityCache,
  listEtlRunPartitions,
  listEtlRuns
} from "../etlApi";
import type {
  EtlPage,
  EtlRunDetailView,
  EtlRunPartitionView,
  EtlRunSummaryView,
  EtlRunTerminalResultView
} from "../etlTypes";

vi.mock("../etlApi", async importOriginal => ({
  ...(await importOriginal<typeof import("../etlApi")>()),
  cancelEtlRun: vi.fn(),
  getEtlRun: vi.fn(),
  invalidateEtlApiCapabilityCache: vi.fn(),
  listEtlRunPartitions: vi.fn(),
  listEtlRuns: vi.fn()
}));

describe("ETL runs page", () => {
  beforeEach(() => {
    vi.mocked(listEtlRuns).mockResolvedValue(page([summary("run-1", "running")]));
    vi.mocked(getEtlRun).mockResolvedValue(detail("run-1", "running"));
    vi.mocked(listEtlRunPartitions).mockResolvedValue(page([partition(0)]));
    vi.mocked(cancelEtlRun).mockResolvedValue({ cancellationAccepted: true, terminalResult: terminal("cancelled") });
  });

  afterEach(() => {
    cleanupActive();
    vi.resetAllMocks();
  });

  it("loads scoped runs and selected partitions page by page, retaining the list after a failed load-more", async () => {
    vi.mocked(listEtlRuns)
      .mockResolvedValueOnce(page([summary("run-1", "running")], "runs-next"))
      .mockRejectedValueOnce(new EtlApiError("provider unavailable", 503, "etl.provider-unavailable"))
      .mockResolvedValueOnce(page([summary("run-2", "succeeded")]));
    vi.mocked(listEtlRunPartitions)
      .mockResolvedValueOnce(page([partition(0)], "partitions-next"))
      .mockResolvedValueOnce(page([partition(1)]));

    const context = endpointContext();
    const rendered = mount(context);
    await waitFor(() => expect(buttonContaining(rendered.host, "run-1")).toBeTruthy());

    click(buttonByText(rendered.host, "Load more runs"));
    await waitFor(() => expect(rendered.host.textContent).toContain("The ETL runtime provider is unavailable for more ETL runs."));
    expect(buttonContaining(rendered.host, "run-1")).toBeTruthy();
    expect(buttonContaining(rendered.host, "run-2")).toBeNull();

    click(buttonByText(rendered.host, "Retry"));
    await waitFor(() => expect(buttonContaining(rendered.host, "run-2")).toBeTruthy());

    click(buttonContaining(rendered.host, "run-1")!);
    await waitFor(() => expect(rendered.host.textContent).toContain("Run run-1"));
    expect(rendered.host.textContent).toContain("0–100 (end exclusive)");
    click(buttonByText(rendered.host, "Load more partitions"));
    await waitFor(() => expect(rendered.host.textContent).toContain("100–200 (end exclusive)"));
    expect(listEtlRuns).toHaveBeenNthCalledWith(1, context, { limit: 50 }, expect.any(AbortSignal));
    expect(listEtlRuns).toHaveBeenNthCalledWith(2, context, { limit: 50, cursor: "runs-next" }, expect.any(AbortSignal));
    expect(listEtlRunPartitions).toHaveBeenNthCalledWith(1, context, "run-1", { limit: 50 }, expect.any(AbortSignal));
    expect(listEtlRunPartitions).toHaveBeenNthCalledWith(2, context, "run-1", { limit: 50, cursor: "partitions-next" }, expect.any(AbortSignal));
  });

  it("distinguishes loading, empty, forbidden, and missing-capability states with retry", async () => {
    let finishInitial: ((value: EtlPage<EtlRunSummaryView>) => void) | undefined;
    vi.mocked(listEtlRuns)
      .mockImplementationOnce(() => new Promise(resolve => { finishInitial = resolve; }))
      .mockResolvedValueOnce(page([]));
    const rendered = mount(endpointContext());
    expect(rendered.host.textContent).toContain("Loading ETL runs…");
    finishInitial?.(page([]));
    await waitFor(() => expect(rendered.host.textContent).toContain("No ETL runs are available in this scope."));

    cleanupActive();
    vi.mocked(listEtlRuns).mockReset()
      .mockRejectedValueOnce(new EtlApiError("forbidden", 403, "etl.permission-denied"))
      .mockResolvedValueOnce(page([]));
    const forbidden = mount(endpointContext());
    await waitFor(() => expect(forbidden.host.textContent).toContain("You do not have permission to access ETL runs."));
    click(buttonByText(forbidden.host, "Retry"));
    await waitFor(() => expect(forbidden.host.textContent).toContain("No ETL runs are available in this scope."));

    cleanupActive();
    vi.mocked(listEtlRuns).mockReset()
      .mockRejectedValueOnce(new EtlApiError("missing", null, "etl.capability-unavailable"));
    const missingCapability = mount(endpointContext());
    await waitFor(() => expect(missingCapability.host.textContent).toContain("This host does not advertise the required ETL runtime capability."));
  });

  it("invalidates cached capability data on retry and refresh so newly available runtime routes recover", async () => {
    const context = endpointContext();
    vi.mocked(listEtlRuns)
      .mockRejectedValueOnce(new EtlApiError("missing capability", null, "etl.capability-unavailable"))
      .mockRejectedValueOnce(new EtlApiError("missing capability", null, "etl.capability-unavailable"))
      .mockResolvedValueOnce(page([summary("recovered-run", "running")]));

    const rendered = mount(context);
    await waitFor(() => expect(rendered.host.textContent).toContain("This host does not advertise the required ETL runtime capability."));
    click(buttonByText(rendered.host, "Retry"));
    await waitFor(() => expect(invalidateEtlApiCapabilityCache).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(rendered.host.textContent).toContain("This host does not advertise the required ETL runtime capability."));

    click(buttonByText(rendered.host, "Refresh"));
    await waitFor(() => expect(buttonContaining(rendered.host, "recovered-run")).toBeTruthy());
    expect(invalidateEtlApiCapabilityCache).toHaveBeenNthCalledWith(1, context);
    expect(invalidateEtlApiCapabilityCache).toHaveBeenNthCalledWith(2, context);
  });

  it("ignores a stale list response after the endpoint context changes", async () => {
    let resolveOld: ((value: EtlPage<EtlRunSummaryView>) => void) | undefined;
    let oldSignal: AbortSignal | undefined;
    vi.mocked(listEtlRuns)
      .mockImplementationOnce((_context, _request, signal) => {
        oldSignal = signal;
        return new Promise(resolve => { resolveOld = resolve; });
      })
      .mockResolvedValueOnce(page([summary("new-scope-run", "succeeded")]));

    const rendered = mount(endpointContext("old"));
    await waitFor(() => expect(resolveOld).toBeDefined());
    rendered.render(endpointContext("new"));
    await waitFor(() => expect(buttonContaining(rendered.host, "new-scope-run")).toBeTruthy());
    resolveOld?.(page([summary("old-scope-run", "running")]));
    await waitFor(() => expect(oldSignal?.aborted).toBe(true));
    expect(buttonContaining(rendered.host, "old-scope-run")).toBeNull();
  });

  it("ignores a stale detail response after the selected run changes", async () => {
    let resolveOld: ((value: EtlRunDetailView) => void) | undefined;
    let oldSignal: AbortSignal | undefined;
    vi.mocked(listEtlRuns).mockResolvedValue(page([
      summary("run-old", "running"),
      summary("run-new", "succeeded")
    ]));
    vi.mocked(getEtlRun).mockImplementation((_, runId, signal) => {
      if (runId === "run-old") {
        oldSignal = signal;
        return new Promise(resolve => { resolveOld = resolve; });
      }
      return Promise.resolve(detail("run-new", "succeeded"));
    });

    const rendered = mount(endpointContext());
    await waitFor(() => expect(buttonContaining(rendered.host, "run-old")).toBeTruthy());
    click(buttonContaining(rendered.host, "run-old")!);
    await waitFor(() => expect(resolveOld).toBeDefined());
    click(buttonContaining(rendered.host, "run-new")!);
    await waitFor(() => expect(rendered.host.textContent).toContain("Run run-new"));
    resolveOld?.(detail("run-old", "running"));
    await waitFor(() => expect(oldSignal?.aborted).toBe(true));
    expect(rendered.host.textContent).not.toContain("Run run-old");
  });

  it("confirms cancellation and displays the actual terminal outcome when success wins the race", async () => {
    const context = endpointContext();
    const dialogs = createDialogs(true);
    vi.mocked(listEtlRuns)
      .mockResolvedValueOnce(page([summary("run-1", "running")]))
      .mockResolvedValueOnce(page([summary("run-1", "succeeded")]));
    vi.mocked(getEtlRun)
      .mockResolvedValueOnce(detail("run-1", "running"))
      .mockResolvedValueOnce(detail("run-1", "succeeded"));
    vi.mocked(cancelEtlRun).mockResolvedValue({ cancellationAccepted: false, terminalResult: terminal("succeeded") });

    const rendered = mount(context, dialogs);
    await waitFor(() => expect(buttonContaining(rendered.host, "run-1")).toBeTruthy());
    click(buttonContaining(rendered.host, "run-1")!);
    await waitFor(() => expect(buttonByText(rendered.host, "Cancel run")).toBeTruthy());
    click(buttonByText(rendered.host, "Cancel run"));

    await waitFor(() => expect(rendered.host.textContent).toContain("Cancellation did not win. Actual terminal outcome: Succeeded."));
    expect(dialogs.confirm).toHaveBeenCalledWith(expect.objectContaining({
      message: expect.stringContaining("Completed output writes are retained"),
      confirmLabel: "Cancel run",
      tone: "danger"
    }));
    expect(cancelEtlRun).toHaveBeenCalledWith(context, "run-1", expect.any(AbortSignal));
    expect(rendered.host.textContent).toContain("Succeeded");
    expect(rendered.host.textContent).toContain("7");
    expect(buttonOrNull(rendered.host, "Cancel run")).toBeNull();
  });

  it("shows partial output and outstanding-work details for a cancelled run", async () => {
    vi.mocked(listEtlRuns).mockResolvedValue(page([summary("run-1", "cancelled")]));
    vi.mocked(getEtlRun).mockResolvedValue(detail("run-1", "cancelled", terminal("cancelled")));
    const rendered = mount(endpointContext());
    await waitFor(() => expect(buttonContaining(rendered.host, "run-1")).toBeTruthy());
    click(buttonContaining(rendered.host, "run-1")!);
    await waitFor(() => expect(rendered.host.textContent).toContain("Output may be partial."));
    expect(rendered.host.textContent).toContain("Outstanding records");
    expect(rendered.host.textContent).toContain("Outstanding bytes");
    expect(rendered.host.textContent).toContain("does not undo file writes");
    expect(buttonOrNull(rendered.host, "Cancel run")).toBeNull();
    expect(cancelEtlRun).not.toHaveBeenCalled();
  });

  it("does not submit cancellation when the confirmation is declined", async () => {
    const dialogs = createDialogs(false);
    const rendered = mount(endpointContext(), dialogs);
    await waitFor(() => expect(buttonContaining(rendered.host, "run-1")).toBeTruthy());
    click(buttonContaining(rendered.host, "run-1")!);
    await waitFor(() => expect(buttonByText(rendered.host, "Cancel run")).toBeTruthy());
    click(buttonByText(rendered.host, "Cancel run"));
    await waitFor(() => expect(dialogs.confirm).toHaveBeenCalledTimes(1));
    expect(cancelEtlRun).not.toHaveBeenCalled();
    expect(rendered.host.textContent).toContain("Processing");
  });

  it("does not submit cancellation when confirmation resolves after the page unmounts", async () => {
    let resolveConfirmation: ((confirmed: boolean) => void) | undefined;
    const dialogs = createDialogs();
    vi.mocked(dialogs.confirm).mockImplementation(() => new Promise(resolve => { resolveConfirmation = resolve; }));

    const rendered = mount(endpointContext(), dialogs);
    await waitFor(() => expect(buttonContaining(rendered.host, "run-1")).toBeTruthy());
    click(buttonContaining(rendered.host, "run-1")!);
    await waitFor(() => expect(buttonByText(rendered.host, "Cancel run")).toBeTruthy());
    click(buttonByText(rendered.host, "Cancel run"));
    await waitFor(() => expect(resolveConfirmation).toBeDefined());

    cleanupActive();
    resolveConfirmation?.(true);
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(cancelEtlRun).not.toHaveBeenCalled();
  });
});

let active: { host: HTMLDivElement; root: Root; cleanup(): void } | null = null;

function cleanupActive() {
  const current = active;
  active = null;
  current?.cleanup();
}

function mount(context: StudioEndpointContext, dialogs = createDialogs()) {
  const host = document.createElement("div");
  document.body.append(host);
  const root = createRoot(host);
  const rendered = {
    host,
    root,
    render(nextContext: StudioEndpointContext) {
      flushSync(() => root.render(<EtlRunsPage context={nextContext} dialogs={dialogs} />));
    },
    cleanup() {
      flushSync(() => root.unmount());
      host.remove();
    }
  };
  active = rendered;
  rendered.render(context);
  return rendered;
}

function createDialogs(confirmed = true): StudioDialogApi {
  return {
    confirm: vi.fn(async () => confirmed),
    prompt: vi.fn(async () => null),
    alert: vi.fn(async () => {})
  };
}

function endpointContext(name = "host"): StudioEndpointContext {
  return { baseUrl: `https://${name}.example/studio`, http: {} as StudioEndpointContext["http"] };
}

function summary(runId: string, status: EtlRunSummaryView["status"]): EtlRunSummaryView {
  return {
    runId,
    selection: "etl1:orders:1:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    status,
    sourceRecordCount: 200,
    outstandingRecords: status === "running" ? 3 : 0,
    outstandingBytes: status === "running" ? 192 : 0,
    acceptedRecords: status === "succeeded" ? 7 : 2,
    rejectedRecords: 1,
    filteredRecords: 4,
    diagnostic: null
  };
}

function detail(runId: string, status: EtlRunDetailView["status"], terminalResult: EtlRunTerminalResultView | null = null): EtlRunDetailView {
  const run = summary(runId, status);
  return {
    ...run,
    bindingAliases: { source: "source-v1", lookup: "lookup-v1", output: "accepted-v1", errors: "errors-v1" },
    terminalResult,
    sourceInput: { bindingRevision: "source-rev-1", expectedSha256: "a".repeat(64), snapshotSha256: "b".repeat(64), snapshotByteLength: 512 },
    lookupInput: { bindingRevision: "lookup-rev-1", expectedSha256: "c".repeat(64), snapshotSha256: "d".repeat(64), snapshotByteLength: 256 }
  };
}

function terminal(outcome: EtlRunTerminalResultView["outcome"]): EtlRunTerminalResultView {
  return {
    outcome,
    acceptedRecords: 7,
    rejectedRecords: 1,
    filteredRecords: 4,
    diagnostic: null,
    settledAt: "2026-10-20T10:00:00Z"
  };
}

function partition(partitionOrdinal: number): EtlRunPartitionView {
  return {
    partitionOrdinal,
    startOrdinal: partitionOrdinal * 100,
    endOrdinalExclusive: (partitionOrdinal + 1) * 100,
    nextOrdinal: partitionOrdinal * 100,
    isClaimed: partitionOrdinal === 0,
    claimFence: partitionOrdinal === 0 ? 3 : null,
    claimExpiresAt: partitionOrdinal === 0 ? "2026-10-20T10:05:00Z" : null,
    owedBatch: partitionOrdinal === 0 ? { startOrdinal: 0, endOrdinalExclusive: 20, inputBytes: 2048 } : null
  };
}

function page<T>(items: T[], nextCursor: string | null = null): EtlPage<T> {
  return { items, nextCursor };
}

function buttonByText(host: HTMLElement, text: string): HTMLButtonElement {
  const button = buttonOrNull(host, text);
  if (!button) throw new Error(`Button not found: ${text}`);
  return button;
}

function buttonOrNull(host: HTMLElement, text: string): HTMLButtonElement | null {
  return Array.from(host.querySelectorAll<HTMLButtonElement>("button")).find(item => item.textContent?.trim() === text) ?? null;
}

function buttonContaining(host: HTMLElement, text: string): HTMLButtonElement | null {
  return Array.from(host.querySelectorAll<HTMLButtonElement>("button")).find(item => item.textContent?.includes(text)) ?? null;
}

function click(button: HTMLButtonElement) {
  flushSync(() => button.click());
}

async function waitFor(assertion: () => void) {
  let lastError: unknown;
  for (let attempt = 0; attempt < 80; attempt++) {
    try {
      assertion();
      return;
    } catch (error) {
      lastError = error;
      await new Promise(resolve => setTimeout(resolve, 5));
    }
  }
  throw lastError;
}
