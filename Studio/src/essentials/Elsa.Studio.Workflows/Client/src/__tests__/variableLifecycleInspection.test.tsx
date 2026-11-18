import { renderToStaticMarkup } from "react-dom/server";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { clearApiCapabilityCache } from "../api/capabilities";
import {
  compareInstances,
  getVariableWritePayload,
  listInstanceJournal,
  listVariableWrites,
  readAllPages,
  supportsVariableLifecycleInspection,
  type JournalCommit,
  type VariableWriteEntry
} from "../api/variableLifecycle";
import { InstanceComparisonSummary, InstanceJournalList, VariableLifecyclePanel, VariableWriteHistoryTable } from "../VariableLifecycleViews";

afterEach(clearApiCapabilityCache);

describe("variable lifecycle client", () => {
  it("lists writes through the advertised relation with filters and the cursor", async () => {
    const runtime = context(async url => url === "/capabilities" ? capabilities : { items: [], next: null, concurrentOverwriteTotal: 0 });

    await listVariableWrites(runtime, "run 1", { variableKey: "count", outcome: "lostUpdateWarning", overwrites: true }, { cursor: "c/1", take: 50 });

    expect(runtime.http.getJson).toHaveBeenNthCalledWith(2,
      "/runtime/workflows/instances/run%201/variable-writes?variableKey=count&outcome=lostUpdateWarning&overwrites=true&cursor=c%2F1&take=50");
  });

  it("reads every page in order, continuing from each cursor", async () => {
    const pages: Record<string, { items: string[]; next: string | null }> = {
      first: { items: ["a", "b"], next: "p2" },
      p2: { items: ["c"], next: "p3" },
      p3: { items: ["d"], next: null }
    };

    const all = await readAllPages(async cursor => pages[cursor ?? "first"]);

    expect(all).toEqual({ items: ["a", "b", "c", "d"], complete: true });
    await expect(readAllPages(async () => ({ items: [], next: "same" }))).rejects.toThrow("repeating page cursor");
  });

  it("pages the journal and rejects an invalid page", async () => {
    const runtime = context(async url => url === "/capabilities" ? capabilities : url.includes("cursor") ? { items: [] } : { nope: true });

    await expect(listInstanceJournal(runtime, "run-1")).rejects.toThrow("invalid journal page");
    await expect(listInstanceJournal(runtime, "run-1", { cursor: "next" })).resolves.toEqual({ items: [] });
  });

  it("reads a write's value only for the entry and value it asked for", async () => {
    const runtime = context(async url => url === "/capabilities" ? capabilities : { entryId: "other", value: "written", captureMode: "Payload", payload: 1 });

    await expect(getVariableWritePayload(runtime, "run-1", { entryId: "entry-1", value: "written" })).rejects.toThrow("different write");
    expect(runtime.http.getJson).toHaveBeenLastCalledWith("/runtime/workflows/instances/run-1/variable-writes/entry-1/payload?value=written");
  });

  it("returns the refusal of a pair of different versions instead of failing", async () => {
    const runtime = context(async url => {
      if (url === "/capabilities") return capabilities;
      throw Object.assign(new Error("Conflict"), { status: 409, payload: { status: 409, code: "differentExecutable", detail: "different versions" } });
    });

    await expect(compareInstances(runtime, "a", "b")).resolves.toMatchObject({ comparable: false, refusalReason: "differentExecutable" });
  });

  it("rethrows a failure that is not a refused comparison", async () => {
    const runtime = context(async url => {
      if (url === "/capabilities") return capabilities;
      throw Object.assign(new Error("Not found"), { status: 404, payload: null });
    });

    await expect(compareInstances(runtime, "a", "missing")).rejects.toThrow("Not found");
  });

  it("knows an older host without the relations does not support the reads", async () => {
    const runtime = context(async () => ({ capabilities: [{ id: "elsa.api.runtime", contractVersion: "1", links: [] }] }));

    await expect(supportsVariableLifecycleInspection(runtime)).resolves.toBe(false);
  });
});

