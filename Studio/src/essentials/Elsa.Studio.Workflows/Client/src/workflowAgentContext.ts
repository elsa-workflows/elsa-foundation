import type { StudioAgentClientBinding, StudioAgentContextAttachment } from "@elsa-workflows/studio-sdk";

/**
 * The read-only page contexts of spec 1193 story 8, as caller attachments the server admits strictly: one exact Executable
 * (`workflows.executable`, bound to its Artifact Hash, admitted by Elsa.Agent.Workflows.Runtime) and one exact persisted
 * workflow version (`workflows.version`, bound to its version identity, admitted by Elsa.Agent.Workflows). Each carries only
 * identity; the server reads the content itself through the owning services. A context missing an identity field is never
 * built, so no malformed attachment is sent.
 */
const moduleId = "Elsa.Studio.Workflows";

export interface WorkflowExecutableIdentity { artifactId?: unknown; artifactHash?: unknown; sourceReferenceId?: unknown }
export interface WorkflowVersionIdentity { definitionId?: unknown; versionId?: unknown; version?: unknown }

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

function boundedId(value: unknown, limit = 128) {
  return typeof value === "string" && value.length > 0 && value.length <= limit ? value : undefined;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}
