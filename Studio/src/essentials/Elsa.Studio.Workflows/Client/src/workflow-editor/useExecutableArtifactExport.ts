import { useCallback, useEffect, useRef, useState } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import {
  describeWorkflowExecutableExportFailure,
  exportWorkflowExecutableClosure,
  isWorkflowExecutableExportAvailable
} from "../api/executableArtifactExport";
import { listExecutables } from "../api/runtime";
import type { WorkflowExecutableSummary } from "../workflowTypes";
import { buildExecutableArtifactFileName, downloadExecutableArtifactJson } from "../workflowSerialization";
import type { WorkflowEditorError } from "./editorTypes";

/** The published version whose compiled artifact closure can be exported, plus the identity its file is named from. */
export interface PublishedExecutableTarget {
  versionId: string;
  definitionId: string;
  artifactVersion: string | null;
  artifactId: string | null;
}

export interface ExecutableArtifactExportState {
  status: "loading" | "ready" | "unavailable";
  /** True only when the backend advertises the executable-export relation (foundation #1304). */
  supported: boolean;
  /** The published version to export, or null when this workflow has no published version yet. */
  target: PublishedExecutableTarget | null;
}

const unavailable: ExecutableArtifactExportState = { status: "unavailable", supported: false, target: null };

/**
 * Gates the "Export artifact" action on the two things it needs: the server advertising the export
 * relation, and the workflow having a published version to export.
 *
 * Degrades the way the publish and draft-validation integrations do — a runtime-less or older server
 * simply does not advertise the relation, so the action is hidden instead of erroring (FR-C-003). A
 * failed probe is treated the same way: an action that cannot work is better absent than broken.
 */
export function useExecutableArtifactExport({
  context,
  definitionId,
  publishedArtifactId,
  enabled = true
}: {
  context: StudioEndpointContext;
  definitionId: string;
  /** Re-probes after a publish so a freshly published workflow becomes exportable without a reload. */
  publishedArtifactId?: string | null;
  enabled?: boolean;
}): ExecutableArtifactExportState {
  const [state, setState] = useState<ExecutableArtifactExportState>({ status: "loading", supported: false, target: null });
  // Hold the context in a ref so the effect depends on the stable `baseUrl` string rather than on the
  // context object identity, which changes on every parent render.
  const contextRef = useRef(context);
  contextRef.current = context;
  const baseUrl = context.baseUrl ?? "";

  useEffect(() => {
    if (!enabled || !definitionId) {
      setState(unavailable);
      return;
    }

    let active = true;
    setState(previous => previous.status === "loading" ? previous : { ...previous, status: "loading" });
    void (async () => {
      try {
        const supported = await isWorkflowExecutableExportAvailable(contextRef.current);
        if (!active) return;
        if (!supported) {
          setState(unavailable);
          return;
        }
        const executables = await listExecutables(contextRef.current, { scope: "published", includeRetired: false });
        if (!active) return;
        setState({ status: "ready", supported: true, target: selectPublishedExecutable(executables, definitionId) });
      } catch {
        if (active) setState(unavailable);
      }
    })();

    return () => { active = false; };
  }, [baseUrl, definitionId, publishedArtifactId, enabled]);

  return state;
}

/**
 * Picks the version the toolbar exports: this definition's most recently published artifact.
 *
 * Read from the published-scope executables rather than from the activation slots, because a slot no
 * longer carries the publication it was activated by (foundation T117) and therefore names no definition
 * version. The executable does — and it also carries the artifact version the download name uses.
 */
export function selectPublishedExecutable(
  executables: WorkflowExecutableSummary[],
  definitionId: string
): PublishedExecutableTarget | null {
  const published = executables
    .filter(executable => executable.definitionId === definitionId && !!executable.definitionVersionId)
    .sort((left, right) => publishedTimestamp(right) - publishedTimestamp(left));
  const latest = published[0];
  if (!latest) return null;

  return {
    versionId: latest.definitionVersionId,
    definitionId: latest.definitionId,
    artifactVersion: latest.artifactVersion ?? null,
    artifactId: latest.artifactId ?? null
  };
}

function publishedTimestamp(executable: WorkflowExecutableSummary) {
  const parsed = Date.parse(executable.publishedAt ?? executable.createdAt);
  return Number.isFinite(parsed) ? parsed : Number.NEGATIVE_INFINITY;
}

/** Identity an export needs: the version to ask for, and the two segments its file name is built from. */
export interface ExecutableArtifactExportTarget {
  versionId: string;
  definitionId: string;
  artifactVersion: string | null;
}

/**
 * Fetches one published version's closure and saves it, returning the file name written.
 *
 * The single place an export is performed: the editor toolbar and the artifact lists both come through
 * here, so the request, the server-chosen file name and the verbatim payload cannot drift between them.
 */
