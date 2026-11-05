import React from "react";
import type { StudioDashboardWidgetBodyProps } from "@elsa-workflows/studio-sdk";
import { qualifyStudioBackendPath } from "@elsa-workflows/studio-sdk";
import type { FailingDefinition, RunBucket, RunHealthSettings, WorkflowRunHealthSnapshot } from "./workflowDashboardApi";
import { WorkflowDashboardMetric } from "./WorkflowDashboardMetric";
import { WorkflowDashboardUnavailable } from "./WorkflowDashboardUnavailable";

export function WorkflowRunHealthWidget({ snapshot }: StudioDashboardWidgetBodyProps<WorkflowRunHealthSnapshot, RunHealthSettings>) {
  if (!snapshot) return null;

  if (snapshot.status === "unavailable") {
    return <WorkflowDashboardUnavailable title="Run statistics are unavailable." errorCode={snapshot.errorCode} />;
  }

  const hasStarts = snapshot.startedCount > 0 || snapshot.buckets.some(bucket => bucket.startedCount > 0);
  const startDetail = hasStarts ? "In this start window" : "No starts in the selected window";
  const failureDetail = hasStarts
    ? `${formatPercentage(snapshot.failurePercentage)} of starts`
    : "No starts in the selected window";
  const incidentBearingDetail = hasStarts
    ? `${formatPercentage(snapshot.incidentBearingPercentage)} of starts`
    : "No starts in the selected window";

  return (
    <div className="workflow-run-health">
      <dl className="workflow-dashboard-metrics" aria-label="Workflow run metrics">
        <WorkflowDashboardMetric label="Started" value={snapshot.startedCount} detail={startDetail} tone="accent" />
        {typeof snapshot.succeededCount === "number" && (
          <WorkflowDashboardMetric label="Succeeded" value={snapshot.succeededCount} detail="Completed successfully" tone="success" />
        )}
        <WorkflowDashboardMetric label="Failed" value={snapshot.failedCount} detail={failureDetail} tone="danger" />
        <WorkflowDashboardMetric label="Runs with incidents" value={snapshot.incidentBearingRunCount} detail={incidentBearingDetail} tone="warning" />
        <WorkflowDashboardMetric label="Incidents" value={snapshot.incidentCount} detail="Historical records" tone="warning" />
        <WorkflowDashboardMetric label="Running now" value={snapshot.runningCount} detail="Across all start times" tone="accent" />
      </dl>

      <p className="workflow-run-health__scope">
        Start window: <time dateTime={snapshot.from}>{formatFullTime(snapshot.from, snapshot.timeZone)}</time>
        {" – "}
        <time dateTime={snapshot.to}>{formatFullTime(snapshot.to, snapshot.timeZone)}</time>
        {` · ${snapshot.bucket} buckets · ${snapshot.timeZone} · `}
        {snapshot.includeTestRuns ? "Test runs included" : "Test runs excluded"}
      </p>

      <div className="workflow-run-health__analysis">
        <section className="workflow-run-health__panel" aria-labelledby="workflow-run-health-trend-title">
          <div className="workflow-run-health__panel-heading">
            <div>
              <h3 id="workflow-run-health-trend-title">Workflow activity</h3>
              <p>Runs started per {snapshot.bucket}. Red marks those that have failed.</p>
            </div>
            <ul className="workflow-run-health__legend" aria-label="Chart legend">
              <li><span className="workflow-run-health__legend-swatch workflow-run-health__legend-swatch--starts" aria-hidden="true" />Starts</li>
              <li><span className="workflow-run-health__legend-swatch workflow-run-health__legend-swatch--failed" aria-hidden="true" />Failed share</li>
            </ul>
          </div>

          {hasStarts && snapshot.buckets.length > 0 ? (
            <RunBucketChart
              buckets={snapshot.buckets}
              timeZone={snapshot.timeZone}
              granularity={snapshot.bucket}
              totalStarted={snapshot.startedCount}
            />
          ) : (
            <p className="workflow-run-health__empty" role="status">
              {hasStarts
                ? "Activity totals are available, but time-bucket details are unavailable."
                : "No workflow starts in this window. “Running now” is a separate current-state count across all start times."}
            </p>
          )}

          <BucketDetails buckets={snapshot.buckets} timeZone={snapshot.timeZone} />
        </section>

        <section className="workflow-run-health__panel" aria-labelledby="workflow-run-health-failures-title">
          <h3 id="workflow-run-health-failures-title">Most failures</h3>
          <p>Counts are for this start window. Links open all matching failed runs.</p>
          {snapshot.highestFailureDefinitions.length > 0 ? (
            <FailingDefinitionList definitions={snapshot.highestFailureDefinitions} includeTestRuns={snapshot.includeTestRuns} />
          ) : snapshot.failedCount > 0 ? (
            <p className="workflow-run-health__empty">Failed runs were recorded, but a definition ranking is unavailable.</p>
          ) : (
            <p className="workflow-run-health__empty">No failed runs in this window.</p>
          )}
        </section>
      </div>
      <p className="workflow-dashboard-note" role="note">
        Incident counts include historical incidents attached to runs started in this window. “Running now” is current state and is not limited to the selected start window.
      </p>
    </div>
  );
}

