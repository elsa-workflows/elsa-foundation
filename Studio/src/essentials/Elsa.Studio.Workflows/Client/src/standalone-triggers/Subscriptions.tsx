import { useMemo, useRef, useState } from "react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { StudioAlert, StudioButton, StudioCollectionFooter, StudioField, StudioResultState, StudioSearchInput, StudioTableContainer } from "@elsa-workflows/studio-ui";
import { StudioLink } from "../StudioLink";
import type { WorkflowDesignerPanelContext } from "../workflow-editor/editorTypes";
import { triggerClient, type TriggerClient } from "./api";
import { ChangeReview } from "./ChangeReview";
import { MappingEditor, mappingDrafts, realizeMappings, type MappingDrafts } from "./MappingEditor";
import { Findings, Loading, RequestError, actionReason, allowed, label, triggerPath, useTriggerLoad, useTriggerScope } from "./shared";
import type { ActorView, InputMapping, SubscriptionView, TargetSelector, TriggerCommand, TriggerView } from "./types";
import "./styles.css";

const targetRestricted = (subscription: SubscriptionView) => subscription.findings.some(item => item.code === "TargetDetailsRestricted") || !subscription.target.definitionId || !subscription.target.slotName;

function TargetPicker({ client, value, fixedDefinitionId, onChange }: { client: TriggerClient; value: TargetSelector; fixedDefinitionId?: string; onChange(target: TargetSelector): void }) {
  const [search, setSearch] = useState("");
  const [term, setTerm] = useState("");
  const [after, setAfter] = useState<string>();
  const { result, reload } = useTriggerLoad(signal => client.targets({ search: term, after }, signal), JSON.stringify([term, after]));
  if (fixedDefinitionId) return <><StudioField label="Workflow"><span>{fixedDefinitionId}</span></StudioField><StudioField label="Publication slot" description="The association follows this slot's serving version. An unpublished slot can be saved only disabled."><input aria-label="Publication slot" value={value.slotName} onChange={event => onChange({ definitionId: fixedDefinitionId, slotName: event.currentTarget.value })} /></StudioField></>;
  return <section aria-label="Choose subscription target">
    <StudioField label="Find a published workflow"><StudioSearchInput aria-label="Search subscription targets" value={search} onChange={event => setSearch(event.currentTarget.value)} /></StudioField>
    <StudioButton type="button" onClick={() => { setAfter(undefined); setTerm(search.trim()); }}>Search workflows</StudioButton>
    {result.state === "loading" ? <Loading label="Loading subscription targets" /> : result.state === "error" ? <RequestError error={result.error} title="Could not load workflow targets" retry={reload} /> : <>
      <StudioField label="Workflow and publication slot"><select aria-label="Workflow and publication slot" value={JSON.stringify(value)} onChange={event => onChange(JSON.parse(event.currentTarget.value) as TargetSelector)}><option value={JSON.stringify({ definitionId: "", slotName: "default" })}>Choose a workflow…</option>
        {value.definitionId && !result.value.items.some(item => item.definitionId === value.definitionId && item.slotName === value.slotName) ? <option value={JSON.stringify(value)}>{value.definitionId} · {value.slotName}</option> : null}
        {result.value.items.map(item => <option key={`${item.definitionId}:${item.slotName}`} value={JSON.stringify({ definitionId: item.definitionId, slotName: item.slotName })}>{item.displayName || item.definitionId} · {item.slotName} · {label(item.state)}</option>)}
      </select></StudioField>
      {!result.value.items.length ? <p>No accessible workflow slots match this search.</p> : null}
      {result.value.next ? <StudioButton type="button" onClick={() => setAfter(result.value.next ?? undefined)}>Next workflow targets</StudioButton> : null}{after ? <StudioButton type="button" onClick={() => setAfter(undefined)}>First workflow targets</StudioButton> : null}
    </>}
  </section>;
}

