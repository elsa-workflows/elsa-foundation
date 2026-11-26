import { useEffect, useMemo, useRef, useState } from "react";
import { ArrowLeft, Layers, Workflow, Zap } from "lucide-react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import {
  OverflowMenu,
  StatusChip,
  StudioAlert,
  StudioButton,
  StudioCheckboxField,
  StudioCollectionEmptyState,
  StudioCollectionFooter,
  StudioDialog,
  StudioDialogFooter,
  StudioSearchInput,
  StudioStepper,
  StudioTableContainer,
  type OverflowMenuItem
} from "@elsa-workflows/studio-ui";
import { StudioLink } from "../StudioLink";
import { WorkflowIdentity } from "./WorkflowIdentity";
import type { WorkflowDesignerPanelContext } from "../workflow-editor/editorTypes";
import { triggerClient, type TriggerClient } from "./api";
import { ChangeReview, ChangeReviewDialog } from "./ChangeReview";
import { MappingEditor, mappingDrafts, realizeMappings, type MappingDrafts } from "./MappingEditor";
import { Findings, Loading, RequestError, actionReason, allowed, label, triggerPath, useTriggerCompletion, useTriggerLoad, useTriggerScope } from "./shared";
import {
  invocationGrantTone,
  resolveTriggerTypeSummary,
  subscriptionCompatibilityTone,
  targetStateTone,
  triggerStateTone
} from "./triggerTypePresentation";
import type { ActorView, InputMapping, SubscriptionView, TargetSelector, TriggerCommand, TriggerTypeDescriptor, TriggerView } from "./types";
import { WorkflowTargetPicker } from "./WorkflowTargetPicker";
import "./styles.css";

const targetRestricted = (subscription: SubscriptionView) =>
  subscription.findings.some(item => item.code === "TargetDetailsRestricted")
  || !subscription.target.definitionId
  || !subscription.target.slotName;

type SubscriptionStep = "select" | "configure" | "review";

const addWorkflowSteps = [
  { id: "select", label: "Select workflow" },
  { id: "configure", label: "Inputs & access" },
  { id: "review", label: "Review & apply" }
] as const;

const configureSubscriptionSteps = [
  { id: "configure", label: "Inputs & access" },
  { id: "review", label: "Review & apply" }
] as const;

