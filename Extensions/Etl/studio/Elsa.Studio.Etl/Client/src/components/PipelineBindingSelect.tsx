import React, { useId } from "react";
import type { EtlBindingSummary } from "../etlTypes";

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
  onChange
}: {
  label: string;
  role: EtlBindingRole;
  value: string;
  bindings: readonly EtlBindingSummary[];
  disabled: boolean;
  onChange(value: string): void;
}) {
  const id = useId();
  const candidates = bindings.filter(binding => binding.role === role);
  const currentIsAvailable = candidates.some(binding => binding.alias === value);

  return (
    <div className="etl-editor-field">
      <label htmlFor={id}>{label}</label>
      <select id={id} value={value} disabled={disabled} onChange={event => onChange(event.currentTarget.value)}>
        <option value="">Choose an approved binding</option>
        {value && !currentIsAvailable ? <option value={value}>{value} (not loaded for {roleLabels[role]})</option> : null}
        {candidates.map(binding => <option key={`${binding.alias}:${binding.role}`} value={binding.alias}>{binding.alias}</option>)}
      </select>
      <small>{candidates.length === 0
        ? `No ${roleLabels[role].toLowerCase()} aliases are in the loaded bindings yet. Load more to check other pages.`
        : `Choose a host-approved ${roleLabels[role].toLowerCase()} binding.`}</small>
    </div>
  );
}
