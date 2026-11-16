import { useEffect, useMemo, useRef, useState } from "react";
import { StudioHttpError, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { StudioButton, StudioField } from "@elsa-workflows/studio-ui";
import { createActivityDefinition, getActivityDefinitionVersion } from "../api/activityDesign";
import type { ActivityAuthoringCapabilities, CreateActivityDefinitionResponse } from "../activityDefinitionTypes";
import type { ActivityCatalogItem, ActivityNode } from "../workflowTypes";
import { getChildSlots } from "../workflowAdapter";
import { ActivityDefinitionPublicationReview } from "../ActivityDefinitionPublicationReview";
import { describeWorkflowError } from "../workflow-editor/editorHelpers";
import { bindBlockPromotionVersion, planBlockPromotion } from "./blockPromotion";
import type { BlockPromotionContext, BlockPromotionPlan, ReadyBlockPromotion } from "./blockPromotionTypes";
import { reviewPromotionTarget } from "./blockPromotionTarget";
import type { GraphAuthoringResourceKind } from "./graphDocumentAdapter";
import type { NativeBlockContext, NativeBlockDocument } from "./nativeBlocks";
import { nativeBlockEditorDocument, nativeBlockPlannerContext } from "./nativeBlockEditorContext";
import type { PromotionExpressionSupport } from "./promotionExpressionSupport";
import { NativeBlockDialog, type NativeBlockValidation } from "./NativeBlockCommands";

/** What the owning resource editor contributes; it keeps authority, validation, persistence and history. */
export interface BlockPromotionHost {
  resourceKind: GraphAuthoringResourceKind;
  publicInputs: BlockPromotionContext["publicInputs"];
  capabilities: ActivityAuthoringCapabilities | null;
  capabilitiesState: "loading" | "ready" | "failed";
  expressionBoundary: PromotionExpressionSupport;
  /** Reloads the activity catalog only; the draft and its history stay as they are. */
  refreshCatalog(): Promise<void>;
  validate(document: NativeBlockDocument, referenceContext: unknown[]): Promise<NativeBlockValidation>;
  apply(document: NativeBlockDocument, referenceContext: unknown[], selection: string[], isCurrent: () => boolean): void | Promise<void>;
}

export interface BlockPromotionDialogProps {
  endpoint: StudioEndpointContext;
  context: NativeBlockContext;
  createPosition: { x: number; y: number };
  host: BlockPromotionHost;
  blockNodeId: string;
  onClose(): void;
  onReplaced(message: string, versionId: string): void;
}

type Target = "new" | "existing";
const reusableCategory = "Reusable activities";
const catalogWaitMs = 10_000;
// A create that has not answered by then is treated as unknown, so the dialog never stays locked indefinitely.
const createTimeoutMs = 30_000;

export function promotionPlannerContext(context: NativeBlockContext, createPosition: { x: number; y: number }, host: BlockPromotionHost): BlockPromotionContext | null {
  if (!host.capabilities) return null;
  return { ...nativeBlockPlannerContext(context, createPosition), resourceKind: host.resourceKind, publicInputs: host.publicInputs,
    capabilities: host.capabilities, expressionBoundary: host.expressionBoundary };
}

/** The reviewed proposal itself, without the source fingerprint that a catalog refresh or autosave changes. */
function material(plan: ReadyBlockPromotion) {
  return JSON.stringify([plan.blockNodeId, plan.name, plan.candidate, plan.callSite, plan.rewrittenSource, plan.referenceContext, plan.inputs, plan.outputs]);
}

function reusableVersions(catalog: ActivityCatalogItem[]) {
  return catalog.filter(item => item.available !== false && (item.activityDefinitionVersionId || item.provenance?.sourceKind === "ActivityDefinitionDraft"))
    .sort((left, right) => left.displayName.localeCompare(right.displayName) || left.version.localeCompare(right.version));
}

export function BlockPromotionDialog(props: BlockPromotionDialogProps) {
  const current = useRef(props);
  current.current = props;
  const initialName = props.context.document.activityPresentation.find(item => item.nodeId === props.blockNodeId)?.displayName ?? "";
  const [name, setName] = useState(initialName);
  const [category, setCategory] = useState(reusableCategory);
  const [description, setDescription] = useState("");
  const [target, setTarget] = useState<Target>("new");
  const [existingVersionId, setExistingVersionId] = useState("");
  const [reviewed, setReviewed] = useState<ReadyBlockPromotion | null>(null);
  const [created, setCreated] = useState<CreateActivityDefinitionResponse | null>(null);
  const [createUnknown, setCreateUnknown] = useState(false);
  const [published, setPublished] = useState<{ versionId: string; version: string } | null>(null);
  const publishedVersionId = published?.versionId ?? null;
  const [publicationPending, setPublicationPending] = useState(false);
  const [busy, setBusy] = useState<"creating" | "replacing" | null>(null);
  const [messages, setMessages] = useState<string[]>([]);
  const request = useRef(0);
  const catalogWaiters = useRef<Array<() => void>>([]);
  useEffect(() => { for (const resolve of catalogWaiters.current.splice(0)) resolve(); });
  useEffect(() => () => { request.current++; }, []);

  const plannerContext = promotionPlannerContext(props.context, props.createPosition, props.host);
  const plan: BlockPromotionPlan | null = useMemo(
    () => plannerContext ? planBlockPromotion(plannerContext, props.blockNodeId, name) : null,
    // The planner context is rebuilt from these inputs on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [props.context, props.createPosition, props.host.capabilities, props.host.publicInputs, props.host.expressionBoundary, props.host.resourceKind, props.blockNodeId, name]);
  const ready = plan?.status === "ready" ? plan : null;
  const changedSinceReview = Boolean(reviewed && ready && material(ready) !== material(reviewed));
  const versions = reusableVersions(props.context.catalog);
  // A create in flight may still succeed on the server, so the dialog stays until its outcome is known.
  const committing = busy === "replacing" || busy === "creating";
  const close = () => { if (committing || publicationPending) return; request.current++; props.onClose(); };

  const freshPlan = () => {
    const latest = current.current;
    const context = promotionPlannerContext(latest.context, latest.createPosition, latest.host);
    return context ? { context, plan: planBlockPromotion(context, latest.blockNodeId, reviewed?.name ?? name) } : null;
  };

  const create = async () => {
    if (!ready || busy || created || createUnknown) return;
    const id = ++request.current;
    setBusy("creating"); setMessages([]);
    try {
      const response = await createActivityDefinition(current.current.endpoint, {
        category: category.trim() || reusableCategory, displayName: ready.name, description: description.trim() || null,
        provider: ready.candidate.provider, contract: ready.candidate.contract, layout: ready.candidate.layout,
        activityPresentation: ready.candidate.activityPresentation
      }, AbortSignal.timeout(createTimeoutMs));
      if (id !== request.current) return;
      setReviewed(ready); setCreated(response);
    } catch (error) {
      if (id !== request.current) return;
      if (error instanceof StudioHttpError) {
        const failure = describeWorkflowError(error, "The reusable activity could not be created.");
        setMessages(["The reusable activity was not created. Your document has not changed.", failure.message, failure.detail].filter((item): item is string => Boolean(item)));
      } else {
        // No server answer: the definition may or may not exist. Creating again could make a duplicate.
        setCreateUnknown(true);
        setMessages(["Studio could not confirm whether the reusable activity was created. Your document has not changed.",
          "Check Activity Definitions before creating it again. If it was created and published, choose that version under “Use a published version”."]);
      }
    } finally { if (id === request.current) setBusy(null); }
  };

  const waitForCatalog = (versionId: string) => new Promise<boolean>(resolve => {
    const deadline = Date.now() + catalogWaitMs;
    const check = () => {
      if (current.current.context.catalog.some(item => item.activityVersionId === versionId && item.available !== false)) resolve(true);
      else if (Date.now() > deadline) resolve(false);
      else { catalogWaiters.current.push(check); setTimeout(() => { const index = catalogWaiters.current.indexOf(check); if (index >= 0) { catalogWaiters.current.splice(index, 1); check(); } }, 250); }
    };
    check();
  });

  const replace = async (versionId: string) => {
    if (busy || !versionId) return;
    const baseline = reviewed ?? ready;
    if (!baseline) return;
    const id = ++request.current;
    setBusy("replacing"); setMessages([]);
    const stop = (lines: string[]) => { if (id === request.current) setMessages(lines); };
    try {
      await current.current.host.refreshCatalog();
      if (id !== request.current) return;
      if (!await waitForCatalog(versionId)) { stop(["This published version is not in the activity catalog for this editor. Check that it is available here, then try again."]); return; }
      const version = await getActivityDefinitionVersion(current.current.endpoint, versionId);
      if (id !== request.current) return;
      const fresh = freshPlan();
      if (!fresh || fresh.plan.status !== "ready") { stop(fresh?.plan.status === "refused" ? fresh.plan.diagnostics.map(item => item.message) : ["Authoring capabilities are unavailable. Reload the editor and try again."]); return; }
      const reviewedPlan = fresh.plan;
      if (material(reviewedPlan) !== material(baseline)) { setReviewed(null); stop(["The block or its surroundings changed after you reviewed it. Review the updated proposal before replacing it."]); return; }
      const targetReview = reviewPromotionTarget(reviewedPlan, versionId, version);
      if (targetReview.status === "refused") { stop(["The block was not replaced.", ...targetReview.reasons]); return; }
      const bound = bindBlockPromotionVersion(fresh.context, reviewedPlan, versionId);
      if (bound.status !== "ready") { stop(bound.diagnostics.map(item => item.message)); return; }
      const document = nativeBlockEditorDocument(bound.document, current.current.context.catalog);
      const validation = await current.current.host.validate(document, bound.referenceContext);
      if (id !== request.current) return;
      if (validation.state !== "validated") { stop(["The block was not replaced.", ...(validation.messages.length ? validation.messages : ["The owning resource did not accept the replacement."])]); return; }
      const stillCurrent = () => {
        const latest = freshPlan();
        return request.current === id && latest?.plan.status === "ready" && material(latest.plan) === material(reviewedPlan);
      };
      if (!stillCurrent()) { stop(["The document changed while Studio checked the replacement. Nothing was changed; try again."]); return; }
      await current.current.host.apply(document, bound.referenceContext, bound.selection, stillCurrent);
      if (id !== request.current) return;
      const label = version.definition.displayName || baseline.name;
      current.current.onReplaced(`Replaced “${baseline.name}” with ${label} ${version.version}. Undo restores the block; the reusable activity stays published.`, versionId);
    } catch (error) {
      const failure = describeWorkflowError(error, "The replacement could not be completed.");
      stop(["The block was not replaced. Your document has not changed.", failure.message, failure.detail].filter((item): item is string => Boolean(item)));
    } finally { if (id === request.current) setBusy(null); }
  };

  const unavailable = props.host.capabilitiesState === "loading" ? "Loading authoring capabilities…"
    : !plannerContext ? "Reusable activity authoring is unavailable on this server." : null;
  const draftLabel = created ? `${created.definition.displayName ?? reviewed?.name ?? name}` : null;
  return <NativeBlockDialog title="Promote to reusable activity" onClose={close}>
    <h2>Promote to reusable activity</h2>
    <p>Studio proposes a reusable activity built from this block and a call that replaces it. Nothing changes in this document until you replace the block with an exact published version.</p>
    {unavailable ? <p role="status">{unavailable}</p> : <>
      <fieldset className="wf-block-promotion-target" disabled={Boolean(busy) || Boolean(created)}>
        <legend>Reusable activity</legend>
        <label><input type="radio" name="promotion-target" checked={target === "new"} onChange={() => { setTarget("new"); setMessages([]); }} /> Create a new reusable activity</label>
        <label><input type="radio" name="promotion-target" checked={target === "existing"} onChange={() => { setTarget("existing"); setMessages([]); }} /> Use a published version</label>
      </fieldset>
      <StudioField label="Name"><input value={name} maxLength={200} disabled={Boolean(busy) || Boolean(created)} onChange={event => { setName(event.target.value); setMessages([]); }} /></StudioField>
      {target === "new" && !created ? <>
        <StudioField label="Category"><input value={category} maxLength={200} disabled={Boolean(busy)} onChange={event => setCategory(event.target.value)} /></StudioField>
        <StudioField label="Summary"><textarea value={description} maxLength={2000} disabled={Boolean(busy)} onChange={event => setDescription(event.target.value)} /></StudioField>
      </> : null}
      {target === "existing" ? <StudioField label="Published version">
        <select value={existingVersionId} disabled={Boolean(busy)} onChange={event => { setExistingVersionId(event.target.value); setMessages([]); }}>
          <option value="">Choose a published version</option>
          {versions.map(item => <option key={item.activityVersionId} value={item.activityVersionId}>{item.displayName} · {item.version}</option>)}
        </select>
      </StudioField> : null}
      {plan?.status === "refused" ? <div role="alert"><p>This block cannot become a reusable activity:</p><ul>{plan.diagnostics.map((item, index) => <li key={index}>{item.message}</li>)}</ul></div> : null}
      {ready ? <PromotionProposal plan={reviewed ?? ready} context={props.context} host={props.host} /> : null}
      {changedSinceReview ? <p role="alert">The block changed after you created the reusable activity. Replacement checks the block again and refuses if it no longer matches.</p> : null}
      {created ? <div className="wf-block-preview" role="status">
        <h3>Draft created</h3>
        <p><strong>{draftLabel}</strong> is a new reusable activity draft in {category.trim() || reusableCategory}. Publish it to use it here. Closing this dialog keeps the draft; your document has not changed.</p>
      </div> : null}
      {created && !publishedVersionId ? <ActivityDefinitionPublicationReview
        context={props.endpoint}
        definitionId={created.definition.definitionId}
        draftId={created.draft.draftId}
        currentRevision={created.draft.revision}
        currentSignature="block-promotion"
        disabled={Boolean(busy)}
        flushExactSavedRevision={async () => ({ revision: created.draft.revision, signature: "block-promotion" })}
        onPublicationPending={setPublicationPending}
        titleLevel="h3"
        onPublished={outcome => setPublished({ versionId: outcome.definitionVersionId, version: outcome.version })}
        onFocusDiagnostic={async () => ({ kind: "unsupported", announcement: "Open the draft in Activity Definitions to change this." })}
      /> : null}
      {published ? <div className="wf-block-preview" role="status"><h3>Published version {published.version}</h3><p>Replace the block with this exact version. Studio checks the block and the published version again first.</p></div> : null}
      {messages.length ? <ul role="alert">{messages.map((message, index) => <li key={index}>{message}</li>)}</ul> : null}
    </>}
    <div className="wf-block-dialog-actions">
      <button type="button" disabled={committing || publicationPending} onClick={close}>{created || publishedVersionId ? "Close" : "Cancel"}</button>
      {!unavailable && target === "new" && !created ? <StudioButton type="button" variant="primary" disabled={!ready || Boolean(busy) || createUnknown} onClick={() => void create()}>{busy === "creating" ? "Creating draft…" : "Create reusable activity draft"}</StudioButton> : null}
      {!unavailable && (target === "existing" || publishedVersionId) ? <StudioButton type="button" variant="primary" disabled={!ready || Boolean(busy) || !(target === "existing" ? existingVersionId : publishedVersionId)}
        onClick={() => void replace((target === "existing" ? existingVersionId : publishedVersionId) ?? "")}>{busy === "replacing" ? "Checking and replacing…" : "Replace block with this version"}</StudioButton> : null}
    </div>
  </NativeBlockDialog>;
}

function countSteps(node: ActivityNode, catalog: ActivityCatalogItem[]): number {
  return getChildSlots(node, catalog).reduce((sum, slot) => sum + slot.activities.reduce((inner, child) => inner + 1 + countSteps(child, catalog), 0), 0);
}

function findBlock(node: ActivityNode, nodeId: string, catalog: ActivityCatalogItem[]): ActivityNode | undefined {
  if (node.nodeId === nodeId) return node;
  for (const slot of getChildSlots(node, catalog)) for (const child of slot.activities) {
    const found = findBlock(child, nodeId, catalog);
    if (found) return found;
  }
  return undefined;
}

/** Where the call takes an input's value from today, in the author's terms. */
function describeSource(source: unknown, context: NativeBlockContext, host: BlockPromotionHost) {
  const record = source && typeof source === "object" ? source as { expressionType?: string; value?: Record<string, unknown> } : {};
  const kind = record.expressionType?.toLowerCase();
  if (kind === "input") {
    const key = record.value?.referenceKey;
    const input = host.publicInputs.find(item => item.referenceKey === key);
    return `from ${host.resourceKind === "workflow-definition" ? "workflow" : "activity"} input ${input?.name ?? String(key)}`;
  }
  if (kind === "activityresult") {
    const producer = String(record.value?.producerNodeId ?? "");
    return `from the result of ${context.document.activityPresentation.find(item => item.nodeId === producer)?.displayName || producer}`;
  }
  return `from its current ${record.expressionType ?? "value"}`;
}

function PromotionProposal({ plan, context, host }: { plan: ReadyBlockPromotion; context: NativeBlockContext; host: BlockPromotionHost }) {
  const outcomes = plan.candidate.contract.outcomes;
  // The author's block as it is, without the result-transfer steps the proposal adds.
  const block = findBlock(context.document.rootActivity, plan.blockNodeId, context.catalog);
  const steps = block ? countSteps(block, context.catalog) : 0;
  const typeLabel = (type: { alias: string; collectionKind: string }) => type.collectionKind && type.collectionKind.toLowerCase() !== "single" ? `${type.alias} (${type.collectionKind})` : type.alias;
  return <div className="wf-block-preview">
    <h3>Proposed reusable activity</h3>
    <p>{steps} {steps === 1 ? "step" : "steps"}; {plan.inputs.length} {plan.inputs.length === 1 ? "input" : "inputs"}, {plan.outputs.length} {plan.outputs.length === 1 ? "output" : "outputs"} and {outcomes.length} {outcomes.length === 1 ? "outcome" : "outcomes"}. The call passes the block’s current values.</p>
    {plan.inputs.length ? <details open><summary>Inputs</summary><ul>{plan.inputs.map(input => <li key={input.referenceKey}><strong>{input.name}</strong> · {typeLabel(input.type)} · {describeSource(input.source, context, host)} · used by {input.uses.length} {input.uses.length === 1 ? "step" : "steps"}</li>)}</ul></details> : null}
    {plan.outputs.length ? <details open><summary>Outputs</summary><ul>{plan.outputs.map(output => <li key={output.referenceKey}><strong>{output.name}</strong> · {typeLabel(output.type)}</li>)}</ul></details> : null}
    {outcomes.length ? <details><summary>Outcomes</summary><ul>{outcomes.map(outcome => <li key={outcome.referenceKey}>{outcome.name}</li>)}</ul></details> : null}
    <details><summary>Reusable activity material</summary><pre>{JSON.stringify(plan.candidate, null, 2)}</pre></details>
    <details><summary>Replacement call</summary><pre>{JSON.stringify(plan.callSite, null, 2)}</pre></details>
  </div>;
}
