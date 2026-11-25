import { useStudioTimeZone } from "@elsa-workflows/studio-sdk";
import { useId } from "react";
import { AlertTriangle, History, RefreshCw } from "lucide-react";
import { StatusChip } from "@elsa-workflows/studio-ui";
import type {
  ActivityExecutionInspection,
  ActivityExecutionInspectionValueSnapshot,
  ActivityInputRefreshView,
  VariableLifecycleValueState,
  VariableWriteRecordView
} from "../workflowTypes";
import {
  acceptedOverrideOutcome,
  countLostUpdates,
  describeInputRefreshReason,
  describeVariableWrite,
  lostUpdateFaultOutcome,
  lostUpdateBadgeLabel,
  lostUpdateWarningOutcome,
  outcomeLabel,
  presentValueState,
  variableLostUpdateSubStatus,
  variableWriteDiagnostics,
  versionSummary,
  writerLabel
} from "../variableLifecycle";
import { formatDate } from "../workflowFormatting";
import { RuntimeValueEvidenceContent } from "./RuntimeValueEvidence";
import "./variableLifecycle.css";

// Spec 1221 (S-10) in the run view (#4735). Spec 1229 (S-6) builds the write-history, journal and comparison views on
// its own read API and extends LostUpdateBadge to open them; these components stay presentation only.

type LabelOf = (executableNodeId: string) => string | undefined;

/**
 * The lost-update count as a diagnostic badge. Reusable: pass `onSelect` to make it open a view (spec 1229 opens the
 * write history filtered to lost updates); without it the badge only reports, as the shared warning status chip.
 */
export function LostUpdateBadge({ count, onSelect, label }: { count: number; onSelect?: () => void; label?: string }) {

  if (count <= 0) return null;
  const text = label ?? (count === 1 ? "1 lost update" : `${count} lost updates`);
  const content = <><AlertTriangle size={12} aria-hidden="true" /> {text}</>;
  const chip = <StatusChip tone="warning">{content}</StatusChip>;
  return onSelect
    ? <button type="button" className="wf-lost-update-status wf-lost-update-button" onClick={onSelect}>{chip}</button>
    : <span className="wf-lost-update-status">{chip}</span>;
}

/** A value state, linked to its captured value evidence when the record names one. */
export function VariableValueState({ label, value, evidence, note, stateLabel }: {
  label: string;
  value: VariableLifecycleValueState | null | undefined;
  evidence?: ActivityExecutionInspectionValueSnapshot;
  /** Replaces the state's generic explanation when the record says more about where the value is. */
  note?: string;
  /** Replaces the state's generic name, for a value kept for a purpose of its own (a recorded completion's write). */
  stateLabel?: string;
}) {

  const presentation = presentValueState(value);
  return (
    <div className="wf-variable-value-state" data-tone={presentation.tone}>
      <dt>{label}</dt>
      <dd>
        <span className="wf-variable-value-state-label">{stateLabel ?? presentation.label}</span>
        {/* The fingerprint identifies a captured value without showing it; beside a missing value it means nothing. */}
        {value?.digest && value.state === "captured"
          ? <small className="wf-variable-value-digest" title="Identifies the value without showing it">Fingerprint <code>{shortDigest(value.digest)}</code></small>
          : null}
        {evidence ? <RuntimeValueEvidenceContent snapshot={evidence} /> : <small>{note ?? presentation.detail}</small>}
      </dd>
    </div>
  );
}

// Only a step's own output capture and Set Variable keep a value snapshot of what they wrote; an operator alteration
// and an accepted completion keep none (docs/runtime-variable-lifecycle.md, "What is not covered yet").
function keepsValueEvidence(writer: VariableWriteRecordView["overwrittenWriter"]) {
  return !!writer?.executableNodeId && (writer.source === "Capture" || writer.source === "Intrinsic");
}

function shortDigest(digest: string) {
  const value = digest.includes(":") ? digest.slice(digest.indexOf(":") + 1) : digest;
  return value.length > 12 ? `${value.slice(0, 12)}…` : value;
}