function RunBucketChart({ buckets, timeZone, granularity, totalStarted }: { buckets: RunBucket[]; timeZone: string; granularity: "hour" | "day"; totalStarted: number }) {
  const maxStarted = Math.max(1, ...buckets.map(bucket => bucket.startedCount));
  const tickIndices = sparseTickIndices(buckets.length);

  return (
    <figure className="workflow-run-health__figure">
      <div
        className="workflow-run-health__chart"
        role="img"
        aria-label={`Run starts by ${granularity}: ${totalStarted.toLocaleString()} starts in the selected window. Red marks failed share within each bucket.`}
      >
        <ol className="workflow-run-health__bars">
          {buckets.map(bucket => {
            const startHeight = bucket.startedCount > 0 ? bucket.startedCount / maxStarted * 100 : 0;
            const failedShare = bucket.startedCount > 0 ? Math.min(100, bucket.failedCount / bucket.startedCount * 100) : 0;
            return (
              <li key={bucket.from} className="workflow-run-health__bar-slot">
                <span
                  className="workflow-run-health__bar"
                  style={{
                    "--workflow-dashboard-start-height": `${startHeight}%`,
                    "--workflow-dashboard-failed-share": `${failedShare}%`,
                  } as React.CSSProperties}
                >
                  <span className="workflow-run-health__bar-failed" />
                </span>
              </li>
            );
          })}
        </ol>
      </div>
      <div className="workflow-run-health__time-labels" aria-hidden="true">
        {tickIndices.map(index => (
          <span key={buckets[index].from} className={index === 0 ? "workflow-run-health__time-label--first" : index === buckets.length - 1 ? "workflow-run-health__time-label--last" : undefined}>
            {formatAxisTime(buckets[index].from, timeZone, granularity)}
          </span>
        ))}
      </div>
      <figcaption className="workflow-run-health__chart-caption">
        View exact counts and time intervals below.
      </figcaption>
    </figure>
  );
}

function BucketDetails({ buckets, timeZone }: { buckets: RunBucket[]; timeZone: string }) {
  if (buckets.length === 0) return null;

  return (
    <details className="workflow-run-health__bucket-details">
      <summary>View exact bucket counts</summary>
      <div className="workflow-run-health__table-scroll">
        <table>
          <caption className="workflow-dashboard-sr-only">Exact workflow run counts for every time bucket, displayed in {timeZone}.</caption>
          <thead>
            <tr>
              <th scope="col">Bucket</th>
              <th scope="col">Started</th>
              <th scope="col">Failed</th>
              <th scope="col">Incident-bearing runs</th>
            </tr>
          </thead>
          <tbody>
            {buckets.map(bucket => (
              <tr key={bucket.from}>
                <th scope="row">
                  <time dateTime={bucket.from}>{formatFullTime(bucket.from, timeZone)}</time>
                  <span aria-hidden="true"> – </span>
                  <time dateTime={bucket.to}>{formatFullTime(bucket.to, timeZone)}</time>
                </th>
                <td>{bucket.startedCount.toLocaleString()}</td>
                <td>{bucket.failedCount.toLocaleString()}</td>
                <td>{bucket.incidentBearingRunCount.toLocaleString()}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </details>
  );
}

function FailingDefinitionList({ definitions, includeTestRuns }: { definitions: FailingDefinition[]; includeTestRuns: boolean }) {
  const ranked = [...definitions].sort((a, b) => b.failedCount - a.failedCount);
  const maxFailures = Math.max(1, ...ranked.map(definition => definition.failedCount));

  return (
    <ol className="workflow-run-health__definitions">
      {ranked.map((definition, index) => {
        const label = definition.displayName?.trim() || definition.definitionId;
        const href = qualifyStudioBackendPath(failingDefinitionHref(definition.definitionId, includeTestRuns));
        const width = `${definition.failedCount > 0 ? definition.failedCount / maxFailures * 100 : 0}%`;
        return (
          <li key={definition.definitionId}>
            <a href={href} aria-label={`View all failed runs for ${label}`}>
              <span className="workflow-run-health__definition-label">
                <span className="workflow-run-health__rank" aria-hidden="true">{index + 1}</span>
                <span className="workflow-run-health__definition-name" title={label}>{label}</span>
                <strong>{definition.failedCount.toLocaleString()}<span className="workflow-dashboard-sr-only"> failed</span></strong>
              </span>
              <span className="workflow-run-health__definition-track" aria-hidden="true">
                <span style={{ width }} />
              </span>
            </a>
          </li>
        );
      })}
    </ol>
  );
}

function failingDefinitionHref(definitionId: string, includeTestRuns: boolean) {
  const query = new URLSearchParams({ definitionId, status: "Faulted" });
  if (!includeTestRuns) query.set("runKind", "PublishedRun");
  return `/workflows/instances?${query.toString()}`;
}

function formatPercentage(value: number) {
  return `${new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 }).format(value)}%`;
}

function formatFullTime(value: string, timeZone: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat(undefined, {
    timeZone,
    year: "numeric", month: "short", day: "numeric",
    hour: "2-digit", minute: "2-digit", second: "2-digit",
    timeZoneName: "shortOffset",
  }).format(date);
}

function formatAxisTime(value: string, timeZone: string, granularity: "hour" | "day") {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat(undefined, {
    timeZone,
    month: "short",
    day: "numeric",
    ...(granularity === "hour" ? { hour: "2-digit" } : {}),
  }).format(date);
}

function sparseTickIndices(count: number) {
  if (count <= 0) return [];
  return [...new Set([0, Math.floor((count - 1) / 2), count - 1])];
}
