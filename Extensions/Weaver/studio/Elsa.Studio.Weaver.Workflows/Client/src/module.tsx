import React from "react";
import type {
  ElsaStudioModuleApi,
  StudioWeaverContextAttachment,
  StudioWeaverResultRendererProps
} from "@elsa-workflows/studio-sdk";
import { workflowExecutableAttachment, workflowRunAttachment, workflowVersionAttachment } from "@elsa-workflows/studio-workflows";

// Every prompt and renderer below is registered under this package's own module identity, and names only tools and
// result kinds the Workflows packages register (spec 1193 FR-019, SC-007). The explain-executable prompt reads through the
// Runtime-side workflow_read_executable (Elsa.Weaver.Workflows.Runtime) and the explain-version prompt through
// workflow_read_version (slice F); the explain-instance prompt reads one exact Run through the Runtime-side
// workflow_read_run (slice G).
const moduleId = "Elsa.Studio.Weaver.Workflows";

export function register(api: ElsaStudioModuleApi) {
  api.weaver.promptActions.add({
    id: "weaver.workflows.suggest-create-metadata",
    label: "Suggest name and description",
    description: "Ask Weaver to propose a workflow display name and description for the draft being created.",
    moduleId,
    placement: "field-adornment",
    contextKind: "workflow-create-draft",
    createPrompt: context => {
      // Only the user's intent and selected catalog-authored root are relevant to naming. Deliberately omit any
      // activity catalog or large draft payload so the conversation isn't flooded with technical JSON.
      const draft: Record<string, unknown> = isRecord(context) && isRecord(context.draft) ? context.draft : {};
      const intent = isRecord(context) && typeof context.intent === "string" ? context.intent.trim() : "";
      const rootActivityVersionId = typeof draft.rootActivityVersionId === "string" ? draft.rootActivityVersionId : undefined;
      const attachment = { intent, rootActivityVersionId, name: draft.name ?? "", description: draft.description ?? "" };
      return {
        message: [
          "Suggest a concise display name and a one-sentence description for a new Elsa workflow.",
          intent
            ? `The user describes what the workflow should do as: "${intent}".`
            : "The user has not described the workflow yet — infer a sensible, generic automation name and description.",
          rootActivityVersionId ? `The selected root comes from catalog activity version ${rootActivityVersionId}.` : "",
          "Reply with one short friendly sentence, then a fenced ```json code block containing exactly " +
          '{"name": string, "description": string} and nothing else in that block.'
        ].filter(Boolean).join("\n"),
        mode: "enqueue",
        attachments: [createAttachment("workflow-create-draft", "new-workflow", attachment)],
        source: { moduleId, actionId: "weaver.workflows.suggest-create-metadata", label: "Suggest workflow metadata" }
      };
    }
  });

  api.weaver.promptActions.add({
    id: "weaver.workflows.explain-definition",
    label: "Explain workflow",
    description: "Ask Weaver to explain the selected workflow definition.",
    moduleId,
    placement: "selection",
    contextKind: "workflow-definition",
    createPrompt: context => ({
      message: "Explain what this workflow definition does, where it starts, and what a maintainer should inspect first.",
      mode: "enqueue",
      attachments: [createAttachment("workflow-definition", readReferenceId(context), context)],
      source: { moduleId, actionId: "weaver.workflows.explain-definition", label: "Explain workflow" }
    })
  });

  // An Executable is read-only (spec 1193 story 8, FR-028). The page that contributes its own workflows.executable context
  // (the Executable Inspector) dispatches with attached: true, so this prompt never duplicates it; elsewhere (the executables
  // list) the prompt attaches the exact identity itself, bound to its Artifact Hash.
  api.weaver.promptActions.add({
    id: "weaver.workflows.explain-executable",
    label: "Explain executable",
    description: "Ask Weaver to explain a workflow Executable read-only: its identity, Artifact Hash, graph and Source References.",
    moduleId,
    placement: "selection",
    contextKind: "workflow-executable",
    createPrompt: context => {
      const attachment = isRecord(context) && context.attached === true ? null : workflowExecutableAttachment(context);
      return {
        message: [
          "Explain this workflow Executable read-only: its identity and Artifact Hash, its root activity, its graph and the Source References that point at it.",
          "Read it with workflow_read_executable and name the Artifact Hash you read. An Executable is immutable: refuse any request to change it, change nothing, and offer to work on the workflow's current draft instead."
        ].join("\n"),
        mode: "enqueue",
        attachments: attachment ? [attachment] : [],
        source: { moduleId, actionId: "weaver.workflows.explain-executable", label: "Explain executable" }
      };
    }
  });

  // One exact Run, read-only (spec 1193 story 9, FR-029). The run page contributes its own workflows.run context (the Run and
  // the selected activity execution) and dispatches with attached: true, so this prompt never duplicates it; elsewhere the
  // prompt attaches the exact identity itself. Never "the latest run" of a definition.
  api.weaver.promptActions.add({
    id: "weaver.workflows.explain-instance",
    label: "Explain run",
    description: "Ask Weaver to explain one exact workflow run read-only: why it faulted, where it waits and what the selected activity execution recorded.",
    moduleId,
    placement: "selection",
    contextKind: "workflow-instance",
    createPrompt: context => {
      const attachment = isRecord(context) && context.attached === true ? null : workflowRunAttachment(context);
      const selected = isRecord(context) && typeof context.activityExecutionId === "string" ? context.activityExecutionId.slice(0, 256) : null;
      return {
        message: [
          "Explain this workflow run read-only: its status, why it faulted or where it is waiting, citing its recorded incidents and activity executions by id" +
            (selected ? `, and what the selected activity execution ${selected} recorded.` : "."),
          "Read it with workflow_read_run and name the Run you describe. If it is still in progress, say so and describe it as of the read time the result gives.",
          "A withheld value stays withheld: never guess it. Report unavailable data as unavailable, with the reason the result gives.",
          "Weaver never changes a run: refuse any request to retry, cancel, delete or dispatch it again, change nothing, and point to the run page's own controls."
        ].join("\n"),
        mode: "enqueue",
        attachments: attachment ? [attachment] : [],
        source: { moduleId, actionId: "weaver.workflows.explain-instance", label: "Explain run" }
      };
    }
  });

  // A persisted workflow definition version is read-only (spec 1193 story 8, FR-026).
  api.weaver.promptActions.add({
    id: "weaver.workflows.explain-version",
    label: "Explain version",
    description: "Ask Weaver to explain one exact workflow version read-only, or compare it with another version of the same workflow.",
    moduleId,
    placement: "selection",
    contextKind: "workflow-version",
    createPrompt: context => {
      const attachment = isRecord(context) && context.attached === true ? null : workflowVersionAttachment(context);
      const version = isRecord(context) && typeof context.version === "string" ? ` v${context.version.slice(0, 64)}` : "";
      return {
        message: [
          `Explain what this workflow version${version} does, read-only, and name the exact version you describe.`,
          "Read it with workflow_read_version; to say what changed, call it with compareToVersionId set to the other version of the same workflow, and present the comparison as the tool's own.",
          "A version is immutable: refuse any request to change it, change nothing, and offer to work on the workflow's current draft instead."
        ].join("\n"),
        mode: "enqueue",
        attachments: attachment ? [attachment] : [],
        source: { moduleId, actionId: "weaver.workflows.explain-version", label: "Explain version" }
      };
    }
  });

  api.weaver.promptActions.add({
    id: "weaver.workflows.find-draft-risks",
    label: "Find draft risks",
    description: "Ask Weaver to inspect draft validation errors and graph structure for likely runtime issues.",
    moduleId,
    placement: "toolbar",
    contextKind: "workflow-definition",
    createPrompt: context => ({
      message: "Find correctness, validation, maintainability, and runtime risks in the open workflow draft. Use the designer's current validation diagnostics in its working-state context, name the revision you read, and prefer concrete findings over generic advice.",
      mode: "enqueue",
      attachments: [createAttachment("workflow-definition", readReferenceId(context), context)],
      source: { moduleId, actionId: "weaver.workflows.find-draft-risks", label: "Find workflow risks" }
    })
  });

  api.weaver.promptActions.add({
    id: "weaver.workflows.propose-update",
    label: "Propose update",
    description: "Ask Weaver for one reviewed Operation Batch for the open draft instead of applying mutations directly.",
    moduleId,
    placement: "toolbar",
    contextKind: "workflow-definition",
    createPrompt: context => ({
      message: "Propose one safe change to the open workflow draft as a reviewed Operation Batch (workflow_apply_operation_batch), using installed activities and declared inputs only. Ask me when an activity, input or target is unclear. Saving and publishing stay with me.",
      mode: "enqueue",
      attachments: [createAttachment("workflow-definition", readReferenceId(context), context)],
      source: { moduleId, actionId: "weaver.workflows.propose-update", label: "Propose workflow update" }
    })
  });

  // Contributed by this package for the Workflows module's results; the host matches the result's resource module
  // against resourceModuleIds. The designer-history Undo is Studio Workflows' own result control, shown after this card.
  api.weaver.resultRenderers.add({
    id: "weaver.workflows.batch-result", moduleId, resourceModuleIds: ["Elsa.Studio.Workflows"],
    displayName: "Workflow change", resultTypes: ["workflows.batch-result"], schemaVersions: ["1"],
    component: (props: StudioWeaverResultRendererProps) => <WorkflowBatchResult {...props} />
  });
  // Spec 1193 slice E: the library's reviewed writes (per-target outcomes), and the library pages' own working-state changes.
  api.weaver.resultRenderers.add({
    id: "weaver.workflows.library-result", moduleId, resourceModuleIds: ["Elsa.Studio.Workflows"],
    displayName: "Workflow library change", resultTypes: ["workflows.library-result"], schemaVersions: ["1"],
    component: (props: StudioWeaverResultRendererProps) => <WorkflowLibraryResult {...props} />
  });
  api.weaver.resultRenderers.add({
    id: "weaver.workflows.list-state-result", moduleId, resourceModuleIds: ["Elsa.Studio.Workflows"],
    displayName: "Workflow list change", resultTypes: ["workflows.list-state"], schemaVersions: ["1"],
    component: (props: StudioWeaverResultRendererProps) => <WorkflowPageChangeResult {...props} label="Workflow list change" />
  });
  api.weaver.resultRenderers.add({
    id: "weaver.workflows.create-dialog-result", moduleId, resourceModuleIds: ["Elsa.Studio.Workflows"],
    displayName: "New workflow values", resultTypes: ["workflows.create-dialog-prefill"], schemaVersions: ["1"],
    component: (props: StudioWeaverResultRendererProps) => <WorkflowPageChangeResult {...props} label="New workflow values" />
  });
}