export async function downloadExecutableArtifact(
  context: StudioEndpointContext,
  target: ExecutableArtifactExportTarget
): Promise<string> {
  const exported = await exportWorkflowExecutableClosure(context, target.versionId);
  // The server names the download; the reconstruction covers a host that does not expose the header.
  const fileName = exported.fileName ?? buildExecutableArtifactFileName(target);
  // Only reached on 200, so a failed export never leaves a partial or empty file behind.
  downloadExecutableArtifactJson(exported.closure, fileName);
  return fileName;
}

/**
 * Per-artifact export for the artifact lists (the editor's Artifacts tab and the executables page).
 *
 * Each listed artifact already carries the definition version it was compiled from, so this action needs
 * no slot lookup — it exports exactly the row the operator clicked. `supported` is the same capability
 * probe the editor toolbar uses, so an engine without the relation renders no button at all.
 */
export function useExecutableArtifactDownload({ context, onExported, onFailed }: {
  context: StudioEndpointContext;
  onExported(fileName: string, executable: WorkflowExecutableSummary): void;
  onFailed(message: string, executable: WorkflowExecutableSummary): void;
}) {
  const [supported, setSupported] = useState(false);
  const [exportingArtifactId, setExportingArtifactId] = useState<string | null>(null);
  const contextRef = useRef(context);
  contextRef.current = context;
  const baseUrl = context.baseUrl ?? "";

  useEffect(() => {
    let active = true;
    isWorkflowExecutableExportAvailable(contextRef.current).then(
      available => { if (active) setSupported(available); },
      // A failed probe hides the action rather than surfacing an error the operator cannot act on.
      () => { if (active) setSupported(false); });
    return () => { active = false; };
  }, [baseUrl]);

  const exportArtifact = useCallback(async (executable: WorkflowExecutableSummary) => {
    if (exportingArtifactId) return;
    setExportingArtifactId(executable.artifactId);
    try {
      const fileName = await downloadExecutableArtifact(contextRef.current, {
        versionId: executable.definitionVersionId,
        definitionId: executable.definitionId,
        artifactVersion: executable.artifactVersion
      });
      onExported(fileName, executable);
    } catch (error) {
      onFailed(formatExecutableArtifactExportError(error), executable);
    } finally {
      setExportingArtifactId(null);
    }
  }, [exportingArtifactId, onExported, onFailed]);

  return { supported, exportingArtifactId, exportArtifact };
}

export const publishFirstMessage =
  "Publish this workflow before exporting its executable artifact. Only a published version has a compiled runtime artifact.";

/** The name both identity segments fall back to: the exported artifact carried no identity at all. */
export const anonymousArtifactFileName = "workflow-unversioned-closure.json";

/**
 * Turns an export failure into editor copy that matches the endpoint's contracted responses: the two
 * 409s get their own affordance (publish first, and the missing dependencies the server named), and the
 * engine faults say they are about the server rather than the workflow. The server's own message is kept
 * as the alert detail so an operator can still see it verbatim.
 */
export function describeExecutableArtifactExportFailure(error: unknown): WorkflowEditorError {
  const failure = describeWorkflowExecutableExportFailure(error);
  const base = failure.status === undefined ? {} : { status: failure.status };
  const withDetail = (message: string, extraDetail: string[] = []): WorkflowEditorError => {
    const detail = [...extraDetail, ...failure.serverErrors, failure.message]
      .filter((line, index, lines) => !!line && line !== message && lines.indexOf(line) === index)
      .join("\n");
    return { ...base, message, ...(detail ? { detail } : {}) };
  };

  switch (failure.kind) {
    case "notPublished":
      return withDetail(publishFirstMessage);
    case "incompleteClosure":
      // The server reports the gaps as one problem-detail error entry per missing dependency; those
      // entries are the list, rendered verbatim rather than re-assembled from ids parsed back out.
      return withDetail("The executable artifact was not exported: its dependency closure is incomplete on the server. Republish the workflow so its dependencies are compiled again.");
    case "notFound":
      return withDetail("There is nothing to export for this workflow version: the server holds no executable for it. Publish the workflow again, then export.");
    case "endpointUnavailable":
      return withDetail("This server advertises artifact export, but its export endpoint did not answer. The API host may be older than the capability it advertises.");
    case "engineMisconfigured":
      return withDetail("This engine cannot export executable artifacts: it has no export delivery configured. Ask an operator to check the server composition.");
    case "engineFault":
      return withDetail("The server could not produce the executable artifact, so nothing was downloaded.");
    default:
      return withDetail(failure.message || "The executable artifact export failed, so nothing was downloaded.");
  }
}

/** The same copy, flattened for the artifact lists, whose alert renders a single string. */
export function formatExecutableArtifactExportError(error: unknown): string {
  const described = describeExecutableArtifactExportFailure(error);
  return [described.message, described.detail].filter(Boolean).join(" ");
}