describe("variable lifecycle views", () => {
  it("marks the current value, a lost update and a concurrent overwrite, and never shows a value inline", () => {
    const markup = renderToStaticMarkup(<VariableWriteHistoryTable entries={[
      entry({ entryId: "1", commitSequence: 3, newVersion: 1 }),
      entry({
        entryId: "2", commitSequence: 4, currentVersion: 1, newVersion: 2, outcome: "lostUpdateWarning", isCurrentValue: true,
        writer: { source: "capture", executableNodeId: "node-right", branchId: "right" },
        concurrentOverwrite: { overwrittenEntryId: "1", overwrittenWriter: { source: "capture", executableNodeId: "node-left", branchId: "left" }, isLostUpdate: true },
        overwrittenValue: { state: "captured", isOperational: true, payloadRef: { ownerKind: "activityExecution", ownerId: "r", entryId: "2", value: "overwritten" } }
      })
    ]} onShowValue={() => {}} />);

    expect(markup).toContain("Current value");
    expect(markup).toContain("Lost update over node-left · branch left");
    expect(markup).toContain("node-right · branch right");
    expect(markup).toContain("Show value");
    expect(markup).toContain("Overwritten");
  });

  it("shows omitted and redacted values as states, distinct from a value", () => {
    const markup = renderToStaticMarkup(<VariableWriteHistoryTable entries={[
      entry({ entryId: "1", value: { state: "omitted", isOperational: false } }),
      entry({ entryId: "2", value: { state: "redacted", digest: "sha256:abcdef0123", isOperational: false } })
    ]} />);

    expect(markup).toContain("Not captured");
    expect(markup).toContain("Redacted");
    expect(markup).not.toContain("Show value");
    expect(markup).not.toContain(">null<");
    expect(markup).toContain("abcdef01");
  });

  it("shows a refused Fault write as refused and not applied, and an approximate legacy record", () => {
    const markup = renderToStaticMarkup(<VariableWriteHistoryTable entries={[
      entry({ entryId: "legacy", commitSequence: null, orderApproximate: true }),
      entry({ entryId: "refused", outcome: "lostUpdateFault", isApplied: false, refusedValue: { state: "captured", isOperational: true }, value: { state: "captured", isOperational: true } })
    ]} />);

    expect(markup).toContain("approximate");
    expect(markup).toContain("Refused (Fault)");
    expect(markup).toContain("not applied");
  });

  it("names the writer a refused lost update met", () => {
    const markup = renderToStaticMarkup(<VariableWriteHistoryTable entries={[
      entry({ entryId: "refused", outcome: "lostUpdateFault", isApplied: false, otherWriter: { source: "capture", executableNodeId: "node-left", branchId: "left" } })
    ]} />);

    expect(markup).toContain("Met a version of node-left · branch left");
  });

  it("names its views tablist and links each tab to its panel with one tab in the tab order", () => {
    const markup = renderToStaticMarkup(<VariableLifecyclePanel context={context(async () => capabilities)} workflowExecutionId="run-1" />);

    expect(markup).toContain('role="tablist" aria-label="Variable lifecycle views"');
    expect(markup.match(/role="tab"/g)).toHaveLength(3);
    expect(markup.match(/tabindex="0"/g)).toHaveLength(1);
    expect(markup).toMatch(/aria-controls="[^"]+-history-panel"/);
    expect(markup).toMatch(/role="tabpanel" id="[^"]+-history-panel" aria-labelledby="[^"]+-history-tab"/);
  });

  it("says when an instance has no recorded history", () => {
    expect(renderToStaticMarkup(<VariableWriteHistoryTable entries={[]} />)).toContain("No variable writes recorded.");
    expect(renderToStaticMarkup(<InstanceJournalList commits={[]} />)).toContain("No commits recorded.");
  });

  it("lists journal commits in order with transitions, input refreshes and writes", () => {
    const commits: JournalCommit[] = [
      { commitSequence: 2 ** 32 + 1, orderApproximate: false, transitions: [{ activityExecutionId: "a", executableNodeId: "node-a", runTimePath: "root[0]/node-a[0]", kind: "started", branchId: "left" }], inputRefreshes: [], writes: [] },
      { commitSequence: 2 ** 32 + 2, orderApproximate: false, transitions: [], inputRefreshes: [{ activityExecutionId: "bg", supersededEvaluation: 1, replacedByEvaluation: 2, reason: "count v0 → v1", changedVariables: ["count"] }], writes: [entry({ entryId: "w" })] }
    ];

    const markup = renderToStaticMarkup(<InstanceJournalList commits={commits} />);

    expect(markup.indexOf("Commit 1.1")).toBeLessThan(markup.indexOf("Commit 1.2"));
    expect(markup).toContain("branch left");
    expect(markup).toContain("evaluation 1 → 2: count v0 → v1");
    expect(markup).toContain("by node-a");
  });

  it("shows the first divergence and differing variables, and a refusal for different versions", () => {
    const compared = renderToStaticMarkup(<InstanceComparisonSummary comparison={{
      leftWorkflowExecutionId: "a", rightWorkflowExecutionId: "b", executable: "x", comparable: true, partial: true,
      firstDivergence: { commitIndex: 2, left: { commitSequence: 3, orderApproximate: false, transitions: [], inputRefreshes: [], writes: [] }, right: null },
      variables: [{ framePath: "scope:root", variableKey: "count", finalDigestDiffers: true, writtenDigestsDiffer: true, leftWriteCount: 2, rightWriteCount: 2 }]
    }} />);
    const refused = renderToStaticMarkup(<InstanceComparisonSummary comparison={{
      leftWorkflowExecutionId: "a", rightWorkflowExecutionId: "b", executable: "x", comparable: false,
      refusalReason: "differentExecutable", variables: [], partial: false
    }} />);

    expect(compared).toContain("First difference at commit #3");
    expect(compared).toContain("still running");
    expect(compared).toContain("count");
    expect(compared).toContain("differs");
    expect(refused).toContain("they run different versions");
  });

  it("says a comparison is unknown when a value kept no digest", () => {
    const markup = renderToStaticMarkup(<InstanceComparisonSummary comparison={{
      leftWorkflowExecutionId: "a", rightWorkflowExecutionId: "b", executable: "x", comparable: true, partial: false, firstDivergence: null,
      variables: [{ framePath: "scope:root", variableKey: "secret", finalDigestDiffers: false, writtenDigestsDiffer: false, leftWriteCount: 1, rightWriteCount: 1, digestUnknown: true }]
    }} />);

    expect(markup).toContain("unknown");
    expect(markup).toContain("secret");
  });
});

