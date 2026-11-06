import { useState } from "react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { CopyableIdentifier, StudioAlert, StudioButton, StudioCollectionFooter, StudioPage, StudioPageHeader, StudioResultState, StudioTableContainer } from "@elsa-workflows/studio-ui";
import { StudioLink } from "../StudioLink";
import { ChangeReview } from "./ChangeReview";
import type { TriggerClient } from "./api";
import { Findings, Loading, RequestError, actionReason, allowed, deliveryPath, label, triggerPath, useTriggerLoad } from "./shared";
import type { DeliveryView, RecipientView, TriggerCommand, TriggerView } from "./types";

export function DeliveriesSection({ client, trigger }: { client: TriggerClient; trigger: TriggerView }) {
  const [after, setAfter] = useState<string>();
  const { result, reload } = useTriggerLoad(signal => client.deliveries({ triggerId: trigger.id, after }, signal), JSON.stringify([trigger.id, after]));
  return <section aria-label="Trigger deliveries"><div className="wf-trigger-section-heading"><h3>Deliveries</h3><StudioButton onClick={reload}>Refresh deliveries</StudioButton></div>
    {result.state === "loading" ? <Loading label="Loading trigger deliveries" /> : result.state === "error" ? <RequestError error={result.error} title="Delivery evidence unavailable" retry={reload} /> : !result.value.items.length ? <StudioResultState headingLevel={3} title="No delivery evidence yet" description="Accepted work, refused attempts and recipient outcomes appear here when recorded." /> : <>
      <StudioTableContainer role="region" aria-label="Trigger deliveries table" tabIndex={0}><table className="studio-table"><thead><tr><th scope="col">Delivery</th><th scope="col">Admission</th><th scope="col">Accepted / refused</th><th scope="col">Recorded</th></tr></thead><tbody>{result.value.items.map(delivery => <tr key={delivery.id}><th scope="row"><StudioLink href={deliveryPath(delivery.id)}>{delivery.sourceOccurrenceId || delivery.id}</StudioLink></th><td>{label(delivery.admission)}</td><td>{delivery.acceptedCount} / {delivery.refusedCount}</td><td>{delivery.recordedAt || "Not recorded"}</td></tr>)}</tbody></table></StudioTableContainer>
      <StudioCollectionFooter><span>{result.value.items.length} deliveries shown</span>{result.value.next ? <StudioButton onClick={() => setAfter(result.value.next ?? undefined)}>Next deliveries</StudioButton> : null}{after ? <StudioButton onClick={() => setAfter(undefined)}>First deliveries</StudioButton> : null}</StudioCollectionFooter>
    </>}
  </section>;
}

