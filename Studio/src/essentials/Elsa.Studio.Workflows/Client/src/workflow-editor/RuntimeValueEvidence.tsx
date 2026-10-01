import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { getActivityExecutionValuePayload } from "../api/activityExecutionValuePayload";
import type {
  ActivityExecutionInspectionValueSnapshot,
  DiagnosticSnapshotArrayNode,
  DiagnosticSnapshotNode,
  DiagnosticSnapshotObjectNode,
  DiagnosticSnapshotPayloadReferenceNode,
  DiagnosticSnapshotUnknownNode
} from "../workflowTypes";
import { runtimeValueTypeLabel } from "../runtimeValueFormatting";

export interface RuntimeValueEvidenceResolutionScope {
  context: StudioEndpointContext;
  workflowExecutionId: string;
  activityExecutionId: string;
}

const RuntimeValueEvidenceResolutionContext = createContext<RuntimeValueEvidenceResolutionScope | null>(null);
const runtimeValueContextIds = new WeakMap<object, number>();
let nextRuntimeValueContextId = 1;

export function runtimeValueContextId(context: StudioEndpointContext | null | undefined) {
  if (!context) return "no-context";
  let id = runtimeValueContextIds.get(context);
  if (!id) {
    id = nextRuntimeValueContextId++;
    runtimeValueContextIds.set(context, id);
  }
  return id;
}

export function RuntimeValueEvidenceResolutionProvider({
  scope,
  children
}: {
  scope: RuntimeValueEvidenceResolutionScope | null;
  children: ReactNode;
}) {
  return (
    <RuntimeValueEvidenceResolutionContext.Provider value={scope}>
      {children}
    </RuntimeValueEvidenceResolutionContext.Provider>
  );
}

type RuntimeValueResolutionState = {
  identity: string;
  status: "loading" | "ready" | "failed";
  payload?: unknown;
  error?: string;
};

export function RuntimeValueEvidenceCard({ snapshot, listItem = true }: { snapshot: ActivityExecutionInspectionValueSnapshot; listItem?: boolean }) {
  const typeName = runtimeValueTypeLabel(snapshot.type) || "Unknown";
  return (
    <article className="wf-runtime-input" role={listItem ? "listitem" : undefined}>
      <header>
        <span>
          <strong>{snapshot.name}</strong>
          <small>{typeName}</small>
        </span>
        <small>{formatCaptureMode(snapshot.captureMode)}</small>
      </header>
      <RuntimeValueEvidenceContent snapshot={snapshot} />
    </article>
  );
}

