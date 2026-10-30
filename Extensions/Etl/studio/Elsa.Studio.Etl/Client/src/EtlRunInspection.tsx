import { CopyableValue } from "@elsa-workflows/studio-ui";
import React from "react";
import type {
  EtlRunConnectorPartitionView,
  EtlRunDetailView,
  EtlRunEffectView,
  EtlRunInputIdentityView,
  EtlRunPartitionView,
  EtlRunRetryView
} from "./etlTypes";

const statusLabels: Record<string, string> = {
  preparing: "Preparing input", running: "Processing", succeeded: "Succeeded",
  businessError: "Data rejected", faulted: "Faulted", cancelled: "Cancelled"
};

export function etlRunStatusLabel(status: string): string {
  return statusLabels[status] ?? status;
}

export function isConnectorRun(run: EtlRunDetailView): boolean {
  return run.formatVersion === 2 || run.connector != null;
}

export function EtlRunInspection({ run }: { run: EtlRunDetailView }) {
  const partial = run.status === "faulted" || run.status === "cancelled";
  return <section className="etl-run-inspection" aria-label="Run details">
    <h2>Run <CopyableValue label="ETL run ID" value={run.runId} /></h2>
    <p role="status">{etlRunStatusLabel(run.status)}</p>
    {partial && <p>Output may be partial. The counts below describe accepted work; cancellation does not undo output already written or accepted.</p>}
    <dl className="etl-facts">
      <dt>Published pipeline</dt><dd><code><CopyableValue label="Published pipeline" value={run.selection} /></code></dd>
      <dt>Source records</dt><dd>{sourceRecordCountLabel(run)}</dd>
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
      <p><code><CopyableValue label="Diagnostic code" value={run.diagnostic.code} /></code></p>
      {run.diagnostic.recordOrdinal !== null && <p>Record ordinal: {run.diagnostic.recordOrdinal}</p>}
      {run.diagnostic.field !== null && <p>Field: {run.diagnostic.field}</p>}
    </section>}
    <InputIdentity title="Source input" alias={run.bindingAliases?.source} identity={run.sourceInput}
      emptyMessage={run.connector?.sourceKind === "http" ? "Source progress is tracked by page." : undefined} />
    <InputIdentity title="Lookup input" alias={run.bindingAliases?.lookup} identity={run.lookupInput}
      emptyMessage={run.connector && run.bindingAliases?.lookup === null ? "No lookup configured." : undefined} />
    {run.connector ? <section aria-label="Connector output progress">
      <h3>Connector output progress</h3>
      <dl className="etl-facts">
        <dt>Source type</dt><dd>{kindLabel(run.connector.sourceKind)}</dd>
        <dt>Main output type</dt><dd>{kindLabel(run.connector.mainKind)}</dd>
        <dt>Error output type</dt><dd>{kindLabel(run.connector.errorKind)}</dd>
        <dt>Main route bodies accepted</dt><dd>{run.connector.acceptedMainEffects}</dd>
        <dt>Error route bodies accepted</dt><dd>{run.connector.acceptedErrorEffects}</dd>
      </dl>
      <p>These counts describe settled output bodies, not records, attempts, or physical executions.</p>
    </section> : null}
    {run.terminalResult && <p>Settled at <time dateTime={run.terminalResult.settledAt}>{run.terminalResult.settledAt}</time>.</p>}
  </section>;
}

function InputIdentity({ title, alias, identity, emptyMessage }: {
  title: string; alias?: string | null; identity?: EtlRunInputIdentityView | null; emptyMessage?: string;
}) {
  return <section aria-label={title}>
    <h3>{title}</h3>
    <p>{alias ?? emptyMessage ?? "No binding admitted"}</p>
    {identity ? <>
      <dl className="etl-facts">
        <dt>Binding revision</dt><dd>{identity.bindingRevision}</dd>
        <dt>Expected SHA-256</dt><dd><code><CopyableValue label="Expected SHA-256" value={identity.expectedSha256} placeholder="Not available" /></code></dd>
        <dt>Prepared snapshot SHA-256</dt><dd><code><CopyableValue label="Prepared snapshot SHA-256" value={identity.snapshotSha256} placeholder="Not yet prepared" /></code></dd>
        <dt>Prepared snapshot bytes</dt><dd>{identity.snapshotByteLength ?? "Not yet prepared"}</dd>
      </dl>
      <p>Snapshot metadata describes the last verified preparation. This view does not recheck the files.</p>
    </> : emptyMessage ? alias != null ? <p>{emptyMessage}</p> : null : <p>Retained input identity is unavailable.</p>}
  </section>;
}

function sourceRecordCountLabel(run: EtlRunDetailView) {
  if (run.sourceRecordCount !== null) return run.sourceRecordCount;
  if (run.status === "preparing") return "Not yet prepared";
  if (run.connector && isHttpKind(run.connector.sourceKind)) return "Unknown for an HTTP source";
  return "Not reported";
}

function isHttpKind(kind: string) {
  return kind === "http";
}

