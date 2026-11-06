import { useEffect, useRef, useState } from "react";
import { StudioAlert, StudioButton, StudioTableContainer } from "@elsa-workflows/studio-ui";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { operationId, type TriggerClient } from "./api";
import { ActionError, Findings, Loading, RequestError, label, useTriggerCompletion, useTriggerLoad } from "./shared";
import type { ChangeResult, TriggerCommand, TriggerView } from "./types";

export function ChangeReview({ api, client, trigger, command, onCancel, onApplied }: {
  api: ElsaStudioModuleApi; client: TriggerClient; trigger: TriggerView; command: TriggerCommand; onCancel(): void; onApplied(result: ChangeResult): void;
}) {
  const [base, setBase] = useState({ trigger, command, key: operationId() });
  const requestId = useRef(operationId());
  const [busy, setBusy] = useState(false);
  const lock = useRef(false);
  const complete = useTriggerCompletion();
  const [error, setError] = useState<unknown>();
  const heading = useRef<HTMLHeadingElement>(null);
  const { result, reload } = useTriggerLoad(signal => client.preview(base.trigger.id, base.trigger.revision, base.command, signal), base.key);
  useEffect(() => { const previous = document.activeElement; heading.current?.focus(); return () => { if (previous instanceof HTMLElement && previous.isConnected) previous.focus(); }; }, []);
  async function reloadCurrent() {
    if (lock.current) return;
    lock.current = true; setBusy(true);
    try {
      const current = await client.read(base.trigger.id);
      let nextCommand = base.command;
      if ("subscriptionId" in nextCommand) {
        const subscription = await client.subscription(nextCommand.subscriptionId);
        nextCommand = { ...nextCommand, expectedSubscriptionRevision: subscription.revision, ...("expectedGrantRevision" in nextCommand ? { expectedGrantRevision: subscription.invocationGrant.revision ?? null } : {}) };
      }
      if ("deliveryId" in nextCommand) {
        const delivery = await client.delivery(nextCommand.deliveryId);
        const selected = new Set(nextCommand.recipients.map(item => item.recipientId));
        nextCommand = { ...nextCommand, expectedDeliveryRevision: delivery.revision, recipients: delivery.recipients.filter(item => selected.has(item.id)).map(item => ({ recipientId: item.id, expectedRevision: item.revision })) };
      }
      complete(() => { requestId.current = operationId(); setError(undefined); setBase({ trigger: current, command: nextCommand, key: operationId() }); setBusy(false); });
    } catch (caught) { complete(() => { setError(caught); setBusy(false); }); }
    finally { lock.current = false; }
  }
  async function apply() {
    if (lock.current || result.state !== "loaded" || !result.value.canApply) return;
    lock.current = true; setBusy(true); setError(undefined);
    try {
      const saved = await client.apply(base.trigger.id, base.trigger.revision, base.command, result.value.previewId, requestId.current);
      complete(() => { api.notifications?.notify({ level: "success", title: `${label(base.command.kind)} applied` }); setBusy(false); onApplied(saved); });
    } catch (caught) { complete(() => { setError(caught); setBusy(false); }); }
    finally { lock.current = false; }
  }
  return <section className="wf-trigger-review" aria-labelledby="trigger-review-title" aria-busy={busy}>
    <h3 id="trigger-review-title" tabIndex={-1} ref={heading}>Review {label(base.command.kind).toLowerCase()}</h3>
    <p>{base.trigger.name} · Current resource revision {base.trigger.revision}</p>
    {result.state === "loading" ? <Loading label="Preparing change review" /> : result.state === "error" ? <RequestError error={result.error} title="Could not prepare change review" retry={reload} /> : <>
      <Findings items={result.value.findings} />
      {result.value.impact.length ? <StudioTableContainer role="region" aria-label="Change impact" tabIndex={0}><table className="studio-table"><thead><tr><th scope="col">Affected item</th><th scope="col">Current</th><th scope="col">Proposed</th></tr></thead><tbody>{result.value.impact.map((item, index) => <tr key={`${item.label}:${index}`}><th scope="row">{item.label}{item.description ? <small className="wf-trigger-muted">{item.description}</small> : null}</th><td>{item.before ?? "—"}</td><td>{item.after ?? "—"}</td></tr>)}</tbody></table></StudioTableContainer> : <p>The server reports no additional affected items.</p>}
      {!result.value.canApply ? <StudioAlert tone="warning">Resolve the findings before this change can be applied.</StudioAlert> : null}
    </>}
    {error ? <ActionError error={error} reload={() => void reloadCurrent()} /> : null}
    <div className="studio-page-actions"><StudioButton disabled={busy} onClick={onCancel}>Back to editing</StudioButton><StudioButton disabled={busy} onClick={() => void reloadCurrent()}>Reload and review current state</StudioButton><StudioButton variant="primary" disabled={busy || result.state !== "loaded" || !result.value.canApply} onClick={() => void apply()}>{busy ? "Applying…" : "Apply reviewed change"}</StudioButton></div>
  </section>;
}