export function RuntimeValueEvidenceContent({ snapshot }: { snapshot: ActivityExecutionInspectionValueSnapshot }) {
  const scope = useContext(RuntimeValueEvidenceResolutionContext);
  const evidenceId = snapshot.evidenceId?.trim() || null;
  const access = snapshot.accessState ?? snapshot.access;
  const accessKey = access?.toLowerCase() ?? "";
  const captureModeKey = snapshot.captureMode.replace(/[^a-z]/gi, "").toLowerCase();
  const captureStateKey = snapshot.captureState?.replace(/[^a-z]/gi, "").toLowerCase();
  const supportedCaptureMode = captureModeKey === "payload" || captureModeKey === "diagnosticsnapshot";
  const capturedState = !captureStateKey || captureStateKey === "payloadcaptured" || captureStateKey === "diagnosticsnapshotcaptured";
  const permissionAllowsResolution = accessKey === "resolutionavailable";
  const canResolve = !!scope && !!evidenceId && supportedCaptureMode && capturedState && permissionAllowsResolution;
  const resolutionIdentity = JSON.stringify([runtimeValueContextId(scope?.context), scope?.workflowExecutionId, scope?.activityExecutionId, evidenceId, captureModeKey]);
  const [requestedSensitiveIdentity, setRequestedSensitiveIdentity] = useState<string | null>(null);
  const [retrySequence, setRetrySequence] = useState(0);
  const [resolution, setResolution] = useState<RuntimeValueResolutionState | null>(null);
  const explicitlyRequested = snapshot.isSensitive && requestedSensitiveIdentity === resolutionIdentity;
  const shouldResolve = canResolve && (!snapshot.isSensitive || explicitlyRequested);

  useEffect(() => {
    if (!shouldResolve || !scope || !evidenceId) return;

    const controller = new AbortController();
    setResolution({ identity: resolutionIdentity, status: "loading" });
    getActivityExecutionValuePayload(
      scope.context,
      scope.workflowExecutionId,
      scope.activityExecutionId,
      evidenceId,
      snapshot.captureMode,
      controller.signal
    ).then(
      value => {
        if (!controller.signal.aborted) setResolution({ identity: resolutionIdentity, status: "ready", payload: value.payload });
      },
      error => {
        if (!controller.signal.aborted) {
          setResolution({
            identity: resolutionIdentity,
            status: "failed",
            error: error instanceof Error ? error.message : String(error)
          });
        }
      }
    );

    return () => controller.abort();
  }, [evidenceId, resolutionIdentity, retrySequence, scope, shouldResolve, snapshot.captureMode]);

  const hasInlinePayload = !permissionAllowsResolution && snapshot.payload !== undefined;
  const diagnosticSnapshot = permissionAllowsResolution
    ? null
    : snapshot.snapshot ?? (snapshot.captureMode === "DiagnosticSnapshot" && hasInlinePayload ? snapshot.payload : null);
  const hasPayload = snapshot.captureMode === "Payload" && hasInlinePayload;
  const currentResolution = resolution?.identity === resolutionIdentity ? resolution : null;
  const protectedAccess = ["redacted", "permissionhidden", "permission-hidden", "resolutionpermissionrequired", "unavailable"].includes(accessKey);
  const canShowSensitiveValue = snapshot.isSensitive && canResolve;
  const invalidResolutionMetadata = permissionAllowsResolution && (!evidenceId || !supportedCaptureMode || !capturedState);
  const resolvedPayload = currentResolution?.status === "ready" &&
    canResolve &&
    (!snapshot.isSensitive || explicitlyRequested) &&
    !protectedAccess;

  return (
    <>
      <div className="wf-runtime-input-content">
        {resolvedPayload && currentResolution?.status === "ready" ? (
          captureModeKey === "diagnosticsnapshot" && isDiagnosticSnapshotNode(currentResolution.payload)
            ? <DiagnosticSnapshotTree node={currentResolution.payload} />
            : <>
                <small>Captured value</small>
                <RuntimeInputPayload payload={currentResolution.payload} />
              </>
        ) : protectedAccess ? (
          <p>{formatEvidenceMessage(snapshot)}</p>
        ) : snapshot.isSensitive ? (
          <>
            {isProtectedDiagnosticSnapshot(diagnosticSnapshot) && isDiagnosticSnapshotNode(diagnosticSnapshot)
              ? <DiagnosticSnapshotTree node={diagnosticSnapshot} />
              : <p>Runtime value is protected because this input is sensitive.</p>}
            {canShowSensitiveValue ? (
              <button
                type="button"
                disabled={currentResolution?.status === "loading"}
                onClick={() => {
                  setRequestedSensitiveIdentity(resolutionIdentity);
                  if (currentResolution?.status === "failed") setRetrySequence(sequence => sequence + 1);
                }}
              >
                {currentResolution?.status === "failed" ? "Retry captured value" : "Show captured value"}
              </button>
            ) : null}
            {currentResolution?.status === "loading" ? <p role="status">Resolving captured value...</p> : null}
            {currentResolution?.status === "failed" ? <p role="alert">Could not resolve captured value: {currentResolution.error}</p> : null}
          </>
        ) : snapshot.failure ? (
          <p>{snapshot.failure.message || snapshot.failure.code || "The input could not be evaluated."}{snapshot.failure.incidentId ? ` Incident ${snapshot.failure.incidentId}.` : ""}</p>
        ) : isDiagnosticSnapshotNode(diagnosticSnapshot) ? (
          <DiagnosticSnapshotTree node={diagnosticSnapshot} />
        ) : hasPayload ? (
          <RuntimeInputPayload payload={snapshot.payload} />
        ) : (
          <p>{invalidResolutionMetadata
            ? "Captured value resolution is unavailable because the Runtime evidence reference is incomplete."
            : formatEvidenceMessage(snapshot)}</p>
        )}
        {!snapshot.isSensitive && !protectedAccess && currentResolution?.status === "loading" ? <p role="status">Resolving captured value...</p> : null}
        {!snapshot.isSensitive && !protectedAccess && currentResolution?.status === "failed" ? (
          <p role="alert">
            Could not resolve captured value: {currentResolution.error} {" "}
            <button type="button" onClick={() => setRetrySequence(sequence => sequence + 1)}>Retry</button>
          </p>
        ) : null}
      </div>
      {snapshot.isSensitive ? <p className="wf-instance-note">Marked sensitive by runtime evidence.</p> : null}
    </>
  );
}