function VariableWriteCard({ record, labelOf, evidenceById }: {
  record: VariableWriteRecordView;
  labelOf?: LabelOf;
  evidenceById: ReadonlyMap<string, ActivityExecutionInspectionValueSnapshot>;
}) {
  useStudioTimeZone();
  const evidenceOf = (value: VariableLifecycleValueState | null | undefined) =>
    value?.evidenceId ? evidenceById.get(value.evidenceId) : undefined;
  return (
    <article className="wf-variable-write" role="listitem" data-outcome={record.outcome}>
      <header>
        <strong>{record.variableKey}</strong>
        <span className="wf-variable-write-outcome">{outcomeLabel(record.outcome)}</span>
        <small>{versionSummary(record)}</small>
      </header>
      <p>{describeVariableWrite(record, labelOf)}</p>
      <dl className="wf-variable-write-facts">
        <div><dt>Writer</dt><dd>{writerLabel(record.writer, labelOf)}</dd></div>
        <div><dt>{record.outcome === lostUpdateFaultOutcome ? "Changed by" : "Overwritten writer"}</dt><dd>{writerLabel(record.overwrittenWriter, labelOf)}</dd></div>
        <div><dt>When</dt><dd>{formatDate(record.occurredAt)}</dd></div>
        {record.writtenValue ? (
          <VariableValueState
            label="Written value"
            value={record.writtenValue}
            evidence={evidenceOf(record.writtenValue)}
            note={record.outcome === acceptedOverrideOutcome
              ? "The value the accepted completion recorded; an accepted completion captures no separate value evidence."
              : undefined}
          />
        ) : null}
        {record.overwrittenValue ? <VariableValueState label="Overwritten value" value={record.overwrittenValue} evidence={evidenceOf(record.overwrittenValue)} /> : null}
        {record.rejectedValue ? <VariableValueState label="Refused value" value={record.rejectedValue} evidence={evidenceOf(record.rejectedValue)} /> : null}
      </dl>
      {record.outcome === lostUpdateWarningOutcome ? (
        <p className="wf-variable-write-hint">
          To restore the overwritten value, set it again with the runtime API's ModifyVariable alteration. Studio cannot
          show the kept value yet
          {keepsValueEvidence(record.overwrittenWriter)
            ? `; in runs captured with Variable writes set to Diagnostic snapshots or Full payloads under Runtime evidence settings, the step that wrote it (${writerLabel(record.overwrittenWriter, labelOf)}) can show the value it wrote, where the runtime captured one.`
            : "."}
        </p>
      ) : null}
    </article>
  );
}

/**
 * The step's lost-update warnings, refused writes and accepted overrides, with both writers and each value as a state.
 * Renders nothing for a step whose writes all applied cleanly.
 */
