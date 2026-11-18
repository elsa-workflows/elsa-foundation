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
  describeVariableWrite,
  inputRefreshReason,
  lostUpdateFaultOutcome,
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
  return onSelect
    ? <button type="button" className="wf-lost-update-badge" onClick={onSelect}>{content}</button>
    : <span className="wf-lost-update-status"><StatusChip tone="warning">{content}</StatusChip></span>;
}

/** A value state, linked to its captured value evidence when the record names one. */
export function VariableValueState({ label, value, evidence, note }: {
  label: string;
  value: VariableLifecycleValueState | null | undefined;
  evidence?: ActivityExecutionInspectionValueSnapshot;
  /** Replaces the state's generic explanation when the record says more about where the value is. */
  note?: string;
}) {
  const presentation = presentValueState(value);
  return (
    <div className="wf-variable-value-state" data-tone={presentation.tone}>
      <dt>{label}</dt>
      <dd>
        <span className="wf-variable-value-state-label">{presentation.label}</span>
        {value?.digest ? <code className="wf-variable-value-digest" title="Value digest">{shortDigest(value.digest)}</code> : null}
        {evidence ? <RuntimeValueEvidenceContent snapshot={evidence} /> : <small>{note ?? presentation.detail}</small>}
      </dd>
    </div>
  );
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
          To restore the overwritten value, set it again with the Modify variable alteration.
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
  inspection: Pick<ActivityExecutionInspection, "variableWrites" | "recordedCompletion" | "subStatus" | "valueSnapshots">;
  labelOf?: LabelOf;
}) {
  const records = variableWriteDiagnostics(inspection);
  const recorded = inspection.recordedCompletion;
  const refusedByLostUpdate = inspection.subStatus === variableLostUpdateSubStatus;
  if (records.length === 0 && !recorded && !refusedByLostUpdate) return null;
  const evidenceById = new Map((inspection.valueSnapshots ?? []).filter(item => item.evidenceId).map(item => [item.evidenceId!, item]));
  return (
    <section className="wf-instance-section wf-variable-writes" aria-label="Variable write diagnostics">
      <header className="wf-runtime-evidence-heading">
        <h4><History size={14} aria-hidden="true" /> Variable writes</h4>
        <LostUpdateBadge count={countLostUpdates(records)} />
      </header>
      {refusedByLostUpdate ? (
        <p className="wf-variable-write-hint" role="note">
          This step faulted with {variableLostUpdateSubStatus}: a variable it read and writes changed before its write
          could commit, so the write was refused instead of overwriting the newer value.
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
        <div className="wf-recorded-completion" aria-label="Recorded completion">
          <strong>Recorded completion kept</strong>
          <p>
            The body finished{recorded.attemptOrdinal ? ` on attempt ${recorded.attemptOrdinal}` : ""} with outcome {recorded.outcomeNames.length > 0 ? recorded.outcomeNames.join(", ") : "none"}
            {recorded.variableWrites.length > 0 ? ` and would write ${recorded.variableWrites.map(write => write.variableKey).join(", ")}` : ""}.
            Accept recorded completion, in the incident's operator actions, completes the step with it without running the body again.
          </p>
          {recorded.variableWrites.length > 0 ? (
            <dl className="wf-variable-write-facts">
              {recorded.variableWrites.map(write => (
                <VariableValueState key={write.variableKey} label={`Recorded ${write.variableKey}`} value={write.value} />
              ))}
            </dl>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}

/** The input evaluations a background admission refresh superseded, each with the changed variables that caused it. */
export function InputRefreshHistory({ refreshes }: { refreshes: readonly ActivityInputRefreshView[] | null | undefined }) {
  if (!refreshes?.length) return null;
  return (
    <div className="wf-input-refreshes" aria-label="Superseded input evaluations">
      <strong><RefreshCw size={13} aria-hidden="true" /> Inputs evaluated again before this background step ran</strong>
      <ol>
        {refreshes.map(refresh => (
          <li key={`${refresh.supersededEvaluation}:${refresh.refreshedAt}`}>
            <p>
              Evaluation {refresh.supersededEvaluation} (from {formatDate(refresh.supersededMaterializedAt)}) was replaced by
              evaluation {refresh.replacedByEvaluation} at {formatDate(refresh.refreshedAt)}, because {inputRefreshReason(refresh)}.
            </p>
            {refresh.supersededInputs.length > 0 ? (
              <dl className="wf-variable-write-facts">
                {refresh.supersededInputs.map(input => (
                  <VariableValueState key={input.inputKey} label={`${input.inputKey} (evaluation ${refresh.supersededEvaluation})`} value={input.value} />
                ))}
              </dl>
            ) : null}
          </li>
        ))}
      </ol>
    </div>
  );
}
