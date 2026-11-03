import type { StudioAgentClientBinding, StudioAgentContextAttachment } from "@elsa-workflows/studio-sdk";

/**
 * The read-only page contexts of spec 1193 stories 8 and 9, as caller attachments the server admits strictly: one exact
 * Executable (`workflows.executable`, bound to its Artifact Hash, admitted by Elsa.Agent.Workflows.Runtime), one exact
 * persisted workflow version (`workflows.version`, bound to its version identity, admitted by Elsa.Agent.Workflows) and one
 * exact Run (`workflows.run`, bound to its Workflow Instance identity and the selected activity execution, admitted by
 * Elsa.Agent.Workflows.Runtime). Each carries only identity; the server reads the content itself through the owning
 * services. A context missing an identity field is never built, so no malformed attachment is sent.
 */
const moduleId = "Elsa.Studio.Workflows";

export interface WorkflowExecutableIdentity { artifactId?: unknown; artifactHash?: unknown; sourceReferenceId?: unknown }
export interface WorkflowVersionIdentity { definitionId?: unknown; versionId?: unknown; version?: unknown }
export interface WorkflowRunIdentity { workflowExecutionId?: unknown; activityExecutionId?: unknown }

export function workflowExecutableAttachment(identity: unknown, client?: StudioAgentClientBinding | null): StudioAgentContextAttachment | null {
  if (!isRecord(identity)) return null;
  const artifactId = boundedId(identity.artifactId);
  const artifactHash = boundedId(identity.artifactHash, 256);
  if (!artifactId || !artifactHash) return null;
  const sourceReferenceId = boundedId(identity.sourceReferenceId) ?? null;
  return {
    id: `workflows.executable:${artifactId}`, moduleId, schemaVersion: "1", contentType: "workflows.executable",
    sensitivity: "internal", scope: "screen", origin: "clientSnapshot", source: "workflow-executable", sourceId: artifactId,
    label: artifactId, summary: `Workflow Executable ${artifactId}`,
    target: { moduleId, resourceType: "workflow-executable", resourceId: artifactId, revision: artifactHash },
    ...(client ? { client } : {}),
    content: { artifactId, artifactHash, sourceReferenceId }
  };
}

export function workflowVersionAttachment(identity: unknown, client?: StudioAgentClientBinding | null): StudioAgentContextAttachment | null {
  if (!isRecord(identity)) return null;
  const definitionId = boundedId(identity.definitionId);
  const versionId = boundedId(identity.versionId);
  if (!definitionId || !versionId) return null;
  const version = typeof identity.version === "string" && identity.version ? identity.version.slice(0, 64) : null;
  return {
    id: `workflows.version:${versionId}`, moduleId, schemaVersion: "1", contentType: "workflows.version",
    sensitivity: "internal", scope: "selection", origin: "clientSnapshot", source: "workflow-version", sourceId: versionId,
    label: version ? `v${version}` : versionId, summary: version ? `Workflow version v${version}` : `Workflow version ${versionId}`,
    target: { moduleId, resourceType: "workflow-version", resourceId: versionId, revision: versionId },
    ...(client ? { client } : {}),
    content: { definitionId, versionId }
  };
}

/**
 * One exact Run (its Workflow Instance) and, where one is selected, the selected activity execution: never "the latest run"
 * of a definition. Its revision identity is the selected activity execution's, or the instance's when none is selected
 * (spec 1193 FR-005).
 */
export function workflowRunAttachment(identity: unknown, client?: StudioAgentClientBinding | null): StudioAgentContextAttachment | null {
  if (!isRecord(identity)) return null;
  const workflowExecutionId = boundedId(identity.workflowExecutionId, 256);
  if (!workflowExecutionId) return null;
  const selected = identity.activityExecutionId;
  const activityExecutionId = selected === null || selected === undefined ? null : boundedId(selected, 256);
  if (activityExecutionId === undefined || activityExecutionId === workflowExecutionId) return null;
  return {
    id: `workflows.run:${workflowExecutionId}`, moduleId, schemaVersion: "1", contentType: "workflows.run",
    sensitivity: "internal", scope: "screen", origin: "clientSnapshot", source: "workflow-run", sourceId: workflowExecutionId,
    label: activityExecutionId ? `Run ${workflowExecutionId} · ${activityExecutionId}` : `Run ${workflowExecutionId}`,
    summary: activityExecutionId ? `Workflow Run ${workflowExecutionId}, activity execution ${activityExecutionId}` : `Workflow Run ${workflowExecutionId}`,
    target: { moduleId, resourceType: "workflow-run", resourceId: workflowExecutionId, revision: activityExecutionId ?? workflowExecutionId },
    ...(client ? { client } : {}),
    content: { workflowExecutionId, activityExecutionId }
  };
}

function boundedId(value: unknown, limit = 128) {
  return typeof value === "string" && value.length > 0 && value.length <= limit ? value : undefined;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}
