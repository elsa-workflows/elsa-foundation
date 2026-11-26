import { Check } from "lucide-react";
import "./Stepper.css";

export interface StudioStepperProps<Step extends string> {
  label: string;
  steps: readonly { id: Step; label: string }[];
  current: Step;
}

export function StudioStepper<Step extends string>({ label, steps, current }: StudioStepperProps<Step>) {
  const active = steps.findIndex(step => step.id === current);
  return <ol className="studio-stepper" aria-label={label}>
    {steps.map((step, index) => <li key={step.id} aria-current={index === active ? "step" : undefined} data-completed={index < active || undefined}>
      <span className="studio-stepper-marker" aria-hidden="true">{index < active ? <Check size={14} /> : index + 1}</span>
      <span>{step.label}</span>
    </li>)}
  </ol>;
}