// `DiagnosticSnapshotUnknownNode` has `kind: string`, which defeats discriminated-union narrowing —
// route unknown kinds out first so the switch below narrows to the concrete node interfaces.
type KnownDiagnosticSnapshotNode = Exclude<DiagnosticSnapshotNode, DiagnosticSnapshotUnknownNode>;

// Record<K, true> makes the compiler reject a missing or misspelled kind when the union grows.
const knownSnapshotKinds: Record<KnownDiagnosticSnapshotNode["kind"], true> = {
  null: true, scalar: true, number: true, string: true, object: true, array: true,
  redacted: true, truncated: true, unsupported: true, error: true,
  permissionHidden: true, payloadReference: true
};

export function isKnownSnapshotNode(node: DiagnosticSnapshotNode): node is KnownDiagnosticSnapshotNode {
  return Object.hasOwn(knownSnapshotKinds, node.kind);
}

function DiagnosticSnapshotTree({ node, depth = 0 }: { node: DiagnosticSnapshotNode; depth?: number }) {
  if (!isKnownSnapshotNode(node)) {
    return <DiagnosticSnapshotMarker node={{ kind: "unsupported", reason: `Unknown snapshot node: ${node.kind}` }} />;
  }

  switch (node.kind) {
    case "null":
      return <code className="wf-runtime-input-value wf-runtime-snapshot-value">null</code>;
    case "scalar":
    case "number":
      return <code className="wf-runtime-input-value wf-runtime-snapshot-value">{formatSnapshotPayload(node.value)}</code>;
    case "string": {
      const preview = node.preview ?? "";
      return (
        <code className="wf-runtime-input-value wf-runtime-snapshot-value">
          {preview.length === 0 ? '""' : preview}
          {node.truncated ? ` (${node.length ?? "unknown"} chars, truncated)` : ""}
        </code>
      );
    }
    case "object":
      return <DiagnosticSnapshotObject node={node} depth={depth} />;
    case "array":
      return <DiagnosticSnapshotArray node={node} depth={depth} />;
    case "redacted":
    case "truncated":
    case "unsupported":
    case "error":
    case "permissionHidden":
      return <DiagnosticSnapshotMarker node={node} />;
    case "payloadReference":
      return <DiagnosticSnapshotReference node={node} />;
  }
}

function DiagnosticSnapshotObject({ node, depth }: { node: DiagnosticSnapshotObjectNode; depth: number }) {
  const properties = node.properties ?? [];
  if (properties.length === 0) return <code className="wf-runtime-input-value wf-runtime-snapshot-value">{"{}"}</code>;

  return (
    <details className="wf-runtime-snapshot-node" open={depth === 0}>
      <summary>{node.typeName || "Object"}{node.truncated ? " (truncated)" : ""}</summary>
      <div className="wf-runtime-snapshot-children">
        {properties.map(property => (
          <div className="wf-runtime-snapshot-property" key={property.name}>
            <span>{property.name}</span>
            <DiagnosticSnapshotTree node={property.value} depth={depth + 1} />
          </div>
        ))}
      </div>
    </details>
  );
}

function DiagnosticSnapshotArray({ node, depth }: { node: DiagnosticSnapshotArrayNode; depth: number }) {
  const items = node.items ?? [];
  if (items.length === 0) return <code className="wf-runtime-input-value wf-runtime-snapshot-value">[]</code>;

  return (
    <details className="wf-runtime-snapshot-node" open={depth === 0}>
      <summary>Array ({node.itemCount ?? items.length}){node.truncated ? " (truncated)" : ""}</summary>
      <div className="wf-runtime-snapshot-children">
        {items.map((item, index) => (
          <div className="wf-runtime-snapshot-property" key={index}>
            <span>{index}</span>
            <DiagnosticSnapshotTree node={item} depth={depth + 1} />
          </div>
        ))}
      </div>
    </details>
  );
}

