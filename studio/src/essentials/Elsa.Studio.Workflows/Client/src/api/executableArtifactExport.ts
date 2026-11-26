import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { capabilityIds, hasCapabilityLink, resolveCapabilityLink } from "./capabilities";

// Client half of the executable-artifact export (foundation #1304, studio #493). The server owns the
// closure: this module resolves the advertised link, GETs it, and hands the payload back untouched.
// Kept in its own module — imported only by the deferred workflow-editor surface — so it stays out of
// the Definitions landing bundle, like the draft-validation client.

/** The relation elsa-foundation advertises under the publishing capability for the export endpoint. */
export const workflowExecutableExportRelation = "workflow-executable-export";

/**
 * Reports whether the backend advertises the executable-export relation. Runtime-less and older servers
 * omit it; callers hide the action rather than rendering a button that cannot work.
 */
export function isWorkflowExecutableExportAvailable(context: StudioEndpointContext): Promise<boolean> {
  return hasCapabilityLink(context, capabilityIds.publishing, workflowExecutableExportRelation);
}

export interface WorkflowExecutableClosureExport {
  /** The closure exactly as the server produced it. */
  closure: unknown;
  /**
   * The file name the server named in `Content-Disposition`, or null when the header was not readable —
   * it is not CORS-safelisted, so an API host that does not expose it (and any http client that hides
   * response headers) leaves this null and the caller reconstructs the name instead.
   */
  fileName: string | null;
}

/**
 * GETs the artifact closure for a *published* workflow definition version.
 *
 * The response is returned exactly as the server produced it: Studio neither assembles nor transforms
 * the closure (FR-C-004). Server error statuses propagate as-is so callers can branch on them through
 * {@link describeWorkflowExecutableExportFailure}.
 */
export async function exportWorkflowExecutableClosure(
  context: StudioEndpointContext,
  versionId: string,
  signal?: AbortSignal
): Promise<WorkflowExecutableClosureExport> {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.publishing,
    workflowExecutableExportRelation,
    { versionId });

  // The server names the download; the header is only readable when the http client surfaces response
  // headers and the API exposes it through CORS, so both absences degrade to the caller's own name.
  const http = context.http;
  if (typeof http.getJsonWithHeaders === "function") {
    const response = await http.getJsonWithHeaders<unknown>(path, { signal });
    return {
      closure: response.value,
      fileName: readContentDispositionFileName(response.headers.get("content-disposition"))
    };
  }
  return { closure: await http.getJson<unknown>(path, { signal }), fileName: null };
}

/**
 * Reads the `filename` from a `Content-Disposition` header, preferring RFC 5987's `filename*`.
 *
 * The value is a server-supplied string that becomes a download name, so it is accepted only after the
 * same reduction the server applies to its own segments: path separators, quotes and control characters
 * cannot survive, and anything that reduces to nothing is treated as no name at all.
 */