interface LibraryTargetOutcome { definitionId?: string; status?: string; message?: string | null }

const libraryStatusLabel: Record<string, string> = {
  applied: "Changed", denied: "Not permitted", conflict: "Changed elsewhere; reload", "not-found": "Unavailable", invalid: "Refused",
  unavailable: "Organization unavailable", unsupported: "Not supported", "not-applied": "Not changed", proposed: "Proposed"
};

/**
 * A reviewed library write (spec 1193 FR-021, SC-008): one line per workflow with its own outcome, the tag creations it
 * makes first, and an overall outcome that says "partial" whenever some workflows were not changed; never "done" for those.
 */
export function WorkflowLibraryResult({ proposal, preparedInvocation, toolResult }: StudioWeaverResultRendererProps) {
  const data = recordOf(toolResult?.payload?.data) ?? recordOf(preparedInvocation?.preview.data) ?? {};
  const targets = Array.isArray(data.targets) ? data.targets.filter(isRecord) as LibraryTargetOutcome[] : [];
  const status = typeof data.status === "string" ? data.status : "proposed";
  const changes = Array.isArray(data.changes) ? data.changes.filter(isRecord) : [];
  const overall = toolResult ? status === "applied" ? "Changed in the workflow library." : status === "partial" ? "Partly changed: some workflows were not changed." : "Nothing was changed."
    : proposal?.status === "denied" ? "Declined: nothing was changed." : "Review this library change.";
  return (
    <section aria-label="Workflow library change" className="weaver-workflow-library" data-outcome={toolResult ? status : "proposed"}>
      <p><strong>{overall}</strong></p>
      {changes.some(change => change.change === "create-and-add") ? <p data-creates-tags="true">Creates the new tag(s) first; this needs tag-management permission.</p> : null}
      {changes.length ? <ul aria-label="Tag changes">{changes.map((change, position) => <li key={position}>{String(change.change)}: {String(change.tag)}{change.value ? ` = ${String(change.value)}` : ""}</li>)}</ul> : null}
      {targets.length ? <ul aria-label="Workflows">{targets.map((target, position) => (
        <li key={target.definitionId ?? position} data-status={target.status}>{target.definitionId}: {libraryStatusLabel[target.status ?? "proposed"] ?? target.status}{target.message ? ` — ${target.message}` : ""}</li>
      ))}</ul> : null}
      {toolResult?.content ? <p>{toolResult.content}</p> : null}
    </section>
  );
}