function DiagnosticSnapshotMarker({ node }: { node: Pick<DiagnosticSnapshotNode, "kind" | "displayName"> & { reason?: string | null; omittedCount?: number | null; message?: string | null; requiredPermission?: string | null } }) {
  const reason = node.message || node.reason || node.requiredPermission || node.displayName;
  return (
    <span className={`wf-runtime-snapshot-marker ${node.kind}`}>
      {formatSnapshotKind(node.kind)}
      {reason ? `: ${reason}` : ""}
      {node.omittedCount ? ` (${node.omittedCount} omitted)` : ""}
    </span>
  );
}

function DiagnosticSnapshotReference({ node }: { node: DiagnosticSnapshotPayloadReferenceNode }) {
  const label = node.displayName || node.referenceKind || "Referenced payload";
  const resolutionReason = node.resolution?.reason || "Reference resolution is not available.";
  return (
    <div className="wf-runtime-snapshot-reference">
      <strong>{label}</strong>
      {node.contentType ? <small>{node.contentType}</small> : null}
      {typeof node.size === "number" ? <small>{node.size} bytes</small> : null}
      <span>{resolutionReason}</span>
    </div>
  );
}

function RuntimeInputPayload({ payload }: { payload: unknown }) {
  const text = formatSnapshotPayload(payload);
  const displayText = text.length === 0 ? '""' : text;
  const compact = text.length <= 160 && !text.includes("\n");

  return compact ? (
    <code className="wf-runtime-input-value wf-runtime-snapshot-value">{displayText}</code>
  ) : (
    <details className="wf-runtime-input-value-details">
      <summary>{previewSnapshotPayload(displayText)}</summary>
      <pre>{displayText}</pre>
    </details>
  );
}

export function formatCaptureMode(mode: string) {
  return mode.replace(/([a-z])([A-Z])/g, "$1 $2");
}

export function formatSnapshotKind(kind: string) {
  return kind.replace(/([a-z])([A-Z])/g, "$1 $2");
}

function formatEvidenceMessage(snapshot: ActivityExecutionInspectionValueSnapshot) {
  const access = (snapshot.accessState ?? snapshot.access)?.toLowerCase();
  const captureState = (snapshot.captureState ?? snapshot.state)?.replace(/[^a-z]/gi, "").toLowerCase();
  if (access === "redacted") return "Runtime value evidence is redacted.";
  if (access === "resolutionpermissionrequired") return "Additional permission is required to resolve this captured value.";
  if (access === "unavailable") {
    if (captureState === "metadataonly" || captureState === "notcaptured") {
      return snapshot.captureReason || "No captured value is available.";
    }
    return captureState === "capturefailed"
      ? snapshot.failure?.message || snapshot.captureReason || "Runtime value capture failed."
      : "No captured value is available.";
  }
  if (access === "permissionhidden" || access === "permission-hidden") return "Runtime value evidence is hidden by permissions.";
  if (snapshot.state === "permissionHidden") return "Runtime value evidence is hidden by permissions.";
  if (captureState === "metadataonly" || snapshot.state === "metadataOnly") return snapshot.captureReason || "Runtime value evidence is metadata-only.";
  if (captureState === "notcaptured" || snapshot.state === "notCaptured") return snapshot.captureReason || "Runtime value evidence was not captured.";
  if (captureState === "capturefailed") return snapshot.captureReason || snapshot.failure?.message || "Runtime value capture failed.";
  return snapshot.captureReason || "The runtime capture policy did not include this value.";
}

function isProtectedDiagnosticSnapshot(value: unknown) {
  return isDiagnosticSnapshotNode(value) && (value.kind === "redacted" || value.kind === "permissionHidden");
}

function isDiagnosticSnapshotNode(value: unknown): value is DiagnosticSnapshotNode {
  return typeof value === "object" && value !== null && typeof (value as { kind?: unknown }).kind === "string";
}

export function previewSnapshotPayload(text: string) {
  const firstLine = text.split("\n", 1)[0] || text;
  return firstLine.length > 120 ? `${firstLine.slice(0, 117)}...` : firstLine;
}

export function formatSnapshotPayload(payload: unknown) {
  if (payload === null) return "null";
  if (payload === undefined) return "undefined";
  if (typeof payload === "string") return payload;
  if (typeof payload === "number" || typeof payload === "boolean" || typeof payload === "bigint") return String(payload);

  try {
    return JSON.stringify(payload, null, 2);
  } catch {
    return String(payload);
  }
}
