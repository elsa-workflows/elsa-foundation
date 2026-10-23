import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { EtlConnectorPartitionTable, EtlPartitionTable, EtlRunInspection } from "../EtlRunInspection";
import type { EtlRunConnectorPartitionView, EtlRunDetailView } from "../etlTypes";

describe("ETL run inspection", () => {
  let container: HTMLDivElement;
  let root: Root;
  beforeEach(() => {
    container = document.createElement("div");
    document.body.append(container);
    root = createRoot(container);
  });
  afterEach(() => {
    flushSync(() => root.unmount());
    container.remove();
  });

  it("shows retained input fingerprints and partial accepted work without promising rollback", () => {
    const run = fixture();
    flushSync(() => root.render(<EtlRunInspection run={run} />));
    expect(container.textContent).toContain("Output may be partial");
    expect(container.textContent).toContain("cancellation does not undo output already written or accepted");
    expect(container.textContent).toContain(run.selection);
    expect(container.querySelector('[aria-label="Source input"]')?.textContent).toContain("source-v7");
    expect(container.querySelector('[aria-label="Source input"]')?.textContent).toContain("a".repeat(64));
    expect(container.querySelector('[aria-label="Lookup input"]')?.textContent).toContain("lookup-v3");
    expect(container.querySelector('[aria-label="Lookup input"]')?.textContent).toContain("b".repeat(64));
    expect(container.textContent).toContain("does not recheck the files");
    expect(container.textContent).toContain("Record ordinal: 0");
  });

  it("distinguishes preparation and absent identity from an empty successful run", () => {
    flushSync(() => root.render(<EtlRunInspection run={{ ...fixture(), status: "preparing", sourceRecordCount: null,
      sourceInput: null, lookupInput: null, bindingAliases: null, diagnostic: null, terminalResult: null }} />));
    expect(container.textContent).toContain("Preparing input");
    expect(container.textContent).toContain("Not yet prepared");
    expect(container.textContent).toContain("No binding admitted");
    expect(container.textContent).not.toContain("Succeeded");
    expect(container.textContent).not.toContain("Settled at");
  });

  it("reports an expired retained claim with its owed work, without claiming worker termination", () => {
    flushSync(() => root.render(<EtlPartitionTable now={Date.parse("2026-10-20T12:00:00Z")} partitions={[{
      partitionOrdinal: 1, startOrdinal: 10, endOrdinalExclusive: 20, nextOrdinal: 12,
      isClaimed: true, claimFence: 3, claimExpiresAt: "2026-10-20T11:00:00Z",
      owedBatch: { startOrdinal: 12, endOrdinalExclusive: 14, inputBytes: 128 }
    }]} />));
    expect(container.textContent).toContain("Recorded claim expired");
    expect(container.textContent).toContain("12–14 (end exclusive), 128 bytes");
    expect(container.textContent).toContain("does not prove a worker stopped or released memory");
    expect(container.querySelector('[role="region"]')?.getAttribute("tabindex")).toBe("0");
    expect(container.textContent).not.toContain("Complete");
  });

  it("describes captured HTTP source input without calling it output", () => {
    flushSync(() => root.render(<EtlConnectorPartitionTable partitions={[{
      partitionOrdinal: 0, file: null,
      http: { pageSequence: 0, nextItem: 0, pageRecordCount: 2, exhausted: false,
        fetch: { phase: "captured", attemptCount: 1, deadline: "2026-10-20T12:00:00Z", notBefore: null, lastStatusCode: 200 } },
      isClaimed: false, claimFence: null, claimExpiresAt: null, notBefore: null,
      owedBatch: null, acceptedMainEffects: 0, acceptedErrorEffects: 0
    }]} />));
    expect(container.textContent).toContain("Fetch state");
    expect(container.textContent).toContain("Retry phase: captured");
    expect(container.textContent).not.toContain("output captured");
  });

  it("shows HTTP total as unknown, retry state, partial acknowledgement, and settled route-body counts", () => {
    const run = {
      ...fixture(),
      status: "running" as const,
      sourceRecordCount: null,
      sourceInput: null,
      bindingAliases: { source: "orders-http", lookup: null, output: "accepted", errors: "rejected" },
      lookupInput: null,
      connector: { sourceKind: "http", mainKind: "http", errorKind: "jsonl", acceptedMainEffects: 0, acceptedErrorEffects: 1 }
    };
    flushSync(() => root.render(<EtlRunInspection run={run} />));
    expect(container.textContent).toContain("Unknown for an HTTP source");
    expect(container.querySelector('[aria-label="Source input"]')?.textContent).toContain("Source progress is tracked by page.");
    expect(container.querySelector('[aria-label="Source input"]')?.textContent).not.toContain("Retained input identity is unavailable.");
    expect(container.textContent).toContain("No lookup configured.");
    expect(container.textContent).toContain("Main route bodies accepted0");
    expect(container.textContent).toContain("Error route bodies accepted1");
    expect(container.textContent).toContain("not records, attempts, or physical executions");
  });

  it("renders file and HTTP connector positions and keeps acknowledged output visibly pending", () => {
    const retry = {
      phase: "waiting", attemptCount: 3, deadline: "2026-10-20T12:00:00Z",
      notBefore: "2026-10-20T11:00:00Z", lastStatusCode: 503
    };
    const partitions: EtlRunConnectorPartitionView[] = [
      {
        partitionOrdinal: 0,
        file: { startOrdinal: 10, endOrdinalExclusive: 20, nextOrdinal: 14 },
        http: null,
        isClaimed: false,
        claimFence: null,
        claimExpiresAt: null,
        notBefore: null,
        owedBatch: null,
        acceptedMainEffects: 0,
        acceptedErrorEffects: 0
      },
      {
        partitionOrdinal: 1,
        file: null,
        http: {
          pageSequence: 4, nextItem: 2, pageRecordCount: 3, exhausted: false,
          fetch: { phase: "inFlight", attemptCount: 2, deadline: "2026-10-20T12:00:00Z", notBefore: null, lastStatusCode: 429 }
        },
        isClaimed: true,
        claimFence: 7,
        claimExpiresAt: "2026-10-20T13:00:00Z",
        notBefore: "2026-10-20T11:00:00Z",
        owedBatch: {
          recordCount: 2, inputBytes: 128, outputRetained: true,
          main: { kind: "http", recordCount: 2, state: "acknowledged", retry },
          errors: { kind: "jsonl", recordCount: 0, state: "published", retry: null }
        },
        acceptedMainEffects: 0,
        acceptedErrorEffects: 1
      },
      {
        partitionOrdinal: 2,
        file: null,
        http: { pageSequence: 5, nextItem: 0, pageRecordCount: null, exhausted: true, fetch: null },
        isClaimed: false,
        claimFence: null,
        claimExpiresAt: null,
        notBefore: null,
        owedBatch: null,
        acceptedMainEffects: 2,
        acceptedErrorEffects: 0
      }
    ];
    flushSync(() => root.render(<EtlConnectorPartitionTable partitions={partitions} now={Date.parse("2026-10-20T10:00:00Z")} />));
    expect(container.textContent).toContain("File range 10–20 (next 14)");
    expect(container.textContent).toContain("HTTP page 4, next item 2 of 3; not yet exhausted");
    expect(container.textContent).toContain("HTTP page 5, next item 0 of unknown count; source exhausted");
    expect(container.textContent).toContain("Retry phase: in progress; attempt 2");
    expect(container.textContent).toContain("Retry phase: waiting; attempt 3");
    expect(container.textContent).toContain("Last HTTP status: 429");
    expect(container.textContent).toContain("Last HTTP status: 503");
    expect(container.textContent).toContain("2 records, 128 input bytes; output retained.");
    expect(container.textContent).toContain("acknowledged; awaiting batch acceptance");
    expect(container.textContent).toContain("published; awaiting batch acceptance");
    expect(container.textContent).toContain("Main: 0; errors: 1");
    expect(container.textContent).toContain("Acknowledged or published output may still be waiting for batch acceptance.");
  });
});

function fixture(): EtlRunDetailView {
  return {
    runId: "run-1", selection: `etl1:orders:1:${"c".repeat(64)}`, status: "cancelled",
    sourceRecordCount: 20, acceptedRecords: 5, rejectedRecords: 1, filteredRecords: 2,
    outstandingRecords: 2, outstandingBytes: 128,
    bindingAliases: { source: "orders", lookup: "products", output: "accepted", errors: "rejected" },
    sourceInput: { bindingRevision: "source-v7", expectedSha256: "a".repeat(64), snapshotSha256: "a".repeat(64), snapshotByteLength: 512 },
    lookupInput: { bindingRevision: "lookup-v3", expectedSha256: "b".repeat(64), snapshotSha256: "b".repeat(64), snapshotByteLength: 128 },
    diagnostic: { code: "etl.fixture-diagnostic", recordOrdinal: 0, field: "id" },
    terminalResult: { outcome: "cancelled", acceptedRecords: 5, rejectedRecords: 1, filteredRecords: 2,
      diagnostic: null, settledAt: "2026-10-20T12:00:00Z" }
  };
}
