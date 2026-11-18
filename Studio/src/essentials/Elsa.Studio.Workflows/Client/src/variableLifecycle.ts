import type {
  ActivityExecutionInspection,
  ActivityInputRefreshView,
  VariableLifecycleValueState,
  VariableWriteRecordView,
  VariableWriterView
} from "./workflowTypes";

// Spec 1221 (S-10) as Studio shows it (#4735): the write history, refusals and background input refreshes an activity
// execution's read carries. Values are value states only (spec 1229 FR-006); this module never reads a payload.

export const lostUpdateWarningOutcome = "LostUpdateWarning";
export const lostUpdateFaultOutcome = "LostUpdateFault";
export const acceptedOverrideOutcome = "AcceptedOverride";

/** The sub-status a step faulted by a refused `Fault` write carries. */
export const variableLostUpdateSubStatus = "VariableLostUpdate";

export function isLostUpdate(record: Pick<VariableWriteRecordView, "outcome">) {
  return record.outcome === lostUpdateWarningOutcome || record.outcome === lostUpdateFaultOutcome;
}

/** The write-history records that need an operator's attention: lost-update warnings, refusals and accepted overrides. */
export function variableWriteDiagnostics(inspection: Pick<ActivityExecutionInspection, "variableWrites"> | null | undefined): VariableWriteRecordView[] {
  return (inspection?.variableWrites ?? []).filter(record => isLostUpdate(record) || record.outcome === acceptedOverrideOutcome);
}

/** How many lost updates (warnings and refusals) a set of records holds: the number a lost-update badge shows. */
export function countLostUpdates(records: readonly Pick<VariableWriteRecordView, "outcome">[] | null | undefined) {
  return (records ?? []).filter(isLostUpdate).length;
}

export function outcomeLabel(outcome: string) {
  switch (outcome) {
    case lostUpdateWarningOutcome:
      return "Lost update";
    case lostUpdateFaultOutcome:
      return "Write refused";
    case acceptedOverrideOutcome:
      return "Accepted override";
    case "ReturnedValue":
      return "Value came back";
    case "Blind":
      return "Blind write";
    case "Current":
      return "Applied";
    default:
      return outcome;
  }
}

const writeSourceLabels: Record<string, string> = {
  Capture: "output capture",
  Intrinsic: "Set Variable",
  SessionChange: "variable session",
  Alteration: "operator alteration",
  BackgroundResult: "background result",
  AcceptedCompletion: "accepted completion"
};

/** "Increment (invocation inv-1)" — the node's label when known, then its node id, then the kind of write. */
export function writerLabel(writer: VariableWriterView | null | undefined, labelOf?: (executableNodeId: string) => string | undefined) {
  if (!writer) return "an earlier writer";
  const node = writer.executableNodeId ? labelOf?.(writer.executableNodeId) ?? writer.executableNodeId : null;
  const source = writeSourceLabels[writer.source] ?? writer.source;
  if (!node) return writer.source === "Alteration" ? "an operator alteration" : `a ${source}`;
  // A capture is named by its node; another kind of write adds its kind, unless the node is already called that.
  return writer.source === "Capture" || node.toLowerCase() === source.toLowerCase() ? `"${node}"` : `"${node}" (${source})`;
}

/**
 * The variable's versions around the write, in words: "Read version 0 · was version 1 · now version 2". A variable's
 * version counts the writes that changed it; a lost update is a write whose read version is older than the current one.
 */
export function versionSummary(record: Pick<VariableWriteRecordView, "baseVersion" | "currentVersion" | "newVersion" | "outcome">) {
  const read = record.baseVersion == null ? "Written without reading it" : `Read version ${record.baseVersion}`;
  if (record.outcome === lostUpdateFaultOutcome) return `${read} · was version ${record.currentVersion} · not written`;
  return `${read} · was version ${record.currentVersion} · now version ${record.newVersion}`;
}

/** What one record means for an operator, in one sentence. */
export function describeVariableWrite(record: VariableWriteRecordView, labelOf?: (executableNodeId: string) => string | undefined) {
  const variable = `"${record.variableKey}"`;
  const overwritten = writerLabel(record.overwrittenWriter, labelOf);
  switch (record.outcome) {
    case lostUpdateWarningOutcome:
      return `This step wrote ${variable} without seeing the value ${overwritten} wrote, and replaced it (last write wins).`;
    case lostUpdateFaultOutcome:
      return `This step's write to ${variable} was refused: ${overwritten} changed it after this step read it, and the step uses the Fault lost-update mode.`;
    case acceptedOverrideOutcome:
      return `An operator accepted this step's recorded completion: its write to ${variable} replaced the value ${overwritten} wrote.`;
    default:
      return `This step wrote ${variable}.`;
  }
}