/** A reviewed change to a library page's own working state: applied there only, revertible, and never saved. */
export function WorkflowPageChangeResult({ toolResult, preparedInvocation, label }: StudioWeaverResultRendererProps & { label: string }) {
  const data = recordOf(toolResult?.payload?.data) ?? recordOf(preparedInvocation?.preview.data) ?? {};
  return (
    <section aria-label={label} className="weaver-workflow-page-change">
      <p><strong>{toolResult ? "Applied on the page only; nothing was saved." : "Review this change to the page."}</strong></p>
      <pre>{JSON.stringify(data.change ?? {}, null, 2)}</pre>
      {toolResult?.content ? <p>{toolResult.content}</p> : null}
    </section>
  );
}

interface BatchOperationLabel { index?: number; kind?: string; summary?: string; risk?: string; reasons?: string[] }

/**
 * The review and result card of a reviewed Operation Batch (spec 1193 FR-011, FR-012): each operation with its risk
 * label, the batch's highest risk and complexity label, what applying clears, the outcome in product words, and the
 * outcome in product words. The whole-change Undo and the saved state are Studio Workflows' result control, shown after it.
 */
export function WorkflowBatchResult({ proposal, preparedInvocation, toolResult }: StudioWeaverResultRendererProps) {
  const data = recordOf(toolResult?.payload?.data) ?? recordOf(preparedInvocation?.preview.data) ?? {};
  const operations = Array.isArray(data.operations) ? data.operations.filter(isRecord) as BatchOperationLabel[] : [];
  const risk = typeof data.risk === "string" ? data.risk : preparedInvocation?.risk;
  return (
    <section aria-label="Workflow change" className="weaver-workflow-batch">
      <p><strong>{batchOutcome(proposal, toolResult)}</strong></p>
      <p>
        <span data-risk={risk}>{riskLabel(risk)}</span>
        {data.highComplexity === true ? <> · <span data-complexity="high">High complexity: applied whole or not at all, never split</span></> : null}
      </p>
      {operations.length ? (
        <ol aria-label="Operations">
          {operations.map((operation, position) => (
            <li key={operation.index ?? position} data-risk={operation.risk}>
              <span>{riskLabel(operation.risk)}</span>{" "}
              <span>{operation.summary ?? operation.kind ?? "Operation"}</span>
            </li>
          ))}
        </ol>
      ) : null}
      <p>The last test-run result and published-artifact association are cleared; Undo does not restore them. Saving and publishing remain your explicit actions.</p>
      {toolResult?.content ? <p>{toolResult.content}</p> : null}
    </section>
  );
}