export function SubscriptionEditor({
  api,
  client,
  trigger,
  subscription,
  fixedDefinitionId,
  embedded = false,
  onCancel,
  onSaved
}: {
  api: ElsaStudioModuleApi;
  client: TriggerClient;
  actor: ActorView;
  trigger: TriggerView;
  subscription?: SubscriptionView;
  fixedDefinitionId?: string;
  embedded?: boolean;
  onCancel(): void;
  onSaved(): void;
}) {
  const scope = useTriggerScope(api);
  const canPickTarget = !fixedDefinitionId;
  const startsWithPicker = !subscription && canPickTarget;
  const [step, setStep] = useState<SubscriptionStep>(startsWithPicker ? "select" : "configure");
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

  function continueToConfigure() {
    if (!target.definitionId || !target.slotName) {
      setErrors({ target: "Choose a workflow and publication slot." });
      return;
    }
    setErrors({});
    setStep("configure");
  }

  function review() {
    if (!target.definitionId || !target.slotName) { setErrors({ target: "Choose a workflow and publication slot." }); return; }
    if (result.state !== "loaded" || !result.value) return;
    if (enabled && result.value.target.state !== "Available") { setErrors({ target: "Save this association disabled until its serving target is available." }); return; }
    const built = result.value.target.inputContract
      ? realizeMappings(result.value.target.inputContract.inputs, drafts)
      : { mappings: mappings.current.filter(item => drafts[item.inputName] && drafts[item.inputName].kind !== "Omit"), errors: {} };
    setErrors(built.errors);
    if (Object.keys(built.errors).length) return;
    mappings.current = built.mappings;
    setCommand(subscription
      ? { kind: "SaveSubscription", subscriptionId: subscription.id, expectedSubscriptionRevision: subscription.revision, target, mappings: built.mappings, enabled, invocationRequest: grant ? "Grant" : "Keep" }
      : { kind: "AddSubscription", target, mappings: built.mappings, enabled, invocationRequest: grant ? "Grant" : "None" });
    setStep("review");
  }

  const actualTarget = result.state === "loaded" ? result.value?.target : undefined;
  const readonly = !!subscription?.origin.configurationReadOnly;
  const retainsTarget = !!subscription && target.definitionId === subscription.target.definitionId && target.slotName === subscription.target.slotName;
  const footerClassName = embedded ? "studio-dialog-footer" : "studio-page-actions";
  const steps = startsWithPicker ? addWorkflowSteps : configureSubscriptionSteps;
  const currentStep = step === "select" && !startsWithPicker ? "configure" : step;

  if (subscription && targetRestricted(subscription)) {
    return (
      <section aria-label="Restricted workflow subscription" className="wf-trigger-subscription-editor">
        {!embedded ? <h3>Restricted workflow</h3> : null}
        <StudioAlert>Workflow details and input mappings require workflow read permission. Use the association's available controls to pause or revoke its authority.</StudioAlert>
        <div className={footerClassName}>
          <StudioButton type="button" onClick={onCancel}>Back to associations</StudioButton>
        </div>
      </section>
    );
  }

  return (
    <section className="wf-trigger-subscription-editor" aria-label={subscription ? "Edit live subscription" : "Add live subscription"}>
      {!embedded ? <h3>{subscription ? "Edit subscription" : "Add subscription"}</h3> : null}
      <StudioStepper label="Subscription setup steps" steps={steps} current={currentStep} />

      {step === "review" && command ? (
        <ChangeReview
          api={api}
          client={client}
          trigger={trigger}
          command={command}
          embedded={embedded}
          onCancel={() => { setCommand(undefined); setStep("configure"); }}
          onApplied={onSaved}
        />
      ) : step === "select" ? (
        <>
          <StudioAlert>These associations are live resources. Saving them does not save or publish a workflow draft and does not change its business graph.</StudioAlert>
          {trigger.http ? (
            <div className="wf-trigger-inline-banner">
              <StatusChip tone="accent">HTTP</StatusChip>
              <span>{trigger.http.mode === "WorkflowResponse" ? "One workflow owns the response" : `Acknowledge and start subscribers · ${trigger.http.admissionPolicy === "AcceptAvailable" ? "Accept available subscribers" : "Require all subscribers"}`}</span>
            </div>
          ) : null}
          <WorkflowTargetPicker
            api={api}
            client={client}
            trigger={trigger}
            value={target}
            disabled={readonly}
            onChange={next => {
              setTarget(next);
              setErrors({});
            }}
          />
          {errors.target ? <p role="alert" className="wf-trigger-mapping-error">{errors.target}</p> : null}
          <div className={footerClassName}>
            <StudioButton type="button" onClick={onCancel}>Cancel</StudioButton>
            <StudioButton
              type="button"
              variant="primary"
              disabled={!target.definitionId || !target.slotName}
              onClick={continueToConfigure}
            >
              Continue to inputs &amp; access
            </StudioButton>
          </div>
        </>
      ) : (
        <>
          {!startsWithPicker ? (
            <StudioAlert>These associations are live resources. Saving them does not save or publish a workflow draft and does not change its business graph.</StudioAlert>
          ) : null}
          {trigger.http ? (
            <div className="wf-trigger-inline-banner">
              <StatusChip tone="accent">HTTP</StatusChip>
              <span>{trigger.http.mode === "WorkflowResponse" ? "One workflow owns the response" : `Acknowledge and start subscribers · ${trigger.http.admissionPolicy === "AcceptAvailable" ? "Accept available subscribers" : "Require all subscribers"}`}</span>
            </div>
          ) : null}
          {readonly ? <StudioAlert tone="warning">This association is managed by {subscription?.origin.sourceId || "its source"}. Its configuration is read-only.</StudioAlert> : null}

          <div className="wf-trigger-form wf-trigger-subscription-body">
            {fixedDefinitionId ? (
              <WorkflowTargetPicker
                api={api}
                client={client}
                trigger={trigger}
                value={target}
                fixedDefinitionId={fixedDefinitionId}
                disabled={readonly}
                onChange={next => { setTarget(next); setErrors({}); }}
              />
            ) : (
              <div className="wf-trigger-target-summary">
                <div className="wf-trigger-target-summary-main">
                  <span className="wf-trigger-picker-card-icon" aria-hidden="true">
                    <Workflow size={16} />
                  </span>
                  <div className="wf-trigger-target-summary-info">
                    <WorkflowIdentity api={api} scope={scope} definitionId={target.definitionId} />
                    <div className="wf-trigger-target-summary-badges">
                      <span className="wf-trigger-slot-badge">
                        <Layers size={12} aria-hidden />
                        <span>Slot: {target.slotName}</span>
                      </span>
                      {actualTarget ? (
                        <StatusChip tone={targetStateTone(actualTarget.state)}>
                          {actualTarget.serving?.version || actualTarget.serving?.versionId ? `Serving: ${actualTarget.serving.version || actualTarget.serving.versionId}` : label(actualTarget.state)}
                        </StatusChip>
                      ) : null}
                    </div>
                  </div>
                </div>
                {!readonly ? (
                  <StudioButton type="button" size="sm" onClick={() => setStep("select")}>
                    Change workflow
                  </StudioButton>
                ) : null}
              </div>
            )}

            {errors.target ? <p role="alert" className="wf-trigger-mapping-error">{errors.target}</p> : null}

            {result.state === "loading" && target.definitionId ? (
              <Loading label="Loading serving input contract" />
            ) : result.state === "error" ? (
              <RequestError error={result.error} title="Mapping context unavailable" retry={reload} />
            ) : result.state === "loaded" && result.value ? (
              <>
                <p className="wf-trigger-serving-summary">
                  Serving version: {result.value.target.serving?.version || result.value.target.serving?.versionId || "Not serving"} · Slot: {target.slotName}
                </p>
                {trigger.http?.mode === "WorkflowResponse" && result.value.target.supportsWorkflowOwnedHttpResponse !== true ? (
                  <StudioAlert tone="warning">This target cannot own an HTTP response. Select a compatible target or review a trigger mode change.</StudioAlert>
                ) : null}
                <MappingEditor
                  key={targetKey}
                  client={client}
                  triggerId={trigger.id}
                  triggerRevision={trigger.revision}
                  target={result.value.target}
                  context={result.value}
                  drafts={drafts}
                  onChange={setDrafts}
                  disabled={readonly}
                  errors={errors}
                />
              </>
            ) : null}

            <div className="wf-trigger-access-section">
              <h4>Access &amp; activation</h4>
              <div className="wf-trigger-toggle-cards">
                <div className="wf-trigger-toggle-card" data-disabled={readonly || !allowed(trigger.actions, "AuthorizeSubscriptions") || (retainsTarget && !allowed(subscription!.actions, "GrantInvocation")) || undefined}>
                  <StudioCheckboxField
                    checked={grant}
                    disabled={readonly || !allowed(trigger.actions, "AuthorizeSubscriptions") || (retainsTarget && !allowed(subscription!.actions, "GrantInvocation"))}
                    onChange={setGrant}
                    label="Authorize this shared trigger to invoke the workflow"
                  />
                  <p className="wf-trigger-muted">An authorized manager grants shared-resource authority. It survives the creator's departure and can be revoked explicitly.</p>
                </div>
                <div className="wf-trigger-toggle-card" data-disabled={readonly || !allowed(trigger.actions, "OperateSubscriptions") || undefined}>
                  <StudioCheckboxField
                    checked={enabled}
                    disabled={readonly || !allowed(trigger.actions, "OperateSubscriptions")}
                    onChange={setEnabled}
                    label="Enable this subscription"
                  />
                  {actualTarget?.state !== "Available" ? (
                    <p className="wf-trigger-muted">Save disabled while its serving slot or input contract is unavailable.</p>
                  ) : (
                    <p className="wf-trigger-muted">Enabled subscriptions receive work when the trigger fires and is enabled.</p>
                  )}
                </div>
              </div>
            </div>
          </div>

          <div className={footerClassName}>
            {startsWithPicker ? (
              <StudioButton type="button" onClick={() => setStep("select")}>
                <ArrowLeft size={14} aria-hidden /> Back
              </StudioButton>
            ) : null}
            <StudioButton type="button" onClick={onCancel}>Cancel</StudioButton>
            <StudioButton
              type="button"
              variant="primary"
              disabled={readonly || !allowed(subscription?.actions ?? trigger.actions, subscription ? "SaveSubscription" : "AddSubscription") || result.state !== "loaded" || !result.value}
              onClick={review}
            >
              Review subscription
            </StudioButton>
          </div>
        </>
      )}
    </section>
  );
}