export function SubscriptionEditor({ api, client, actor, trigger, subscription, fixedDefinitionId, onCancel, onSaved }: {
  api: ElsaStudioModuleApi; client: TriggerClient; actor: ActorView; trigger: TriggerView; subscription?: SubscriptionView; fixedDefinitionId?: string; onCancel(): void; onSaved(): void;
}) {
  const [target, setTarget] = useState<TargetSelector>(subscription?.target ?? { definitionId: fixedDefinitionId ?? "", slotName: "default" });
  const [drafts, setDrafts] = useState<MappingDrafts>(() => mappingDrafts(subscription?.mappings ?? []));
  const [enabled, setEnabled] = useState(subscription?.enabled ?? false);
  const [grant, setGrant] = useState(false);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [command, setCommand] = useState<TriggerCommand>();
  const targetKey = JSON.stringify([trigger.id, trigger.revision, target]);
  const mappings = useRef<InputMapping[]>(subscription?.mappings ?? []);
  const { result, reload } = useTriggerLoad(async signal => {
    if (!target.definitionId || !target.slotName) return null;
    const context = await client.mappingContext(trigger.id, trigger.revision, target, mappings.current, signal);
    return context;
  }, targetKey);
  function review() {
    if (!target.definitionId || !target.slotName) { setErrors({ target: "Choose a workflow and publication slot." }); return; }
    if (result.state !== "loaded" || !result.value) return;
    if (enabled && result.value.target.state !== "Available") { setErrors({ target: "Save this association disabled until its serving target is available." }); return; }
    const built = result.value.target.inputContract ? realizeMappings(result.value.target.inputContract.inputs, drafts) : { mappings: mappings.current.filter(item => drafts[item.inputName] && drafts[item.inputName].kind !== "Omit"), errors: {} };
    setErrors(built.errors);
    if (Object.keys(built.errors).length) return;
    mappings.current = built.mappings;
    setCommand(subscription ? { kind: "SaveSubscription", subscriptionId: subscription.id, expectedSubscriptionRevision: subscription.revision, target, mappings: built.mappings, enabled, invocationRequest: grant ? "Grant" : "Keep" }
      : { kind: "AddSubscription", target, mappings: built.mappings, enabled, invocationRequest: grant ? "Grant" : "None" });
  }
  if (command) return <ChangeReview api={api} client={client} trigger={trigger} command={command} onCancel={() => setCommand(undefined)} onApplied={onSaved} />;
  const actualTarget = result.state === "loaded" ? result.value?.target : undefined;
  const readonly = !!subscription?.origin.configurationReadOnly;
  if (subscription && targetRestricted(subscription)) return <section aria-label="Restricted workflow subscription"><h3>Restricted workflow</h3><StudioAlert>Workflow details and input mappings require workflow read permission. Use the association's available controls to pause or revoke its authority.</StudioAlert><StudioButton onClick={onCancel}>Back to associations</StudioButton></section>;
  return <section className="wf-trigger-subscription-editor" aria-label={subscription ? "Edit live subscription" : "Add live subscription"}>
    <h3>{subscription ? "Edit subscription" : "Add subscription"}</h3>
    <StudioAlert>These associations are live resources. Saving them does not save or publish a workflow draft and does not change its business graph.</StudioAlert>
    {trigger.http ? <p>{trigger.http.mode === "WorkflowResponse" ? "HTTP · One workflow owns the response" : `HTTP · Acknowledge and start subscribers · ${trigger.http.admissionPolicy === "AcceptAvailable" ? "Accept available subscribers" : "Require all subscribers"}`}</p> : null}
    {readonly ? <StudioAlert tone="warning">This association is managed by {subscription?.origin.sourceId || "its source"}. Its configuration is read-only.</StudioAlert> : null}
    <fieldset disabled={readonly} className="wf-trigger-form"><legend>Workflow target and mapping</legend>
      <TargetPicker client={client} value={target} fixedDefinitionId={fixedDefinitionId} onChange={setTarget} />
      {errors.target ? <p role="alert">{errors.target}</p> : null}
      {result.state === "loading" && target.definitionId ? <Loading label="Loading serving input contract" /> : result.state === "error" ? <RequestError error={result.error} title="Mapping context unavailable" retry={reload} /> : result.state === "loaded" && result.value ? <>
        <p>Serving version: {result.value.target.serving?.version || result.value.target.serving?.versionId || "Not serving"} · Slot: {target.slotName}</p>
        {trigger.http?.mode === "WorkflowResponse" && result.value.target.supportsWorkflowOwnedHttpResponse !== true ? <StudioAlert tone="warning">This target cannot own an HTTP response. Select a compatible target or review a trigger mode change.</StudioAlert> : null}
        <MappingEditor key={targetKey} client={client} triggerId={trigger.id} triggerRevision={trigger.revision} target={result.value.target} context={result.value} drafts={drafts} onChange={setDrafts} disabled={readonly} errors={errors} />
      </> : null}
      <label className="wf-trigger-check"><input type="checkbox" checked={grant} disabled={!actor.canAuthorize} onChange={event => setGrant(event.currentTarget.checked)} />Authorize this shared trigger to invoke the workflow</label>
      <p className="wf-trigger-muted">An authorized manager grants shared-resource authority. It survives the creator's departure and can be revoked explicitly.</p>
      <label className="wf-trigger-check"><input type="checkbox" checked={enabled} disabled={!actor.canOperate} onChange={event => setEnabled(event.currentTarget.checked)} />Enable this subscription</label>
      {actualTarget?.state !== "Available" ? <p>Save disabled while its serving slot or input contract is unavailable.</p> : null}
    </fieldset>
    <div className="studio-page-actions"><StudioButton onClick={onCancel}>Cancel</StudioButton><StudioButton variant="primary" disabled={readonly || !actor.canManage || result.state !== "loaded" || !result.value} onClick={review}>Review subscription</StudioButton></div>
  </section>;
}

