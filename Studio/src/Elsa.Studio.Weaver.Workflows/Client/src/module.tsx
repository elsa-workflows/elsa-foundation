import React from "react";
import type {
  ElsaStudioModuleApi,
  StudioAiContextAttachment,
  StudioAiProposalRendererProps
} from "@elsa-workflows/studio-sdk";

const moduleId = "Elsa.Studio.Weaver.Workflows";

export function register(api: ElsaStudioModuleApi) {
  api.ai.contextProviders.add({
    id: "weaver.workflows.definition-context",
    kind: "workflow-definition",
    label: "Workflow definition",
    description: "Attaches workflow definition metadata, draft state, versions, and validation diagnostics.",
    moduleId,
    createAttachment: reference => createWorkflowAttachment("workflow-definition", reference)
  });

  api.ai.contextProviders.add({
    id: "weaver.workflows.executable-context",
    kind: "workflow-executable",
    label: "Workflow executable",
    description: "Attaches runtime executable identity, source, root activity, and resume target metadata.",
    moduleId,
    createAttachment: reference => createWorkflowAttachment("workflow-executable", reference)
  });

  api.ai.contextProviders.add({
    id: "weaver.workflows.instance-context",
    kind: "workflow-instance",
    label: "Workflow instance",
    description: "Attaches workflow instance state, incidents, bookmarks, and recent execution history when available.",
    moduleId,
    createAttachment: reference => createWorkflowAttachment("workflow-instance", reference)
  });

  api.ai.promptActions.add({
    id: "weaver.workflows.suggest-create-metadata",
    label: "Suggest name and description",
    description: "Ask Weaver to propose a workflow display name and description for the draft being created.",
    moduleId,
    placement: "field-adornment",
    contextKind: "workflow-create-draft",
    createPrompt: context => {
      // Only the user's intent and the chosen root kind are relevant to naming. Deliberately omit any
      // activity catalog or large draft payload so the conversation isn't flooded with technical JSON.
      const draft: Record<string, unknown> = isRecord(context) && isRecord(context.draft) ? context.draft : {};
      const intent = isRecord(context) && typeof context.intent === "string" ? context.intent.trim() : "";
      const rootKind = typeof draft.rootKind === "string" ? draft.rootKind : undefined;
      const attachment = { intent, rootKind, name: draft.name ?? "", description: draft.description ?? "" };
      return {
        message: [
          "Suggest a concise display name and a one-sentence description for a new Elsa workflow.",
          intent
            ? `The user describes what the workflow should do as: "${intent}".`
            : "The user has not described the workflow yet — infer a sensible, generic automation name and description.",
          rootKind ? `The root activity is a ${rootKind}.` : "",
          "Reply with one short friendly sentence, then a fenced ```json code block containing exactly " +
          '{"name": string, "description": string} and nothing else in that block.'
        ].filter(Boolean).join("\n"),
        mode: "enqueue",
        attachments: [createAttachment("workflow-create-draft", "new-workflow", attachment)],
        source: { moduleId, actionId: "weaver.workflows.suggest-create-metadata", label: "Suggest workflow metadata" }
      };
    }
  });

  api.ai.promptActions.add({
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

  api.ai.promptActions.add({
    id: "weaver.workflows.explain-executable",
    label: "Explain executable",
    description: "Ask Weaver to explain a published workflow executable and its runtime start conditions.",
    moduleId,
    placement: "selection",
    contextKind: "workflow-executable",
    createPrompt: context => ({
      message: "Explain this workflow executable, its root activity, source version, and runtime execution implications.",
      mode: "enqueue",
      attachments: [createAttachment("workflow-executable", readReferenceId(context), context)],
      source: { moduleId, actionId: "weaver.workflows.explain-executable", label: "Explain executable" }
    })
  });

  api.ai.promptActions.add({
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

  api.ai.promptActions.add({
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

  api.ai.promptActions.add({
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

  api.ai.tools.add({
    name: "workflow.definition.read",
    displayName: "Read workflow definition",
    description: "Reads workflow definition metadata, draft state, versions, and validation diagnostics.",
    mutability: "read-only",
    dangerLevel: "low",
    tenantBehavior: "tenant-scoped",
    moduleId,
    permissions: ["workflows:read"],
    agentScopes: ["workflow-author"]
  });

  api.ai.tools.add({
    name: "workflow.proposal.create",
    displayName: "Create workflow proposal",
    description: "Creates a reviewed workflow create/update proposal that requires explicit user approval before apply.",
    mutability: "proposal",
    dangerLevel: "medium",
    tenantBehavior: "tenant-scoped",
    moduleId,
    permissions: ["workflows:write"],
    agentScopes: ["workflow-author"]
  });

  api.ai.proposalRenderers.add({
    id: "weaver.workflows.workflow-proposal-renderer",
    kind: "WorkflowCreate",
    moduleId,
    component: WorkflowProposalRenderer
  });

  api.ai.proposalRenderers.add({
    id: "weaver.workflows.workflow-update-proposal-renderer",
    kind: "WorkflowUpdate",
    moduleId,
    component: WorkflowProposalRenderer
  });
}

export function WorkflowProposalRenderer({ proposal }: StudioAiProposalRendererProps) {
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

function createWorkflowAttachment(kind: string, reference: unknown) {
  return createAttachment(kind, readReferenceId(reference), reference);
}

function createAttachment(kind: string, referenceId: string | null, reference: unknown): StudioAiContextAttachment {
  return {
    kind,
    referenceId,
    metadata: isRecord(reference) ? reference : { value: reference }
  };
}

function readReferenceId(reference: unknown) {
  if (!isRecord(reference)) return null;
  const id = reference.id ?? reference.definitionId ?? reference.artifactId ?? reference.instanceId;
  return typeof id === "string" ? id : null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}
