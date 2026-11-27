import { useState } from "react";
import { Activity } from "lucide-react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import {
  CopyableIdentifier,
  StatusChip,
  StudioAlert,
  StudioButton,
  StudioCollectionEmptyState,
  StudioCollectionFooter,
  StudioPage,
  StudioPageHeader,
  StudioTableContainer,
  studioRowInteraction
} from "@elsa-workflows/studio-ui";
import { StudioLink } from "../StudioLink";
import { useWorkflowName } from "./WorkflowIdentity";
import { ChangeReviewDialog } from "./ChangeReview";
import type { TriggerClient } from "./api";
import { Findings, Loading, RequestError, actionReason, allowed, deliveryPath, label, triggerPath, useTriggerLoad, useTriggerScope } from "./shared";
import { deliveryAdmissionTone, recipientDispatchTone, transportStateTone } from "./triggerTypePresentation";
import type { DeliveryView, RecipientView, TriggerCommand, TriggerView } from "./types";

export function DeliveriesSection({ client, trigger, navigate }: { client: TriggerClient; trigger: TriggerView; navigate?(path: string): void }) {
  const [after, setAfter] = useState<string>();
  const { result, reload } = useTriggerLoad(signal => client.deliveries({ triggerId: trigger.id, after }, signal), JSON.stringify([trigger.id, after]));
  const showFooter = result.state === "loaded" && (result.value.items.length > 0 || Boolean(result.value.next) || Boolean(after));
  return (
    <section aria-label="Trigger deliveries" className="wf-trigger-deliveries">
      <div className="wf-trigger-section-heading">
        <h3>Deliveries</h3>
        <StudioButton size="sm" onClick={reload}>Refresh deliveries</StudioButton>
      </div>
      {result.state === "loading" ? (
        <Loading label="Loading trigger deliveries" />
      ) : result.state === "error" ? (
        <RequestError error={result.error} title="Delivery evidence unavailable" retry={reload} />
      ) : !result.value.items.length ? (
        <StudioCollectionEmptyState
          headingLevel={3}
          icon={<Activity size={22} aria-hidden />}
          title={result.value.next || after ? "No visible deliveries on this page" : "No delivery evidence yet"}
          description={result.value.next
            ? "Continue to the next page for retained delivery evidence."
            : "Accepted work, refused attempts and recipient outcomes appear here when recorded."}
        />
      ) : (
        <StudioTableContainer role="region" aria-label="Trigger deliveries table" tabIndex={0}>
          <table className="studio-table wf-trigger-table">
            <thead>
              <tr>
                <th scope="col">Delivery</th>
                <th scope="col">Admission</th>
                <th scope="col">Accepted / refused</th>
                <th scope="col">Recorded</th>
              </tr>
            </thead>
            <tbody>
              {result.value.items.map(delivery => {
                const href = deliveryPath(delivery.id);
                return (
                  <tr key={delivery.id} {...studioRowInteraction<HTMLTableRowElement>(navigate ? () => navigate(href) : undefined)}>
                    <th scope="row">
                      <StudioLink className="studio-row-link wf-trigger-row-title" href={href} navigateToStudioPath={navigate}>
                        {delivery.sourceOccurrenceId || delivery.id}
                      </StudioLink>
                    </th>
                    <td>
                      <StatusChip tone={deliveryAdmissionTone(delivery.admission)}>
                        {label(delivery.admission)}
                      </StatusChip>
                    </td>
                    <td>{delivery.acceptedCount} / {delivery.refusedCount}</td>
                    <td>{delivery.recordedAt || "Not recorded"}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </StudioTableContainer>
      )}
      {showFooter ? (
        <StudioCollectionFooter>
          <span>{result.value.items.length} {result.value.items.length === 1 ? "delivery" : "deliveries"} shown</span>
          {result.value.next ? <StudioButton onClick={() => setAfter(result.value.next ?? undefined)}>Next deliveries</StudioButton> : null}
          {after ? <StudioButton onClick={() => setAfter(undefined)}>First deliveries</StudioButton> : null}
        </StudioCollectionFooter>
      ) : null}
    </section>
  );
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

export function RecipientEvidence({ recipient, backendId, definitionName }: { recipient: RecipientView; backendId?: string; definitionName?: string }) {
  const execution = recipient.executionReference;
  const path = safeExecutionPath(execution?.inspectionPath, backendId);
  return (
    <section className="wf-trigger-recipient" aria-label={`Recipient ${recipient.id}`}>
      <div className="wf-trigger-recipient-header">
        <h3>{definitionName || recipient.targetDisplayName || recipient.target?.definitionId || recipient.subscriptionId || recipient.id}</h3>
        <div className="wf-trigger-chip-group">
          <StatusChip tone={deliveryAdmissionTone(recipient.admission)}>{label(recipient.admission)}</StatusChip>
          <StatusChip tone={recipientDispatchTone(recipient.dispatch)}>{label(recipient.dispatch)}</StatusChip>
        </div>
      </div>
      {recipient.target ? <CopyableIdentifier label="Workflow definition ID" value={recipient.target.definitionId} /> : null}
      {recipient.admission === "Refused" && recipient.target ? (
        <p className="wf-trigger-muted">Identified from the unchanged association; no target version was captured.</p>
      ) : null}
      <dl className="wf-trigger-facts">
        <dt>Admission</dt>
        <dd><StatusChip tone={deliveryAdmissionTone(recipient.admission)}>{label(recipient.admission)}</StatusChip></dd>
        <dt>Dispatch</dt>
        <dd><StatusChip tone={recipientDispatchTone(recipient.dispatch)}>{label(recipient.dispatch)}</StatusChip></dd>
        <dt>Accepted target version</dt>
        <dd>{recipient.capturedVersion || "Not captured"}</dd>
        <dt>Current serving version</dt>
        <dd>{recipient.currentVersion || "Not serving / unavailable"}</dd>
        <dt>Execution</dt>
        <dd>
          {execution ? (
            <>
              {execution.status || "Status not observed"}
              {path ? <> · <StudioLink href={path}>Inspect actual run</StudioLink></> : null}
              {execution.observedAt ? <small className="wf-trigger-muted">Observed {execution.observedAt}</small> : null}
            </>
          ) : "Not observed"}
        </dd>
        {execution?.cleanupStatus ? <><dt>Model cleanup</dt><dd>{execution.cleanupStatus}</dd></> : null}
        <dt>Attempts</dt>
        <dd>{recipient.attemptCount ?? "Not recorded"}</dd>
        {recipient.nextRetryAt ? <><dt>Next retry</dt><dd>{recipient.nextRetryAt}</dd></> : null}
      </dl>
      {recipient.dispatch === "DispatchCommitted" ? (
        <StudioAlert>The dispatch decision is committed. The target may not be queued or started yet. Recovery still owes the exact command; cancel-pending can no longer stop it.</StudioAlert>
      ) : recipient.dispatch === "AdmissionUnknown" ? (
        <StudioAlert tone="warning">Target admission is uncertain. Recovery must reconcile retained receipts before another start or cancellation is reported.</StudioAlert>
      ) : null}
      {recipient.plannedExecutionIdentity ? (
        <>
          <CopyableIdentifier label="Planned execution identity" value={recipient.plannedExecutionIdentity} />
          <p className="wf-trigger-muted">A planned identity does not establish queue acceptance or execution.</p>
        </>
      ) : null}
      {execution ? <CopyableIdentifier label="Observed execution identity" value={execution.id} /> : null}
      <Findings items={recipient.findings} />
    </section>
  );
}

function RecipientDetails({ api, scope, recipient }: { api: ElsaStudioModuleApi; scope: string; recipient: RecipientView }) {
  // Runtime-only sources may have no authoring label. Optional Design reads use its normal authorization.
  const definitionId = recipient.target?.definitionId;
  const lookupId = recipient.targetDisplayName && recipient.targetDisplayName !== definitionId ? undefined : definitionId;
  const name = useWorkflowName(api, scope, lookupId);
  return <RecipientEvidence recipient={recipient} backendId={api.backendSession?.profile.id} definitionName={name} />;
}

export function DeliveryDetails({ api, client, deliveryId, navigate }: { api: ElsaStudioModuleApi; client: TriggerClient; deliveryId: string; navigate(path: string): void }) {
  const scope = useTriggerScope(api);
  const [generation, setGeneration] = useState(0);
  const [review, setReview] = useState<{ trigger: Pick<TriggerView, "id" | "name" | "revision">; command: TriggerCommand }>();
  const { result, reload } = useTriggerLoad(signal => client.delivery(deliveryId, signal), `${deliveryId}:${generation}`);

  function control(delivery: DeliveryView, recipient: RecipientView, kind: "RetryRecipients" | "CancelPendingRecipients") {
    const trigger = { id: delivery.triggerId, name: delivery.triggerName || delivery.triggerId, revision: delivery.triggerRevision };
    setReview({ trigger, command: { kind, deliveryId: delivery.id, expectedDeliveryRevision: delivery.revision, recipients: [{ recipientId: recipient.id, expectedRevision: recipient.revision }] } });
  }

  return (
    <StudioPage className="wf-trigger-page" aria-label="Trigger delivery details">
      <StudioPageHeader
        title="Trigger delivery"
        description="Acceptance, dispatch, execution and transport are separate outcomes."
        refresh={{
          id: `standalone-delivery-${deliveryId}`,
          label: "Refresh",
          onRefresh: reload,
          refreshing: result.state === "loading",
          autoRefresh: true
        }}
        actions={
          <StudioButton onClick={() => navigate(result.state === "loaded" ? triggerPath(result.value.triggerId) : "/workflows/triggers")}>
            Back to trigger
          </StudioButton>
        }
      />
      {result.state === "loading" ? (
        <Loading label="Loading trigger delivery" />
      ) : result.state === "error" ? (
        <RequestError error={result.error} title="Delivery evidence unavailable" retry={reload} />
      ) : (
        <>
          <section className="wf-trigger-card" aria-label="Delivery summary">
            <div className="wf-trigger-card-header">
              <h2 className="wf-trigger-card-title">Delivery summary</h2>
              <StatusChip tone={deliveryAdmissionTone(result.value.admission)}>
                {label(result.value.admission)}
              </StatusChip>
            </div>
            <CopyableIdentifier label="Delivery ID" value={result.value.id} />
            <dl className="wf-trigger-facts">
              <dt>Admission</dt>
              <dd>
                <span className="wf-trigger-inline-status">
                  <StatusChip tone={deliveryAdmissionTone(result.value.admission)}>{label(result.value.admission)}</StatusChip>
                  <span>· {result.value.acceptedCount} accepted / {result.value.refusedCount} refused</span>
                </span>
              </dd>
              <dt>Admission policy</dt>
              <dd>{result.value.admissionPolicy ? label(result.value.admissionPolicy) : "Not applicable"}</dd>
              <dt>Accepted trigger revision</dt>
              <dd>{result.value.acceptedTriggerRevision ?? (result.value.acceptedCount > 0 ? "Not recorded" : "Not accepted")}</dd>
              <dt>Accepted at</dt>
              <dd>{result.value.acceptedAt || (result.value.acceptedCount > 0 ? "Not recorded" : "Not accepted")}</dd>
              <dt>HTTP transport</dt>
              <dd>
                {result.value.transport ? (
                  <span className="wf-trigger-inline-status">
                    <StatusChip tone={transportStateTone(result.value.transport.state)}>
                      {label(result.value.transport.state)}
                    </StatusChip>
                    {result.value.transport.statusCode ? <span>· {result.value.transport.statusCode}</span> : null}
                  </span>
                ) : "Not recorded"}
              </dd>
            </dl>
            {result.value.transport?.state === "WriteCompleted" ? (
              <p className="wf-trigger-muted">Server response writing completed. This does not establish that the client consumed the response.</p>
            ) : null}
            <Findings items={result.value.findings} />
          </section>

          <div className="wf-trigger-recipients">
            {result.value.recipients.map(recipient => (
              <div key={recipient.id} className="wf-trigger-card wf-trigger-recipient-card">
                <RecipientDetails api={api} scope={scope} recipient={recipient} />
                <div className="wf-trigger-card-actions">
                  {([
                    ["RetryRecipients", "Retry owed recipient"],
                    ["CancelPendingRecipients", "Cancel pending recipient"]
                  ] as const).map(([kind, title]) => (
                    <StudioButton
                      key={kind}
                      size="sm"
                      disabled={!allowed(recipient.actions, kind)}
                      title={actionReason(recipient.actions, kind)}
                      onClick={() => { if (result.state === "loaded") void control(result.value, recipient, kind); }}
                    >
                      {title}
                    </StudioButton>
                  ))}
                </div>
              </div>
            ))}
          </div>

          {review ? (
            <ChangeReviewDialog
              api={api}
              client={client}
              trigger={review.trigger}
              command={review.command}
              onCancel={() => setReview(undefined)}
              onApplied={() => {
                setReview(undefined);
                setGeneration(value => value + 1);
              }}
            />
          ) : null}
        </>
      )}
    </StudioPage>
  );
}