export function SubscriptionsSection({ api, client, actor, trigger, definitionId, onChanged }: { api: ElsaStudioModuleApi; client: TriggerClient; actor: ActorView; trigger?: TriggerView; definitionId?: string; onChanged?(): void }) {
  const [generation, setGeneration] = useState(0);
  const [after, setAfter] = useState<string>();
  const [editing, setEditing] = useState<{ trigger: TriggerView; subscription?: SubscriptionView }>();
  const [control, setControl] = useState<{ trigger: TriggerView; command: TriggerCommand }>();
  const [error, setError] = useState<unknown>();
  const [adding, setAdding] = useState(false);
  const key = JSON.stringify([trigger?.id, trigger?.revision, definitionId, after, generation]);
  const { result, reload } = useTriggerLoad(signal => client.subscriptions(trigger ? { triggerId: trigger.id, after } : { definitionId, after }, signal), key);
  async function choose(subscription: SubscriptionView, kind?: "RemoveSubscription" | "EnableSubscription" | "PauseSubscription" | "GrantInvocation" | "RevokeInvocation") {
    setError(undefined);
    if (targetRestricted(subscription) && (!kind || kind === "GrantInvocation")) return;
    try {
      const actualTrigger = await client.read(subscription.triggerId);
      if (kind) setControl({ trigger: actualTrigger, command: kind === "GrantInvocation" || kind === "RevokeInvocation" ? { kind, subscriptionId: subscription.id, expectedSubscriptionRevision: subscription.revision, expectedGrantRevision: subscription.invocationGrant.revision ?? null } : { kind, subscriptionId: subscription.id, expectedSubscriptionRevision: subscription.revision } });
      else setEditing({ trigger: actualTrigger, subscription });
    } catch (caught) { setError(caught); }
  }
  const saved = () => { setEditing(undefined); setControl(undefined); setAdding(false); setGeneration(value => value + 1); onChanged?.(); };
  async function add() { if (!trigger) { setAdding(true); return; } try { setEditing({ trigger: await client.read(trigger.id) }); } catch (caught) { setError(caught); } }
  if (editing) return <SubscriptionEditor api={api} client={client} actor={actor} trigger={editing.trigger} subscription={editing.subscription} fixedDefinitionId={definitionId} onCancel={() => setEditing(undefined)} onSaved={saved} />;
  if (control) return <ChangeReview api={api} client={client} trigger={control.trigger} command={control.command} onCancel={() => setControl(undefined)} onApplied={saved} />;
  return <section className="wf-trigger-subscriptions" aria-label="Live workflow subscriptions">
    <div className="wf-trigger-section-heading"><h3>{definitionId ? "Live triggers" : "Workflow subscriptions"}</h3><div className="studio-page-actions">{actor.canManage ? <StudioButton disabled={!!trigger && !allowed(trigger.actions, "AddSubscription")} title={trigger ? actionReason(trigger.actions, "AddSubscription") : undefined} onClick={() => void add()}>Add {definitionId ? "trigger" : "workflow"}</StudioButton> : null}<StudioButton onClick={() => { setError(undefined); reload(); }}>Refresh associations</StudioButton></div></div>
    {definitionId ? <StudioAlert>These entry points target the logical workflow's current serving slots. Changes take effect independently of this draft or historical graph.</StudioAlert> : null}
    {error ? <RequestError error={error} title="Could not open the association" retry={() => setError(undefined)} /> : null}
    {adding ? <TriggerPicker client={client} onCancel={() => setAdding(false)} onChoose={selected => { setAdding(false); setEditing({ trigger: selected }); }} /> : null}
    {result.state === "loading" ? <Loading label="Loading live trigger associations" /> : result.state === "error" ? <RequestError error={result.error} title="Could not load live associations" retry={reload} /> : !result.value.items.length ? <StudioResultState headingLevel={3} title="No live associations" description={definitionId ? "This workflow has no standalone trigger subscriptions in the selected scope." : "Associate a workflow, map its declared inputs and authorize its invocation."} /> : <StudioTableContainer role="region" aria-label="Workflow subscriptions" tabIndex={0}><table className="studio-table"><thead><tr><th scope="col">{definitionId ? "Trigger" : "Workflow"}</th><th scope="col">Publication slot</th><th scope="col">State</th><th scope="col">Actions</th></tr></thead><tbody>{result.value.items.map(subscription => {
      const restricted = targetRestricted(subscription);
      return <tr key={subscription.id}>
      <th scope="row">{definitionId ? <StudioLink href={triggerPath(subscription.triggerId)}>{subscription.triggerSummary?.name || subscription.triggerId}</StudioLink> : restricted ? "Restricted workflow" : subscription.target.definitionId}{!restricted ? <small className="wf-trigger-muted">{subscription.serving?.version || subscription.serving?.versionId || "Not serving"}</small> : null}{subscription.triggerSummary?.http ? <small className="wf-trigger-muted">{subscription.triggerSummary.http.mode === "WorkflowResponse" ? "Workflow-owned response" : `Async · ${subscription.triggerSummary.http.admissionPolicy === "AcceptAvailable" ? "Accept available" : "Require all"}`}</small> : null}</th>
      <td>{restricted ? "Restricted" : subscription.target.slotName}</td><td>{subscription.enabled ? "Enabled" : "Paused"} · {restricted ? "Workflow details restricted" : label(subscription.compatibility)}<small className="wf-trigger-muted">Invocation: {label(subscription.invocationGrant.state)}</small><Findings items={subscription.findings} />{subscription.origin.configurationReadOnly ? <small>Managed by {subscription.origin.sourceId || "source"}</small> : null}</td>
      <td><div className="studio-page-actions">{([ ["SaveSubscription", "Edit"], [subscription.enabled ? "PauseSubscription" : "EnableSubscription", subscription.enabled ? "Pause" : "Enable"], [subscription.invocationGrant.state === "Granted" ? "RevokeInvocation" : "GrantInvocation", subscription.invocationGrant.state === "Granted" ? "Revoke authority" : "Authorize"], ["RemoveSubscription", "Remove"] ] as const).filter(([action]) => !restricted || action !== "SaveSubscription" && action !== "GrantInvocation").map(([action, text]) => <StudioButton key={action} size="sm" disabled={!allowed(subscription.actions, action)} title={actionReason(subscription.actions, action)} onClick={() => void choose(subscription, action === "SaveSubscription" ? undefined : action)}>{text}</StudioButton>)}</div></td>
    </tr>; })}</tbody></table></StudioTableContainer>}
    {result.state === "loaded" ? <StudioCollectionFooter><span>{result.value.items.length} associations shown</span>{result.value.next ? <StudioButton onClick={() => setAfter(result.value.next ?? undefined)}>Next associations</StudioButton> : null}{after ? <StudioButton onClick={() => setAfter(undefined)}>First associations</StudioButton> : null}</StudioCollectionFooter> : null}
  </section>;
}
function TriggerPicker({ client, onChoose, onCancel }: { client: TriggerClient; onChoose(trigger: TriggerView): void; onCancel(): void }) {
  const [search, setSearch] = useState("");
  const [term, setTerm] = useState("");
  const [after, setAfter] = useState<string>();
  const { result, reload } = useTriggerLoad(signal => client.list({ search: term, after }, signal), JSON.stringify([term, after]));
  return <section aria-label="Choose standalone trigger"><h4>Choose a trigger</h4><div className="studio-page-actions"><StudioSearchInput aria-label="Search triggers to associate" value={search} onChange={event => setSearch(event.currentTarget.value)} /><StudioButton onClick={() => { setAfter(undefined); setTerm(search.trim()); }}>Search</StudioButton><StudioButton onClick={onCancel}>Cancel</StudioButton></div>
    {result.state === "loading" ? <Loading /> : result.state === "error" ? <RequestError error={result.error} retry={reload} /> : <>{!result.value.items.length ? <p>No accessible triggers match this search.</p> : <ul>{result.value.items.map(trigger => <li key={trigger.id}><StudioButton disabled={!allowed(trigger.actions, "AddSubscription")} title={actionReason(trigger.actions, "AddSubscription")} onClick={() => onChoose(trigger)}>{trigger.name}</StudioButton> · {label(trigger.effectiveState)}</li>)}</ul>}{result.value.next ? <StudioButton onClick={() => setAfter(result.value.next ?? undefined)}>Next triggers</StudioButton> : null}{after ? <StudioButton onClick={() => setAfter(undefined)}>First triggers</StudioButton> : null}</>}
  </section>;
}

export function WorkflowTriggersPanel({ api, context }: { api: ElsaStudioModuleApi; context: WorkflowDesignerPanelContext }) {
  const scope = useTriggerScope(api);
  return <WorkflowTriggerAssociations key={`${scope}:${context.definition.id}`} api={api} definitionId={context.definition.id} />;
}
function WorkflowTriggerAssociations({ api, definitionId }: { api: ElsaStudioModuleApi; definitionId: string }) {
  const client = useMemo(() => triggerClient(api.backend), [api.backend]);
  const { result, reload } = useTriggerLoad(signal => client.actor(signal), definitionId);
  return result.state === "loading" ? <Loading label="Loading live trigger access" /> : result.state === "error" ? <RequestError error={result.error} retry={reload} /> : <SubscriptionsSection api={api} client={client} actor={result.value} definitionId={definitionId} />;
}
