import { useMemo, useRef, useState } from "react";
import { useStudioRouteLocation, useStudioWorkspaceWhenActive, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { CopyableIdentifier, StudioAlert, StudioButton, StudioCollectionFooter, StudioCollectionToolbar, StudioField, StudioPage, StudioPageHeader, StudioResultState, StudioSearchInput, StudioTableContainer } from "@elsa-workflows/studio-ui";
import { StudioLink } from "../StudioLink";
import { operationId, triggerClient, type TriggerClient } from "./api";
import { serializeTriggerPayload } from "./json";
import { ChangeReview } from "./ChangeReview";
import { ConfigurationForm, type ConfigurationDraft } from "./ConfigurationForm";
import { DeliveriesSection, DeliveryDetails } from "./Deliveries";
import { SubscriptionsSection } from "./Subscriptions";
import { ActionError, Findings, Loading, RequestError, actionReason, allowed, label, triggerPath, triggersPath, useTriggerCompletion, useTriggerLoad, useTriggerScope } from "./shared";
import type { ActorView, TriggerCommand, TriggerTypeDescriptor } from "./types";
import "./styles.css";

export type TriggerRouteView = "list" | "create" | "details" | "delivery";
export function TriggersRoute({ api, view, navigate }: { api: ElsaStudioModuleApi; view: TriggerRouteView; navigate(path: string): void }) {
  const location = useStudioRouteLocation();
  const scope = useTriggerScope(api);
  let id = "";
  try { id = view === "details" || view === "delivery" ? decodeURIComponent(location.pathname.split("/").at(-1) ?? "") : ""; }
  catch { return <StudioPage><RequestError error={new Error("The trigger address is invalid.")} /></StudioPage>; }
  return <ScopedTriggerRoute key={`${scope}:${view}:${id}`} api={api} view={view} id={id} navigate={navigate} />;
}
function ScopedTriggerRoute({ api, view, id, navigate }: { api: ElsaStudioModuleApi; view: TriggerRouteView; id: string; navigate(path: string): void }) {
  const client = useMemo(() => triggerClient(api.backend), [api.backend]);
  const { result, reload } = useTriggerLoad(signal => client.actor(signal), "actor");
  if (result.state !== "loaded") return <StudioPage className="wf-trigger-page"><StudioPageHeader title="Triggers" />{result.state === "loading" ? <Loading label="Loading trigger access" /> : <RequestError error={result.error} retry={reload} />}</StudioPage>;
  if (view === "delivery") return <DeliveryDetails api={api} client={client} deliveryId={id} navigate={navigate} />;
  const actor = result.value;
  if (!actor.canRead && !actor.canManage && !actor.canOperate && !actor.canAuthorize) return <StudioPage><StudioPageHeader title="Triggers" /><StudioResultState title="Trigger access unavailable" description="You do not have permission to read or manage standalone triggers." /></StudioPage>;
  return view === "list" ? <TriggerCollection client={client} actor={actor} navigate={navigate} /> : view === "create" ? <CreateTrigger api={api} client={client} actor={actor} navigate={navigate} /> : <TriggerDetails api={api} client={client} actor={actor} id={id} navigate={navigate} />;
}
function TriggerCollection({ client, actor, navigate }: { client: TriggerClient; actor: ActorView; navigate(path: string): void }) {
  const [search, setSearch] = useState("");
  const [term, setTerm] = useState("");
  const [after, setAfter] = useState<string>();
  const { result, reload } = useTriggerLoad(signal => client.list({ search: term, after }, signal), JSON.stringify([term, after]));
  return <StudioPage className="wf-trigger-page" aria-labelledby="triggers-title"><StudioPageHeader title="Triggers" titleId="triggers-title" description="Manage shared entry points and the workflows they start." actions={<>{actor.canManage ? <StudioButton variant="primary" onClick={() => navigate(`${triggersPath}/new`)}>Create trigger</StudioButton> : null}<StudioButton onClick={reload}>Refresh</StudioButton></>} />
    <form onSubmit={event => { event.preventDefault(); setAfter(undefined); setTerm(search.trim()); }}><StudioCollectionToolbar><StudioSearchInput aria-label="Search triggers" placeholder="Search triggers" value={search} onChange={event => setSearch(event.currentTarget.value)} /><StudioButton type="submit">Search</StudioButton></StudioCollectionToolbar></form>
    {result.state === "loading" ? <Loading /> : result.state === "error" ? <RequestError error={result.error} retry={reload} /> : !result.value.items.length ? <StudioResultState title={term ? "No matching triggers" : "No triggers yet"} description={term ? "Change your search to see other triggers." : "Create a shared HTTP, Event or schedule entry point, then associate its workflows."} actions={term ? <StudioButton onClick={() => { setSearch(""); setTerm(""); setAfter(undefined); }}>Clear search</StudioButton> : actor.canManage ? <StudioButton variant="primary" onClick={() => navigate(`${triggersPath}/new`)}>Create trigger</StudioButton> : undefined} /> : <>
      <StudioTableContainer role="region" aria-label="Triggers table" tabIndex={0}><table className="studio-table"><thead><tr><th scope="col">Name</th><th scope="col">Type</th><th scope="col">State</th><th scope="col">Eligible / total workflows</th><th scope="col">Entry point</th></tr></thead><tbody>{result.value.items.map(trigger => <tr key={trigger.id}><th scope="row"><StudioLink href={triggerPath(trigger.id)} navigateToStudioPath={navigate}>{trigger.name}</StudioLink>{trigger.origin.configurationReadOnly ? <small className="wf-trigger-muted">Source-managed</small> : null}</th><td>{trigger.typeKey}</td><td>{label(trigger.effectiveState)}<Findings items={trigger.findings} /></td><td>{trigger.eligibleSubscriptionCount} / {trigger.subscriptionCount}</td><td>{trigger.http ? <>{trigger.http.proposedEndpoint || "No route"}<small className="wf-trigger-muted">{label(trigger.http.reservationState)}</small></> : trigger.schedule?.nextDueUtc ? <>{trigger.schedule.nextDueUtc}<small className="wf-trigger-muted">{trigger.schedule.timezone || "UTC"}</small></> : "—"}</td></tr>)}</tbody></table></StudioTableContainer>
      <StudioCollectionFooter><span>{result.value.items.length} triggers shown</span>{result.value.next ? <StudioButton onClick={() => setAfter(result.value.next ?? undefined)}>Next triggers</StudioButton> : null}{after ? <StudioButton onClick={() => setAfter(undefined)}>First triggers</StudioButton> : null}</StudioCollectionFooter>
    </>}
  </StudioPage>;
}
function CreateTrigger({ api, client, actor, navigate }: { api: ElsaStudioModuleApi; client: TriggerClient; actor: ActorView; navigate(path: string): void }) {
  const { result, reload } = useTriggerLoad(signal => client.types(signal), "types");
  const [typeKey, setTypeKey] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>();
  const request = useRef<{ fingerprint: string; id: string } | undefined>(undefined);
  const lock = useRef(false);
  const complete = useTriggerCompletion();
  async function create(descriptor: TriggerTypeDescriptor, draft: ConfigurationDraft) {
    if (lock.current) return;
    lock.current = true; setBusy(true); setError(undefined);
    const fingerprint = JSON.stringify({ typeKey: descriptor.typeKey, ...draft });
    if (request.current?.fingerprint !== fingerprint) request.current = { fingerprint, id: operationId() };
    try {
      const saved = await client.create({ ...draft, typeKey: descriptor.typeKey, schemaVersion: descriptor.schemaVersion, requestId: request.current.id });
      complete(() => { setBusy(false); api.notifications?.notify({ level: "success", title: `Created trigger ${saved.name}` }); navigate(triggersPath); });
    } catch (caught) { complete(() => { setError(caught); setBusy(false); }); }
    finally { lock.current = false; }
  }
  const descriptor = result.state === "loaded" ? result.value.items.find(item => `${item.typeKey}:${item.schemaVersion}` === typeKey) : undefined;
  return <StudioPage className="wf-trigger-page"><StudioPageHeader title="Create trigger" description="Configure a live entry point independently of workflow graphs." actions={<StudioButton disabled={busy} onClick={() => navigate(triggersPath)}>Back to triggers</StudioButton>} />
    {!actor.canManage ? <StudioResultState title="Creation is not allowed" description="You do not have permission to create triggers." /> : result.state === "loading" ? <Loading label="Loading trigger types" /> : result.state === "error" ? <RequestError error={result.error} retry={reload} /> : !result.value.items.length ? <StudioResultState title="No trigger types installed" description="Install a source module before creating a trigger." /> : <>
      <StudioField label="Trigger type"><select aria-label="Trigger type" value={typeKey} disabled={busy} onChange={event => { setTypeKey(event.currentTarget.value); setError(undefined); }}><option value="">Choose a trigger type…</option>{result.value.items.map(type => <option key={`${type.typeKey}:${type.schemaVersion}`} value={`${type.typeKey}:${type.schemaVersion}`} disabled={!type.canCreate}>{type.displayName}{type.canCreate ? "" : " · Unavailable"}</option>)}</select></StudioField>
      {descriptor ? <ConfigurationForm key={typeKey} descriptor={descriptor} editors={api.triggerConfigurationEditors?.list()} busy={busy} onCancel={() => navigate(triggersPath)} onSubmit={draft => void create(descriptor, draft)} /> : null}
    </>}{error ? <ActionError error={error} /> : null}
  </StudioPage>;
}
function TriggerDetails({ api, client, actor, id, navigate }: { api: ElsaStudioModuleApi; client: TriggerClient; actor: ActorView; id: string; navigate(path: string): void }) {
  const { result, reload } = useTriggerLoad(async signal => { const [trigger, types] = await Promise.all([client.read(id, signal), client.types(signal)]); return { trigger, types }; }, id);
  const [editing, setEditing] = useState(false);
  const [configurationDraft, setConfigurationDraft] = useState<ConfigurationDraft>();
  const [command, setCommand] = useState<TriggerCommand>();
  const trigger = result.state === "loaded" ? result.value.trigger : undefined;
  const descriptor = result.state === "loaded" ? result.value.types.items.find(item => item.typeKey === trigger?.typeKey && item.schemaVersion === trigger.schemaVersion) : undefined;
  const whenActive = useStudioWorkspaceWhenActive();
  const applied = () => { const deleted = command?.kind === "DeleteTrigger"; setCommand(undefined); setEditing(false); setConfigurationDraft(undefined); if (deleted) whenActive(() => navigate(triggersPath)); else reload(); };
  return <StudioPage className="wf-trigger-page"><StudioPageHeader title={trigger?.name || "Trigger details"} description="Configuration and subscriptions apply to this live shared entry point." actions={<><StudioButton onClick={() => navigate(triggersPath)}>Back to triggers</StudioButton><StudioButton onClick={reload}>Refresh</StudioButton></>} />
    {result.state === "loading" ? <Loading label="Loading trigger details" /> : result.state === "error" ? <RequestError error={result.error} retry={reload} /> : trigger ? command ? <ChangeReview api={api} client={client} trigger={trigger} command={command} onCancel={() => setCommand(undefined)} onApplied={applied} /> : editing && descriptor ? <ConfigurationForm descriptor={descriptor} trigger={trigger} initialDraft={configurationDraft} editors={api.triggerConfigurationEditors?.list()} onCancel={() => { setEditing(false); setConfigurationDraft(undefined); }} onSubmit={draft => { setConfigurationDraft(draft); setCommand({ kind: "SaveConfiguration", ...draft }); }} /> : <>
      <CopyableIdentifier label="Trigger ID" value={trigger.id} /><dl className="wf-trigger-facts"><dt>Type</dt><dd>{descriptor?.displayName || trigger.typeKey}</dd><dt>Operational state</dt><dd>{label(trigger.effectiveState)}</dd><dt>Desired state</dt><dd>{label(trigger.desiredState)}</dd><dt>Workflow eligibility</dt><dd>{trigger.eligibleSubscriptionCount} eligible / {trigger.subscriptionCount} associated</dd><dt>Configuration owner</dt><dd>{trigger.origin.kind === "Provider" ? trigger.origin.sourceId || "Provider" : "Studio"}{trigger.origin.configurationReadOnly ? " · Read-only" : ""}</dd><dt>Revision</dt><dd>{trigger.revision}</dd></dl><Findings items={trigger.findings} />
      {trigger.effectiveState === "EnabledUnavailable" ? <StudioAlert tone="warning">Enabled, with unavailable source or subscriber capabilities. New work cannot be accepted. Review the findings or pause the trigger.</StudioAlert> : null}
      {!descriptor ? <StudioAlert tone="warning">The configuration module for {trigger.typeKey} version {trigger.schemaVersion} is unavailable. Saved configuration is preserved; install a compatible module to edit it.</StudioAlert> : null}
      <section aria-label="Saved trigger configuration"><h3>Configuration</h3><dl className="wf-trigger-facts">{Object.entries(trigger.configuration).map(([key, value]) => { const field = descriptor?.configurationFields.find(item => item.key === key); return <div className="wf-trigger-fact-row" key={key}><dt>{field?.label || key}</dt><dd>{field?.sensitive ? "Protected value / reference" : typeof value === "string" ? value : serializeTriggerPayload(value)}</dd></div>; })}</dl></section>
      {trigger.http ? <section aria-label="HTTP mode and endpoint"><h3>HTTP entry point</h3><p>{trigger.http.mode === "WorkflowResponse" ? "One workflow owns the response" : `Acknowledge and start subscribers · ${trigger.http.admissionPolicy === "AcceptAvailable" ? "Accept available subscribers" : "Require all subscribers"}`}</p><p>{trigger.http.proposedEndpoint || "Route unavailable"} · {label(trigger.http.reservationState)}</p>{trigger.http.reservedEndpoint && trigger.http.reservedEndpoint !== trigger.http.proposedEndpoint ? <p>Reserved endpoint: {trigger.http.reservedEndpoint}</p> : null}<p>{trigger.http.reservationState === "Unreserved" ? "Enable requires an actual endpoint reservation." : trigger.http.reservationState === "Reserved" ? "Pausing retains the endpoint reservation and stops new admissions." : "Reservation authority is unavailable or conflicting; review the findings before enabling."}</p></section> : null}
      {trigger.schedule ? <section aria-label="Schedule"><h3>Schedule</h3><p>{trigger.schedule.isActive === false ? "Paused schedule · saved cursor: " : "Next firing: "}{trigger.schedule.nextDueUtc || "Not scheduled"} · {trigger.schedule.timezone || "UTC"}{trigger.schedule.offset ? ` (${trigger.schedule.offset})` : ""}</p>{trigger.schedule.isOverdue ? <p>An overdue occurrence remains retained until accepted.</p> : null}{trigger.schedule.nextRetryAt ? <p>Recovery eligible after: {trigger.schedule.nextRetryAt}</p> : null}{trigger.schedule.missedPolicy ? <p>After downtime: {label(trigger.schedule.missedPolicy)}</p> : null}{trigger.schedule.pausePolicy ? <p>After pause: {label(trigger.schedule.pausePolicy)}</p> : null}</section> : null}
      <div className="studio-page-actions"><StudioButton disabled={!descriptor || !allowed(trigger.actions, "SaveConfiguration")} title={actionReason(trigger.actions, "SaveConfiguration")} onClick={() => setEditing(true)}>Edit configuration</StudioButton>{([ [trigger.desiredState === "Enabled" ? "PauseTrigger" : "EnableTrigger", trigger.desiredState === "Enabled" ? "Pause new starts" : "Enable trigger"], ["DeleteTrigger", "Delete trigger"] ] as const).map(([kind, title]) => <StudioButton key={kind} disabled={!allowed(trigger.actions, kind)} title={actionReason(trigger.actions, kind)} onClick={() => setCommand({ kind })}>{title}</StudioButton>)}</div>
      <SubscriptionsSection api={api} client={client} actor={actor} trigger={trigger} onChanged={reload} />
      {actor.canReadDeliveries ? <DeliveriesSection client={client} trigger={trigger} /> : <StudioAlert>Delivery evidence requires permission to read trigger deliveries.</StudioAlert>}
    </> : null}
  </StudioPage>;
}
