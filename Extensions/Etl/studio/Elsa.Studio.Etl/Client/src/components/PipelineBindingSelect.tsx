import React, { useId } from "react";
import type { EtlBindingSummary, EtlConnectorBindingSummary } from "../etlTypes";

export type EtlBindingRole = "Source" | "Lookup" | "MainOutput" | "ErrorOutput";

const roleLabels: Record<EtlBindingRole, string> = {
  Source: "Source CSV",
  Lookup: "Lookup CSV",
  MainOutput: "Accepted JSONL",
  ErrorOutput: "Rejected JSONL"
};

export function PipelineBindingSelect({
  label,
  role,
  value,
  bindings,
  disabled,
  onChange,
  kind,
  bindingsLoading = false,
  bindingsUnavailable = false,
  subjectLabel
}: {
  label: string;
  role: EtlBindingRole;
  value: string;
  bindings: readonly (EtlBindingSummary | EtlConnectorBindingSummary)[];
  disabled: boolean;
  onChange(value: string): void;
  /** When set, only aliases with this v2 connector kind are offered. */
  kind?: EtlConnectorBindingSummary["kind"];
  bindingsLoading?: boolean;
  bindingsUnavailable?: boolean;
  /** Overrides the role and kind wording, for approvals whose kind is broader than the declared format. */
  subjectLabel?: string;
}) {
  const id = useId();
  const roleCandidates = bindings.filter(binding => binding.role === role);
  const candidates = roleCandidates.filter(binding => kind === undefined || ("kind" in binding && binding.kind === kind));
  const currentIsAvailable = candidates.some(binding => binding.alias === value);
  const currentIsLoaded = roleCandidates.some(binding => binding.alias === value);
  const subject = subjectLabel ?? (kind === undefined ? roleLabels[role] : connectorRoleLabels[role][kind]);

  return (
    <div className="etl-editor-field">
      <label htmlFor={id}>{label}</label>
      <select id={id} value={value} disabled={disabled} onChange={event => onChange(event.currentTarget.value)}>
        <option value="" disabled={kind !== undefined || bindingsLoading || bindingsUnavailable}>
          {bindingsLoading ? "Loading approved bindings…" : bindingsUnavailable && candidates.length === 0 ? "Approved bindings unavailable" : "Choose an approved binding"}
        </option>
        {value && !currentIsAvailable ? <option value={value}>{value} ({currentIsLoaded ? "not approved" : "not loaded"} for {subject})</option> : null}
        {candidates.map(binding => <option key={`${binding.alias}:${binding.role}`} value={binding.alias}>{binding.alias}</option>)}
      </select>
      <small>{bindingsLoading ? "Loading host-approved aliases. Existing selections are retained while they load."
        : bindingsUnavailable && candidates.length === 0 ? "Approved aliases could not be loaded. Retry loading bindings before choosing a replacement."
        : candidates.length === 0
        ? `No ${inSentence(subject)} aliases are in the loaded bindings yet. Load more to check other pages.`
        : `Choose a host-approved ${inSentence(subject)} binding.`}</small>
    </div>
  );
}

const connectorRoleLabels: Record<EtlBindingRole, Record<EtlConnectorBindingSummary["kind"], string>> = {
  Source: { csv: "Source CSV", http: "Source HTTP", jsonl: "Source JSONL", sql: "Source SQL table", rabbitMq: "Source RabbitMQ" },
  Lookup: { csv: "Lookup CSV", http: "Lookup HTTP", jsonl: "Lookup JSONL", sql: "Lookup SQL", rabbitMq: "Lookup RabbitMQ" },
  MainOutput: { csv: "Accepted CSV output", http: "Accepted HTTP output", jsonl: "Accepted JSONL output", sql: "Accepted SQL table", rabbitMq: "Accepted RabbitMQ exchange" },
  ErrorOutput: { csv: "Rejected CSV output", http: "Rejected HTTP output", jsonl: "Rejected JSONL output", sql: "Rejected SQL table", rabbitMq: "Rejected RabbitMQ exchange" }
};

/** Lower-cases only the first letter, so acronyms such as CSV, HTTP, SQL and RabbitMQ keep their case mid-sentence. */
function inSentence(subject: string) {
  return subject.charAt(0).toLowerCase() + subject.slice(1);
}
