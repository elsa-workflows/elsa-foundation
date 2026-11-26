import { useEffect, useId, useMemo, useState } from "react";
import { Activity, CalendarClock, Globe, Workflow, Zap } from "lucide-react";
import { useStudioRouteLocation, useStudioWorkspaceWhenActive, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import {
  CopyableIdentifier,
  OverflowMenu,
  StatusChip,
  StudioAlert,
  StudioButton,
  StudioCollectionEmptyState,
  StudioCollectionFooter,
  StudioCollectionToolbar,
  StudioPage,
  StudioPageHeader,
  StudioSearchInput,
  StudioTabPanel,
  StudioTabs,
  StudioTableContainer,
  studioRowInteraction,
  type OverflowMenuItem
} from "@elsa-workflows/studio-ui";
import { StudioLink } from "../StudioLink";
import { triggerClient, type TriggerClient } from "./api";
import { serializeTriggerPayload } from "./json";
import { ChangeReviewDialog } from "./ChangeReview";
import { CreateTriggerDialog } from "./CreateTriggerDialog";
import { DeliveriesSection, DeliveryDetails } from "./Deliveries";
import { EditTriggerDialog } from "./EditTriggerDialog";
import { SubscriptionsSection } from "./Subscriptions";
import { Findings, Loading, RequestError, ScheduleFailure, actionReason, allowed, label, triggerPath, triggersPath, useTriggerLoad, useTriggerScope } from "./shared";
import { reservationStateTone, resolveTriggerTypeSummary, triggerStateTone } from "./triggerTypePresentation";
import type { ActorView, TriggerCommand, TriggerSchedule, TriggerTypeDescriptor, TriggerView } from "./types";
import "./styles.css";

export type TriggerRouteView = "list" | "create" | "details" | "delivery";
export function TriggersRoute({ api, view, navigate }: { api: ElsaStudioModuleApi; view: TriggerRouteView; navigate(path: string): void }) {
  const location = useStudioRouteLocation();
  const scope = useTriggerScope(api);
  let id = "";
  try { id = view === "details" || view === "delivery" ? decodeURIComponent(location.pathname.split("/").at(-1) ?? "") : ""; }
  catch { return <StudioPage><RequestError error={new Error("The trigger address is invalid.")} /></StudioPage>; }
  const initialEdit = view === "details" && location.searchParams.get("edit") === "configuration";
  return <ScopedTriggerRoute key={`${scope}:${view}:${id}`} api={api} view={view} id={id} initialEdit={initialEdit} navigate={navigate} />;
}
function ScopedTriggerRoute({ api, view, id, initialEdit, navigate }: { api: ElsaStudioModuleApi; view: TriggerRouteView; id: string; initialEdit: boolean; navigate(path: string): void }) {
  const client = useMemo(() => triggerClient(api.backend), [api.backend]);
  const { result, reload } = useTriggerLoad(signal => client.actor(signal), "actor");
  if (result.state !== "loaded") return <StudioPage className="wf-trigger-page"><StudioPageHeader title="Triggers" />{result.state === "loading" ? <Loading label="Loading trigger access" /> : <RequestError error={result.error} retry={reload} />}</StudioPage>;
  if (view === "delivery") return <DeliveryDetails api={api} client={client} deliveryId={id} navigate={navigate} />;
  const actor = result.value;
  if (view === "details") return <TriggerDetails api={api} client={client} actor={actor} id={id} initialEdit={initialEdit} navigate={navigate} />;
  return <TriggerCollection api={api} client={client} actor={actor} initialCreateOpen={view === "create"} navigate={navigate} />;
}

function isTriggerRow(value: unknown): value is TriggerView {
  return Boolean(value && typeof value === "object" && "id" in value && "name" in value && "origin" in value);
}

function TriggerCollection({
  api,
  client,
  actor,
  initialCreateOpen = false,
  navigate
}: {
  api: ElsaStudioModuleApi;
  client: TriggerClient;
  actor: ActorView;
  initialCreateOpen?: boolean;
  navigate(path: string): void;
}) {
  const [search, setSearch] = useState("");
  const [term, setTerm] = useState("");
  const [after, setAfter] = useState<string>();
  const [createOpen, setCreateOpen] = useState(initialCreateOpen);
  const [typeDescriptors, setTypeDescriptors] = useState<TriggerTypeDescriptor[]>([]);
  const { result, reload } = useTriggerLoad(signal => client.list({ search: term, after }, signal), JSON.stringify([term, after]));
  const items = useMemo(() => (result.state === "loaded" ? result.value.items.filter(isTriggerRow) : []), [result]);
  const hasItems = items.length > 0;

  // Enrich type badges with backend display names when triggers are present without adding extra calls on empty/error collections.
  useEffect(() => {
    if (!hasItems || typeDescriptors.length > 0) return;
    const controller = new AbortController();
    void client.types(controller.signal)
      .then(page => { if (!controller.signal.aborted) setTypeDescriptors(page.items); })
      .catch(() => { /* Type presentations still resolve cleanly from registered Studio contributions and built-in defaults. */ });
    return () => controller.abort();
  }, [client, hasItems, typeDescriptors.length]);

  const presentations = api.triggerTypePresentations?.list() ?? [];
  const editors = api.triggerConfigurationEditors?.list() ?? [];

  function openCreate() {
    setCreateOpen(true);
  }

  function closeCreate() {
    setCreateOpen(false);
    if (initialCreateOpen) navigate(triggersPath);
  }

  function created(trigger: TriggerView) {
    setCreateOpen(false);
    navigate(triggerPath(trigger.id));
  }

  const showFooter = result.state === "loaded" && (items.length > 0 || Boolean(result.value.next) || Boolean(after));

  return (
    <StudioPage className="wf-trigger-page" data-studio-collection aria-labelledby="triggers-title">
      <StudioPageHeader
        title="Triggers"
        titleId="triggers-title"
        description="Manage shared entry points and the workflows they start."
        refresh={{
          id: "standalone-triggers",
          label: "Refresh",
          onRefresh: reload,
          refreshing: result.state === "loading",
          autoRefresh: true
        }}
        actions={actor.canManage ? <StudioButton variant="primary" onClick={openCreate}>Create trigger</StudioButton> : undefined}
      />
      <StudioCollectionToolbar>
        <form
          className="wf-trigger-search"
          onSubmit={event => {
            event.preventDefault();
            setAfter(undefined);
            setTerm(search.trim());
          }}
        >
          <StudioSearchInput
            aria-label="Search triggers"
            placeholder="Search triggers"
            value={search}
            onChange={event => setSearch(event.currentTarget.value)}
          />
          <StudioButton type="submit">Search</StudioButton>
        </form>
      </StudioCollectionToolbar>
      {result.state === "loading" ? (
        <Loading />
      ) : result.state === "error" ? (
        <RequestError error={result.error} retry={reload} />
      ) : !items.length ? (
        <StudioCollectionEmptyState
          icon={<Zap size={22} aria-hidden />}
          title={result.value.next || after ? "No accessible triggers on this page" : term ? "No matching triggers" : "No triggers yet"}
          description={result.value.next ? "Continue to the next page to find accessible triggers." : term ? "Change your search to see other triggers." : "Create a shared HTTP, Event or schedule entry point, then associate its workflows."}
          actions={term ? (
            <StudioButton onClick={() => { setSearch(""); setTerm(""); setAfter(undefined); }}>Clear search</StudioButton>
          ) : actor.canManage ? (
            <StudioButton variant="primary" onClick={openCreate}>Create trigger</StudioButton>
          ) : undefined}
        />
      ) : (
        <StudioTableContainer role="region" aria-label="Triggers table" tabIndex={0}>
          <table className="studio-table wf-trigger-table">
            <thead>
              <tr>
                <th scope="col">Name</th>
                <th scope="col">Type</th>
                <th scope="col">State</th>
                <th scope="col">Eligible / total workflows</th>
                <th scope="col">Entry point</th>
                <th scope="col" className="wf-trigger-actions-col">Actions</th>
              </tr>
            </thead>
            <tbody>
              {items.map(trigger => {
                const detailHref = triggerPath(trigger.id);
                const typePresentation = resolveTriggerTypeSummary(trigger.typeKey, trigger.schemaVersion, typeDescriptors, presentations, editors);
                const TypeIcon = typePresentation.icon;
                const canEditConfiguration = allowed(trigger.actions, "SaveConfiguration");
                const menuItems: OverflowMenuItem[] = [
                  { id: "open", label: "Manage trigger & subscriptions", onSelect: () => navigate(detailHref) },
                  ...(canEditConfiguration ? [{ id: "edit", label: "Edit configuration", onSelect: () => navigate(`${detailHref}?edit=configuration`) }] : [])
                ];
                return (
                  <tr
                    key={trigger.id}
                    aria-label={`Open trigger ${trigger.name}`}
                    {...studioRowInteraction<HTMLTableRowElement>(() => navigate(detailHref))}
                  >
                    <th scope="row">
                      <div className="wf-trigger-cell-stack-tight">
                        <StudioLink className="studio-row-link wf-trigger-row-title" href={detailHref} navigateToStudioPath={navigate}>
                          {trigger.name}
                        </StudioLink>
                        {trigger.origin.configurationReadOnly ? <small className="wf-trigger-muted">Source-managed</small> : null}
                      </div>
                    </th>
                    <td>
                      <span className="wf-trigger-type-badge" data-accent={typePresentation.accent}>
                        <TypeIcon size={14} aria-hidden />
                        <span>{typePresentation.displayName}</span>
                      </span>
                    </td>
                    <td>
                      <div className="wf-trigger-cell-stack">
                        <StatusChip tone={triggerStateTone(trigger.effectiveState)}>{label(trigger.effectiveState)}</StatusChip>
                        <Findings items={trigger.findings} variant="inline" />
                      </div>
                    </td>
                    <td>{trigger.eligibleSubscriptionCount} / {trigger.subscriptionCount}</td>
                    <td>
                      {trigger.http ? (
                        <div className="wf-trigger-cell-stack-tight">
                          <span>{trigger.http.proposedEndpoint || "No route"}</span>
                          <small className="wf-trigger-muted">{label(trigger.http.reservationState)}</small>
                        </div>
                      ) : trigger.schedule?.nextDueUtc ? (
                        <div className="wf-trigger-cell-stack-tight">
                          <span>{trigger.schedule.nextDueUtc}</span>
                          <small className="wf-trigger-muted">{trigger.schedule.timezone || "UTC"}</small>
                        </div>
                      ) : "—"}
                    </td>
                    <td className="wf-trigger-actions-cell" onClick={event => event.stopPropagation()}>
                      <div className="wf-trigger-row-actions">
                        <StudioButton size="sm" onClick={() => navigate(detailHref)}>Manage</StudioButton>
                        <OverflowMenu items={menuItems} ariaLabel={`Actions for ${trigger.name}`} />
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </StudioTableContainer>
      )}
      {showFooter ? (
        <StudioCollectionFooter>
          <span>{items.length} {items.length === 1 ? "trigger" : "triggers"} shown</span>
          {result.value.next ? <StudioButton onClick={() => setAfter(result.value.next ?? undefined)}>Next triggers</StudioButton> : null}
          {after ? <StudioButton onClick={() => setAfter(undefined)}>First triggers</StudioButton> : null}
        </StudioCollectionFooter>
      ) : null}
      {createOpen ? (
        <CreateTriggerDialog
          api={api}
          client={client}
          actor={actor}
          onClose={closeCreate}
          onCreated={created}
        />
      ) : null}
    </StudioPage>
  );
}

function TriggerDetails({
  api,
  client,
  actor,
  id,
  initialEdit = false,
  navigate
}: {
  api: ElsaStudioModuleApi;
  client: TriggerClient;
  actor: ActorView;
  id: string;
  initialEdit?: boolean;
  navigate(path: string): void;
}) {
  const { result, reload } = useTriggerLoad(async signal => { const [trigger, types] = await Promise.all([client.read(id, signal), client.types(signal)]); return { trigger, types }; }, id);
  const [editing, setEditing] = useState(initialEdit);
  const [command, setCommand] = useState<TriggerCommand>();
  const [activeTab, setActiveTab] = useState<"subscriptions" | "deliveries">("subscriptions");
  const tabsBaseId = useId();

  const trigger = result.state === "loaded" ? result.value.trigger : undefined;
  const descriptor = result.state === "loaded" ? result.value.types.items.find(item => item.typeKey === trigger?.typeKey && item.schemaVersion === trigger.schemaVersion) : undefined;
  const presentations = api.triggerTypePresentations?.list() ?? [];
  const editors = api.triggerConfigurationEditors?.list() ?? [];
  const typePresentation = trigger
    ? resolveTriggerTypeSummary(trigger.typeKey, trigger.schemaVersion, result.state === "loaded" ? result.value.types.items : [], presentations, editors)
    : undefined;
  const TypeIcon = typePresentation?.icon;
  const canEdit = Boolean(descriptor && trigger && allowed(trigger.actions, "SaveConfiguration"));
  const whenActive = useStudioWorkspaceWhenActive();

  const applied = () => {
    const deleted = command?.kind === "DeleteTrigger";
    setCommand(undefined);
    setEditing(false);
    if (deleted) whenActive(() => navigate(triggersPath));
    else reload();
  };

  const lifecycleAction = trigger?.desiredState === "Enabled"
    ? { kind: "PauseTrigger" as const, label: "Pause new starts", variant: "default" as const }
    : { kind: "EnableTrigger" as const, label: "Enable trigger", variant: "primary" as const };
  const configurationEntries = trigger ? Object.entries(trigger.configuration) : [];

  return (
    <StudioPage className="wf-trigger-page">
      <StudioPageHeader
        title={trigger?.name || "Trigger details"}
        description={trigger?.description || "Configuration and subscriptions apply to this live shared entry point."}
        refresh={{
          id: `standalone-trigger-${id}`,
          label: "Refresh",
          onRefresh: reload,
          refreshing: result.state === "loading",
          autoRefresh: true
        }}
        actions={
          <>
            <StudioButton onClick={() => navigate(triggersPath)}>Back to triggers</StudioButton>
            {trigger ? (
              <>
                <StudioButton
                  disabled={!descriptor || !allowed(trigger.actions, "SaveConfiguration")}
                  title={actionReason(trigger.actions, "SaveConfiguration")}
                  onClick={() => setEditing(true)}
                >
                  Edit configuration
                </StudioButton>
                <StudioButton
                  variant={lifecycleAction.variant}
                  disabled={!allowed(trigger.actions, lifecycleAction.kind)}
                  title={actionReason(trigger.actions, lifecycleAction.kind)}
                  onClick={() => setCommand({ kind: lifecycleAction.kind })}
                >
                  {lifecycleAction.label}
                </StudioButton>
                <StudioButton
                  disabled={!allowed(trigger.actions, "DeleteTrigger")}
                  title={actionReason(trigger.actions, "DeleteTrigger")}
                  onClick={() => setCommand({ kind: "DeleteTrigger" })}
                >
                  Delete trigger
                </StudioButton>
              </>
            ) : null}
          </>
        }
      />
      {result.state === "loading" ? (
        <Loading label="Loading trigger details" />
      ) : result.state === "error" ? (
        <RequestError error={result.error} retry={reload} />
      ) : trigger ? (
        <>
          <div className="wf-trigger-detail-meta">
            {typePresentation && TypeIcon ? (
              <span className="wf-trigger-type-badge" data-accent={typePresentation.accent}>
                <TypeIcon size={15} aria-hidden />
                <span>{typePresentation.displayName}</span>
              </span>
            ) : (
              <span className="wf-trigger-type-badge">{descriptor?.displayName || trigger.typeKey}</span>
            )}
            <StatusChip tone={triggerStateTone(trigger.effectiveState)}>{label(trigger.effectiveState)}</StatusChip>
            <CopyableIdentifier label="Trigger ID" value={trigger.id} />
          </div>

          {trigger.effectiveState === "EnabledUnavailable" ? (
            <StudioAlert tone="warning">Enabled, with unavailable source or subscriber capabilities. New work cannot be accepted. Review the findings or pause the trigger.</StudioAlert>
          ) : null}
          {!descriptor ? (
            <StudioAlert tone="warning">The configuration module for {trigger.typeKey} version {trigger.schemaVersion} is unavailable. Saved configuration is preserved; install a compatible module to edit it.</StudioAlert>
          ) : null}

          <div className="wf-trigger-detail-grid">
            <section className="wf-trigger-card" aria-label="Trigger overview">
              <div className="wf-trigger-card-header">
                <h3>Overview</h3>
              </div>
              <dl className="wf-trigger-facts">
                <dt>Type</dt>
                <dd>
                  {typePresentation && TypeIcon ? (
                    <span className="wf-trigger-type-badge" data-accent={typePresentation.accent}>
                      <TypeIcon size={14} aria-hidden />
                      <span>{typePresentation.displayName}</span>
                    </span>
                  ) : descriptor?.displayName || trigger.typeKey}
                </dd>
                <dt>Operational state</dt>
                <dd><StatusChip tone={triggerStateTone(trigger.effectiveState)}>{label(trigger.effectiveState)}</StatusChip></dd>
                <dt>Desired state</dt>
                <dd><StatusChip tone={triggerStateTone(trigger.desiredState)}>{label(trigger.desiredState)}</StatusChip></dd>
                <dt>Workflow eligibility</dt>
                <dd>{trigger.eligibleSubscriptionCount} eligible / {trigger.subscriptionCount} associated</dd>
                <dt>Configuration owner</dt>
                <dd>{trigger.origin.kind === "Provider" ? trigger.origin.sourceId || "Provider" : "Studio"}{trigger.origin.configurationReadOnly ? " · Read-only" : ""}</dd>
                <dt>Revision</dt>
                <dd>{trigger.revision}</dd>
              </dl>
              <Findings items={trigger.findings} />
            </section>

            <div className="wf-trigger-card wf-trigger-config-card">
              <section aria-label="Saved trigger configuration" className="wf-trigger-card-section">
                <div className="wf-trigger-card-header">
                  <h3>Configuration</h3>
                </div>
                {configurationEntries.length > 0 ? (
                  <dl className="wf-trigger-facts">
                    {configurationEntries.map(([key, value]) => {
                      const field = descriptor?.configurationFields.find(item => item.key === key);
                      return (
                        <div className="wf-trigger-fact-row" key={key}>
                          <dt>{field?.label || key}</dt>
                          <dd>{field?.sensitive ? "Protected value / reference" : typeof value === "string" ? value : serializeTriggerPayload(value)}</dd>
                        </div>
                      );
                    })}
                  </dl>
                ) : (
                  <p className="wf-trigger-muted">No saved configuration fields.</p>
                )}
              </section>

              {trigger.http ? (
                <section className="wf-trigger-callout" aria-label="HTTP mode and endpoint">
                  <div className="wf-trigger-callout-header">
                    <span className="wf-trigger-callout-title">
                      <Globe size={15} aria-hidden />
                      <h3>HTTP entry point</h3>
                    </span>
                    <StatusChip tone={reservationStateTone(trigger.http.reservationState)}>
                      {label(trigger.http.reservationState)}
                    </StatusChip>
                  </div>
                  <p className="wf-trigger-callout-endpoint">
                    <code className="wf-trigger-code">{trigger.http.proposedEndpoint || "Route unavailable"}</code>
                    <span className="wf-trigger-muted-inline"> · {label(trigger.http.reservationState)}</span>
                  </p>
                  <p>
                    {trigger.http.mode === "WorkflowResponse"
                      ? "One workflow owns the response"
                      : `Acknowledge and start subscribers · ${trigger.http.admissionPolicy === "AcceptAvailable" ? "Accept available subscribers" : "Require all subscribers"}`}
                  </p>
                  {trigger.http.reservedEndpoint && trigger.http.reservedEndpoint !== trigger.http.proposedEndpoint ? (
                    <p>Reserved endpoint: <code className="wf-trigger-code">{trigger.http.reservedEndpoint}</code></p>
                  ) : null}
                  <p className="wf-trigger-muted">
                    {trigger.http.reservationState === "Unreserved"
                      ? "Enable requires an actual endpoint reservation."
                      : trigger.http.reservationState === "Reserved"
                        ? "Pausing retains the endpoint reservation and stops new admissions."
                        : "Reservation authority is unavailable or conflicting; review the findings before enabling."}
                  </p>
                </section>
              ) : null}

              {trigger.schedule ? <ScheduleSummary schedule={trigger.schedule} /> : null}
            </div>
          </div>

          <div className="wf-trigger-workspace">
            <StudioTabs
              baseId={tabsBaseId}
              ariaLabel="Trigger workspace sections"
              activeTab={activeTab}
              onSelect={tab => setActiveTab(tab as "subscriptions" | "deliveries")}
              tabs={[
                {
                  id: "subscriptions",
                  label: "Subscriptions",
                  icon: <Workflow size={14} />,
                  trailing: <span className="wf-trigger-tab-count">{trigger.subscriptionCount}</span>
                },
                {
                  id: "deliveries",
                  label: "Deliveries",
                  icon: <Activity size={14} />
                }
              ]}
            />
            <StudioTabPanel baseId={tabsBaseId} index={0} hidden={activeTab !== "subscriptions"} className="wf-trigger-tab-panel">
              <SubscriptionsSection api={api} client={client} actor={actor} trigger={trigger} onChanged={reload} />
            </StudioTabPanel>
            <StudioTabPanel baseId={tabsBaseId} index={1} hidden={activeTab !== "deliveries"} className="wf-trigger-tab-panel">
              {activeTab === "deliveries" ? (
                allowed(trigger.actions, "ReadDeliveries")
                  ? <DeliveriesSection client={client} trigger={trigger} navigate={navigate} />
                  : <StudioAlert>Delivery evidence is unavailable for this resource in the current backend or access scope.</StudioAlert>
              ) : null}
            </StudioTabPanel>
          </div>

          {editing && canEdit && descriptor ? (
            <EditTriggerDialog
              api={api}
              client={client}
              trigger={trigger}
              descriptor={descriptor}
              editors={editors}
              typePresentation={typePresentation}
              onClose={() => setEditing(false)}
              onApplied={() => {
                setEditing(false);
                reload();
              }}
            />
          ) : null}

          {command ? (
            <ChangeReviewDialog
              api={api}
              client={client}
              trigger={trigger}
              command={command}
              onCancel={() => setCommand(undefined)}
              onApplied={applied}
            />
          ) : null}
        </>
      ) : null}
    </StudioPage>
  );
}

export function ScheduleSummary({ schedule }: { schedule: TriggerSchedule }) {
  const inactive = schedule.isActive === false;
  return (
    <section className="wf-trigger-callout" aria-label="Schedule">
      <div className="wf-trigger-callout-header">
        <span className="wf-trigger-callout-title">
          <CalendarClock size={15} aria-hidden />
          <h3>Schedule</h3>
        </span>
        <StatusChip tone={inactive ? "neutral" : schedule.isOverdue ? "warning" : "success"}>
          {inactive ? "Inactive" : schedule.isOverdue ? "Overdue" : "Active"}
        </StatusChip>
      </div>
      <p>
        {inactive ? "Saved cursor UTC: " : "Next firing UTC: "}
        {schedule.nextDueUtc || "Not scheduled"} · {schedule.timezone || "UTC"}
        {schedule.offset ? ` (${schedule.offset})` : ""}
      </p>
      {inactive ? (
        <p className="wf-trigger-muted">No firing is scheduled while this schedule is inactive.</p>
      ) : schedule.isOverdue ? (
        <p className="wf-trigger-muted">An overdue occurrence remains retained until accepted.</p>
      ) : null}
      <ScheduleFailure reason={schedule.failureReason} isActive={schedule.isActive} />
      {schedule.nextRetryAt ? (
        <p className="wf-trigger-muted">
          {inactive ? "Last recorded retry time: " : "Recovery eligible after: "}
          {schedule.nextRetryAt}
        </p>
      ) : null}
      {(schedule.missedPolicy || schedule.pausePolicy) ? (
        <div className="wf-trigger-chip-group">
          {schedule.missedPolicy ? <span className="wf-trigger-policy-pill">After downtime: {label(schedule.missedPolicy)}</span> : null}
          {schedule.pausePolicy ? <span className="wf-trigger-policy-pill">After pause: {label(schedule.pausePolicy)}</span> : null}
        </div>
      ) : null}
    </section>
  );
}