export function VariableWriteDiagnosticsSection({ inspection, labelOf }: {
  inspection: Pick<ActivityExecutionInspection, "variableWrites" | "recordedCompletion" | "subStatus" | "valueSnapshots"> & { status?: string | null };
  labelOf?: LabelOf;
}) {

  const headingId = useId();
  const records = variableWriteDiagnostics(inspection);
  const recorded = inspection.recordedCompletion;
  const refusedByLostUpdate = inspection.subStatus === variableLostUpdateSubStatus;
  // A retry or an accepted completion supersedes the faulted step; the source keeps its records as history.
  const superseded = inspection.status === "Superseded";
  if (records.length === 0 && !recorded && !refusedByLostUpdate) return null;
  const evidenceById = new Map((inspection.valueSnapshots ?? []).filter(item => item.evidenceId).map(item => [item.evidenceId!, item]));
  return (
    <section className="wf-instance-section wf-variable-writes" aria-labelledby={headingId} data-testid="variable-write-diagnostics">
      <header className="wf-runtime-evidence-heading">
        <h4 id={headingId}><History size={14} aria-hidden="true" /> Variable writes</h4>
        <LostUpdateBadge count={countLostUpdates(records)} label={lostUpdateBadgeLabel(records)} />
      </header>
      {refusedByLostUpdate ? (
        <p className="wf-variable-write-hint" role="note">
          This step faulted with {variableLostUpdateSubStatus}: a variable it reads and writes changed before its write
          could commit, so the write was refused instead of overwriting the newer value.
          {superseded ? " An operator action has since replaced this step with a new execution." : null}
        </p>
      ) : null}
      {records.length > 0 ? (
        <div className="wf-variable-write-list" role="list">
          {records.map((record, index) => (
            <VariableWriteCard key={`${record.frameId ?? record.scopeId}:${record.variableKey}:${record.commitOrder}:${index}`} record={record} labelOf={labelOf} evidenceById={evidenceById} />
          ))}
        </div>
      ) : null}
      {recorded ? (
        <section className="wf-recorded-completion" aria-label="Recorded completion">
          <strong>{superseded ? "Recorded completion (history)" : "Recorded completion kept"}</strong>
          <p>
            The body finished{recorded.attemptOrdinal ? ` on attempt ${recorded.attemptOrdinal}` : ""} with outcome {recorded.outcomeNames.length > 0 ? recorded.outcomeNames.join(", ") : "none"}
            {recorded.variableWrites.length > 0 ? ` and would write ${recorded.variableWrites.map(write => `"${write.variableKey}"`).join(", ")}` : ""}.
            {superseded
              ? " This step has since been replaced by a new execution, so the recorded completion can no longer be accepted here."
              : " While the step waits for intervention, an operator who may manage this run can apply it with Accept recorded completion, under Issues › Open incident details. It completes the step without running the body again."}
          </p>
          {recorded.variableWrites.length > 0 ? (
            <dl className="wf-variable-write-facts">
              {recorded.variableWrites.map(write => (
                <VariableValueState
                  key={write.variableKey}
                  label={`"${write.variableKey}" (recorded)`}
                  value={write.value}
                  {...(write.value.state === "captured"
                    ? { stateLabel: "Kept with the completion", note: "Kept so that accepting the completion can write it; Studio cannot show it yet." }
                    : {})}
                />
              ))}
            </dl>
          ) : null}
        </section>
      ) : null}
    </section>
  );
}

/** The input evaluations a background admission refresh superseded, each with the changed variables that caused it. */
export function InputRefreshHistory({ refreshes }: { refreshes: readonly ActivityInputRefreshView[] | null | undefined }) {
  useStudioTimeZone();
  if (!refreshes?.length) return null;
  return (
    <section className="wf-input-refreshes" aria-label="Superseded input evaluations">
      <strong><RefreshCw size={13} aria-hidden="true" /> Inputs evaluated again before this background step ran</strong>
      <ol>
        {refreshes.map(refresh => (
          <li key={`${refresh.supersededEvaluation}:${refresh.refreshedAt}`}>
            <p>
              Evaluation {refresh.supersededEvaluation} (from {formatDate(refresh.supersededMaterializedAt)}) was replaced by
              evaluation {refresh.replacedByEvaluation} at {formatDate(refresh.refreshedAt)}, because {describeInputRefreshReason(refresh)}.
            </p>
            {refresh.supersededInputs.length > 0 ? (
              <dl className="wf-variable-write-facts">
                {refresh.supersededInputs.map(input => (
                  <VariableValueState
                    key={input.inputKey}
                    label={`${input.inputKey} (evaluation ${refresh.supersededEvaluation})`}
                    value={input.value}
                    // An input snapshot is kept on the step's durable state whatever the capture settings; a missing
                    // value there is an input this evaluation gave no value.
                    {...(input.value.state === "omitted" ? { stateLabel: "Not set", note: "This evaluation gave the input no value." } : {})}
                  />
                ))}
              </dl>
            ) : null}
          </li>
        ))}
      </ol>
    </section>
  );
}
