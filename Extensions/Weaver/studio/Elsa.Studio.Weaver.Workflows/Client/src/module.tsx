import React from "react";
import type {
  ElsaStudioModuleApi,
  StudioAgentContextAttachment,
  StudioAgentResultRendererProps,
  StudioEndpointContext
} from "@elsa-workflows/studio-sdk";
import { capabilityIds, resolveCapabilityLink } from "@elsa-workflows/studio-workflows";

interface WorkflowProposalSummary {
  kind?: string; rationale?: string; warnings?: string[];
  diagnostics?: Array<{ code?: string; message: string; severity?: string }>;
  graphDiff?: { addedActivityIds?: string[]; removedActivityIds?: string[]; changedActivityIds?: string[] };
}

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
    id: "weaver.workflows.explain-executable",
    label: "Explain executable",
    description: "Ask Weaver to explain a published workflow executable and its runtime start conditions.",
    moduleId,
    placement: "selection",
    contextKind: "workflow-executable",
    createPrompt: context => ({
      // The attachment is the WorkflowExecutableSummary — metadata only. The prompt directs Weaver to
      // pull the actual structure through the get-executable-detail tool so the explanation is grounded
      // in the real node tree instead of guessed from the summary.
      message: [
        "Explain this workflow executable, its root activity, source version, and runtime execution implications.",
        "The attached summary carries identity metadata only. Before explaining, call the get-executable-detail tool with this artifact id to retrieve the compiled node tree, input bindings, and references, and ground the explanation in that structure."
      ].join("\n"),
      mode: "enqueue",
      attachments: [createAttachment("workflow-executable", readReferenceId(context), context)],
      source: { moduleId, actionId: "weaver.workflows.explain-executable", label: "Explain executable" }
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
      message: "Find correctness, validation, maintainability, and runtime risks in this workflow draft. Prefer concrete findings over generic advice.",
      mode: "enqueue",
      attachments: [createAttachment("workflow-definition", readReferenceId(context), context)],
      source: { moduleId, actionId: "weaver.workflows.find-draft-risks", label: "Find workflow risks" }
    })
  });

  api.agent.promptActions.add({
    id: "weaver.workflows.propose-update",
    label: "Propose update",
    description: "Ask Weaver for a reviewed workflow update proposal instead of applying mutations directly.",
    moduleId,
    placement: "toolbar",
    contextKind: "workflow-definition",
    createPrompt: context => ({
      message: "Propose a safe workflow update. Return rationale, warnings, validation diagnostics, and a graph diff. Do not apply changes directly.",
      mode: "enqueue",
      attachments: [createAttachment("workflow-definition", readReferenceId(context), context)],
      source: { moduleId, actionId: "weaver.workflows.propose-update", label: "Propose workflow update" }
    })
  });

  api.agent.promptActions.add({
    id: "weaver.workflows.explain-instance",
    label: "Explain instance",
    description: "Ask Weaver to explain workflow instance state and history when instance data is available.",
    moduleId,
    placement: "empty-state",
    contextKind: "workflow-instance",
    createPrompt: context => ({
      message: "Explain how to reason about workflow instances in this Studio view. If an instance is attached, summarize its state, incidents, bookmarks, and next actions.",
      mode: "enqueue",
      attachments: [createAttachment("workflow-instance", readReferenceId(context), context)],
      source: { moduleId, actionId: "weaver.workflows.explain-instance", label: "Explain workflow instance" }
    })
  });

  api.agent.resultRenderers.add({
    id: "weaver.workflows.workflow-proposal-renderer", moduleId: "Elsa.Studio.Workflows",
    displayName: "Workflow proposal", resultTypes: ["workflows.proposal"], schemaVersions: ["1"],
    component: ({ result }: StudioAgentResultRendererProps) => <WorkflowProposalRenderer proposal={(isRecord(result) && isRecord(result.data) ? result.data : result) as WorkflowProposalSummary} />
  });
}

export function WorkflowProposalRenderer({ proposal }: { proposal: WorkflowProposalSummary }) {
  if (!proposal || typeof proposal !== "object") return <p>Workflow proposal data is unavailable.</p>;
  const graphDiff = proposal.graphDiff;

  return (
    <section aria-label="Workflow proposal">
      <h3>{proposal.kind} proposal</h3>
      <p>{proposal.rationale || "No rationale provided."}</p>
      {proposal.warnings?.length ? <ul>{proposal.warnings.map(warning => <li key={warning}>{warning}</li>)}</ul> : null}
      {proposal.diagnostics?.length ? (
        <ul>
          {proposal.diagnostics.map(diagnostic => (
            <li key={`${diagnostic.code ?? ""}-${diagnostic.message}`}>
              {diagnostic.severity ?? "info"}: {diagnostic.message}
            </li>
          ))}
        </ul>
      ) : null}
      {graphDiff ? (
        <dl>
          <dt>Added</dt>
          <dd>{graphDiff.addedActivityIds?.length ?? 0}</dd>
          <dt>Removed</dt>
          <dd>{graphDiff.removedActivityIds?.length ?? 0}</dd>
          <dt>Changed</dt>
          <dd>{graphDiff.changedActivityIds?.length ?? 0}</dd>
        </dl>
      ) : null}
    </section>
  );
}

// Backs the get-executable-detail Weaver Tool. Exported for the test suite. 404 (unknown artifact)
// and 403 (missing workflow read permission) surface as the endpoint's errors, unmodified.
export async function getExecutableDetail(context: StudioEndpointContext, input: Record<string, unknown>) {
  const artifactId = readTrimmedString(input.artifactId);
  if (!artifactId) throw new Error("get-executable-detail requires a non-empty artifactId.");

  const sourceReferenceId = readTrimmedString(input.sourceReferenceId);
  const query = sourceReferenceId ? `?ref=${encodeURIComponent(sourceReferenceId)}` : "";
  const path = await resolveCapabilityLink(context, capabilityIds.runtime, "workflow-executable", { artifactId });
  return context.http.getJson<unknown>(`${path}${query}`);
}

function readTrimmedString(value: unknown) {
  return typeof value === "string" && value.trim() ? value.trim() : null;
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