export function SubscriptionDialog({
  api,
  client,
  actor,
  trigger,
  subscription,
  fixedDefinitionId,
  onCancel,
  onSaved
}: {
  api: ElsaStudioModuleApi;
  client: TriggerClient;
  actor: ActorView;
  trigger: TriggerView;
  subscription?: SubscriptionView;
  fixedDefinitionId?: string;
  onCancel(): void;
  onSaved(): void;
}) {
  const restricted = Boolean(subscription && targetRestricted(subscription));
  return (
    <StudioDialog
      open
      title={restricted ? "Restricted workflow" : subscription ? "Edit subscription" : "Add workflow subscription"}
      description={restricted
        ? "Workflow details require read permission."
        : subscription
          ? `Configure input mappings and invocation authority for ${trigger.name}.`
          : `Associate a workflow with ${trigger.name}, map declared inputs, and authorize invocation.`}
      className="wf-trigger-subscription-dialog"
      dismissOnOutsideClick={false}
      onClose={onCancel}
    >
      <SubscriptionEditor
        api={api}
        client={client}
        actor={actor}
        trigger={trigger}
        subscription={subscription}
        fixedDefinitionId={fixedDefinitionId}
        embedded
        onCancel={onCancel}
        onSaved={onSaved}
      />
    </StudioDialog>
  );
}