export function readContentDispositionFileName(header: string | null | undefined): string | null {
  if (!header) return null;

  const extended = /filename\*\s*=\s*(?:UTF-8|utf-8)?''([^;]+)/i.exec(header);
  const quoted = /filename\s*=\s*"([^"]*)"/i.exec(header);
  const bare = /filename\s*=\s*([^;"]+)/i.exec(header);
  const raw = extended ? safeDecode(extended[1]) : (quoted?.[1] ?? bare?.[1] ?? "");
  const sanitized = raw
    .trim()
    // Drop control characters (a CRLF in a header value must never reach a file name), then any
    // directory prefix, then reduce the rest to the alphabet the server itself emits.
    // eslint-disable-next-line no-control-regex
    .replace(/[\u0000-\u001f\u007f]+/g, "")
    .replace(/^.*[\\/]/, "")
    .replace(/[^A-Za-z0-9._-]+/g, "-")
    .replace(/^[-.]+/, "")
    .replace(/[-.]+$/, "");
  return sanitized || null;
}

function safeDecode(value: string): string {
  try {
    return decodeURIComponent(value);
  } catch {
    return value;
  }
}

export type WorkflowExecutableExportFailureKind =
  /**
   * 404 with no problem detail — the advertised href answered nothing. Nothing on the server
   * cross-checks a capability href against a registered route, so advertisement is a declaration and
   * not proof of a live route; this is that case, not a client-side URL mistake.
   */
  | "endpointUnavailable"
  /** 404 — the version is unknown, has no source reference of any scope, or its published artifact is gone from the store. */
  | "notFound"
  /** 409 — the version exists but was never published; only expiring test-run snapshots exist. */
  | "notPublished"
  /** 409 — the stored closure is incomplete; `serverErrors` carries one entry per missing dependency. */
  | "incompleteClosure"
  /** 500 — no download target is registered, or one answered with a non-inline delivery: engine composition, not a workflow problem. */
  | "engineMisconfigured"
  /** 500 — a cycle in the stored dependency graph, or a storage/codec fault. */
  | "engineFault"
  | "unknown";

export interface WorkflowExecutableExportFailure {
  kind: WorkflowExecutableExportFailureKind;
  status?: number;
  /** The server's own summary message (problem-detail `detail`/`title`, or the thrown message). */
  message: string;
  /**
   * Best-effort ids for the `incompleteClosure` kind, recovered from the wording of the error entries.
   * There is no structured field on the wire — the server adds one problem-detail entry per missing
   * dependency and the id lives inside its `reason` — so this is a classification aid only. Anything
   * user-facing should render {@link serverErrors}, which is what the server actually said.
   */
  missingArtifactIds: string[];
  /**
   * Every problem-detail error entry the server returned, verbatim and in order. For an incomplete
   * closure this is the list of missing dependencies (plus the summary entry the server appends).
   */
  serverErrors: string[];
}

const missingDependencyEntryPattern = /dependency artifact\s+'([^']+)'\s+is missing from the executable store/i;
// Fallback for the summary sentence: "Unresolved dependency artifact(s): 'id@hash' (declared by ...), ...".
const missingDependencySummaryPattern = /'([^'@]+)@[^']*'/g;

/**
 * Maps an export failure onto the endpoint's contracted response taxonomy so the editor can offer the
 * right affordance (publish first, the missing dependencies, or a plain error) instead of one toast.
 *
 * The failure modes are distinguished by exception type on the server but arrive here as a status plus
 * the standard error collection, so the classification reads that collection first and only falls back
 * to the summary text.
 */
export function describeWorkflowExecutableExportFailure(error: unknown): WorkflowExecutableExportFailure {
  const status = readStatus(error);
  const payload = readPayload(error);
  const serverErrors = readServerErrors(payload, error);
  const message = readMessage(payload, error, status);
  const missingArtifactIds = readMissingArtifactIds(serverErrors, message);

  return {
    kind: classify(status, missingArtifactIds, [message, ...serverErrors].join(" "), !!payload || !!serverErrors.length),
    ...(status === undefined ? {} : { status }),
    message,
    missingArtifactIds,
    serverErrors
  };
}

function classify(
  status: number | undefined,
  missingArtifactIds: string[],
  text: string,
  hasProblemDetail: boolean
): WorkflowExecutableExportFailureKind {
  // The endpoint always answers a 404 with a problem detail. A bodyless one means the advertised route
  // is not mapped on this host, which is a different conversation from "this version has nothing".
  if (status === 404) return hasProblemDetail ? "notFound" : "endpointUnavailable";
  if (status === 409) {
    // Only two 409s are contracted. An incomplete closure is recognised by its per-id error entries (or,
    // failing that, by the summary phrasing); every other 409 is the never-published refusal.
    return missingArtifactIds.length || /closure rooted at .* is incomplete|unresolved dependency artifact/i.test(text)
      ? "incompleteClosure"
      : "notPublished";
  }
  if (status === 500 || status === 503) {
    // The engine-composition faults (no download target registered, a non-inline delivery) are not
    // something the author can fix by changing the workflow, so they read differently in the UI.
    return /export is not available on this engine|produced an unexpected delivery/i.test(text)
      ? "engineMisconfigured"
      : "engineFault";
  }
  return "unknown";
}

function readMissingArtifactIds(serverErrors: string[], message: string): string[] {
  const ids = serverErrors.flatMap(entry => {
    const match = missingDependencyEntryPattern.exec(entry);
    return match ? [match[1]] : [];
  });
  if (ids.length) return Array.from(new Set(ids));

  if (!/unresolved dependency artifact/i.test(message)) return [];
  return Array.from(new Set(Array.from(message.matchAll(missingDependencySummaryPattern), match => match[1])));
}

function readStatus(error: unknown): number | undefined {
  if (!error || typeof error !== "object") return undefined;
  const candidate = error as { status?: unknown; statusCode?: unknown; response?: { status?: unknown } };
  for (const value of [candidate.status, candidate.statusCode, candidate.response?.status]) {
    if (typeof value === "number") return value;
  }
  const payload = readPayload(error);
  const payloadStatus = isRecord(payload) ? payload.status : undefined;
  return typeof payloadStatus === "number" ? payloadStatus : undefined;
}

function readPayload(error: unknown): unknown {
  if (!error || typeof error !== "object") return null;
  const candidate = error as { payload?: unknown; response?: { data?: unknown } };
  if (candidate.payload != null) return candidate.payload;
  if (candidate.response?.data != null) return candidate.response.data;
  return null;
}

/**
 * Reads the server's error entries out of either FastEndpoints shape: ProblemDetails' array of
 * `{ name, reason }` entries, and the classic `errors` dictionary of `path -> string[]` (which the
 * Studio http client also surfaces as `validationErrors`).
 */
function readServerErrors(payload: unknown, error: unknown): string[] {
  const messages: string[] = [];
  const collect = (value: unknown) => {
    if (typeof value === "string" && value.trim()) messages.push(value.trim());
  };

  const errors = isRecord(payload) ? payload.errors : undefined;
  if (Array.isArray(errors)) {
    for (const entry of errors) {
      if (typeof entry === "string") { collect(entry); continue; }
      if (!isRecord(entry)) continue;
      collect(entry.reason ?? entry.message ?? entry.detail ?? entry.errorMessage);
    }
  } else if (isRecord(errors)) {
    for (const value of Object.values(errors)) {
      if (Array.isArray(value)) value.forEach(collect);
      else collect(value);
    }
  }

  if (!messages.length) {
    const validationErrors = error && typeof error === "object"
      ? (error as { validationErrors?: unknown }).validationErrors
      : undefined;
    if (isRecord(validationErrors)) {
      for (const value of Object.values(validationErrors)) {
        if (Array.isArray(value)) value.forEach(collect);
        else collect(value);
      }
    }
  }

  return Array.from(new Set(messages));
}

function readMessage(payload: unknown, error: unknown, status: number | undefined): string {
  if (isRecord(payload)) {
    for (const key of ["detail", "title", "message"]) {
      const value = payload[key];
      if (typeof value === "string" && value.trim()) return value.trim();
    }
  }
  const thrown = error instanceof Error ? error.message.trim() : typeof error === "string" ? error.trim() : "";
  if (thrown) return thrown;
  return status === undefined
    ? "The executable artifact export failed."
    : `The executable artifact export failed with ${status}.`;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value != null && typeof value === "object" && !Array.isArray(value);
}
