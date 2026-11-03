import React from "react";
import type {
  ElsaStudioModuleApi,
  StudioAgentContextAttachment,
  StudioAgentResultRendererProps
} from "@elsa-workflows/studio-sdk";

// Every prompt and renderer below is registered under this package's own module identity, and names only tools and
// result kinds the Workflows packages register (spec 1193 FR-019, SC-007). The explain-executable prompt returns with the
// Executable read tool it needs (slice F), and the explain-instance prompt with the Run context and tool (slice G).
const moduleId = "Elsa.Studio.Weaver.Workflows";

export function register(api: ElsaStudioModuleApi) {
  api.agent.promptActions.add({
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

  api.agent.promptActions.add({
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

  api.agent.promptActions.add({
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

  api.agent.promptActions.add({
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
  api.agent.resultRenderers.add({
    id: "weaver.workflows.batch-result", moduleId, resourceModuleIds: ["Elsa.Studio.Workflows"],
    displayName: "Workflow change", resultTypes: ["workflows.batch-result"], schemaVersions: ["1"],
    component: (props: StudioAgentResultRendererProps) => <WorkflowBatchResult {...props} />
  });
}

interface BatchOperationLabel { index?: number; kind?: string; summary?: string; risk?: string; reasons?: string[] }

/**
 * The review and result card of a reviewed Operation Batch (spec 1193 FR-011, FR-012): each operation with its risk
 * label, the batch's highest risk and complexity label, what applying clears, the outcome in product words, and the
 * outcome in product words. The whole-change Undo and the saved state are Studio Workflows' result control, shown after it.
 */
export function WorkflowBatchResult({ proposal, preparedInvocation, toolResult }: StudioAgentResultRendererProps) {
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

function batchOutcome(proposal: unknown, toolResult: StudioAgentResultRendererProps["toolResult"]) {
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

function createAttachment(kind: string, referenceId: string | null, reference: unknown): StudioAgentContextAttachment {
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