export function SubscriptionsSection({
  api,
  client,
  actor,
  trigger,
  definitionId,
  onChanged
}: {
  api: ElsaStudioModuleApi;
  client: TriggerClient;
  actor: ActorView;
  trigger?: TriggerView;
  definitionId?: string;
  onChanged?(): void;
}) {
  const scope = useTriggerScope(api);
  const [generation, setGeneration] = useState(0);
  const [after, setAfter] = useState<string>();
  const [editing, setEditing] = useState<{ trigger: TriggerView; subscription?: SubscriptionView }>();
  const [control, setControl] = useState<{ trigger: TriggerView; command: TriggerCommand }>();
  const [error, setError] = useState<unknown>();
  const [adding, setAdding] = useState(false);
  const key = JSON.stringify([trigger?.id, trigger?.revision, definitionId, after, generation]);
  const { result, reload } = useTriggerLoad(signal => client.subscriptions(trigger ? { triggerId: trigger.id, after } : { definitionId, after }, signal), key);
  const opening = useRef(0);
  const complete = useTriggerCompletion();

  function completeOpening(request: number, action: () => void) {
    complete(() => { if (request === opening.current) action(); });
  }
  function closeEditor() {
    opening.current++;
    setEditing(undefined);
    setControl(undefined);
  }
  async function choose(subscription: SubscriptionView, kind?: "RemoveSubscription" | "EnableSubscription" | "PauseSubscription" | "GrantInvocation" | "RevokeInvocation") {
    const request = ++opening.current;
    setError(undefined);
    if (targetRestricted(subscription) && (!kind || kind === "GrantInvocation")) return;
    try {
      const actualTrigger = await client.read(subscription.triggerId);
      completeOpening(request, () => {
        if (kind) {
          setControl({
            trigger: actualTrigger,
            command: kind === "GrantInvocation" || kind === "RevokeInvocation"
              ? { kind, subscriptionId: subscription.id, expectedSubscriptionRevision: subscription.revision, expectedGrantRevision: subscription.invocationGrant.revision ?? null }
              : { kind, subscriptionId: subscription.id, expectedSubscriptionRevision: subscription.revision }
          });
        } else {
          setEditing({ trigger: actualTrigger, subscription });
        }
      });
    } catch (caught) {
      completeOpening(request, () => setError(caught));
    }
  }
  const saved = () => {
    closeEditor();
    setAdding(false);
    setGeneration(value => value + 1);
    onChanged?.();
  };
  async function add() {
    const request = ++opening.current;
    if (!trigger) { setAdding(true); return; }
    try {
      const actual = await client.read(trigger.id);
      completeOpening(request, () => setEditing({ trigger: actual }));
    } catch (caught) {
      completeOpening(request, () => setError(caught));
    }
  }

  const canAdd = !trigger || allowed(trigger.actions, "AddSubscription");
  const isWorkflowDirection = Boolean(definitionId);

  function buildOverflowItems(subscription: SubscriptionView, restricted: boolean): OverflowMenuItem[] {
    const toggleAction = subscription.enabled ? "PauseSubscription" : "EnableSubscription";
    const toggleLabel = subscription.enabled ? "Pause" : "Enable";
    const grantAction = subscription.invocationGrant.state === "Granted" ? "RevokeInvocation" : "GrantInvocation";
    const grantLabel = subscription.invocationGrant.state === "Granted" ? "Revoke authority" : "Authorize";

    const items: OverflowMenuItem[] = [
      {
        id: toggleAction,
        label: toggleLabel,
        disabled: !allowed(subscription.actions, toggleAction),
        title: actionReason(subscription.actions, toggleAction),
        onSelect: () => void choose(subscription, toggleAction)
      }
    ];
    if (!restricted || grantAction === "RevokeInvocation") {
      items.push({
        id: grantAction,
        label: grantLabel,
        disabled: !allowed(subscription.actions, grantAction),
        title: actionReason(subscription.actions, grantAction),
        onSelect: () => void choose(subscription, grantAction)
      });
    }
    items.push({
      id: "RemoveSubscription",
      label: "Remove",
      destructive: true,
      disabled: !allowed(subscription.actions, "RemoveSubscription"),
      title: actionReason(subscription.actions, "RemoveSubscription"),
      onSelect: () => void choose(subscription, "RemoveSubscription")
    });
    return items;
  }

  return (
    <section className="wf-trigger-subscriptions" aria-label="Live workflow subscriptions">
      <div className="wf-trigger-section-heading">
        <h3>{isWorkflowDirection ? "Live triggers" : "Workflow subscriptions"}</h3>
        <div className="studio-page-actions">
          {canAdd ? (
            <StudioButton
              variant="primary"
              size="sm"
              disabled={!!trigger && !allowed(trigger.actions, "AddSubscription")}
              title={trigger ? actionReason(trigger.actions, "AddSubscription") : undefined}
              onClick={() => void add()}
            >
              Add {isWorkflowDirection ? "trigger" : "workflow"}
            </StudioButton>
          ) : null}
          <StudioButton size="sm" onClick={() => { opening.current++; setError(undefined); reload(); }}>
            Refresh associations
          </StudioButton>
        </div>
      </div>

      {isWorkflowDirection ? (
        <StudioAlert>These entry points target the logical workflow's current serving slots. Changes take effect independently of this draft or historical graph.</StudioAlert>
      ) : null}
      {error ? <RequestError error={error} title="Could not open the association" retry={() => setError(undefined)} /> : null}

      {result.state === "loading" ? (
        <Loading label="Loading live trigger associations" />
      ) : result.state === "error" ? (
        <RequestError error={result.error} title="Could not load live associations" retry={reload} />
      ) : !result.value.items.length ? (
        <StudioCollectionEmptyState
          headingLevel={3}
          icon={isWorkflowDirection ? <Zap size={22} aria-hidden /> : <Workflow size={22} aria-hidden />}
          title="No live associations"
          description={isWorkflowDirection
            ? "This workflow has no standalone trigger subscriptions in the selected scope."
            : "Associate a workflow, map its declared inputs and authorize its invocation."}
          actions={canAdd ? (
            <StudioButton variant="primary" onClick={() => void add()}>
              Add {isWorkflowDirection ? "trigger" : "workflow"}
            </StudioButton>
          ) : undefined}
        />
      ) : isWorkflowDirection ? (
        <StudioTableContainer role="region" aria-label="Workflow subscriptions" tabIndex={0}>
          <table className="studio-table wf-trigger-table wf-trigger-association-table">
            <thead>
              <tr>
                <th scope="col">Trigger</th>
                <th scope="col">Status &amp; actions</th>
              </tr>
            </thead>
            <tbody>
              {result.value.items.map(subscription => {
                const restricted = targetRestricted(subscription);
                const triggerLabel = subscription.triggerSummary?.name || subscription.triggerId;
                return (
                  <tr key={subscription.id}>
                    <th scope="row">
                      <StudioLink className="studio-row-link wf-trigger-row-title" href={triggerPath(subscription.triggerId)}>
                        {triggerLabel}
                      </StudioLink>
                      {!restricted ? (
                        <>
                          <small className="wf-trigger-muted">Publication slot: {subscription.target.slotName}</small>
                          <small className="wf-trigger-muted">{subscription.serving?.version || subscription.serving?.versionId || "Not serving"}</small>
                        </>
                      ) : null}
                      {subscription.triggerSummary?.http ? (
                        <small className="wf-trigger-muted">
                          {subscription.triggerSummary.http.mode === "WorkflowResponse"
                            ? "Workflow-owned response"
                            : `Async · ${subscription.triggerSummary.http.admissionPolicy === "AcceptAvailable" ? "Accept available" : "Require all"}`}
                        </small>
                      ) : null}
                    </th>
                    <td>
                      <div className="wf-trigger-cell-stack">
                        <div className="wf-trigger-chip-group">
                          <StatusChip tone={subscription.enabled ? "success" : "neutral"}>
                            {subscription.enabled ? "Enabled" : "Paused"}
                          </StatusChip>
                          <StatusChip tone={restricted ? "warning" : subscriptionCompatibilityTone(subscription.compatibility)}>
                            {restricted ? "Workflow details restricted" : label(subscription.compatibility)}
                          </StatusChip>
                          <StatusChip tone={invocationGrantTone(subscription.invocationGrant.state)}>
                            Invocation: {label(subscription.invocationGrant.state)}
                          </StatusChip>
                        </div>
                        <Findings items={subscription.findings} variant="inline" />
                        {subscription.origin.configurationReadOnly ? (
                          <small className="wf-trigger-muted">Managed by {subscription.origin.sourceId || "source"}</small>
                        ) : null}
                        <div className="wf-trigger-row-actions wf-trigger-inspector-actions">
                          {!restricted ? (
                            <StudioButton
                              size="sm"
                              disabled={!allowed(subscription.actions, "SaveSubscription")}
                              title={actionReason(subscription.actions, "SaveSubscription")}
                              onClick={() => void choose(subscription)}
                            >
                              Edit
                            </StudioButton>
                          ) : null}
                          <OverflowMenu
                            items={buildOverflowItems(subscription, restricted)}
                            ariaLabel={`Actions for ${triggerLabel}`}
                          />
                        </div>
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </StudioTableContainer>
      ) : (
        <StudioTableContainer role="region" aria-label="Workflow subscriptions" tabIndex={0}>
          <table className="studio-table wf-trigger-table wf-trigger-subscriptions-table">
            <thead>
              <tr>
                <th scope="col">Workflow &amp; slot</th>
                <th scope="col">Status</th>
                <th scope="col">Authority</th>
                <th scope="col" className="wf-trigger-actions-col">Actions</th>
              </tr>
            </thead>
            <tbody>
              {result.value.items.map(subscription => {
                const restricted = targetRestricted(subscription);
                const rowTargetLabel = restricted ? "Restricted workflow" : subscription.target.definitionId;
                return (
                  <tr key={subscription.id}>
                    <th scope="row">
                      {restricted ? (
                        <span className="wf-trigger-row-title">Restricted workflow</span>
                      ) : (
                        <>
                          <WorkflowIdentity api={api} scope={scope} definitionId={subscription.target.definitionId} />
                          <div className="wf-trigger-sub-meta">
                            <span className="wf-trigger-slot-badge">
                              <Layers size={12} aria-hidden />
                              <span>Publication slot: {subscription.target.slotName}</span>
                            </span>
                            <small className="wf-trigger-muted">
                              {subscription.serving?.version || subscription.serving?.versionId || "Not serving"}
                            </small>
                          </div>
                        </>
                      )}
                    </th>
                    <td>
                      <div className="wf-trigger-cell-stack">
                        <div className="wf-trigger-chip-group">
                          <StatusChip tone={subscription.enabled ? "success" : "neutral"}>
                            {subscription.enabled ? "Enabled" : "Paused"}
                          </StatusChip>
                          <StatusChip tone={restricted ? "warning" : subscriptionCompatibilityTone(subscription.compatibility)}>
                            {restricted ? "Workflow details restricted" : label(subscription.compatibility)}
                          </StatusChip>
                        </div>
                        <Findings items={subscription.findings} variant="inline" />
                        {subscription.origin.configurationReadOnly ? (
                          <small className="wf-trigger-muted">Managed by {subscription.origin.sourceId || "source"}</small>
                        ) : null}
                      </div>
                    </td>
                    <td>
                      <StatusChip tone={invocationGrantTone(subscription.invocationGrant.state)}>
                        Invocation: {label(subscription.invocationGrant.state)}
                      </StatusChip>
                    </td>
                    <td className="wf-trigger-actions-cell">
                      <div className="wf-trigger-row-actions">
                        {!restricted ? (
                          <StudioButton
                            size="sm"
                            disabled={!allowed(subscription.actions, "SaveSubscription")}
                            title={actionReason(subscription.actions, "SaveSubscription")}
                            onClick={() => void choose(subscription)}
                          >
                            Edit
                          </StudioButton>
                        ) : null}
                        <OverflowMenu
                          items={buildOverflowItems(subscription, restricted)}
                          ariaLabel={`Actions for ${rowTargetLabel}`}
                        />
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </StudioTableContainer>
      )}

      {result.state === "loaded" && (result.value.items.length > 0 || Boolean(result.value.next) || Boolean(after)) ? (
        <StudioCollectionFooter>
          <span>{result.value.items.length} {result.value.items.length === 1 ? "association" : "associations"} shown</span>
          {result.value.next ? <StudioButton onClick={() => setAfter(result.value.next ?? undefined)}>Next associations</StudioButton> : null}
          {after ? <StudioButton onClick={() => setAfter(undefined)}>First associations</StudioButton> : null}
        </StudioCollectionFooter>
      ) : null}

      {adding ? (
        <TriggerPickerDialog
          api={api}
          client={client}
          onCancel={() => { opening.current++; setAdding(false); }}
          onChoose={selected => {
            opening.current++;
            setAdding(false);
            setEditing({ trigger: selected });
          }}
        />
      ) : null}

      {editing ? (
        <SubscriptionDialog
          key={`${editing.trigger.id}:${editing.subscription?.id ?? "new"}`}
          api={api}
          client={client}
          actor={actor}
          trigger={editing.trigger}
          subscription={editing.subscription}
          fixedDefinitionId={definitionId}
          onCancel={closeEditor}
          onSaved={saved}
        />
      ) : null}

      {control ? (
        <ChangeReviewDialog
          key={opening.current}
          api={api}
          client={client}
          trigger={control.trigger}
          command={control.command}
          onCancel={closeEditor}
          onApplied={saved}
        />
      ) : null}
    </section>
  );
}

function TriggerPickerDialog({
  api,
  client,
  onChoose,
  onCancel
}: {
  api: ElsaStudioModuleApi;
  client: TriggerClient;
  onChoose(trigger: TriggerView): void;
  onCancel(): void;
}) {
  const [search, setSearch] = useState("");
  const [term, setTerm] = useState("");
  const [after, setAfter] = useState<string>();
  const [typeDescriptors, setTypeDescriptors] = useState<TriggerTypeDescriptor[]>([]);
  const { result, reload } = useTriggerLoad(signal => client.list({ search: term, after }, signal), JSON.stringify([term, after]));
  const items = useMemo(() => (result.state === "loaded" ? result.value.items : []), [result]);

  useEffect(() => {
    if (!items.length || typeDescriptors.length > 0 || typeof client.types !== "function") return;
    const controller = new AbortController();
    void client.types(controller.signal)
      .then(page => { if (!controller.signal.aborted) setTypeDescriptors(page.items); })
      .catch(() => { /* Type presentations resolve from built-in defaults if types endpoint is unavailable. */ });
    return () => controller.abort();
  }, [client, items.length, typeDescriptors.length]);

  const presentations = api.triggerTypePresentations?.list() ?? [];
  const editors = api.triggerConfigurationEditors?.list() ?? [];

  return (
    <StudioDialog
      open
      title="Choose a trigger"
      description="Select a standalone trigger to associate with this workflow."
      className="wf-trigger-picker-dialog"
      dismissOnOutsideClick={false}
      onClose={onCancel}
      footer={
        <StudioDialogFooter
          onCancel={onCancel}
          cancelLabel="Cancel"
        />
      }
    >
      <section className="wf-trigger-picker-dialog-body" aria-label="Choose standalone trigger">
        <form
          className="wf-trigger-picker-search"
          onSubmit={event => {
            event.preventDefault();
            setAfter(undefined);
            setTerm(search.trim());
          }}
        >
          <StudioSearchInput
            aria-label="Search triggers to associate"
            placeholder="Search triggers…"
            value={search}
            onChange={event => setSearch(event.currentTarget.value)}
          />
          <StudioButton type="submit">Search</StudioButton>
        </form>
        {result.state === "loading" ? (
          <Loading label="Loading triggers" />
        ) : result.state === "error" ? (
          <RequestError error={result.error} retry={reload} />
        ) : !items.length ? (
          <div className="wf-trigger-picker-empty" role="status">
            <p>No accessible triggers match this search.</p>
          </div>
        ) : (
          <div className="wf-trigger-picker-list">
            {items.map(trigger => {
              const typePresentation = resolveTriggerTypeSummary(trigger.typeKey, trigger.schemaVersion, typeDescriptors, presentations, editors);
              const TypeIcon = typePresentation.icon;
              const canAttach = allowed(trigger.actions, "AddSubscription");
              return (
                <button
                  key={trigger.id}
                  type="button"
                  className="wf-trigger-picker-card"
                  data-accent={typePresentation.accent}
                  disabled={!canAttach}
                  title={actionReason(trigger.actions, "AddSubscription")}
                  onClick={() => onChoose(trigger)}
                >
                  <span className="wf-trigger-type-card-icon" aria-hidden="true">
                    <TypeIcon size={16} />
                  </span>
                  <span className="wf-trigger-picker-card-body">
                    <span className="wf-trigger-picker-card-title">{trigger.name}</span>
                    <span className="wf-trigger-picker-card-meta">
                      <span>{typePresentation.displayName}</span>
                      {trigger.http?.proposedEndpoint ? <span>· {trigger.http.proposedEndpoint}</span> : null}
                      {trigger.schedule?.timezone ? <span>· {trigger.schedule.timezone}</span> : null}
                    </span>
                  </span>
                  <span className="wf-trigger-picker-card-badges">
                    <StatusChip tone={triggerStateTone(trigger.effectiveState)}>{label(trigger.effectiveState)}</StatusChip>
                  </span>
                </button>
              );
            })}
          </div>
        )}
        {result.state === "loaded" && (result.value.next || after) ? (
          <div className="wf-trigger-picker-pagination">
            {after ? <StudioButton type="button" size="sm" onClick={() => setAfter(undefined)}>First triggers</StudioButton> : null}
            {result.value.next ? <StudioButton type="button" size="sm" onClick={() => setAfter(result.value.next ?? undefined)}>Next triggers</StudioButton> : null}
          </div>
        ) : null}
      </section>
    </StudioDialog>
  );
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
