import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { EtlPartitionTable, EtlRunInspection } from "../EtlRunInspection";
import type { EtlRunDetailView } from "../etlTypes";

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
    expect(container.textContent).toContain("cancellation does not undo file writes");
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