function entry(overrides: Partial<VariableWriteEntry>): VariableWriteEntry {
  return {
    entryId: "entry",
    scopeId: "scope-root",
    frameId: "frame-root",
    variableKey: "count",
    writer: { source: "capture", executableNodeId: "node-a" },
    source: "capture",
    outcome: "applied",
    baseVersion: 0,
    currentVersion: 0,
    newVersion: 1,
    commitSequence: 3,
    frameRevision: 1,
    orderApproximate: false,
    isApplied: true,
    isCurrentValue: false,
    concurrentOverwrite: null,
    value: { state: "omitted", isOperational: false },
    overwrittenValue: null,
    refusedValue: null,
    resolvesEntryId: null,
    occurredAt: "2026-11-18T12:00:00Z",
    ...overrides
  };
}

const capabilities = {
  capabilities: [{
    id: "elsa.api.runtime",
    contractVersion: "1",
    links: [
      { rel: "workflow-instance-variables", href: "runtime/workflows/instances/{workflowExecutionId}/variables", templated: true },
      { rel: "workflow-instance-variable-writes", href: "runtime/workflows/instances/{workflowExecutionId}/variable-writes", templated: true },
      { rel: "workflow-instance-variable-write-payload", href: "runtime/workflows/instances/{workflowExecutionId}/variable-writes/{entryId}/payload", templated: true },
      { rel: "workflow-instance-journal", href: "runtime/workflows/instances/{workflowExecutionId}/journal", templated: true },
      { rel: "workflow-instance-compare", href: "runtime/workflows/instances/{workflowExecutionId}/compare/{otherWorkflowExecutionId}", templated: true }
    ]
  }]
};

function context(getJson: (url: string) => Promise<unknown>) {
  return {
    baseUrl: `test://variable-lifecycle-${Math.random()}`,
    http: { getJson: vi.fn(getJson) }
  } as unknown as StudioEndpointContext;
}
