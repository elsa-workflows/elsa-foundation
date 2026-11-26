import { useId, type ReactNode } from "react";
import "./ChoiceFields.css";

export interface StudioRadioGroupProps<Value extends string> {
  label: string;
  value: Value;
  options: readonly { value: Value; label: ReactNode; disabled?: boolean }[];
  onChange(value: Value): void;
  disabled?: boolean;
  inline?: boolean;
}
export function StudioRadioGroup<Value extends string>({ label, value, options, onChange, disabled, inline }: StudioRadioGroupProps<Value>) {
  const name = useId();
  return <div role="radiogroup" aria-label={label} className="studio-radio-group" data-inline={inline || undefined}>
    {options.map(option => <label key={option.value} className="studio-choice-field">
      <input type="radio" name={name} value={option.value} checked={value === option.value}
        disabled={disabled || option.disabled} onChange={() => onChange(option.value)} />
      <span>{option.label}</span>
    </label>)}
  </div>;
}

export function StudioCheckboxField({ label, checked, onChange, disabled, className }: {
  label: ReactNode; checked: boolean; onChange(checked: boolean): void; disabled?: boolean; className?: string;
}) {
  return <label className={["studio-choice-field", className].filter(Boolean).join(" ")}>
    <input type="checkbox" checked={checked} disabled={disabled} onChange={event => onChange(event.target.checked)} />
    <span>{label}</span>
  </label>;
}
