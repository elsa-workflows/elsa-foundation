import { useEffect, useRef, useState } from "react";
import { StudioAlert, StudioButton, StudioDialog, StudioTableContainer } from "@elsa-workflows/studio-ui";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { operationId, type TriggerClient } from "./api";
import { ActionError, Findings, Loading, RequestError, label, status, useTriggerCompletion, useTriggerLoad, useTriggerScope, validationErrors } from "./shared";
import { useWorkflowName } from "./WorkflowIdentity";
import type { ChangeResult, ImpactItem, TriggerCommand, TriggerView } from "./types";

function ImpactLabel({ api, scope, item }: { api: ElsaStudioModuleApi; scope: string; item: ImpactItem }) {
  const name = useWorkflowName(api, scope, item.target?.definitionId);
  return <>{name || item.label}</>;
}

export function ChangeReview({
  api,
  client,
  trigger,
  command,
  embedded = false,
  actionsClassName,
  cancelLabel = "Back to editing",
  onCancel,
  onApplied
}: {
  api: ElsaStudioModuleApi;
  client: TriggerClient;
  trigger: Pick<TriggerView, "id" | "name" | "revision">;
  command: TriggerCommand;
  embedded?: boolean;
  actionsClassName?: string;
  cancelLabel?: string;
  onCancel(errors?: Record<string, string[]>): void;
  onApplied(result: ChangeResult): void;
}) {
  const [base, setBase] = useState({ trigger, command, key: operationId() });
  const requestId = useRef(operationId());
  const [busy, setBusy] = useState(false);
  const lock = useRef(false);
  const complete = useTriggerCompletion();
  const [error, setError] = useState<unknown>();
  const [staleReview, setStaleReview] = useState(false);
  const scope = useTriggerScope(api);
  const heading = useRef<HTMLHeadingElement>(null);
  const { result, reload } = useTriggerLoad(signal => client.preview(base.trigger.id, base.trigger.revision, base.command, signal), base.key);
  useEffect(() => {
    if (embedded) return;
    const previous = document.activeElement;
    heading.current?.focus();
    return () => { if (previous instanceof HTMLElement && previous.isConnected) previous.focus(); };
  }, [embedded]);
  async function reloadCurrent() {
    if (lock.current) return;
    lock.current = true; setBusy(true);
    try {
      let current: Pick<TriggerView, "id" | "name" | "revision">;
      let nextCommand = base.command;
      if ("deliveryId" in nextCommand) {
        const delivery = await client.delivery(nextCommand.deliveryId);
        if (delivery.triggerId !== base.trigger.id) throw new Error("Delivery resource changed; reopen its current details.");
        current = { id: delivery.triggerId, name: delivery.triggerName || delivery.triggerId, revision: delivery.triggerRevision };
        const selected = new Set(nextCommand.recipients.map(item => item.recipientId));
        nextCommand = { ...nextCommand, expectedDeliveryRevision: delivery.revision, recipients: delivery.recipients.filter(item => selected.has(item.id)).map(item => ({ recipientId: item.id, expectedRevision: item.revision })) };
      } else {
        current = await client.read(base.trigger.id);
      }
      if ("subscriptionId" in nextCommand) {
        const subscription = await client.subscription(nextCommand.subscriptionId);
        nextCommand = { ...nextCommand, expectedSubscriptionRevision: subscription.revision, ...("expectedGrantRevision" in nextCommand ? { expectedGrantRevision: subscription.invocationGrant.revision ?? null } : {}) };
      }
      complete(() => { requestId.current = operationId(); setError(undefined); setStaleReview(false); setBase({ trigger: current, command: nextCommand, key: operationId() }); setBusy(false); });
    } catch (caught) { complete(() => { setError(caught); setBusy(false); }); }
    finally { lock.current = false; }
  }
  async function apply() {
    if (lock.current || staleReview || result.state !== "loaded" || !result.value.canApply) return;
    lock.current = true; setBusy(true); setError(undefined);
    try {
      const saved = await client.apply(base.trigger.id, base.trigger.revision, base.command, result.value.previewId, requestId.current);
      complete(() => { api.notifications?.notify({ level: "success", title: `${label(base.command.kind)} applied` }); setBusy(false); onApplied(saved); });
    } catch (caught) { complete(() => { setError(caught); if (status(caught) === 409) setStaleReview(true); setBusy(false); }); }
    finally { lock.current = false; }
  }
  const footerClassName = actionsClassName ?? (embedded ? "studio-dialog-footer wf-trigger-review-actions" : "studio-page-actions wf-trigger-review-actions");
  return <section className="wf-trigger-review" aria-labelledby={embedded ? undefined : "trigger-review-title"} aria-label={embedded ? `Review ${label(base.command.kind).toLowerCase()}` : undefined} aria-busy={busy}>
    {!embedded ? <h3 id="trigger-review-title" tabIndex={-1} ref={heading}>Review {label(base.command.kind).toLowerCase()}</h3> : null}
    <p className="wf-trigger-review-context">{base.trigger.name} · Current resource revision {base.trigger.revision}</p>
    {result.state === "loading" ? <Loading label="Preparing change review" /> : result.state === "error" ? <RequestError error={result.error} title="Could not prepare change review" retry={reload} /> : <>
      <Findings items={result.value.findings} />
      {result.value.impact.length ? <StudioTableContainer role="region" aria-label="Change impact" tabIndex={0}><table className="studio-table wf-trigger-table wf-trigger-impact-table"><thead><tr><th scope="col">Affected item</th><th scope="col">Current</th><th scope="col">Proposed</th></tr></thead><tbody>{result.value.impact.map((item, index) => <tr key={`${item.label}:${index}`}><th scope="row"><ImpactLabel api={api} scope={scope} item={item} />{item.description ? <small className="wf-trigger-muted">{item.description}</small> : null}</th><td>{item.before ?? "—"}</td><td>{item.after ?? "—"}</td></tr>)}</tbody></table></StudioTableContainer> : <p className="wf-trigger-muted">The server reports no additional affected items.</p>}
      {!result.value.canApply ? <StudioAlert tone="warning">Resolve the findings before this change can be applied.</StudioAlert> : null}
    </>}
    {error ? <ActionError error={error} reload={() => void reloadCurrent()} /> : null}
    <div className={footerClassName}>
      <StudioButton type="button" disabled={busy} onClick={() => onCancel(validationErrors(error ?? (result.state === "error" ? result.error : undefined)))}>{cancelLabel}</StudioButton>
      <StudioButton type="button" disabled={busy} onClick={() => void reloadCurrent()}>Reload and review current state</StudioButton>
      <StudioButton type="button" variant={base.command.kind === "DeleteTrigger" || base.command.kind === "RemoveSubscription" ? "danger" : "primary"} disabled={busy || staleReview || result.state !== "loaded" || !result.value.canApply} onClick={() => void apply()}>{busy ? "Applying…" : "Apply reviewed change"}</StudioButton>
    </div>
  </section>;
}

export function ChangeReviewDialog({
  api,
  client,
  trigger,
  command,
  onCancel,
  onApplied
}: {
  api: ElsaStudioModuleApi;
  client: TriggerClient;
  trigger: Pick<TriggerView, "id" | "name" | "revision">;
  command: TriggerCommand;
  onCancel(errors?: Record<string, string[]>): void;
  onApplied(result: ChangeResult): void;
}) {
  return (
    <StudioDialog
      open
      title={`Review ${label(command.kind).toLowerCase()}`}
      description="Inspect the projected impact on this shared trigger before applying the change."
      className="wf-trigger-review-dialog"
      dismissOnOutsideClick={false}
      onClose={() => onCancel()}
    >
      <ChangeReview
        api={api}
        client={client}
        trigger={trigger}
        command={command}
        embedded
        onCancel={onCancel}
        onApplied={onApplied}
      />
    </StudioDialog>
  );
}
