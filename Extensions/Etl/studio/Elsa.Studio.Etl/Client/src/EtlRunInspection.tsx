import React from "react";
import type { EtlRunDetailView, EtlRunInputIdentityView, EtlRunPartitionView } from "./etlTypes";

const statusLabels: Record<string, string> = {
  preparing: "Preparing input", running: "Processing", succeeded: "Succeeded",
  businessError: "Data rejected", faulted: "Faulted", cancelled: "Cancelled"
};

export function etlRunStatusLabel(status: string): string {
  return statusLabels[status] ?? status;
}

export function EtlRunInspection({ run }: { run: EtlRunDetailView }) {
  const partial = run.status === "faulted" || run.status === "cancelled";
  return <section className="etl-run-inspection" aria-label="Run details">
    <h2>Run {run.runId}</h2>
    <p role="status">{etlRunStatusLabel(run.status)}</p>
    {partial && <p>Output may be partial. The counts below describe accepted work; cancellation does not undo file writes.</p>}
    <dl className="etl-facts">
      <dt>Published pipeline</dt><dd><code>{run.selection}</code></dd>
      <dt>Source records</dt><dd>{run.sourceRecordCount ?? "Not yet prepared"}</dd>
      <dt>Accepted records</dt><dd>{run.acceptedRecords}</dd>
      <dt>Rejected records</dt><dd>{run.rejectedRecords}</dd>
      <dt>Filtered records</dt><dd>{run.filteredRecords}</dd>
      <dt>Outstanding records</dt><dd>{run.outstandingRecords}</dd>
      <dt>Outstanding bytes</dt><dd>{run.outstandingBytes}</dd>
      <dt>Main output binding</dt><dd>{run.bindingAliases?.output ?? "Not admitted"}</dd>
      <dt>Error output binding</dt><dd>{run.bindingAliases?.errors ?? "Not admitted"}</dd>
    </dl>
    {run.diagnostic && <section aria-label="Run diagnostic">
      <h3>Diagnostic</h3>
      <p><code>{run.diagnostic.code}</code></p>
      {run.diagnostic.recordOrdinal !== null && <p>Record ordinal: {run.diagnostic.recordOrdinal}</p>}
      {run.diagnostic.field !== null && <p>Field: {run.diagnostic.field}</p>}
    </section>}
    <InputIdentity title="Source input" alias={run.bindingAliases?.source} identity={run.sourceInput} />
    <InputIdentity title="Lookup input" alias={run.bindingAliases?.lookup} identity={run.lookupInput} />
    {run.terminalResult && <p>Settled at <time dateTime={run.terminalResult.settledAt}>{run.terminalResult.settledAt}</time>.</p>}
  </section>;
}

function InputIdentity({ title, alias, identity }: {
  title: string; alias?: string; identity?: EtlRunInputIdentityView | null;
}) {
  return <section aria-label={title}>
    <h3>{title}</h3>
    <p>{alias ?? "No binding admitted"}</p>
    {identity ? <>
      <dl className="etl-facts">
        <dt>Binding revision</dt><dd>{identity.bindingRevision}</dd>
        <dt>Expected SHA-256</dt><dd><code>{identity.expectedSha256 ?? "Not available"}</code></dd>
        <dt>Prepared snapshot SHA-256</dt><dd><code>{identity.snapshotSha256 ?? "Not yet prepared"}</code></dd>
        <dt>Prepared snapshot bytes</dt><dd>{identity.snapshotByteLength ?? "Not yet prepared"}</dd>
      </dl>
      <p>Snapshot metadata describes the last verified preparation. This view does not recheck the files.</p>
    </> : <p>Retained input identity is unavailable.</p>}
  </section>;
}

export function EtlPartitionTable({ partitions, now = Date.now() }: {
  partitions: EtlRunPartitionView[]; now?: number;
}) {
  if (!partitions.length) return <p role="status">No partitions on this page. Preparation may still be pending.</p>;
  return <section aria-label="Partition progress">
    <div className="etl-table-scroll" tabIndex={0} role="region" aria-label="Partition progress table">
      <table>
        <caption>Durable partition progress</caption>
        <thead><tr><th scope="col">Partition</th><th scope="col">Range</th><th scope="col">Next record</th>
          <th scope="col">Claim</th><th scope="col">Owed batch</th></tr></thead>
        <tbody>{partitions.map(partition => {
          const expiry = partition.claimExpiresAt === null ? NaN : Date.parse(partition.claimExpiresAt);
          const claim = !partition.isClaimed ? "Unclaimed"
            : Number.isFinite(expiry) && expiry <= now ? "Recorded claim expired" : "Recorded claim";
          return <tr key={partition.partitionOrdinal}>
            <th scope="row">{partition.partitionOrdinal}</th>
            <td>{partition.startOrdinal}–{partition.endOrdinalExclusive} (end exclusive)</td>
            <td>{partition.nextOrdinal}{partition.nextOrdinal === partition.endOrdinalExclusive ? " · Complete" : ""}</td>
            <td>{claim}{partition.claimExpiresAt && <><br /><time dateTime={partition.claimExpiresAt}>{partition.claimExpiresAt}</time></>}</td>
            <td>{partition.owedBatch ? <>{partition.owedBatch.startOrdinal}–{partition.owedBatch.endOrdinalExclusive} (end exclusive), {partition.owedBatch.inputBytes} bytes</> : "None"}</td>
          </tr>;
        })}</tbody>
      </table>
    </div>
    <p>Claims and outstanding budgets describe durable state. An expired claim does not prove a worker stopped or released memory.</p>
  </section>;
}
