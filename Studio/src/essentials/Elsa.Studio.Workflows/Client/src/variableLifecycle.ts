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

/**
 * The badge text: a warning overwrote a newer value, a refusal kept it, so they are named apart, for example
 * "1 lost update", "2 refused writes" or "1 lost update · 1 refused write".
 */
export function lostUpdateBadgeLabel(records: readonly Pick<VariableWriteRecordView, "outcome">[] | null | undefined) {
  const warnings = (records ?? []).filter(record => record.outcome === lostUpdateWarningOutcome).length;
  const refusals = (records ?? []).filter(record => record.outcome === lostUpdateFaultOutcome).length;
  const parts = [
    warnings > 0 ? (warnings === 1 ? "1 lost update" : `${warnings} lost updates`) : null,
    refusals > 0 ? (refusals === 1 ? "1 refused write" : `${refusals} refused writes`) : null
  ].filter((part): part is string => part !== null);
  return parts.join(" · ");
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
      return `This step's write to ${variable} was refused: ${overwritten} changed it after this step read it, and this step is set to refuse a write when the value it read has changed (Fault mode).`;
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
 * is the run's write history (spec 1229, the Variables tab), so the detail points there.
 */
export function presentValueState(value: VariableLifecycleValueState | null | undefined): ValueStatePresentation {
  if (!value) return { label: "Not recorded", detail: "The runtime recorded no value here.", tone: "muted" };
  switch (value.state) {
    case "captured":
      if (value.evidenceId) return { label: "Captured", detail: "Captured as value evidence of this step.", tone: "neutral" };
      return value.isOperational
        ? { label: "Kept for recovery", detail: "Kept on the write history; open the run's Variables tab to show it.", tone: "neutral" }
        : { label: "Captured", detail: "Kept on the durable record; Studio cannot show it yet.", tone: "neutral" };
    case "redacted":
      return { label: "Redacted", detail: "Not shown: the value is sensitive or withheld, or you may not view values in this run.", tone: "warning" };
    case "expired":
      return { label: "Expired", detail: "The value passed its retention and was removed.", tone: "muted" };
    case "omitted":
      // This read cannot tell a value never captured from one retention removed (spec 1229's history read can), so it
      // names the causes instead of claiming one.
      return value.isOperational
        ? { label: "Not kept", detail: "No value was kept.", tone: "muted" }
        : {
          label: "Not available",
          detail: "Variable writes capture was off, the capture failed, or retention removed it.",
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