function kindLabel(kind: string) {
  return ({ http: "HTTP JSON", csv: "CSV", jsonl: "JSON Lines" } as Record<string, string>)[kind] ?? kind;
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

export function EtlConnectorPartitionTable({ partitions, now = Date.now() }: {
  partitions: EtlRunConnectorPartitionView[]; now?: number;
}) {
  if (!partitions.length) return <p role="status">No connector partitions on this page.</p>;
  return <section aria-label="Connector partition progress">
    <div className="etl-table-scroll" tabIndex={0} role="region" aria-label="Connector partition progress table">
      <table>
        <caption>Connector partition progress</caption>
        <thead><tr><th scope="col">Partition</th><th scope="col">Source position</th><th scope="col">Claim</th>
          <th scope="col">Fetch state</th><th scope="col">Owed output</th><th scope="col">Settled route bodies</th></tr></thead>
        <tbody>{partitions.map(partition => {
          const expiry = partition.claimExpiresAt === null ? NaN : Date.parse(partition.claimExpiresAt);
          const claim = !partition.isClaimed ? "Unclaimed"
            : Number.isFinite(expiry) && expiry <= now ? "Recorded claim expired" : "Recorded claim";
          const hasFile = partition.file !== null;
          const hasHttp = partition.http !== null;
          const validSourcePosition = hasFile !== hasHttp;
          const sourcePosition = !validSourcePosition ? "Progress details unavailable"
            : partition.file ? `File range ${partition.file.startOrdinal}–${partition.file.endOrdinalExclusive} (next ${partition.file.nextOrdinal})`
              : `HTTP page ${partition.http!.pageSequence}, next item ${partition.http!.nextItem} of ${partition.http!.pageRecordCount ?? "unknown count"}; ${partition.http!.exhausted ? "source exhausted" : "not yet exhausted"}`;
          return <tr key={partition.partitionOrdinal}>
            <th scope="row">{partition.partitionOrdinal}</th>
            <td>{sourcePosition}{partition.notBefore ? <><br />Eligible after <time dateTime={partition.notBefore}>{partition.notBefore}</time></> : null}</td>
            <td>{claim}{partition.claimExpiresAt ? <><br /><time dateTime={partition.claimExpiresAt}>{partition.claimExpiresAt}</time></> : null}</td>
            <td>{partition.http?.fetch ? <RetryProgress retry={partition.http.fetch} /> : "No fetch retry scheduled"}</td>
            <td>{partition.owedBatch ? <OwedOutput owed={partition.owedBatch} /> : "None"}</td>
            <td>Main: {partition.acceptedMainEffects}; errors: {partition.acceptedErrorEffects}</td>
          </tr>;
        })}</tbody>
      </table>
    </div>
    <p>Route body counts describe settled outputs, not records, attempts, or physical executions. Acknowledged or published output may still be waiting for batch acceptance.</p>
    <p>Claims and outstanding budgets describe durable state. An expired claim does not prove a worker stopped or released memory.</p>
  </section>;
}

function OwedOutput({ owed }: { owed: NonNullable<EtlRunConnectorPartitionView["owedBatch"]> }) {
  return <>
    <p>{owed.recordCount} records, {owed.inputBytes} input bytes; output {owed.outputRetained ? "retained" : "not retained"}.</p>
    {owed.main ? <EffectProgress label="Main output" effect={owed.main} /> : <p>Main output: none owed.</p>}
    {owed.errors ? <EffectProgress label="Error output" effect={owed.errors} /> : <p>Error output: none owed.</p>}
  </>;
}

function EffectProgress({ label, effect }: { label: string; effect: EtlRunEffectView }) {
  return <div>
    <strong>{label} ({kindLabel(effect.kind)})</strong>
    <p>{effect.recordCount} records; {effectStateLabel(effect.state)}.</p>
    {effect.retry ? <RetryProgress retry={effect.retry} /> : null}
  </div>;
}

function effectStateLabel(state: string) {
  switch (state.toLowerCase()) {
    case "acknowledged": return "acknowledged; awaiting batch acceptance";
    case "published": return "published; awaiting batch acceptance";
    case "retained": return "retained; delivery pending";
    case "pending": return "pending";
    default: return `${state}; completion is not confirmed`;
  }
}

function RetryProgress({ retry }: { retry: EtlRunRetryView }) {
  return <div>
    <p>Retry phase: {retryPhaseLabel(retry.phase)}; attempt {retry.attemptCount}.</p>
    <p>Deadline: <time dateTime={retry.deadline}>{retry.deadline}</time>.</p>
    <p>{retry.notBefore
      ? <>Next attempt after <time dateTime={retry.notBefore}>{retry.notBefore}</time>.</>
      : "Next attempt time not scheduled."}</p>
    {retry.lastStatusCode !== null ? <p>Last HTTP status: {retry.lastStatusCode}.</p> : null}
  </div>;
}

function retryPhaseLabel(phase: string) {
  switch (phase.toLowerCase()) {
    case "inflight": return "in progress";
    case "waiting": return "waiting";
    case "failed": return "failed";
    case "captured": return "captured";
    case "acknowledged": return "acknowledged";
    default: return phase;
  }
}