export function safeExecutionPath(path: string | undefined, backendId?: string) {
  if (!path?.startsWith("/") || path.startsWith("//") || path.includes("\\") || !/^\/workflows\/(?:instances|bpmn\/runs)\//.test(path)) return null;
  try {
    const url = new URL(path, window.location.origin);
    if (url.origin !== window.location.origin || !/^\/workflows\/(?:instances|bpmn\/runs)\//.test(url.pathname) || url.searchParams.has("backend") && url.searchParams.get("backend") !== backendId) return null;
    if (backendId) url.searchParams.set("backend", backendId);
    return url.pathname + url.search + url.hash;
  } catch { return null; }
}
export function RecipientEvidence({ recipient, backendId }: { recipient: RecipientView; backendId?: string }) {
  const execution = recipient.executionReference;
  const path = safeExecutionPath(execution?.inspectionPath, backendId);
  return <section className="wf-trigger-recipient" aria-label={`Recipient ${recipient.id}`}>
    <h3>{recipient.target?.definitionId || recipient.subscriptionId || recipient.id}</h3>
    <dl className="wf-trigger-facts"><dt>Admission</dt><dd>{label(recipient.admission)}</dd><dt>Dispatch</dt><dd>{label(recipient.dispatch)}</dd><dt>Accepted target version</dt><dd>{recipient.capturedVersion || "Not captured"}</dd><dt>Current serving version</dt><dd>{recipient.currentVersion || "Not serving / unavailable"}</dd><dt>Execution</dt><dd>{execution ? <>{execution.status || "Status not observed"}{path ? <> · <StudioLink href={path}>Inspect actual run</StudioLink></> : null}{execution.observedAt ? <small className="wf-trigger-muted">Observed {execution.observedAt}</small> : null}</> : "Not observed"}</dd><dt>Attempts</dt><dd>{recipient.attemptCount ?? "Not recorded"}</dd>{recipient.nextRetryAt ? <><dt>Next retry</dt><dd>{recipient.nextRetryAt}</dd></> : null}</dl>
    {recipient.dispatch === "DispatchCommitted" ? <StudioAlert>The dispatch decision is committed. The target may not be queued or started yet. Recovery still owes the exact command; cancel-pending can no longer stop it.</StudioAlert> : recipient.dispatch === "AdmissionUnknown" ? <StudioAlert tone="warning">Target admission is uncertain. Recovery must reconcile retained receipts before another start or cancellation is reported.</StudioAlert> : null}
    {recipient.plannedExecutionIdentity ? <><CopyableIdentifier label="Planned execution identity" value={recipient.plannedExecutionIdentity} /><p className="wf-trigger-muted">A planned identity does not establish queue acceptance or execution.</p></> : null}
    {execution ? <CopyableIdentifier label="Observed execution identity" value={execution.id} /> : null}<Findings items={recipient.findings} />
  </section>;
}

export function DeliveryDetails({ api, client, deliveryId, navigate }: { api: ElsaStudioModuleApi; client: TriggerClient; deliveryId: string; navigate(path: string): void }) {
  const [generation, setGeneration] = useState(0);
  const [review, setReview] = useState<{ trigger: Pick<TriggerView, "id" | "name" | "revision">; command: TriggerCommand }>();
  const { result, reload } = useTriggerLoad(signal => client.delivery(deliveryId, signal), `${deliveryId}:${generation}`);
  function control(delivery: DeliveryView, recipient: RecipientView, kind: "RetryRecipients" | "CancelPendingRecipients") {
    const trigger = { id: delivery.triggerId, name: delivery.triggerName || delivery.triggerId, revision: delivery.triggerRevision };
    setReview({ trigger, command: { kind, deliveryId: delivery.id, expectedDeliveryRevision: delivery.revision, recipients: [{ recipientId: recipient.id, expectedRevision: recipient.revision }] } });
  }
  return <StudioPage className="wf-trigger-page" aria-label="Trigger delivery details"><StudioPageHeader title="Trigger delivery" description="Acceptance, dispatch, execution and transport are separate outcomes." actions={<><StudioButton onClick={() => navigate(result.state === "loaded" ? triggerPath(result.value.triggerId) : "/workflows/triggers")}>Back to trigger</StudioButton><StudioButton onClick={reload}>Refresh</StudioButton></>} />
    {review ? <ChangeReview api={api} client={client} trigger={review.trigger} command={review.command} onCancel={() => setReview(undefined)} onApplied={() => { setReview(undefined); setGeneration(value => value + 1); }} /> : result.state === "loading" ? <Loading label="Loading trigger delivery" /> : result.state === "error" ? <RequestError error={result.error} title="Delivery evidence unavailable" retry={reload} /> : <>
      <CopyableIdentifier label="Delivery ID" value={result.value.id} />
      <dl className="wf-trigger-facts"><dt>Admission</dt><dd>{label(result.value.admission)} · {result.value.acceptedCount} accepted / {result.value.refusedCount} refused</dd><dt>Admission policy</dt><dd>{result.value.admissionPolicy ? label(result.value.admissionPolicy) : "Not applicable"}</dd><dt>Accepted trigger revision</dt><dd>{result.value.acceptedTriggerRevision ?? "Not accepted"}</dd><dt>Accepted at</dt><dd>{result.value.acceptedAt || "Not accepted"}</dd><dt>HTTP transport</dt><dd>{result.value.transport ? label(result.value.transport.state) : "Not recorded"}{result.value.transport?.statusCode ? ` · ${result.value.transport.statusCode}` : ""}</dd></dl>
      {result.value.transport?.state === "WriteCompleted" ? <p>Server response writing completed. This does not establish that the client consumed the response.</p> : null}<Findings items={result.value.findings} />
      {result.value.recipients.map(recipient => <div key={recipient.id}><RecipientEvidence recipient={recipient} backendId={api.backendSession?.profile.id} /><div className="studio-page-actions">{([ ["RetryRecipients", "Retry owed recipient"], ["CancelPendingRecipients", "Cancel pending recipient"] ] as const).map(([kind, title]) => <StudioButton key={kind} disabled={!allowed(recipient.actions, kind)} title={actionReason(recipient.actions, kind)} onClick={() => { if (result.state === "loaded") void control(result.value, recipient, kind); }}>{title}</StudioButton>)}</div></div>)}
    </>}
  </StudioPage>;
}