function batchOutcome(proposal: unknown, toolResult: StudioWeaverResultRendererProps["toolResult"]) {
  // The receipt's own fact. Whether the user has since saved it is the owning module's live state, in its result control.
  if (toolResult?.succeeded && toolResult.receipt?.scope === "clientWorkingState") return "Applied locally to the working draft as one Undo step";
  const status = isRecord(proposal) && typeof proposal.status === "string" ? proposal.status : "";
  switch (status) {
    case "awaiting-approval": case "edited": return "Proposed — awaiting your review";
    case "approved": case "awaiting-client": return "Approved — not yet applied";
    case "denied": case "rejected": case "refused": case "expired": case "cancelled": return "Refused — nothing changed";
    case "uncertain": return "Uncertain — the editor could not confirm the change; it is never replayed";
    default: return toolResult && !toolResult.succeeded ? "Refused — nothing changed" : "Proposed — awaiting your review";
  }
}

function riskLabel(risk: unknown) {
  return risk === "destructive" ? "Destructive" : risk === "reviewRequired" ? "Review required" : "Unclassified — review carefully";
}

function recordOf(value: unknown): Record<string, unknown> | null {
  return isRecord(value) ? value : null;
}

function createAttachment(kind: string, referenceId: string | null, reference: unknown): StudioWeaverContextAttachment {
  const record = isRecord(reference) ? reference : {};
  const definition = isRecord(record.definition) ? record.definition : record;
  const draft = isRecord(record.draft) ? record.draft : record;
  const content: Record<string, string | null> = { requestKind: kind, referenceId };
  for (const key of ["intent", "name", "description", "rootActivityVersionId"] as const) {
    const value = record[key] ?? definition[key] ?? draft[key];
    if (typeof value === "string") content[key] = value.slice(0, 1024);
  }
  return {
    id: `workflow-request:${kind}:${referenceId ?? "new"}`, moduleId: "Elsa.Studio.Workflows", schemaVersion: "1",
    source: moduleId, sourceId: referenceId ?? undefined, label: "Workflow request", contentType: "workflow.request", sensitivity: "internal", scope: "selection",
    summary: "Workflow request context", origin: "clientSnapshot", target: null, content
  };
}

function readReferenceId(reference: unknown) {
  if (!isRecord(reference)) return null;
  const nestedDefinition = isRecord(reference.definition) ? reference.definition : {};
  const id = reference.id ?? reference.definitionId ?? reference.artifactId ?? reference.instanceId ?? nestedDefinition.id;
  return typeof id === "string" ? id : null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}