export interface ValueStatePresentation {
  label: string;
  detail: string;
  tone: "neutral" | "muted" | "warning";
}

/**
 * How a value state reads. A captured operational value (overwritten or refused) is kept for recovery; its payload read
 * is the write-history read of spec 1229, so Studio says so instead of offering a read it cannot make yet.
 */
export function presentValueState(value: VariableLifecycleValueState | null | undefined): ValueStatePresentation {
  if (!value) return { label: "Not recorded", detail: "The runtime recorded no value here.", tone: "muted" };
  switch (value.state) {
    case "captured":
      if (value.evidenceId) return { label: "Captured", detail: "Captured as value evidence of this step.", tone: "neutral" };
      return value.isOperational
        ? { label: "Kept for recovery", detail: "Kept on the write history; Studio cannot show it yet.", tone: "neutral" }
        : { label: "Captured", detail: "Captured on the durable record; Studio cannot read it here.", tone: "neutral" };
    case "redacted":
      return { label: "Redacted", detail: "The value is sensitive or withheld and is not shown.", tone: "warning" };
    case "expired":
      return { label: "Expired", detail: "The value passed its retention and was removed.", tone: "muted" };
    case "omitted":
      // This read cannot tell a value never captured from one retention removed (spec 1229's history read can), so it
      // names the causes instead of claiming one.
      return value.isOperational
        ? { label: "Not kept", detail: "No value was kept.", tone: "muted" }
        : {
          label: "No value evidence",
          detail: "Not captured (Variable writes is off unless given its own diagnostics level), not captured because the capture failed, or removed by retention.",
          tone: "muted"
        };
    default:
      return { label: value.state, detail: "", tone: "muted" };
  }
}

/** "count v0 → v1, total v2 → v3": why a background step ran on a second evaluation of its inputs. */
export function inputRefreshReason(refresh: Pick<ActivityInputRefreshView, "changes">) {
  return refresh.changes.map(change => `${change.variableKey} v${change.readVersion} → v${change.currentVersion}`).join(", ");
}

/** The same reason in words: "count changed from version 0 to version 1". */
export function describeInputRefreshReason(refresh: Pick<ActivityInputRefreshView, "changes">) {
  return refresh.changes
    .map(change => `"${change.variableKey}" changed from version ${change.readVersion} to version ${change.currentVersion}`)
    .join(", ");
}

/** The two node settings of spec 1221 (FR-012, FR-019), as the authored node carries them. */
export const lostUpdateModeProperty = "lostUpdateMode";
export const disableBackgroundInputRefreshProperty = "disableBackgroundInputRefresh";

/** The authored node's lost-update mode. The design API writes the enum as a number (`1` is Fault); a string reads too. */
export function readLostUpdateMode(node: Record<string, unknown>): "Default" | "Fault" {
  const value = node[lostUpdateModeProperty];
  return value === 1 || (typeof value === "string" && value.toLowerCase() === "fault") ? "Fault" : "Default";
}

/** Writes the lost-update mode; Default is omitted, as the design API omits it. */
export function writeLostUpdateMode<T extends Record<string, unknown>>(node: T, mode: "Default" | "Fault"): T {
  const next: Record<string, unknown> = { ...node };
  if (mode === "Fault") next[lostUpdateModeProperty] = 1;
  else delete next[lostUpdateModeProperty];
  return next as T;
}

export function readDisableBackgroundInputRefresh(node: Record<string, unknown>) {
  return node[disableBackgroundInputRefreshProperty] === true;
}

/** Writes the refresh switch; the default (refresh on) is omitted. */
export function writeDisableBackgroundInputRefresh<T extends Record<string, unknown>>(node: T, disabled: boolean): T {
  const next: Record<string, unknown> = { ...node };
  if (disabled) next[disableBackgroundInputRefreshProperty] = true;
  else delete next[disableBackgroundInputRefreshProperty];
  return next as T;
}

/** Whether the authored node runs on a background worker (`executionPlacement` 1 or "Background"). */
export function isBackgroundPlacement(node: Record<string, unknown>) {
  const value = node.executionPlacement;
  return value === 1 || (typeof value === "string" && value.toLowerCase() === "background");
}
