import type { ActivityExecutionObservation } from "../workflowTypes";
import { RuntimeObservationWarning } from "./RuntimeObservationWarning";

export function ActivityObservationsPanel({ activityType, observations = [] }: {
  activityType: string;
  observations?: ActivityExecutionObservation[];
}) {
  const delivery = observations.filter(fact => fact.kind === "http.response.delivery");
  const warnings = observations.filter(fact => fact.severity.toLowerCase() === "warning");
  const isHttpResponse = /(?:^|[.:])WriteHttpResponse(?:,|$)/i.test(activityType);
  if (!isHttpResponse && !warnings.length) return null;
  const written = delivery.some(fact => fact.code === "http.response.written");
  const undelivered = delivery.some(fact => fact.code === "http.response.undelivered");
  return <section className="wf-instance-section wf-activity-observations" aria-label={isHttpResponse ? "HTTP response delivery" : "Activity warnings"}>
    <h4>{isHttpResponse ? "HTTP response delivery" : "Warnings"}</h4>
    <RuntimeObservationWarning messages={warnings.map(fact => fact.message)} />
    {isHttpResponse ? <p><strong>{written ? "Written to HTTP response" : undelivered ? "Not sent to originating request" : "Unknown"}</strong></p> : null}
    {isHttpResponse && !delivery.length ? <p className="wf-muted">No retained delivery evidence is available for this invocation. Activity completion records response preparation, not delivery.</p> : null}
    {observations.filter(fact => fact.kind === "http.response.delivery" || fact.severity.toLowerCase() === "warning")
      .map(fact => <p key={fact.observationId} data-severity={fact.severity.toLowerCase()}>{fact.message}</p>)}
  </section>;
}
